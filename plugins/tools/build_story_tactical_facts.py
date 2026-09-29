"""Extract tactical capabilities and costs from operations, not card identity."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import zipfile
from build_story_combo_effects import ROOT, Reader, calls, compact, split_args


def closure(reader, body):
    visited=set()
    for _ in range(8):
        refs=set(re.findall(r'\b(?:s|c\d+)\.\w+',body))-visited
        if not refs: break
        visited |= refs
        body += '\n'+'\n'.join(reader.body(n) for n in sorted(refs))
    return body


def target_pool(reader, body):
    for name,offset in [('Duel.SelectTarget',1),('aux.SelectTargetFromFieldFirst',1),('Duel.SelectMatchingCard',1),('Duel.GetMatchingGroup',0),('Duel.IsExistingTarget',0),('Duel.IsExistingMatchingCard',0)]:
        for a in calls(body,name):
            if len(a)<4+offset: continue
            own,enemy=reader.number(a[2+offset]),reader.number(a[3+offset])
            if own is None or enemy is None: continue
            yield a[offset],own,enemy,a


def pool_predicate(reader, pool):
    name,own,enemy,a=pool
    # Selection/existence/group calls have different exclusion positions.
    offset=8 if len(a)>2 and a[0]=='tp' else 6 if len(a)>5 and a[4] in ('0','1','2') else 5
    return reader.pool_predicate(name,a[offset:])


def tactical_cost(reader,e,p,cost):
    """A cost must be known even when the engine currently offers an effect."""
    if not e.get('Cost'): return True
    if e.get('Cost')=='aux.bfgcost': p['SelfCost']=True; return True
    if not cost or re.search(r'\b(?:s|c\d+)\.\w+\(',cost): return False
    writes=[n for n in re.findall(r'Duel\.(\w+)\(',cost) if n not in {
        'CheckLPCost','GetLocationCount','GetMZoneCount','GetLP','Hint','SelectMatchingCard','SelectTarget',
        'IsExistingMatchingCard','IsExistingTarget','GetMatchingGroup','CheckReleaseGroup','GetReleaseGroup',
        'SelectReleaseGroup','SelectReleaseGroupEx','IsPlayerAffectedByEffect','GetFlagEffect'}]
    if set(writes)-{'PayLPCost','DiscardHand','SendtoGrave','Remove','Release','RegisterFlagEffect'}: return False
    spends=[n for n in writes if n!='RegisterFlagEffect']
    if len(spends)>1: return False
    for n in ('Duel.SelectReleaseGroup','Duel.SelectReleaseGroupEx'):
        for a in calls(cost,n):
            if len(a)<4 or a[2:4]!=['1','1']: return False
    if spends:
        name=spends[0]; a=next(calls(cost,'Duel.'+name),None)
        if name=='PayLPCost':
            if len(a)!=2 or a[0]!='tp' or reader.number(a[1]) is None: return False
            p['Life']=reader.number(a[1])
        elif name=='DiscardHand':
            if len(a)<4 or a[0]!='tp' or a[2]!=a[3] or reader.number(a[2]) not in (1,2): return False
            pred=reader.predicate(a[1])
            if pred is None: return False
            p['DiscardCount']=reader.number(a[2]); p['CostFilter']=pred; p['CostLocations']=2; p['CostHint']='Discard'
        elif a[0] in ('e:GetHandler()','c'):
            if a[0]=='c' and 'localc=e:GetHandler()' not in compact(cost): return False
            p['SelfCost']=True
        elif not p.get('CostFilter'): return False
    for pool in reader.selections(cost):
        if pool[3:5]!=('1','1'): return False
    if 'RemoveOverlayCard' in cost:
        m=re.search(r':RemoveOverlayCard\(tp,(\d+),(\d+),',compact(cost))
        if not m or m[1]!=m[2]: return False
        p['OverlayCost']=int(m[1])
    if 'RemoveCounter' in cost and not p.get('LinkCounters'): return False
    # Unknown effect registrations in costs may install unmodelled restrictions.
    if ':SetCode(' in cost: return False
    return True


def removal_modes(reader,op,target):
    if 'Duel.Destroy(g,REASON_EFFECT)' not in op: return None
    options=[a[1:] for a in calls(target,'Duel.SelectOption') if len(a)>2 and a[0]=='tp']
    if len(options)!=1: return None
    descs=[]
    for item in options[0]:
        a=next(calls(item,'aux.Stringid'),None)
        if not a or reader.number(a[0])!=reader.cid or reader.number(a[1]) is None: return None
        descs.append(reader.cid*16+reader.number(a[1]))
    modes=[]
    for m in re.finditer(r'if e:GetLabel\(\)==(\d+) then\s+g=(Duel.GetMatchingGroup\([^\n]+\))\s+end',op):
        index=int(m[1]); a=next(calls(m[2],'Duel.GetMatchingGroup'),None)
        if index>=len(descs) or not a or a[1:3]!=['tp','0']: return None
        pred=reader.pool_predicate(a[0],a[5:]); location=reader.number(a[3])
        if pred is None or location is None: return None
        modes.append(dict(Purpose='EnemyRemoval',Locations=location,TargetFilter=pred,NonTargeting=True,
                          AllTargets=True,Hint='Destroy',Option=descs[index]))
    return modes if len(modes)==len(descs) else None


def extract(reader):
    facts,ps={},[]
    script=reader.script
    listed=[]
    for a in calls(script,'aux.AddCodeList'):
        if a and a[0]=='c': listed += [reader.number(v) for v in a[1:]]
    m=re.search(r'\b(?:s|c\d+)\.listed_names\s*=\s*\{([^}]+)\}',script)
    if m: listed += [reader.number(v) for v in split_args(m[1])]
    if listed and all(v is not None for v in listed): facts['ListedCodes']=sorted(set(listed))
    limits=[]
    for e in reader.effects:
        a=split_args(e.get('CountLimit',''))
        if len(a)<2 or a[0]!='1' or 'EFFECT_COUNT_CODE_CHAIN' in a[1]: continue
        key=reader.number(re.sub(r'\+EFFECT_COUNT_CODE_(?:OATH|DUEL)','',compact(a[1])))
        desc=next(calls(e.get('Description',''),'aux.Stringid'),None)
        if key is None or desc and (reader.number(desc[0])!=reader.cid or reader.number(desc[1]) is None): continue
        origin=reader.number(e.get('Range','LOCATION_MZONE')) or 4
        if 'ACTIVATE' in e.get('Type',''): origin=10
        if e.get('Code') in ('EVENT_TO_GRAVE','EVENT_BE_MATERIAL','EVENT_MOVE','EVENT_RELEASE'): origin=16
        p=dict(CountKey=key,Origin=8 if origin==512 else origin,Offset=reader.number(desc[1]) if desc else 0,
               DefaultDescription=not bool(desc),Trigger='TRIGGER_' in e.get('Type',''),DuelOnce='EFFECT_COUNT_CODE_DUEL' in a[1])
        if p not in limits: limits.append(p)
    if limits: facts['Limits']=limits
    summon_once=next(calls(reader.body('s.initial_effect') or reader.body(f'c{reader.cid}.initial_effect'),'c:SetSPSummonOnce'),None)
    if summon_once and len(summon_once)==1 and reader.number(summon_once[0]) is not None:
        facts['SummonOnceKey']=reader.number(summon_once[0])
    # Passive facts and material-dependent ATK are script parameters.
    if 'EFFECT_NONTUNER' in script: facts['FlexibleTuner']=True
    for e in reader.effects:
        if e.get('Code')=='EFFECT_MATERIAL_CHECK':
            body=reader.body(e.get('Value'))
            m=re.search(r'local\s+(\w+)\s*=\s*c:GetMaterialCount\(\)',body)
            n=re.search(r':SetValue\('+m[1]+r'\*(\d+)\)',compact(body)) if m else None
            if n and 'EFFECT_SET_BASE_ATTACK' in body: facts['MaterialAttack']=int(n[1])
        if e.get('Code')=='EFFECT_IMMUNE_EFFECT' and not e.get('Condition'):
            value=reader.body(e.get('Value'))
            if 'not te:GetOwner():IsSetCard' in value or 'GetOwnerPlayer' in value: facts['ImmunityBonus']=1600
        if e.get('Code')=='EFFECT_CANNOT_BE_EFFECT_TARGET': facts['ProtectionBonus']=700
        if e.get('Code')=='EFFECT_CANNOT_SELECT_BATTLE_TARGET': facts['ProtectionBonus']=1400
        if e.get('Code')=='EFFECT_MUST_ATTACK_MONSTER': facts['ProtectionBonus']=1400
        if e.get('Code')=='EFFECT_TO_GRAVE_REDIRECT' and e.get('Value')=='LOCATION_REMOVED':
            if e.get('TargetRange') in ('LOCATION_ONFIELD+LOCATION_HAND+LOCATION_DECK,LOCATION_ONFIELD+LOCATION_HAND+LOCATION_DECK','0xff,0xff'): facts['GraveRedirect']=True
            elif ',0xff' in e.get('TargetRange',''): facts['OpponentGraveRedirect']=True
    if 'EFFECT_TO_GRAVE_REDIRECT' in script and 'LOCATION_REMOVED' in script:
        # Static floodgates only; an activated lingering effect is tracked separately.
        for e in reader.effects:
            if e.get('Code')=='EFFECT_TO_GRAVE_REDIRECT' and e.get('Value')=='LOCATION_REMOVED':
                a=split_args(e.get('TargetRange',''))
                if len(a)==2:
                    if a[0]!='0' and a[1]!='0': facts['GraveRedirect']=True
                    elif a[0]=='0': facts['OpponentGraveRedirect']=True
    for e in reader.effects:
        typ=e.get('Type','')
        if not re.search(r'EFFECT_TYPE_(IGNITION|ACTIVATE|QUICK_[OF]|TRIGGER_[OF])',typ): continue
        op=reader.body(e.get('Operation')); target=reader.body(e.get('Target')); cost=reader.body(e.get('Cost')); cond=reader.body(e.get('Condition'))
        if not op: continue
        if 'EFFECT_TYPE_ACTIVATE' in typ and not cost and 'Duel.SpecialSummon' in op and 'Duel.Sendto' not in op:
            selections=list(calls(target,'Duel.SelectTarget'))
            if len(selections)==1 and selections[0][3:7]==['LOCATION_GRAVE','LOCATION_GRAVE','1','1']:
                predicate=reader.predicate(selections[0][1])
                if predicate in ('Has(c, CardType.Monster)','(Has(c, (CardType)1) && Has(c, CardType.Monster))'):
                    facts['UniversalRevival']=True
        p={'Origin':reader.number(e.get('Range','LOCATION_MZONE')) or 4}
        if e.get('Code')=='EVENT_TO_GRAVE': p['Origin']=16
        if p['Origin']==512: p['Origin']=8
        if 'EFFECT_TYPE_ACTIVATE' in typ: p['Origin']=10 if reader.card_type&2 else 8
        a=next(calls(e.get('Description',''),'aux.Stringid'),None)
        if a:
            if reader.number(a[0])!=reader.cid or reader.number(a[1]) is None: continue
            p['Offset']=reader.number(a[1])
        else: p['DefaultDescription']=True
        p['Trigger']='TRIGGER_' in typ
        p['Quick']='QUICK_' in typ
        count=split_args(e.get('CountLimit',''))
        if count[0]=='1':
            p['Once']=True
            if len(count)==1: p['InstanceOnce']=True
            elif 'EFFECT_COUNT_CODE_CHAIN' in count[1]: p['Once']=False
            else:
                n=reader.number(re.sub(r'\+EFFECT_COUNT_CODE_(?:OATH|DUEL)','',compact(count[1])))
                if n is not None: p['CountKey']=n; p['SharedLimit']=True
            p['FaceupOnce']='EFFECT_FLAG_NO_TURN_RESET' in e.get('Property','')
        full=closure(reader,op); target_full=closure(reader,target)
        pools=list(target_pool(reader,target+'\n'+op))
        enemy=next((x for x in pools if x[2]),None)
        if enemy:
            p['Locations']=enemy[2]
            pred=pool_predicate(reader,enemy)
            if pred is not None: p['TargetFilter']=pred
        p['NonTargeting']='EFFECT_FLAG_CARD_TARGET' not in e.get('Property','')
        kind=None
        if 'Duel.NegateActivation' in op or 'Duel.NegateEffect' in op:
            kind='Negate'
            p['MonsterOnly']=bool(re.search(r're:IsActiveType\(TYPE_MONSTER\)',cond))
            p['SpellTrapOnly']=bool(re.search(r're:IsActiveType\(TYPE_SPELL\+TYPE_TRAP\)',cond))
            p['Narrow']='CATEGORY_SPECIAL_SUMMON' in cond and 'LOCATION_GRAVE' in cond
            attack=re.search(r':SetCode\(EFFECT_UPDATE_ATTACK\).*?:SetValue\(-(\d+)\)',compact(op))
            if attack: p['AttackCost']=int(attack[1])
            p['LinkCounters']='RemoveCounter' in cost and 'Duel.Equip' in script and 'GetLink()' in script
        elif 'Duel.LinkSummon' in op and p['Quick']: kind='QuickLink'
        elif 'Duel.Remove' in op and 'REASON_TEMPORARY' in op:
            selections=list(calls(target,'Duel.SelectTarget'))
            if len(selections)==2 and selections[0][3:7]==['LOCATION_MZONE','0','1','1'] and selections[1][3:7]==['LOCATION_MZONE','LOCATION_MZONE','1','1']:
                kind='TemporaryPair'
            elif 'RandomSelect' in op and 'LOCATION_HAND' in op and 'Group.FromCards(c,' in compact(op): kind='TemporaryHandRemoval'
            elif 'GetOverlayGroup' in op:
                kind='OverlayModes'
                for m in re.finditer(r'if\s+\w+:IsExists\(Card.IsType,1,nil,(TYPE_\w+)\)\s+then(.*?)(?=\n\s*if\s+\w+:IsExists|\Z)',op,re.S):
                    value=reader.number(m[1]); branch=m[2]
                    if value is None: continue
                    if 'Duel.Draw' in branch: p['DrawOverlayType']=value
                    if 'Duel.SendtoDeck' in branch: p['RemovalOverlayType']=value
                    if 'Duel.Remove' in branch: p['ExitOverlayType']=value
        elif 'Duel.Equip' in op and 'GetLink()' in op: kind='LinkEquip'
        elif 'EFFECT_UPDATE_ATTACK' in op:
            m=re.search(r':SetValue\(\w+:GetLink\(\)\*(\d+)\)',compact(op))
            if m: kind='LinkAttack'; p['LinkAttack']=int(m[1]); facts['LinkAttack']=int(m[1])
            elif 'SetTargetRange(0,LOCATION_MZONE)' in compact(op) and ('return -c:GetBaseDefense()' in full or re.search(r':SetValue\(-\d+\)',op)):
                kind='BattleDebuff'; p['BattleOnly']=True
        elif 'Duel.SendtoDeck(c,' in compact(op) and 'Duel.SpecialSummon' in op:
            kind='RevivalSwap'
            own=next((x for x in pools if x[1]==16 and x[2]==0),None)
            if own:
                p['TargetFilter']=reader.predicate(own[0]) or 'false'
        elif 'Duel.SendtoGrave' in op and enemy and enemy[2]==32 and 'REASON_RETURN' in op:
            kind='RecycleBanished'; p['Hint']='ToGrave'
            m=re.search(r'Duel.Hint\(HINT_SELECTMSG,tp,aux.Stringid\((?:id|\d+),(\d+)\)\)',compact(target))
            if m: p['CustomHint']=reader.cid*16+int(m[1])
        elif 'EFFECT_DISABLE' in op and enemy:
            kind='TargetNegate'; p['Hint']='Disable'
            p['AllTargets']='Duel.GetMatchingGroup' in op and not ('Select' in op or 'SelectTarget' in target)
        else:
            operations=[('Duel.Destroy','Destroy'),('Duel.Remove','Remove'),('Duel.SendtoDeck','ToDeck'),('Duel.SendtoHand','ReturnToHand')]
            mutation=next(((name,hint) for name,hint in operations if name+'(' in op),None)
            if mutation and enemy:
                name,hint=mutation
                # Resource searches and self-return summons are separate actions.
                if 'Duel.SpecialSummon' not in op and not ('CATEGORY_SEARCH' in e.get('Category','')):
                    kind='TargetRemoval'; p['Hint']=hint
                    group=list(calls(op,'Duel.GetMatchingGroup'))
                    if group and not re.search(r'(?:SelectMatchingCard|:Select\(|:FilterSelect)',op):
                        pool=next((x for x in pools if x[0]==group[0][0]),enemy)
                        kind='BoardWipe' if pool[1] and pool[2] else 'EnemyRemoval'; p['AllTargets']=True
                        p['ExcludeSelf']=group[0][-1] in ('e:GetHandler()','c')
                    p['SpecialOnly']='IsSummonType(SUMMON_TYPE_SPECIAL)' in target_full+full
                    p['PreventDirect']='EFFECT_CANNOT_DIRECT_ATTACK' in op
        if kind is None and 'Duel.GetControl' in op and p['Quick']: kind='TargetRemoval'
        modes=removal_modes(reader,op,target)
        if modes:
            kind='EnemyRemoval'; p['Modes']=modes; p.pop('TargetFilter',None)
        if kind is None and 'Duel.Draw' in op and 'EFFECT_TYPE_ACTIVATE' in typ:
            draws=list(calls(op,'Duel.Draw'))
            if 'localp,d=Duel.GetChainInfo(0,CHAININFO_TARGET_PLAYER,CHAININFO_TARGET_PARAM)' in compact(op):
                player=next(calls(target,'Duel.SetTargetPlayer'),None)
                amount=next(calls(target,'Duel.SetTargetParam'),None)
                if player==['tp'] and amount and reader.number(amount[0]) is not None:
                    draws=[['tp',amount[0],*a[2:]] if a[:2]==['p','d'] else a for a in draws]
            own=[a for a in draws if a[0]=='tp']
            allowed={'Draw','Recover','GetChainInfo','RegisterEffect','IsPlayerCanDraw','IsExistingMatchingCard'}
            if len(own)==1 and reader.number(own[0][1]) is not None and not (set(re.findall(r'Duel\.(\w+)\(',op))-allowed):
                kind='Draw'; p['DrawCount']=reader.number(own[0][1])
        if kind is None and 'Duel.Remove(sg,POS_FACEDOWN,REASON_RULE,1-tp)' in compact(op):
            if 'localct=g:GetCount()-Duel.GetFieldGroupCount(tp,LOCATION_ONFIELD,0)' in compact(op) and 'g:FilterSelect(1-tp,Card.IsAbleToRemove,ct,ct' in compact(op):
                kind='BalanceField'; facts['BattleEndBalance']= 'PHASE_BATTLE' in cond and any(x.get('Code')=='EFFECT_TRAP_ACT_IN_HAND' for x in reader.effects)
        if kind is None: continue
        if kind in ('TargetRemoval','TargetNegate','BoardWipe','EnemyRemoval') and not modes:
            if not enemy or 'TargetFilter' not in p: continue
            permitted={'Hint','HintSelection','GetFirstTarget','GetTargetCards','GetTargetsRelateToChain','GetMatchingGroup','GetOperatedGroup',
                       'SelectMatchingCard','GetChainInfo','GetCurrentPhase','GetCurrentChain','BreakEffect','RegisterEffect',
                       'Destroy','Remove','SendtoDeck','SendtoHand','GetControl'}
            if set(re.findall(r'Duel\.(\w+)\(',op))-permitted: continue
            if re.search(r'\b(?:s|c\d+)\.\w+\(',op): continue
            mutations=re.findall(r'Duel\.(Destroy|Remove|SendtoDeck|SendtoHand|GetControl)\(',op)
            if len(mutations)>1: continue
            # A second selection or a random/subgroup algorithm is not a single
            # independently ranked target. Keep it with the core's fallback.
            sels=list(calls(target,'Duel.SelectTarget'))+list(calls(op,'Duel.SelectMatchingCard'))
            if len(sels)>1 or ':RandomSelect(' in op or ':SelectSubGroup(' in op: continue
            for a in sels:
                if len(a)<7 or a[5:7]!=['1','1']: kind=None
            if kind is None: continue
        p['Purpose']=kind
        # Costs are separate predicates with their own locations; the selected
        # opponent target must never be reused as a cost selection.
        costs=reader.selections(cost)
        if 'Duel.DiscardHand' in cost: p['DiscardCount']=1
        elif costs and ('Duel.Remove' in cost or 'Duel.SendtoGrave' in cost):
            p['CostFilter'],p['CostLocations']=costs[0][:2]
            p['CostHint']='Remove' if 'Duel.Remove' in cost else 'ToGrave'
            if 'GetAttribute()' in cost and 'EFFECT_CANNOT_REMOVE' in op: p['AttributeCost']=True
        elif 'Duel.Release' in cost:
            a=next(calls(cost,'Duel.SelectReleaseGroup'),None)
            if a:
                pred=reader.predicate(a[1])
                # A release filter may additionally require the opponent target
                # to survive the cost. That is checked by the tactical planner.
                if pred is None:
                    body=reader.body(a[1]); expression=re.sub(r'\s+and Duel.IsExistingTarget\([^\n]+\)','',body)
                    m=re.fullmatch(r'return\s+(.+?)\s*end',expression,re.S)
                    pred=reader.expression(m[1]) if m else None
                if pred is not None: p.update(CostFilter=pred,CostLocations=4,CostHint='Release')
        if not tactical_cost(reader,e,p,cost): continue
        if p not in ps: ps.append({k:v for k,v in p.items() if v is not None and v is not False})
        if 'EFFECT_AVOID_BATTLE_DAMAGE' in op or 'EFFECT_NO_BATTLE_DAMAGE' in op or 'EFFECT_CHANGE_DAMAGE' in op:
            pass
    # Player damage protection is a state fact, independent of tactical purpose.
    for e in reader.effects:
        body=reader.body(e.get('Operation'))
        if 'EFFECT_CHANGE_DAMAGE' in body and ':SetValue(0)' in body and 'RESET_PHASE+PHASE_END' in compact(body):
            if ':SetTargetRange(1,1)' in compact(body): facts['BothDamageShield']=True
            elif ':SetTargetRange(0,1)' in compact(body): facts['OpponentDamageShield']=True
        cost=reader.body(e.get('Cost'))
        if 'EFFECT_CANNOT_SPECIAL_SUMMON' in cost and 'EFFECT_FLAG_OATH' in cost:
            for ref in re.findall(r':SetTarget\(([\w.]+)\)',cost):
                body=reader.body(ref)
                m=re.search(r'not c:IsSetCard\(([^()]+)\)',body)
                if m and 'LOCATION_EXTRA' not in body:
                    vals=[reader.number(v) for v in split_args(m[1])]
                    if all(v is not None for v in vals): facts['SummonSets']=vals
    if ps: facts['Effects']=ps
    return facts


def value(k,v):
    if k=='Limits': return 'new[] { '+', '.join('new EffectLimit { '+', '.join(key+' = '+value(key,val) for key,val in p.items())+' }' for p in v)+' }'
    if k=='Modes': return 'new[] { '+', '.join('new TacticalFact { '+', '.join(key+' = '+value(key,val) for key,val in p.items())+' }' for p in v)+' }'
    if k.endswith('Filter'): return 'c => '+v
    if k=='Purpose': return 'StoryLuckyExecutor.EffectPurpose.'+v
    if k in ('Hint','CostHint'): return 'WindBot.Game.AI.HintMsg.'+v
    if isinstance(v,bool): return str(v).lower()
    if isinstance(v,list): return 'new[] { '+', '.join(map(str,v))+' }'
    return str(v)


def generate(scripts,database,output,audit):
    facts={}; recipes={}; fusions={}
    with zipfile.ZipFile(scripts) as z, sqlite3.connect(f'file:{database.resolve().as_posix()}?mode=ro',uri=True) as db:
        constants={m[1]:int(m[2],0) for m in re.finditer(r'^(\w+)\s*=\s*(0x[\da-fA-F]+|\d+)\s*(?:--.*)?$',z.read('script/constant.lua').decode('utf-8-sig'),re.M)}
        types=dict(db.execute('select id,type from datas'))
        for name in sorted(z.namelist()):
            m=re.fullmatch(r'(?:script/)?c(\d+)\.lua',name)
            if not m: continue
            cid=int(m[1]); reader=Reader(cid,z.read(name).decode('utf-8-sig'),constants,types.get(cid,0)); f=extract(reader)
            if f: facts[cid]=f
            recipe=read_recipe(reader)
            if recipe: recipes[cid]=recipe
            fusion=read_fusion(reader)
            if fusion is not None: fusions[cid]=fusion
    lines=['// Generated by tools/build_story_tactical_facts.py; identities are extracted script facts.',
           '// script.zip SHA256: '+hashlib.sha256(scripts.read_bytes()).hexdigest(),
           'using System.Collections.Generic;','using YGOSharp.OCGWrapper.Enums;',
           'namespace MDPro3.Plugins.Features.StoryMode { internal sealed partial class StoryAiEvaluation {',
           'private static Dictionary<int, CardFacts> BuildSemanticFacts() => new Dictionary<int, CardFacts> {']
    for cid,f in sorted(facts.items()):
        fs=[k+' = '+value(k,v) for k,v in f.items() if k!='Effects']
        if f.get('Effects'): fs.append('Effects = new[] { '+', '.join('new TacticalFact { '+', '.join(k+' = '+value(k,v) for k,v in p.items())+' }' for p in f['Effects'])+' }')
        lines.append(f'[{cid}] = new CardFacts {{ '+', '.join(fs)+' },')
    lines+=['};','private static Dictionary<int, Recipe> BuildScriptRecipes() => new Dictionary<int, Recipe> {']
    for cid,r in sorted(recipes.items()): lines.append(f'[{cid}] = new Recipe {{ '+', '.join(k+' = '+value(k,v) for k,v in r.items())+' },')
    lines+=['};','private static Dictionary<int, FusionSpell> BuildFusionFacts() => new Dictionary<int, FusionSpell> {']
    for cid,r in sorted(fusions.items()): lines.append(f'[{cid}] = new FusionSpell {{ '+', '.join(k+' = '+value(k,v) for k,v in r.items())+' },')
    lines+=['};','} }']
    output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    audit.write_text(json.dumps(facts,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'{len(facts)} card capabilities / {sum(len(f.get("Effects",[])) for f in facts.values())} effect facts')


def read_recipe(reader):
    """Only complete plain Link/Synchro procedures; complex recipes use text IR."""
    for name in ('aux.AddLinkProcedure','Link.AddProcedure'):
        for a in calls(reader.script,name):
            if len(a)<3 or a[0]!='c': continue
            lo=reader.number(a[2]); hi=reader.number(a[3]) if len(a)>3 else 7
            if lo is None or hi is None: continue
            r={'Min':lo,'Max':min(7,hi)}; filt=a[1]
            if filt!='nil':
                s=compact(filt).replace('Card.IsLinkType','Card.IsType')
                if s=='aux.FilterBoolFunction(Card.IsType,TYPE_EFFECT)': r['Effect']=True
                elif s=='aux.NOT(aux.FilterBoolFunction(Card.IsType,TYPE_TOKEN))': r['NonToken']=True
                elif s=='aux.NOT(aux.FilterBoolFunction(Card.IsType,TYPE_LINK))': r['NonLink']=True
                else:
                    p=reader.predicate(filt)
                    race=re.fullmatch(r'aux.FilterBoolFunction\(Card.IsRace,([^()]+)\)',s)
                    if race and reader.number(race[1]) is not None: r['Race']=reader.number(race[1])
                    elif compact(reader.body(filt))=='returnc:IsSummonLocation(LOCATION_EXTRA)end': r['ExtraOnly']=True
                    else: continue
            if len(a)>4 and a[4]!='nil':
                check=compact(reader.body(a[4]))
                if check in ('returng:GetClassCount(Card.GetLinkCode)==g:GetCount()end','returng:GetClassCount(Card.GetCode)==g:GetCount()end'): r['DifferentNames']=True
                elif check in ('returng:IsExists(Card.IsLinkType,1,nil,TYPE_TUNER)end','returng:IsExists(Card.IsType,1,nil,TYPE_TUNER)end'): r['Tuner']=True
                else: continue
            for e in reader.effects:
                if e.get('Code')=='EFFECT_EXTRA_LINK_MATERIAL' and e.get('TargetRange')=='0,LOCATION_MZONE':
                    # A one-opponent material allowance must reject another
                    # matching opponent in the material group.
                    body=closure(reader,reader.body(e.get('Value')))
                    if re.search(r'mg:IsExists\([^\n]+1,c',body) and 'return true,false' in body: r['EnemyOne']=True
            return r
    for a in calls(reader.script,'aux.AddSynchroProcedure'):
        if len(a)<4 or a[0]!='c' or a[1]!='nil' or a[3]!='1': continue
        r={'Min':2,'Max':7}
        if a[2]=='aux.NonTuner(nil)': pass
        else:
            m=re.fullmatch(r'aux.NonTuner\(Card.IsRace,([^()]+)\)',a[2])
            if not m or reader.number(m[1]) is None: continue
            r['NonTunerRace']=reader.number(m[1])
        return r
    return None


def read_fusion(reader):
    for e in reader.effects:
        if 'CATEGORY_FUSION_SUMMON' not in e.get('Category',''): continue
        op=reader.body(e.get('Operation')); cost=reader.body(e.get('Cost')); target=reader.body(e.get('Target'))
        if not op or e.get('Condition'): continue
        p={'Once':bool(e.get('CountLimit')),'Persistent':e.get('Range')=='LOCATION_PZONE'}
        if cost:
            a=next(calls(cost,'Duel.PayLPCost'),None)
            if not a or reader.number(a[1]) is None: continue
            if re.search(r'Duel\.(?!CheckLPCost|PayLPCost)\w+\(',cost): continue
            p['Life']=reader.number(a[1])
        if ':SetMaterial(nil)' in op and 'Duel.SelectFusionMaterial' not in op:
            m=re.search(r'c:IsLevelBelow\((\d+)\)',closure(reader,target))
            if not m or 'EFFECT_CANNOT_ATTACK' not in op or 'EVENT_PHASE+PHASE_END' not in compact(op): continue
            p.update(Instant=True,MaximumLevel=int(m[1])); return p
        if 'Duel.GetFusionMaterial(tp)' not in op or 'Duel.SelectFusionMaterial' not in op: continue
        # Destination filter must consist of the standard fusion legality clause
        # plus an optional set restriction, not a hidden named material recipe.
        destination=None
        for filt,own,enemy,a in target_pool(reader,op):
            if own==64 and enemy==0 and 'CheckFusionMaterial' in reader.body(filt): destination=reader.body(filt); break
        if not destination: continue
        s=compact(destination)
        sets=re.findall(r'c:IsSetCard\(([^()]+)\)',s)
        if sets:
            if len(sets)!=1 or reader.number(sets[0]) is None: continue
            p['DestinationSet']=reader.number(sets[0]); s=s.replace('andc:IsSetCard('+sets[0]+')','')
        if s!='returnc:IsType(TYPE_FUSION)and(notforf(c))andc:IsCanBeSpecialSummoned(e,SUMMON_TYPE_FUSION,tp,false,false)andc:CheckFusionMaterial(m,nil,chkf)end': continue
        extra=[x for x in target_pool(reader,op) if x[1] in (1,16,65) and x[2]==0]
        if extra:
            if len(extra)!=1: continue
            filt,loc,_,_=extra[0]; body=reader.body(filt)
            if loc==16:
                if 'Duel.Remove' not in op or 'IsOnField' not in closure(reader,op): continue
                p['Grave']=True; p['Banish']=True
            elif loc==65:
                m=re.search(r'c:IsSetCard\(([^()]+)\)',body)
                full=closure(reader,target+op)
                if not m or reader.number(m[1]) is None or 'IsSummonLocation(LOCATION_EXTRA)' not in full: continue
                if not re.search(r'FilterCount\(Card.IsLocation,nil,LOCATION_DECK\+LOCATION_EXTRA\)<=1',compact(full)): continue
                p['ExternalSet']=reader.number(m[1]); p['ExternalOnExtraOpponent']=True
            else: continue
        if 'Duel.SetLP' in op:
            full=closure(reader,target+op)
            if 'GetSum(Card.GetAttack)' not in op or 'FilterCount(Card.IsLocation,nil,LOCATION_EXTRA)>ct' not in compact(full): continue
            m=re.search(r'returnsg:GetCount\(\)>=(\d+)',compact(full))
            if not m: continue
            p['Layered']=True; p['MinimumMaterials']=int(m[1])
        allowed={'GetFusionMaterial','GetMatchingGroup','GetChainMaterial','GetFieldGroupCount','Hint','SelectYesNo',
                 'SelectFusionMaterial','SendtoGrave','BreakEffect','SpecialSummon','SpecialSummonStep','SpecialSummonComplete',
                 'Remove','GetLP','SetLP','IsExistingMatchingCard'}
        if set(re.findall(r'Duel\.(\w+)\(',op))-allowed: continue
        if 'Duel.Remove' in op and not (p.get('Banish') or p.get('Layered')): continue
        return p
    return None


if __name__=='__main__':
    parser=argparse.ArgumentParser(__doc__)
    parser.add_argument('--scripts',type=Path,default=ROOT.parent/'MDPro3/Data/script.zip')
    parser.add_argument('--database',type=Path,default=ROOT.parent/'MDPro3/Data/locales/zh-CN/cards.cdb')
    parser.add_argument('--output',type=Path,default=ROOT/'MDPro3Plugins/Runtime/Features/StoryMode/StoryAiSemanticFacts.g.cs')
    parser.add_argument('--audit',type=Path,default=ROOT/'.selfcheck/story/tactical-facts.json')
    a=parser.parse_args()
    if any(ROOT not in p.resolve().parents for p in (a.output,a.audit)): raise ValueError('Output must remain under plugins')
    generate(a.scripts,a.database,a.output,a.audit)
