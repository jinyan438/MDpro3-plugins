"""Compile resource transitions from Lua, without card/deck-specific rules.

The input language is a deliberately bounded subset: one selected resource,
simple boolean card predicates, and explicit summon/search/mill operations.
The output contains card identities only as script facts (including named
targets and shared count codes). Decisions and resource valuation stay in C#.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import zipfile
from build_story_level_effects import clean, compact, functions, integer

ROOT = Path(__file__).resolve().parents[1]


def split_args(text, separator=','):
    result, start, depth = [], 0, 0
    for i, ch in enumerate(text):
        if ch in '({[': depth += 1
        elif ch in ')}]': depth -= 1
        elif depth == 0 and text.startswith(separator, i):
            result.append(text[start:i].strip()); start = i + len(separator)
    result.append(text[start:].strip())
    return result


def calls(text, name):
    for m in re.finditer(re.escape(name) + r'\(', text):
        start, depth = m.end(), 1
        for i in range(start, len(text)):
            depth += (text[i] == '(') - (text[i] == ')')
            if depth == 0:
                yield split_args(text[start:i]); break


def registrations(cid, funcs):
    body = funcs.get('s.initial_effect', funcs.get(f'c{cid}.initial_effect', ''))
    variables, result = {}, []
    # A conditional registration must not become an unconditional action.
    body = re.split(r'\b(?:if|for|while)\b', body, maxsplit=1)[0]
    for line in body.splitlines():
        m = re.search(r'local\s+(\w+)\s*=\s*(?:Effect.CreateEffect\(c\)|(\w+):Clone\(\))', line)
        if m: variables[m[1]] = dict(variables.get(m[2], {})) if m[2] else {}
        m = re.search(r'(\w+):Set(\w+)\((.*)\)\s*$', line)
        if m and m[1] in variables: variables[m[1]][m[2]] = m[3]
        m = re.search(r'c:RegisterEffect\((\w+)\)', line)
        if m and m[1] in variables: result.append(dict(variables[m[1]]))
    return result


class Reader:
    def __init__(self, cid, script, constants, card_type):
        self.cid, self.script, self.card_type = cid, clean(script), card_type
        self.funcs = functions(self.script)
        self.constants = dict(constants, id=cid, o=1 if cid < 100000000 else 100)
        self.effects = registrations(cid, self.funcs)
        self.reason = ''

    def number(self, value):
        return integer(value, self.constants)

    def body(self, name):
        return self.funcs.get(name, '')

    def predicate(self, name, args='', depth=0):
        if depth > 8: return None
        name = compact(name)
        m = re.fullmatch(r'aux.NecroValleyFilter\((.+)\)', name)
        if m: return self.predicate(m[1], args, depth+1)
        m = re.fullmatch(r'aux.FilterBoolFunction(?:Ex)?\((.+)\)', name)
        if m:
            a = split_args(m[1]); return self.predicate(a[0], ','.join(a[1:]), depth+1)
        if name in ('nil', 'aux.TRUE'): return 'true'
        if name=='aux.NegateMonsterFilter': return '(Has(c, CardType.Monster) && c.IsFaceup() && !c.IsDisabled())'
        if name.startswith('Card.'):
            return self.expression('c:'+name[5:]+'('+args+')', depth+1)
        body = self.body(name).strip()
        m = re.fullmatch(r'return\s+(.+?)\s*end', body, re.S)
        if not m: return None
        return self.expression(m[1], depth+1)

    def expression(self, expr, depth=0):
        if depth > 16: return None
        expr = re.sub(r'\s+', ' ', expr).strip()
        if compact(expr)=='notc:IsFusionCode(ec:GetFusionCode())': return 'true'
        while expr.startswith('(') and len(split_args(expr[1:-1], ' or ')) >= 1:
            # Strip only parentheses enclosing the entire expression.
            dep=0; end=-1
            for i,ch in enumerate(expr):
                dep += (ch=='(')-(ch==')')
                if dep==0: end=i; break
            if end != len(expr)-1: break
            expr=expr[1:-1].strip()
        for sep, op in [(' or ', ' || '), (' and ', ' && ')]:
            parts=split_args(expr,sep)
            if len(parts)>1:
                values=[self.expression(p,depth+1) for p in parts]
                return '('+op.join(values)+')' if all(v is not None for v in values) else None
        if expr.startswith('not '):
            p=self.expression(expr[4:],depth+1)
            return '!('+p+')' if p is not None else None
        s=compact(expr)
        if s in ('true','false'): return s
        # A conditional expanded target pool is optional. Keep its always-legal
        # branch; never assume the environment is present on a future board.
        if s in ('check','thchk'): return 'false'
        # Engine legality tests are rechecked at the real prompt; our transition
        # separately verifies revival, zones and costs. Publicness is irrelevant
        # to a private Extra Deck reveal in this model.
        if re.fullmatch(r'c:(?:IsAbleTo(?:Hand|Extra|Grave(?:AsCost)?|Remove(?:AsCost)?|Deck)|IsDiscardable|IsReleasable|IsReleasableByEffect)\(\)',s): return 'true'
        if s=='c:IsPublic()': return 'false'
        if s=='c:IsFaceup()': return 'c.IsFaceup()'
        if s=='c:IsControler(tp)': return 'c.Controller == 0'
        if s in ('c:IsAttackPos()', 'c:IsPosition(POS_ATTACK)'): return 'c.IsAttack()'
        if s=='c:IsSummonType(SUMMON_TYPE_SPECIAL)': return 'c.IsSpecialSummoned'
        if s=='c:IsFacedown()': return 'c.IsFacedown()'
        if s=='c:IsOnField()': return '(c.Location == CardLocation.MonsterZone || c.Location == CardLocation.SpellZone)'
        if re.fullmatch(r'c:IsCanBeSpecialSummoned\(e,0,tp,false,false(?:,POS_FACEUP_DEFENSE)?\)',s): return 'Has(c, CardType.Monster)'
        if s in ('c:IsSummonable(true,nil)', 'c:IsSummonable(true,nil,1)'): return 'SimpleNormalBody(c)'
        if s in ('Duel.GetMZoneCount(tp,c)>0','notc:IsFusionCode(ec:GetFusionCode())'): return 'true'
        m=re.fullmatch(r'c:Is(Type|AllTypes|Race|Attribute|SetCard|Code|Location|Level|LevelAbove|LevelBelow)\((.+)\)',s)
        if m:
            kind=m[1]; values=[self.number(x) for x in split_args(m[2])]
            if any(v is None for v in values): return None
            if kind=='SetCard': return 'SetAny(c, '+', '.join(map(str,values))+')'
            if kind in ('Code','Level'): return '('+' || '.join(('CardIdentity(c)' if kind=='Code' else 'Level(c)')+' == '+str(v) for v in values)+')'
            v=values[0]
            if kind=='Type': return f'Has(c, (CardType){v})'
            if kind=='AllTypes': return f'((c.Type != 0 ? c.Type : c.Data.Type) & {v}) == {v}'
            if kind in ('Race','Attribute'): return f'({kind}(c) & {v}) != 0'
            if kind=='Location': return f'((int)c.Location & {v}) != 0'
            if kind.startswith('Level'): return f'Level(c) {">=" if kind=="LevelAbove" else "<="} {v}'
        m=re.fullmatch(r'c:Get(Level|Attack|BaseAttack)\(\)(>=|<=|>|<|==|~=)(\d+)',s)
        if m: return f'{"Level(c)" if m[1]=="Level" else "Attack(c)"} {"!=" if m[2]=="~=" else m[2]} {m[3]}'
        m=re.fullmatch(r'aux.IsCodeListed\(c,([^()]+)\)',s)
        if m and self.number(m[1]) is not None: return f'MentionsCode(c, {self.number(m[1])})'
        m=re.fullmatch(r'aux.IsCodeOrListed\(c,([^()]+)\)',s)
        if m and self.number(m[1]) is not None: return f'(CardIdentity(c) == {self.number(m[1])} || MentionsCode(c, {self.number(m[1])}))'
        m=re.fullmatch(r'([\w.]+)\(c\)',s)
        if m: return self.predicate(m[1],depth=depth+1)
        return None

    def pool_predicate(self, name, args):
        # Card.* predicates take their parameter after the exclusion argument.
        # Local filters may also receive e/tp; these are parsed by their body.
        return self.predicate(name, ','.join(args) if name.startswith('Card.') else '')

    def selections(self, body):
        result=[]
        for name in ['Duel.SelectMatchingCard','Duel.SelectTarget','Duel.GetMatchingGroup','Duel.IsExistingMatchingCard','Duel.IsExistingTarget']:
            for a in calls(body,name):
                selected='Select' in name
                if selected:
                    if len(a)<8 or a[0]!='tp' or a[2]!='tp': continue
                    filt, own, enemy, low, high=a[1],a[3],a[4],a[5],a[6]
                else:
                    if len(a)<5 or a[1] not in ('tp','c:GetControler()'): continue
                    filt,own,enemy=a[0],a[2],a[3]
                    low,high=(a[4],'1') if 'Existing' in name else ('1','1')
                if self.number(enemy)!=0: continue
                loc=self.number(own)
                skip=8 if selected else 6 if 'Existing' in name else 5
                pred=self.pool_predicate(filt,a[skip:])
                if pred is not None and loc is not None: result.append((pred,loc,filt,low,high))
        return result

    def safe_operations(self, e, p, op, target, cost):
        """Reject unaccounted writes and multi-resource operations.

        Read-only core eligibility is not permission to invent future resources.
        Unusual target callbacks with costs, summon procedures and mandatory
        secondary effects fall back to the live core instead of this model.
        """
        if re.search(r'\b(?:for|while|repeat)\b',op): return False
        if re.search(r'\b(?:s|c\d+)\.\w+\(',op): return False
        base={'Hint','HintSelection','ConfirmCards','ShuffleDeck','GetLocationCount','GetMZoneCount','GetFirstTarget','GetTargetCards',
              'GetTargetsRelateToChain','GetMatchingGroup','GetMatchingGroupCount','GetOperatedGroup',
              'IsExistingMatchingCard','IsExistingTarget','SelectMatchingCard','SelectTarget',
              'SetOperationInfo','SetTargetCard','SetTargetPlayer','SetTargetParam','GetChainInfo',
              'GetCurrentPhase','GetCurrentChain','GetTurnCount','IsPlayerCanDraw','IsPlayerCanDiscardDeck',
              'IsEnvironment','BreakEffect','ShuffleHand','SelectYesNo','SelectOption','GetFieldCard'}
        ops={'Search':{'SendtoHand'},'SearchPair':{'SendtoHand'},'Send':{'SendtoGrave'},
             'DrawDiscard':{'SendtoGrave','Draw'},'Self':{'SpecialSummon','SpecialSummonStep','SpecialSummonComplete'},
             'Recruit':{'SpecialSummon','SpecialSummonStep','SpecialSummonComplete'},'Normal':{'Summon'},'FusionName':set()}
        allowed=base|ops[p['Kind']]
        if p.get('AfterDiscard'): allowed |= {'DiscardHand','SendtoGrave'}
        if p.get('Mill'): allowed.add('DiscardDeck')
        if p.get('Life'): allowed.add('Damage')
        if p['Cost']=='ReturnField': allowed.add('SendtoHand')
        if p.get('Direct'): allowed |= {'Remove','Release'}
        if 'SetCode(' in op: allowed.add('RegisterEffect')
        # These optional alternative branches are explicitly declined by the
        # chosen plan. They are not credited as additional benefits.
        mode=False
        if p['Kind']=='Search' and 'aux.SelectFromOptions' in op+target and 'EFFECT_CHANGE_LEVEL' in op:
            allowed.add('Destroy'); mode=True
        if p['Kind']=='Self' and p.get('RevealFilter') and p.get('FollowupFilter'):
            allowed.add('ChangePosition'); mode=True
        if p['Kind']=='Send' and 'Duel.IsEnvironment' in op and 'Duel.SendtoHand' in op:
            # Only the unconditional else branch is projected. The optional
            # recovery must be explicitly declined at this chain's prompt.
            a=next(calls(op,'Duel.SelectYesNo'),None)
            desc=next(calls(a[1],'aux.Stringid'),None) if a and len(a)==2 else None
            if not desc or self.number(desc[0])!=self.cid or self.number(desc[1]) is None: return False
            if not re.search(r'else\s+Duel.Hint\(HINT_SELECTMSG,tp,HINTMSG_TOGRAVE\)',op): return False
            p['DeclineYesNo']=self.cid*16+self.number(desc[1])
            allowed.add('SendtoHand'); mode=True
        if p['Kind']=='Search' and 'Duel.MoveToField' in op and 'Duel.Recover' in op:
            choices=re.findall(r'ops\[off\]=aux.Stringid\(([^,]+),(\d+)\)\s+opval\[off\]=(\d+)',op)
            branch=re.search(r'if sel==(\d+) then',op)
            if len(choices)!=2 or not branch or not re.search(r'else\s+Duel.Hint\(HINT_SELECTMSG,tp,HINTMSG_ATOHAND\)',op): return False
            other=[a for a in choices if a[2]!=branch[1]]
            if len(other)!=1 or self.number(other[0][0])!=self.cid: return False
            p['PreferredOption']=self.cid*16+int(other[0][1])
            allowed |= {'MoveToField','SendtoGrave','RaiseEvent','Recover'}; mode=True
        if p['Kind']=='Self' and p['Cost']=='Tribute' and 'ct>=2' in compact(op) and 'ct==3' in compact(op):
            allowed |= {'Destroy','Draw'}; mode=True  # only the one-tribute branch is modelled
        if set(re.findall(r'\bDuel\.(\w+)\(',op))-allowed: return False
        # A target callback may choose targets but must not secretly spend cards.
        if set(re.findall(r'\bDuel\.(\w+)\(',target))-(base|{'GetReleaseGroup','CheckReleaseGroup','CheckReleaseGroupEx'}): return False
        cost_allowed=base|{'SendtoGrave','DiscardHand','Remove','Release','PayLPCost','CheckLPCost','GetReleaseGroup'}
        if set(re.findall(r'\bDuel\.(\w+)\(',cost))-cost_allowed: return False
        mutations=[n for n in re.findall(r'\bDuel\.(\w+)\(',cost) if n in {'SendtoGrave','DiscardHand','Remove','Release','PayLPCost'}]
        expected={'None': ['PayLPCost'] if p.get('Life') and 'PayLPCost' in cost else [],
                  'Detach': [], 'Self':['SendtoGrave'], 'SelfAndTuner':['SendtoGrave'],
                  'Discard':['DiscardHand'], 'SelfDiscard':['DiscardHand'], 'BanishSelf':[],
                  'BanishSelfDiscard':['DiscardHand','Remove'], 'Tribute':['Release'], 'SendDeck':['SendtoGrave']}
        # A Fusion-name copy sends its selected material as its sole cost.
        if p['Kind']=='FusionName': expected['None']=['SendtoGrave']
        # Activating a normal spell sends it to GY by rule, not via a cost callback.
        if p.get('ActivationOnly') and not cost and p['Cost']=='Self': expected['Self']=[]
        if not p.get('Direct') and sorted(mutations)!=sorted(expected.get(p['Cost'],[])): return False
        if p['Cost']=='BanishSelfDiscard':
            a=next(calls(cost,'Duel.Remove'),None)
            if not a or a[0] not in ('c','e:GetHandler()'): return False
            if a[0]=='c' and 'localc=e:GetHandler()' not in compact(cost): return False
        if re.search(r':SetCode\(',cost): return False
        for selection in self.selections(cost):
            if selection[3:5]!=('1','1'): return False
        for name in ('Duel.SelectReleaseGroup','Duel.SelectReleaseGroupEx'):
            for a in calls(cost,name):
                if len(a)<4 or a[2:4]!=['1','1']: return False
        if not mode:
            # Multiple alternative transitions need explicit branch semantics.
            if 'Duel.SelectOption' in op or 'aux.SelectFromOptions' in op+target: return False
            if re.search(r'\belseif\b',op): return False
            # Environment-dependent expansion of a search pool: only model the
            # base pool after proving it is contained in the expanded predicate.
            if p['Kind']=='Search' and 'Duel.IsEnvironment' in op:
                pools=self.selections(op)
                if len(pools)==2 and pools[0][1]==pools[1][1]:
                    first=compact(self.body(pools[0][2])).removeprefix('return').removesuffix('end')
                    second=compact(self.body(pools[1][2]))
                    terms=split_args(first,'and')
                    if all(term in second for term in terms):
                        op=re.sub(r'not Duel.IsEnvironment\([^()]+\)','true',op)
            for guard in re.findall(r'\bif\s+(.+?)\s+then',op,re.S):
                guard=compact(re.sub(r'\b(?:and|or|not)\b',' & ',guard))
                # Guards may check completion/relation of the transition, empty
                # selections and available zones. Arbitrary attack, flag, phase,
                # environment and opponent predicates are not silently ignored.
                for name in ('Duel.SpecialSummon','Duel.SpecialSummonStep','Duel.SendtoHand','Duel.SendtoGrave',
                             'Duel.DiscardDeck','Duel.GetLocationCount','Duel.GetMZoneCount',
                             'Duel.IsPlayerCanDraw','Duel.SelectYesNo'):
                    for a in list(calls(guard,name)):
                        guard=guard.replace(name+'('+','.join(a)+')','Q')
                guard=re.sub(r'\b(?:c|tc|g|sg|tg|oc|e:GetHandler\(\)):(?:IsRelateToEffect\(e\)|IsRelateToChain\(\)|IsFaceup\(\)|IsFacedown\(\)|GetCount\(\))','Q',guard)
                guard=re.sub(r'\b(?:tc|oc|g:GetFirst\(\)):IsLocation\(LOCATION_(?:HAND|GRAVE)\)','Q',guard)
                guard=re.sub(r'\b(?:g|tg):IsExists\(Card.IsLocation,1,nil,LOCATION_HAND(?:\+LOCATION_EXTRA)?\)','Q',guard)
                guard=guard.replace('aux.NecroValleyFilter()(c)','Q')
                if p.get('LevelFromTarget'):
                    guard=re.sub(r'\btc:GetLevel\(\)|\bc:IsLevel\(lv\)','Q',guard)
                if p.get('CopyTributeAttack'): guard=guard.replace('atk','Q')
                guard=re.sub(r'\b(?:c|tc|g|sg|tg|oc|true)\b|Q|\d+|[()#<>=~&]', '',guard)
                if guard: return False
        for name in ('Duel.SpecialSummon','Duel.SpecialSummonStep'):
            for a in calls(op,name):
                if len(a)!=7 or a[1:6]!=['0','tp','tp','false','false']: return False
        for a in calls(op,'Duel.DiscardHand'):
            if len(a)<4 or a[0]!='tp' or a[2:4]!=['1','1']: return False
        if p['Cost'] in ('Discard','SelfDiscard','BanishSelfDiscard'):
            for a in calls(cost,'Duel.DiscardHand'):
                if len(a)<4 or a[0]!='tp' or a[2:4]!=['1','1']: return False
        if p['Kind']=='DrawDiscard':
            if any(a[0:2]!=['tp','1'] for a in calls(op,'Duel.Draw')): return False
        if 'EFFECT_UPDATE_ATTACK' in op and not p.get('CopyTributeAttack'): return False
        if 'EFFECT_CHANGE_LEVEL' in op and not (p.get('Level') or p.get('LevelFromTarget')): return False
        if 'EFFECT_UPDATE_LEVEL' in op and not p.get('LevelFromTarget'): return False
        return True

    def classify(self, e):
        typ=e.get('Type',''); direct=e.get('Code')=='EFFECT_SPSUMMON_PROC'
        if not direct and not re.search(r'EFFECT_TYPE_(IGNITION|ACTIVATE|QUICK_O|TRIGGER_O)',typ): return None
        op=self.body(e.get('Operation')); target=self.body(e.get('Target')); cost=self.body(e.get('Cost')); condition=self.body(e.get('Condition'))
        if any(e.get(k) and not self.body(e[k]) for k in ('Operation','Target','Condition')): return None
        if e.get('Cost') and not cost and e['Cost']!='aux.bfgcost': return None
        cat=e.get('Category',''); activation='EFFECT_TYPE_ACTIVATE' in typ
        p={'From':self.number(e.get('Range','LOCATION_MZONE')) or 4,'Once':False}
        if p['From']==512: p['From']=8
        if activation: p['From']=10 if self.card_type&2 else 8; p['ActivationOnly']=True
        desc=e.get('Description','')
        if desc:
            a=next(calls(desc,'aux.Stringid'),None)
            if not a or self.number(a[0])!=self.cid or self.number(a[1]) is None: return None
            p['Offset']=self.number(a[1]); p['ExplicitDescription']=True
        else: p['DefaultDescription']=True
        if direct: p['Direct']=True
        count=split_args(e.get('CountLimit',''))
        if count[0]:
            if count[0]!='1': return None
            if len(count)==1: p['SoftOnce']=True
            else:
                raw=count[1]
                if 'EFFECT_COUNT_CODE_CHAIN' not in raw:
                    raw=re.sub(r'\+EFFECT_COUNT_CODE_(?:OATH|DUEL)','',compact(raw))
                    n=self.number(raw)
                    if n is None: return None
                    p['Once']=True; p['KeyCode']=n
                    p['DuelOnce']='EFFECT_COUNT_CODE_DUEL' in count[1]
        code=e.get('Code','')
        if 'TRIGGER' in typ:
            trigger_condition=compact(condition).replace('localc=e:GetHandler()','').replace('e:GetHandler()', 'c')
            if code in ('EVENT_SUMMON_SUCCESS','EVENT_SPSUMMON_SUCCESS'):
                p['SummonTrigger']=True
                p['NormalTrigger']=code=='EVENT_SUMMON_SUCCESS'; p['SpecialTrigger']=code=='EVENT_SPSUMMON_SUCCESS'
                if 'SUMMON_TYPE_' in condition:
                    if not re.fullmatch(r'returnc:IsSummonType\(SUMMON_TYPE_(?:LINK|SYNCHRO|XYZ|FUSION)\)end',trigger_condition): return None
                    p['ExtraSummonTrigger']=True
                    condition=''
            elif code=='EVENT_BE_MATERIAL' and 'REASON_SYNCHRO' in condition:
                if trigger_condition not in ('returnc:IsLocation(LOCATION_GRAVE)andr==REASON_SYNCHROend',
                        'returnc:IsReason(REASON_SYNCHRO)end','returnr==REASON_SYNCHROend'): return None
                p['MaterialTrigger']=True; p['From']=16; condition=''
            elif code=='EVENT_BE_MATERIAL' and 'REASON_FUSION' in condition:
                if trigger_condition not in ('returnc:IsLocation(LOCATION_GRAVE)andr==REASON_FUSIONandnotc:IsReason(REASON_RETURN)end',
                        'returnc:IsReason(REASON_FUSION)end','returnr==REASON_FUSIONend'): return None
                p['FusionMaterialTrigger']=True; p['From']=16; condition=''
            elif code in ('EVENT_TO_GRAVE','EVENT_MOVE') and 'REASON_EFFECT' in condition:
                if trigger_condition not in ('returnc:IsReason(REASON_EFFECT)end','returnbit.band(r,REASON_EFFECT)~=0end'): return None
                p['SentEffectTrigger']=True; p['From']=16; condition=''
            elif code=='EVENT_RELEASE': p['TributeTrigger']=True; p['From']=16
            elif code=='EVENT_MOVE' and 'LOCATION_OVERLAY' in condition and 'REASON_COST' in condition and 'TYPE_XYZ' in condition:
                if trigger_condition!='returnc:IsReason(REASON_COST)andre:IsActivated()andre:IsActiveType(TYPE_XYZ)andc:IsPreviousLocation(LOCATION_OVERLAY)end': return None
                p['DetachTrigger']=True; p['From']=16; condition=''
            else: return None
        # Source existence predicates are distinct from selected-target filters.
        if condition:
            s=compact(condition)
            selected=self.selections(condition)
            if selected and re.fullmatch(r'(?:ifc==nilthenreturntrueendlocaltp=c:GetControler\(\))?returnDuel.IsExistingMatchingCard\(.+\)end',s):
                p['ConditionFilter'],p['ConditionFrom']=selected[0][:2]; p['ConditionCount']=1
            elif 'GetMatchingGroupCount' in condition and re.search(r'==\d+\s*end$',condition):
                a=next(calls(condition,'Duel.GetMatchingGroupCount'),None)
                if not a or len(a)!=6: return None
                pred=self.predicate(a[0],a[5]); loc=self.number(a[2])
                if pred is None or loc is None: return None
                p.update(ConditionFilter=pred,ConditionFrom=loc,ConditionCount=int(re.search(r'==(\d+)\s*end$',condition)[1]),ConditionExact=True)
            elif s=='returnDuel.GetCurrentPhase()==PHASE_MAIN1orDuel.GetCurrentPhase()==PHASE_MAIN2end': pass
            elif 'GetOverlayGroup():IsExists' in s:
                m=re.search(r'IsExists\(([\w.]+),1,nil(?:,([^()]+))?\)',s)
                pred=self.predicate(m[1],m[2] or '') if m else None
                if pred is None: return None
                p['OverlayFilter']=pred
            elif re.fullmatch(r'returne:GetHandler\(\):GetFlagEffect\([^()]+\)~=0end',s) and 'EFFECT_FLAG_OATH' in self.script:
                p['ActivationTurnOnly']=True
            elif direct and ('CheckReleaseGroup' in condition or 'IsExistingMatchingCard' in condition): pass
            else: return None
        # A single supported operation family; optional modes never add their
        # mutually exclusive benefits to the selected resource transition.
        if direct or re.search(r'Duel.SpecialSummon(?:Step)?\((?:c|e:GetHandler\(\)),',compact(op)): kind='Self'
        elif 'EFFECT_ADD_FUSION_CODE' in op: kind='FusionName'
        elif 'Duel.Summon(' in op: kind='Normal'
        elif 'CATEGORY_DRAW' in cat and 'Duel.SendtoGrave' in op: kind='DrawDiscard'
        elif 'Duel.SendtoGrave' in op and 'CATEGORY_TOGRAVE' in cat and 'Duel.SendtoHand' in op and 'Duel.IsEnvironment' in op: kind='Send'
        elif 'Duel.SpecialSummon' in op and 'CATEGORY_SPECIAL_SUMMON' in cat: kind='Recruit'
        elif 'Duel.SendtoHand' in op and ('CATEGORY_TOHAND' in cat or 'CATEGORY_SEARCH' in cat): kind='Search'
        elif 'Duel.SendtoGrave' in op and 'CATEGORY_TOGRAVE' in cat: kind='Send'
        else: return None
        p['Kind']=kind
        pool=self.selections(cost if kind=='FusionName' else op)+self.selections(target)
        if kind!='Self':
            if not pool: return None
            # Prefer the predicate attached to the operation's selected group.
            pred,loc,name,low,high=pool[0]
            if low not in ('0','1') or high!='1': return None
            p.update(Filter=pred,TargetFrom=loc)
            if kind=='Normal':
                if not loc&2: return None
                p['TargetFrom']=2
        cp=self.selections(cost)
        p['Cost']='None'
        cs=compact(cost)
        if cost or e.get('Cost'):
            if e.get('Cost')=='aux.bfgcost': p['Cost']='BanishSelf'
            elif kind=='FusionName': pass  # its selected grave cost IS the transition
            elif 'RemoveOverlayCard(tp,1,1' in cs: p['Cost']='Detach'
            elif 'Duel.Release' in cost: p['Cost']='Tribute'
            elif 'Duel.ConfirmCards' in cost and cp:
                p['RevealFilter'],p['RevealFrom']=cp[0][:2]
            elif 'Duel.Remove' in cost and 'Duel.DiscardHand' in cost: p['Cost']='BanishSelfDiscard'
            elif 'Duel.DiscardHand' in cost: p['Cost']='Discard'
            elif 'Duel.SendtoGrave' in cost and re.search(r':AddCard\((?:c|e:GetHandler\(\))\)',cs) and cp: p['Cost']='SelfAndTuner'
            elif re.search(r'Duel.SendtoGrave\((?:e:GetHandler\(\)|c),',cs): p['Cost']='Self'
            elif 'Duel.SendtoGrave' in cost and cp:
                if cp[0][1]!=1: return None
                p['Cost']='SendDeck'
            elif 'Duel.PayLPCost' in cost:
                a=next(calls(cost,'Duel.PayLPCost'),None)
                if not a or self.number(a[1]) is None: return None
                p['Life']=self.number(a[1])
            else: return None
            if cp: p['CostFilter'],p['CostFrom']=cp[0][:2]
            discards=list(calls(cost,'Duel.DiscardHand'))
            if discards:
                if len(discards)!=1 or len(discards[0])<4 or discards[0][0]!='tp' or discards[0][2:4]!=['1','1']: return None
                pred=self.predicate(discards[0][1])
                if pred is None: return None
                p['CostFilter']=pred; p['CostFrom']=2
            if p['Cost']=='Tribute' and not cp:
                m=re.search(r':Filter\(([\w.]+),nil,tp\)',cost)
                pred=self.predicate(m[1]) if m else 'true' if 'Duel.GetReleaseGroup(tp)' in cost else None
                if pred is None: return None
                p['CostFilter']=pred
        if direct:
            cp=self.selections(target)+self.selections(condition)
            if 'Duel.Remove' in op: p['Cost']='BanishResource'
            elif 'Duel.Release' in op: p['Cost']='Tribute'
            elif op: return None
            if p['Cost']!='None':
                if cp: p['CostFilter'],p['CostFrom']=cp[0][:2]
                else:
                    m=re.search(r':Filter\(([\w.]+),nil,tp\)',target)
                    pred=self.predicate(m[1]) if m else None
                    if pred is None: return None
                    p['CostFilter']=pred; p['CostFrom']=4
            p['CopyTributeAttack']='GetBaseAttack' in op
        if 'Duel.SendtoHand' in op and kind in ('Self','Recruit'):
            if 'CATEGORY_TOHAND' not in cat: return None
            cp=self.selections(target)
            if not cp: return None
            p['Cost']='ReturnField'; p['CostFilter']=cp[0][0]; p['CostFrom']=cp[0][1]
            p['ReturnNeedsHand']=bool(re.search(r'\btc:IsLocation\(LOCATION_HAND\)',op))
            hint=re.search(r'Duel.Hint\(HINT_SELECTMSG,tp,(HINTMSG_\w+)\)',compact(target))
            if hint: p['CostHint']=hint[1]
            if kind=='Recruit':
                pool=self.selections(op)
                if not pool: return None
                p['Filter'],p['TargetFrom']=pool[-1][:2]
        if activation and not self.card_type & (0x20000|0x40000|0x80000):
            if p['Cost']=='None': p['Cost']='Self'
            elif p['Cost']=='Discard': p['Cost']='SelfDiscard'
        p['BanishOnLeave']='EFFECT_LEAVE_FIELD_REDIRECT' in op and 'SetValue(LOCATION_REMOVED)' in compact(op)
        p['Defense']=bool(re.search(r'Duel.SpecialSummon(?:Step)?\([^\n]*,POS_FACEUP_DEFENSE\)',op))
        p['Disabled']='EFFECT_DISABLE' in op or 'EFFECT_CANNOT_TRIGGER' in op
        p['CannotTribute']='EFFECT_UNRELEASABLE_SUM' in op or 'EFFECT_UNRELEASABLE_NONSUM' in op
        p['CannotAttack']='EFFECT_CANNOT_ATTACK' in op
        p['Temporary']='EVENT_PHASE+PHASE_END' in compact(op) and ':SetOperation(' in op
        p['Mill']='Duel.DiscardDeck(tp,1,' in compact(op)
        p['MillNeedsGrave']=p['Mill'] and ':IsLocation(LOCATION_GRAVE)' in compact(op)
        for ef,var in [('EFFECT_CHANGE_LEVEL','Level'),('EFFECT_SET_ATTACK_FINAL','Attack')]:
            m=re.search(r'(\w+):SetCode\('+ef+r'\)(?:(?!\1:SetValue).)*?\1:SetValue\((\d+)\)',compact(op))
            if m: p[var]=int(m[2])
        if kind in ('Search','Send'):
            if 'EFFECT_UPDATE_LEVEL' in op and 'GetLevel()' in op: p['LevelFromTarget']=1
            elif 'EFFECT_CHANGE_LEVEL' in op and 'GetLevel()' in op: p['LevelFromTarget']=2
        damage=next(calls(op,'Duel.Damage'),None)
        if damage:
            if damage[0]!='tp' or self.number(damage[1]) is None: return None
            p['Life']=self.number(damage[1])
        # Limits are parsed from the callback's prohibited destination predicate.
        if 'EFFECT_CANNOT_SPECIAL_SUMMON' in op:
            refs=re.findall(r':SetTarget\(([\w.]+)\)',op)
            limits=[self.body(ref) for ref in refs]
            limit=next((v for v in limits if 'LOCATION_EXTRA' in v),'')
            m=re.search(r'not c:IsType\((TYPE_SYNCHRO|TYPE_XYZ)\)',limit)
            if m:
                name='SynchroOnly' if m[1]=='TYPE_SYNCHRO' else 'XyzOnly'
                if re.search(r'\bc:RegisterEffect\(',op) and ':SetRange(LOCATION_MZONE)' in compact(op):
                    if name!='SynchroOnly': return None
                    name='BodySynchroOnly'
                p[name]=True
            else:
                m=re.search(r'not c:IsSetCard\(([^()]+)\)',limit)
                if not m or self.number(m[1]) is None: return None
                p['ExtraSetcode']=self.number(m[1])
        # Any unmodelled registration that changes a resource/limitation rejects
        # the action. Pure housekeeping and the listed projected fields are safe.
        allowed={'EFFECT_LEAVE_FIELD_REDIRECT','EFFECT_CHANGE_LEVEL','EFFECT_UPDATE_LEVEL','EFFECT_SET_ATTACK_FINAL',
                 'EFFECT_UPDATE_ATTACK','EFFECT_DISABLE','EFFECT_DISABLE_EFFECT','EFFECT_CANNOT_TRIGGER',
                 'EFFECT_CANNOT_ATTACK','EFFECT_CANNOT_SPECIAL_SUMMON','EFFECT_UNRELEASABLE_SUM','EFFECT_UNRELEASABLE_NONSUM',
                 'EFFECT_ADD_FUSION_CODE','EVENT_PHASE+PHASE_END','EVENT_PHASE+PHASE_STANDBY'}
        codes=set(re.findall(r':SetCode\(([^()]+)\)',compact(op)))
        if codes-allowed: return None
        if kind=='Search' and 'Duel.DiscardHand' in op: p['AfterDiscard']=True
        if kind=='Search' and 'Duel.SendtoGrave' in op and 'REASON_DISCARD' in op: p['AfterDiscard']=True
        if kind=='Self' and p.get('RevealFilter'):
            follow=self.selections(op)
            if follow: p['FollowupFilter'],p['FollowupFrom']=follow[0][:2]
        # Explicit pair search: distinct memberships are data, not archetype names.
        if kind=='Search' and ':SelectSubGroup(' in op:
            m=re.search(r':SelectSubGroup\(tp,([\w.]+),false,1,2\)',compact(op))
            if not m: return None
            check=self.body(m[1]); sets=re.findall(r'IsExists\(Card.IsSetCard,1,nil,([^()]+)\)',compact(check))
            vals=[self.number(s) for s in sets]
            if len(vals)<2 or any(v is None for v in vals): return None
            p['Kind']='SearchPair'; p['SearchSets']=vals
        # Omitted count limits are explicitly repeatable, rather than the C#
        # historical default of one activation per card name.
        p.setdefault('Once',False)
        if not self.safe_operations(e,p,op,target,cost): return None
        # Selected target predicates are mandatory for all non-self actions.
        return {k:v for k,v in p.items() if (v is not False or k=='Once') and v is not None}

    def read(self):
        found=[]
        for e in self.effects:
            p=self.classify(e)
            if p is None: continue
            # Normal/special copies of the same registration share one action.
            identity={k:v for k,v in p.items() if k not in ('NormalTrigger','SpecialTrigger')}
            old=next((x for x in found if {k:v for k,v in x.items() if k not in ('NormalTrigger','SpecialTrigger')}==identity),None)
            if old:
                for k in ('NormalTrigger','SpecialTrigger'):
                    if p.get(k): old[k]=True
            else: found.append(p)
        # A scale/continuous activation only places the card; its resource effect
        # is a separate action with its own availability and count limit.
        if self.card_type & 0x1000000 and (any(p.get('From')==8 for p in found) or 'Fusion' in self.script) or any(p.get('ActivationTurnOnly') for p in found):
            found.insert(0,dict(From=2,Kind='PlaceSpell',Once=False))
        for index,p in enumerate(found):
            if p.get('SoftOnce'): p['InstanceKey']=index+1
            if not any(p.get(k) for k in ('SummonTrigger','MaterialTrigger','TributeTrigger','DetachTrigger','SentEffectTrigger','FusionMaterialTrigger')): continue
            candidates=[]
            for e in self.effects:
                if 'TRIGGER_O' not in e.get('Type',''): continue
                loc=self.number(e.get('Range','LOCATION_MZONE')) or 4
                if e.get('Code') in ('EVENT_BE_MATERIAL','EVENT_TO_GRAVE','EVENT_MOVE','EVENT_RELEASE'): loc=16
                if loc&p['From']: candidates.append((e.get('Description'),e.get('Operation')))
            p['GenericTrigger']=len(set(candidates))==1
        return found


def cs_value(k,v):
    if k.endswith('Filter'): return 'c => '+v
    if k in ('From','TargetFrom','CostFrom','ConditionFrom','RevealFrom','FollowupFrom'): return '(CardLocation)'+str(v)
    if k=='Kind': return 'ComboKind.'+v
    if k=='Cost': return 'ComboCost.'+v
    if k=='CostHint': return 'WindBot.Game.AI.HintMsg.'+{'HINTMSG_SELECT':'Select','HINTMSG_RTOHAND':'ReturnToHand','HINTMSG_ATOHAND':'AddToHand','HINTMSG_TARGET':'Target'}.get(v,'ReturnToHand')
    if isinstance(v,bool): return str(v).lower()
    if isinstance(v,list): return 'new[] { '+', '.join(map(str,v))+' }'
    return str(v)


def generate(scripts,database,output,audit):
    profiles={}
    with zipfile.ZipFile(scripts) as z, sqlite3.connect(f'file:{database.resolve().as_posix()}?mode=ro',uri=True) as db:
        constants={m[1]:int(m[2],0) for m in re.finditer(r'^(\w+)\s*=\s*(0x[\da-fA-F]+|\d+)\s*(?:--.*)?$',z.read('script/constant.lua').decode('utf-8-sig'),re.M)}
        types=dict(db.execute('select id,type from datas'))
        for name in sorted(z.namelist()):
            m=re.fullmatch(r'(?:script/)?c(\d+)\.lua',name)
            if not m: continue
            cid=int(m[1])
            p=Reader(cid,z.read(name).decode('utf-8-sig'),constants,types.get(cid,0)).read()
            if p: profiles[cid]=p
    lines=['// Generated by tools/build_story_combo_effects.py; card IDs are script facts.',
           '// script.zip SHA256: '+hashlib.sha256(scripts.read_bytes()).hexdigest(),
           'using System.Collections.Generic;', 'using YGOSharp.OCGWrapper.Enums;',
           'namespace MDPro3.Plugins.Features.StoryMode { internal sealed partial class StoryAiEvaluation {',
           'private static Dictionary<int, ComboEffect[]> MakeComboEffects() => new Dictionary<int, ComboEffect[]> {']
    for cid,ps in sorted(profiles.items()):
        lines.append(f'[{cid}] = new[] {{')
        for p in ps: lines.append('new ComboEffect { '+', '.join(k+' = '+cs_value(k,v) for k,v in p.items())+' },')
        lines.append('},')
    lines+=['};', '} }']
    output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    audit.write_text(json.dumps(profiles,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(f'{sum(map(len,profiles.values()))} actions / {len(profiles)} cards')


if __name__=='__main__':
    parser=argparse.ArgumentParser(__doc__)
    parser.add_argument('--scripts',type=Path,default=ROOT.parent/'MDPro3/Data/script.zip')
    parser.add_argument('--database',type=Path,default=ROOT.parent/'MDPro3/Data/locales/zh-CN/cards.cdb')
    parser.add_argument('--output',type=Path,default=ROOT/'MDPro3Plugins/Runtime/Features/StoryMode/StoryAiComboEffects.g.cs')
    parser.add_argument('--audit',type=Path,default=ROOT/'.selfcheck/story/combo-effect-profiles.json')
    a=parser.parse_args()
    if any(ROOT not in p.resolve().parents for p in (a.output,a.audit)): raise ValueError('Output must remain under plugins')
    generate(a.scripts,a.database,a.output,a.audit)
