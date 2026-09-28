"""Compile conservative, card-independent level-effect facts from installed Lua.

Recognises a small explicit grammar, never executes Lua. Unsupported costs,
targets, branches and secondary operations reject the entire profile. Card IDs
are output identities only: no card-specific extraction or decision rules.
"""
import argparse
import ast
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def clean(text):
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    return re.sub(r'--[^\n]*', '', text).replace('\r', '')


def compact(text):
    return re.sub(r'\s+', '', text)


def functions(text):
    matches = list(re.finditer(r'^function\s+([\w.]+)\(([^\n]*)\)', text, re.M))
    return {m[1]: text[m.end():matches[i+1].start() if i+1 < len(matches) else len(text)].strip()
            for i, m in enumerate(matches)}


def integer(expression, constants):
    try:
        def visit(node):
            if isinstance(node, ast.Constant) and type(node.value) is int: return node.value
            if isinstance(node, ast.Name): return constants[node.id]
            if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.USub): return -visit(node.operand)
            if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Add, ast.Sub, ast.BitOr)):
                a, b = visit(node.left), visit(node.right)
                return a + b if isinstance(node.op, ast.Add) else a - b if isinstance(node.op, ast.Sub) else a | b
            raise ValueError()
        return visit(ast.parse(expression, mode='eval').body)
    except (SyntaxError, ValueError, KeyError): return None


def registrations(cid, funcs):
    initial = funcs.get('s.initial_effect', funcs.get(f'c{cid}.initial_effect', ''))
    # Conditional registration cannot be represented as an unconditional fact.
    initial = re.split(r'\b(?:if|for|while)\b', initial, maxsplit=1)[0]
    variables, result = {}, []
    for line in initial.splitlines():
        create = re.search(r'local\s+(\w+)\s*=\s*(?:Effect.CreateEffect\(c\)|(\w+):Clone\(\))', line)
        if create: variables[create[1]] = dict(variables.get(create[2], {})) if create[2] else {}
        setting = re.search(r'(\w+):Set(\w+)\((.*)\)\s*$', line)
        if setting and setting[1] in variables: variables[setting[1]][setting[2]] = setting[3]
        register = re.search(r'c:RegisterEffect\((\w+)\)', line)
        if register and register[1] in variables:
            e = dict(variables[register[1]])
            if re.search(r'EFFECT_TYPE_(ACTIVATE|IGNITION|QUICK_[OF]|TRIGGER_[OF])', e.get('Type', '')): result.append(e)
    return result


def card_filter(name, funcs, constants, args=''):
    result = {'min': 1, 'max': 99, 'attribute': 0, 'race': 0, 'type': 0, 'notType': 0, 'sets': [], 'notLevels': [], 'excludeIds': []}
    if name in ['nil', 'aux.TRUE', 'Card.IsFaceup']: return result
    if name in ['Card.IsLevelAbove', 'Card.IsLevelBelow', 'Card.IsLevel']:
        n = integer(args, constants)
        if n is None: return None
        if name != 'Card.IsLevelBelow': result['min'] = n
        if name != 'Card.IsLevelAbove': result['max'] = n
        return result
    body = funcs.get(name, '').strip()
    if not re.fullmatch(r'return\s+.+?\s*end', body, re.S): return None
    expression = re.sub(r'^return\s+|\s*end$', '', body, flags=re.S).strip()
    for term in re.split(r'\s+and\s+', expression):
        term = compact(term)
        if term in ['c:IsFaceup()', 'c:IsControler(tp)', 'c:IsLocation(LOCATION_MZONE)', 'c:HasLevel()']: continue
        match = re.fullmatch(r'(not)?c:Is(Attribute|Race|Type|SetCard|LevelAbove|LevelBelow|Level|Code)\(([^()]+)\)', term)
        if match:
            negate, kind, arg = match.groups()
            values = [integer(v, constants) for v in arg.split(',')]
            if any(v is None for v in values): return None
            value = values[0]
            if kind == 'SetCard' and not negate:
                if result['sets']: return None  # Two ANDed set tests are not an OR union.
                result['sets'] = values
            elif kind == 'Code' and negate: result['excludeIds'] += values
            elif kind == 'Type':
                if result['notType' if negate else 'type']: return None
                result['notType' if negate else 'type'] = value
            elif kind in ['Attribute', 'Race'] and not negate:
                if result[kind.lower()]: return None
                result[kind.lower()] = value
            elif kind == 'Level' and negate: result['notLevels'].append(value)
            elif kind.startswith('Level') and not negate:
                if kind != 'LevelBelow': result['min'] = max(result['min'], value)
                if kind != 'LevelAbove': result['max'] = min(result['max'], value)
            else: return None
            continue
        match = re.fullmatch(r'c:GetLevel\(\)(>=|<=|>|<|==|~=)(\d+)', term)
        if not match: return None
        op, value = match[1], int(match[2])
        if op in ['>', '>=', '==']: result['min'] = max(result['min'], value + (op == '>'))
        if op in ['<', '<=', '==']: result['max'] = min(result['max'], value - (op == '<'))
        if op == '~=': result['notLevels'].append(value)
    return result


def operation(body, cid):
    """Return the transform only if the complete operation is a pure level change."""
    s = compact(body)
    effect_vars = re.findall(r'local\s+(\w+)\s*=\s*(?:Effect.CreateEffect|\w+:Clone)', body)
    if not effect_vars: return None
    ev = '(?:' + '|'.join(map(re.escape, effect_vars)) + ')'
    codes = re.findall(r':SetCode\(([^()]+)\)', s)
    if len(set(codes)) != 1 or codes[0] not in ['EFFECT_CHANGE_LEVEL', 'EFFECT_UPDATE_LEVEL']: return None
    if not re.search(r':SetReset\(RESET_EVENT\+RESETS_STANDARD[^()]*\)', s): return None
    if any(t != 'EFFECT_TYPE_SINGLE' for t in re.findall(r':SetType\(([^()]+)\)', s)): return None
    calls = re.findall(r'(?<![\w.])((?:Duel|aux|Effect|table)\.[A-Za-z_]\w*)\(', s)
    allowed = {'Duel.GetFirstTarget', 'Duel.GetTargetCards', 'Duel.GetMatchingGroup', 'Duel.AnnounceNumber',
               'Duel.SelectOption', 'Duel.Hint', 'aux.Stringid', 'aux.Next', 'Effect.CreateEffect', 'table.insert', 'table.unpack'}
    if set(calls) - allowed: return None
    p = {'delta': codes[0] == 'EFFECT_UPDATE_LEVEL', 'values': [], 'numbers': [], 'options': [], 'forced': [], 'atTarget': False}
    values = re.findall(r':SetValue\(([^\n()]*)\)', s)
    # Explicit piecewise fixed levels with a number prompt (not specific to 4/8).
    piece = re.search(r'local(\w+)if(\w+):GetLevel\(\)==(\d+)then\1=(\d+)elseif\2:GetLevel\(\)==(\d+)then\1=(\d+)else\1=Duel.AnnounceNumber\(tp,([\d,]+)\)end', s)
    number = re.search(r'local(\w+)=Duel.AnnounceNumber\(tp,([\d,]+)\)', s)
    bounded = re.search(r'local(\w+)=\{\}fori=(\d+),(\d+)doif(\w+):GetLevel\(\)-i>0thentable.insert\(\1,i\)endendif#\1==0thenreturnendlocal(\w+)=Duel.AnnounceNumber\(tp,table.unpack\(\1\)\)', s)
    option = re.search(r'local(\w+)=0local(\w+)=(\d+)if(\w+):IsLevel\((\d+)\)then\1=Duel.SelectOption\(tp,aux.Stringid\((?:id|'+str(cid)+r'),(\d+)\)\)else\1=Duel.SelectOption\(tp,aux.Stringid\((?:id|'+str(cid)+r'),\6\),aux.Stringid\((?:id|'+str(cid)+r'),(\d+)\)\)endif\1==1then\2=-(\d+)end', s)
    if piece:
        if values != [piece[1]] or int(piece[3]) != int(piece[6]) or int(piece[4]) != int(piece[5]) or p['delta']: return None
        p['values'] = list(map(int, piece[7].split(','))); p['numbers'] = p['values'][:]
        p['forced'] = list(map(int, piece.group(3, 4, 5, 6))); s = s.replace(piece[0], '')
    elif bounded:
        if values != ['-'+bounded[5]] or not p['delta'] or not 1 <= int(bounded[2]) <= int(bounded[3]) <= 12: return None
        p['numbers'] = list(range(int(bounded[2]), int(bounded[3])+1)); p['values'] = [-i for i in p['numbers']]
        s = s.replace(bounded[0], '')
    elif number:
        if len(values) != 1 or values[0] not in [number[1], '-'+number[1]]: return None
        p['numbers'] = list(map(int, number[2].split(',')))
        p['values'] = [n * (-1 if values[0].startswith('-') else 1) for n in p['numbers']]
        s = s.replace(number[0], '')
    elif option:
        if values != [option[2]] or not p['delta'] or int(option[3]) != int(option[8]) or int(option[5]) != int(option[3]): return None
        p['values'] = [int(option[3]), -int(option[3])]
        p['options'] = [cid*16+int(option[6]), cid*16+int(option[7])]; s = s.replace(option[0], '')
    elif len(values) == 1 and re.fullmatch(r'-?\d+', values[0]): p['values'] = [int(values[0])]
    else: return None
    if len(p['values']) > 12 or any(abs(v) > 99 for v in p['values']): return None
    # Verify the remaining syntax, including guards, instead of accepting any
    # effect that happens to contain a SetCode/SetValue pair.
    s = re.sub(r'local\w+=e:GetHandler\(\)', '', s)
    s = re.sub(r'local\w+=Duel.GetFirstTarget\(\)', '', s)
    s = re.sub(r'local\w+=Duel.GetTargetCards\(e\)', '', s)
    s = re.sub(r'if\w+:GetCount\(\)<=0thenreturnend', '', s)
    s = re.sub(r'local\w+=Duel.GetMatchingGroup\([\w.]+,tp,LOCATION_MZONE,0,(?:nil|c)(?:,\d+)?\)', '', s)
    s = re.sub(r'local\w+=\w+:GetFirst\(\)', '', s)
    s = re.sub(r'for\w+inaux.Next\(\w+\)do|while\w+do|\w+=\w+:GetNext\(\)', '', s)
    s = re.sub(r'local\w+=(?:Effect.CreateEffect\((?:c|e:GetHandler\(\))\)|\w+:Clone\(\))', '', s)
    s = re.sub(ev + r':Set(?:Type|Code|Property|Reset|Value)\([^()]*\)', '', s)
    s = re.sub(r'(?:tc|c|e:GetHandler\(\)):RegisterEffect\('+ev+r'\)', '', s)
    # Only relation/face-up/nil guards may remain. Unknown level comparisons,
    # costs and conditional extra effects fail closed.
    subjects = set(re.findall(r'local(\w+)=(?:e:GetHandler\(\)|Duel.GetFirstTarget\(\)|\w+:GetFirst\(\))', compact(body)))
    subjects.update(re.findall(r'for(\w+)inaux.Next\(', compact(body)))
    subject = '(?:' + '|'.join(map(re.escape, sorted(subjects))) + ')'
    negative = r'(?:not' + subject + r'(?::IsRelateToEffect\(e\))?|' + subject + r':IsFacedown\(\))'
    s = re.sub(r'if' + negative + r'(?:or' + negative + r')*thenreturnend', '', s)
    guards = subject + r'(?::(?:IsRelateToEffect\(e\)|IsFaceup\(\)))?'
    def guard(m):
        expression = m[1]
        terms = re.split(r'and', expression)
        return '' if all(re.fullmatch(guards, t) for t in terms) else m[0]
    s = re.sub(r'if(.*?)then', guard, s)
    s = s.replace('end', '')
    if s: return None
    registers = re.findall(r'(\w+|e:GetHandler\(\)):RegisterEffect\(\w+\)', compact(body))
    p['selfAlso'] = 'c' in registers and 'tc' in registers
    if not registers or set(registers) - {'c', 'tc', 'e:GetHandler()'}: return None
    p['self'] = not any('Duel.'+n in body for n in ['GetFirstTarget', 'GetTargetCards', 'GetMatchingGroup'])
    return p


def target_safety(target, p, name=None, args=''):
    """Verify the complete target callback; report whether it can be predicted."""
    s = compact(target)
    if not s: return p['scope'] == 'Self'
    s = s.removeprefix('localc=e:GetHandler()')
    if p['scope'] == 'Summoned':
        # Same grammar for any filtered summon-event group, independent of ID.
        pattern = (r'localg=eg:Filter\([\w.]+,nil,tp\):Filter\(Card.IsLocation,nil,LOCATION_MZONE\)'
                   r'ifchkcthenreturnchkc:IsLocation\(LOCATION_MZONE\)andaux.IsInGroup\(chkc,g\)end'
                   r'ifchk==0thenreturnDuel.IsExistingTarget\(aux.IsInGroup,tp,LOCATION_MZONE,0,1,nil,g\)end'
                   r'localsgifg:GetCount\(\)==1thensg=g:Clone\(\)Duel.SetTargetCard\(sg\)else'
                   r'Duel.Hint\(HINT_SELECTMSG,tp,HINTMSG_TARGET\)'
                   r'sg=Duel.SelectTarget\(tp,aux.IsInGroup,tp,LOCATION_MZONE,0,1,1,nil,g\)endend')
        return False if re.fullmatch(pattern, s) else None
    if p['scope'] == 'Self':
        # Current legality is supplied by the core. Do not predict unmodelled
        # source level predicates; they are pure reads, with no target choices.
        predicate = r'(?:not)?(?:c|e:GetHandler\(\)):IsLevel(?:Above|Below)?\(\d+\)'
        return False if re.fullmatch(r'ifchk==0thenreturn' + predicate + r'(?:and' + predicate + r')*endend', s) else None
    check = re.search(r'ifchkcthenreturn(.*?)end', s)
    if check:
        allowed = {'chkc:IsLocation(LOCATION_MZONE)', 'chkc:IsControler(tp)', 'chkc~=c', name+'(chkc)'}
        if any(t not in allowed for t in check[1].split('and')): return None
        s = s.replace(check[0], '')
    exclude = 'c' if p['excludeSource'] else 'nil'
    extra = ',' + compact(args) if args else ''
    exists = 'IsExistingTarget' if p['scope'] == 'Target' else 'IsExistingMatchingCard'
    minimum = p['minimum'] if p['scope'] == 'Target' else 1
    expected = f'ifchk==0thenreturnDuel.{exists}({name},tp,LOCATION_MZONE,0,{minimum},{exclude}{extra})end'
    # Some old scripts pass a redundant tp only to SelectTarget.
    expected_plain = expected.replace(',nil,tp)', ',nil)')
    if not s.startswith(expected) and not s.startswith(expected_plain): return None
    s = s[len(expected if s.startswith(expected) else expected_plain):]
    if p['scope'] == 'Target':
        selection = f'Duel.SelectTarget(tp,{name},tp,LOCATION_MZONE,0,{p["minimum"]},{p["maximum"]},{exclude}{extra})'
        pattern = r'Duel.Hint\(HINT_SELECTMSG,tp,HINTMSG_(?:TARGET|FACEUP)\)(?:local\w+=)?' + re.escape(selection) + 'end'
        if not re.fullmatch(pattern, s): return None
    elif s != 'end': return None
    return True


def classify(cid, effect, funcs, constants, card_type):
    op = funcs.get(effect.get('Operation', ''), '')
    def related_group(match):
        helper = compact(funcs.get(match[1], ''))
        if helper in ['returnc:IsFaceup()andc:IsRelateToEffect(e)end', 'returnc:IsRelateToEffect(e)andc:IsFaceup()end']:
            return 'Duel.GetTargetCards(e)'
        return match[0]
    op = re.sub(r'Duel.GetChainInfo\(0,CHAININFO_TARGET_CARDS\):Filter\(([\w.]+),nil,e\)', related_group, op)
    p = operation(op, cid)
    if p is None: return None
    typ = effect.get('Type', '')
    if re.search(r'EFFECT_TYPE_(?:QUICK|TRIGGER)_F', typ) or not re.search(r'EFFECT_TYPE_(IGNITION|ACTIVATE|QUICK_O|TRIGGER_O)', typ): return None
    activation = 'EFFECT_TYPE_ACTIVATE' in typ
    description = re.fullmatch(r'aux.Stringid\((id|\d+),\s*(\d+)\)', effect.get('Description', ''))
    if description and description[1] not in ['id', str(cid)]: return None
    if not description and not activation: return None
    desc = cid*16+int(description[2]) if description else 0
    origin = integer(effect.get('Range', 'LOCATION_MZONE'), constants)
    if activation:
        # Persistent activation and set-turn rules are not inferred from a level operation.
        if card_type & (0x20000 | 0x40000 | 0x80000 | 0x1000000): return None
        origin = 10 if card_type & 2 else 8
    if origin not in [4, 8, 10, 16]: return None
    p.update(source=cid, description=desc, origin=origin, activation=activation,
             trigger='TRIGGER_O' in typ, genericTrigger=False, predictable=not effect.get('Condition'),
             cost='None', life=0, consume=activation, minimum=1, maximum=1, excludeSource=False,
             scope='Self' if p.pop('self') else 'Target', filter=card_filter('nil', funcs, constants))
    cost = effect.get('Cost', '')
    if cost == 'aux.bfgcost': p['cost'] = 'BanishSelf'
    elif cost:
        body = compact(funcs.get(cost, ''))
        match = re.fullmatch(r'ifchk==0thenreturnDuel.CheckLPCost\(tp,(\d+)\)endDuel.PayLPCost\(tp,\1\)end', body)
        if not match: return None
        p['cost'], p['life'] = 'Life', int(match[1])
    target = funcs.get(effect.get('Target', ''), '')
    # Unknown side effects in the target callback can be costs or summon locks.
    calls = set(re.findall(r'\bDuel\.(\w+)\(', target))
    if calls - {'IsExistingTarget', 'IsExistingMatchingCard', 'SelectTarget', 'Hint', 'SetTargetCard', 'SetOperationInfo'}: return None
    if re.search(r'RegisterEffect|SetLabel|SelectSubGroup|GetLabel', target): return None
    selections = re.findall(r'Duel.SelectTarget\(tp,([\w.]+),tp,LOCATION_MZONE,0,(\d+),(\d+),(nil|c)(?:,([^\n]*?))?\)', target)
    group = re.search(r'Duel.GetMatchingGroup\(([\w.]+),tp,LOCATION_MZONE,0,(nil|c)(?:,(\d+))?\)', compact(op))
    name, args = None, ''
    if p['scope'] != 'Self':
        if group:
            p['scope'] = 'All'; name, exclude, args = group.groups(); p['minimum'], p['maximum'] = 0, 7
        elif len(selections) == 1:
            name, low, high, exclude, args = selections[0]; p['minimum'], p['maximum'] = int(low), int(high)
            if not 1 <= p['minimum'] <= p['maximum'] <= 5: return None
        else: return None
        p['excludeSource'] = exclude == 'c'
        if name == 'aux.IsInGroup':
            # A filtered triggering summon group, with no unknown pool restrictions.
            pool = re.search(r'local\s+g=eg:Filter\(([\w.]+),nil,tp\):Filter\(Card.IsLocation,nil,LOCATION_MZONE\)', target)
            if not pool or effect.get('Code') != 'EVENT_SPSUMMON_SUCCESS': return None
            p['scope'] = 'Summoned'; name, args = pool[1], ''
        p['filter'] = card_filter(name, funcs, constants, args or '')
        if p['filter'] is None: return None
    safe = target_safety(target, p, name, args or '')
    if safe is None: return None
    p['predictable'] &= safe
    if p['trigger'] or card_type & 4 or origin == 8: p['predictable'] = False
    if p['scope']=='Summoned' and p['trigger'] and 'EFFECT_TYPE_FIELD' in typ:
        condition=compact(funcs.get(effect.get('Condition',''),''))
        p['specialArrival']=not condition or condition=='returneg:IsExists('+name+',1,nil,tp)end'
    count = effect.get('CountLimit', '')
    p['once'] = bool(count)
    p['limit'] = ''
    if count:
        if not re.fullmatch(r'1(?:,[\w+]+)?', compact(count)): return None
        if ',' in count:
            code = compact(count).split(',', 1)[1]
            if code == 'EFFECT_COUNT_CODE_CHAIN':
                if not p['trigger']: return None
                # The core supplies each chain's offer. This is not a name-wide
                # once-per-turn limit and must not disable other copies/cards.
                p['once'] = False
            else:
                value = integer(code, constants)
                if value is None or not 0 < value < 0x10000000: return None
                p['limit'] = str(value)
    return p


def read_effects(cid, text, constants, card_type=33):
    text = clean(text)
    constants = dict(constants)
    if re.search(r'local\s+s,id(?:,o)?\s*=\s*GetID\(\)', text):
        constants['id'] = cid
        if re.search(r'local\s+s,id,o\s*=\s*GetID\(\)', text): constants['o'] = 1 if cid < 100000000 else 100
    funcs = functions(text); effects = registrations(cid, funcs)
    profiles, rejected = [], []
    for effect in effects:
        op = funcs.get(effect.get('Operation', ''), '')
        if not re.search(r'EFFECT_(?:CHANGE|UPDATE)_LEVEL', op): continue
        p = classify(cid, effect, funcs, constants, card_type)
        if p is None: rejected.append(effect.get('Description', '') + ' / ' + effect.get('Operation', '')); continue
        # Generic prompts are accepted only for an unambiguous optional trigger
        # in this source zone, counting unrecognised effects as well.
        triggers = [e for e in effects if 'TRIGGER_O' in e.get('Type', '') and
                    integer(e.get('Range', 'LOCATION_MZONE'), constants) == p['origin']]
        p['genericTrigger'] = p['trigger'] and len({(e.get('Description'), e.get('Operation')) for e in triggers}) == 1
        conflicts = [e for e in effects if e.get('Description', '') == effect.get('Description', '') and
                     integer(e.get('Range', 'LOCATION_MZONE'), constants) == integer(effect.get('Range', 'LOCATION_MZONE'), constants)]
        if any(e.get('Operation') != effect.get('Operation') for e in conflicts): continue
        if p['limit']:
            # A different effect sharing this name limit may have been consumed
            # on a hypothetical route. Wait for an actual core offer in that case.
            shared = [e for e in effects if ',' in e.get('CountLimit', '') and
                      integer(e['CountLimit'].split(',', 1)[1], constants) == int(p['limit'])]
            if any(e.get('Operation') != effect.get('Operation') for e in shared): p['predictable'] = False
        if p not in profiles: profiles.append(p)
    return profiles, rejected


def generate(scripts, database, output, audit):
    with zipfile.ZipFile(scripts) as archive, sqlite3.connect(f'file:{database.resolve().as_posix()}?mode=ro', uri=True) as db:
        constants = {m[1]: int(m[2], 0) for m in re.finditer(r'^(\w+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*(?:--.*)?$', archive.read('script/constant.lua').decode('utf-8-sig'), re.M)}
        types = dict(db.execute('SELECT id,type FROM datas'))
        profiles, rejected = [], {}
        for name in sorted(archive.namelist()):
            match = re.fullmatch(r'(?:script/)?c(\d+)\.lua', name)
            if not match: continue
            cid = int(match[1]); accepted, skipped = read_effects(cid, archive.read(name).decode('utf-8-sig'), constants, types.get(cid, 0))
            profiles += accepted
            if skipped: rejected[cid] = skipped
    profiles.sort(key=lambda p: (p['source'], p['description'], p['origin']))
    lines = ['// Generated by tools/build_story_level_effects.py. Do not edit by hand.',
             '// script.zip SHA256: ' + hashlib.sha256(scripts.read_bytes()).hexdigest(),
             'using System.Collections.Generic;', 'namespace MDPro3.Plugins.Features.StoryMode', '{',
             '    internal static partial class StoryAiLevelEffects', '    {',
             '        private static List<Profile> Build() => new List<Profile>', '        {']
    for p in profiles:
        fields = [f'SourceId = {p["source"]}', f'Description = {p["description"]}', f'From = (YGOSharp.OCGWrapper.Enums.CardLocation){p["origin"]}',
                  f'Scope = Scope.{p["scope"]}', f'Cost = Cost.{p["cost"]}', f'Life = {p["life"]}', f'Minimum = {p["minimum"]}', f'Maximum = {p["maximum"]}',
                  'Limit = '+json.dumps(p['limit'])]
        for key in ['delta', 'activation', 'trigger', 'genericTrigger', 'predictable', 'consume', 'excludeSource', 'selfAlso', 'once', 'atTarget', 'specialArrival']:
            if p.get(key): fields.append(key[0].upper()+key[1:]+' = true')
        for key in ['values', 'numbers', 'options', 'forced']:
            if p[key]: fields.append(key[0].upper()+key[1:]+' = new[] { '+', '.join(map(str,p[key]))+' }')
        filt = p['filter']; fs=[]
        for key in ['min','max','attribute','race','type','notType']:
            fs.append(key[0].upper()+key[1:]+' = '+str(filt[key]))
        for key in ['sets','notLevels','excludeIds']:
            if filt[key]: fs.append(key[0].upper()+key[1:]+' = new[] { '+', '.join(map(str,filt[key]))+' }')
        fields.append('Filter = new Filter { '+', '.join(fs)+' }')
        lines.append('            new Profile { '+', '.join(fields)+' },')
    lines += ['        };', '    }', '}']
    output.write_text('\n'.join(lines)+'\n', encoding='utf-8')
    audit.write_text(json.dumps({'scripts_sha256': hashlib.sha256(scripts.read_bytes()).hexdigest(),
                                'database_sha256': hashlib.sha256(database.read_bytes()).hexdigest(),
                                'profiles': profiles, 'rejected': rejected}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'{len(profiles)} profiles / {len(set(p["source"] for p in profiles))} cards; scopes {dict(Counter(p["scope"] for p in profiles))}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--scripts', type=Path, default=ROOT.parent/'MDPro3/Data/script.zip')
    parser.add_argument('--database', type=Path, default=ROOT.parent/'MDPro3/Data/locales/zh-CN/cards.cdb')
    parser.add_argument('--output', type=Path, default=ROOT/'MDPro3Plugins/Runtime/Features/StoryMode/StoryAiLevelEffects.g.cs')
    parser.add_argument('--audit', type=Path, default=ROOT/'.selfcheck/story/level-effect-profiles.json')
    args = parser.parse_args()
    if any(ROOT not in path.resolve().parents for path in [args.output, args.audit]): raise ValueError('Outputs must stay inside plugins')
    generate(args.scripts, args.database, args.output, args.audit)
