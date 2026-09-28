"""Freeze conservative effect identities from the installed Lua scripts.

This is deliberately a small structural reader, not a Lua interpreter. Only
literal, unambiguous registered effects and simple public-field target filters
are accepted. Costs never supply an operation's purpose. Unrecognised effects
remain with the runtime policy. All output stays in plugins.
"""
import argparse
import collections
import hashlib
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KINDS = {'CATEGORY_DESTROY': 'Destroy', 'CATEGORY_REMOVE': 'Banish',
         'CATEGORY_TOGRAVE': 'SendGrave', 'CATEGORY_TOHAND': 'ReturnHand',
         'CATEGORY_TODECK': 'ReturnDeck', 'CATEGORY_DISABLE': 'Disable'}
LOCATIONS = {'0': 0, 'LOCATION_MZONE': 4, 'LOCATION_SZONE': 8,
             'LOCATION_ONFIELD': 12, 'LOCATION_GRAVE': 16, 'LOCATION_REMOVED': 32}


def location(expression):
    parts = re.split(r'[+|]', expression.replace(' ', ''))
    if any(p not in LOCATIONS for p in parts):
        return None
    result = 0
    for p in parts:
        result |= LOCATIONS[p]
    return result


def functions(text):
    matches = list(re.finditer(r'^function\s+([\w.]+)\([^\n]*\)', text, re.M))
    return {m[1]: text[m.end():matches[i+1].start() if i+1 < len(matches) else len(text)]
            for i, m in enumerate(matches)}


def simple_filter(name, funcs):
    known = {'nil': 0, 'aux.TRUE': 0, 'Card.IsDestructable': 0,
             'Card.IsAbleToRemove': 0, 'Card.IsAbleToGrave': 0,
             'Card.IsAbleToHand': 0, 'Card.IsAbleToDeck': 0,
             'Card.IsFaceup': 1, 'Card.IsFacedown': 2,
             'aux.NegateEffectMonsterFilter': 1}
    if name in known:
        return known[name]
    body = funcs.get(name, '').strip()
    # Reject parameters, comparisons, branches, set codes, and any unknown
    # predicate: an enemy with the wrong attribute must not justify self-removal.
    if not re.fullmatch(r'return\s+.+?\s*end', body, re.S):
        return None
    expression = re.sub(r'^return\s+|\s*end$', '', body, flags=re.S)
    flags = 0
    for predicate in re.split(r'\s+and\s+', expression.strip()):
        predicate = predicate.strip()
        if predicate == 'c:IsFaceup()': flags |= 1
        elif predicate == 'c:IsFacedown()': flags |= 2
        elif predicate in ('c:IsDestructable()', 'c:IsAbleToRemove()', 'c:IsAbleToGrave()',
                           'c:IsAbleToHand()', 'c:IsAbleToDeck()', 'c:IsOnField()'): pass
        elif predicate == 'c:IsType(TYPE_MONSTER)': flags |= 4
        elif predicate == 'c:IsType(TYPE_SPELL+TYPE_TRAP)': flags |= 8
        else: return None
    return flags


def classify(effect, funcs):
    operation = funcs.get(effect.get('Operation', ''), '')
    target = funcs.get(effect.get('Target', ''), '')
    code = effect.get('Code', '')
    if code == 'EVENT_CHAINING' and re.search(r'Duel\.Negate(?:Activation|Effect)\(ev\)', operation):
        return ('Negate', 0, 0, 0)
    if code in ('EVENT_ATTACK_ANNOUNCE', 'EVENT_BE_BATTLE_TARGET', 'EVENT_FREE_CHAIN') and \
            'Duel.NegateAttack()' in operation:
        return ('StopAttack', 0, 0, 0)
    categories = set(re.findall(r'CATEGORY_\w+', effect.get('Category', '')))
    if len(categories) != 1 or next(iter(categories)) not in KINDS:
        return None
    kind = KINDS[next(iter(categories))]
    if 'EFFECT_FLAG_CARD_TARGET' not in effect.get('Property', ''):
        return None
    # Exactly one selection of public targets, with a fixed minimum. Multi-step
    # trades (S:P, Icarus-style pairs, self-destruction engines) require explicit
    # joint benefit planning and are never inferred from category alone.
    selections = re.findall(r'Duel\.SelectTarget\(tp,([\w.]+),tp,([^,]+),([^,]+),(\d+),[^\n]+', target)
    if len(selections) != 1 or 'Duel.SelectMatchingCard' in target:
        return None
    filter_name, own_text, enemy_text, minimum = selections[0]
    own, enemy = location(own_text), location(enemy_text)
    flags = simple_filter(filter_name, funcs)
    if own is None or enemy is None or flags is None or own & ~12 or enemy & ~12:
        return None
    if enemy == 0 or int(minimum) < 1:
        return None
    # Only pure removal operations: do not infer intent from a single category
    # when the same operation also draws, revives, searches, or grants effects.
    dangerous_extras = r'Duel\.(?:SpecialSummon|Draw|SendtoHand|SendtoDeck|SendtoGrave|Remove|Destroy|Release|ChangePosition|MoveToField|Equip)|RegisterEffect|:AddCard'
    intended = {'Destroy': 'Destroy', 'Banish': 'Remove', 'SendGrave': 'SendtoGrave',
                'ReturnHand': 'SendtoHand', 'ReturnDeck': 'SendtoDeck', 'Disable': None}[kind]
    if kind == 'Disable':
        # Generic effect-disable scripts commonly also grant attack buffs or
        # revived-body restrictions. Those need their own semantics.
        return None
    if not re.search(r'Duel\.' + intended + r'\([^\n]*REASON_EFFECT', operation):
        return None
    if len(re.findall(r'Duel\.' + intended + r'\(', operation)) != 1:
        return None
    # Do not silently ignore an indirect payoff in a helper function.
    for name in funcs:
        if re.search(r'\b' + re.escape(name) + r'\b', operation) and simple_filter(name, funcs) is None:
            return None
    remaining = re.sub(r'Duel\.' + intended + r'\(', 'EffectOperation(', operation)
    if re.search(dangerous_extras, remaining) or 'REASON_TEMPORARY' in operation:
        return None
    # Conditions may constrain the target set indirectly. Leave them unknown.
    if any(x in target for x in ('Duel.SelectYesNo', 'Duel.SelectOption', 'SetLabelObject')):
        return None
    return (kind, enemy or own, int(minimum), flags)


def read_effects(cid, text):
    # Line comments, including misleading category words, are not code.
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    text = re.sub(r'--[^\n]*', '', text)
    funcs = functions(text)
    initial = funcs.get('s.initial_effect', funcs.get(f'c{cid}.initial_effect', ''))
    variables, registered = {}, []
    for line in initial.splitlines():
        create = re.search(r'local\s+(\w+)\s*=\s*(?:Effect.CreateEffect\(c\)|(\w+):Clone\(\))', line)
        if create:
            variables[create[1]] = dict(variables.get(create[2], {})) if create[2] else {}
        setting = re.search(r'(\w+):Set(\w+)\((.*)\)\s*$', line)
        if setting and setting[1] in variables:
            variables[setting[1]][setting[2]] = setting[3]
        register = re.search(r'c:RegisterEffect\((\w+)\)', line)
        if register and register[1] in variables:
            effect = dict(variables[register[1]])
            if re.search(r'EFFECT_TYPE_(?:ACTIVATE|IGNITION|QUICK_[OF]|TRIGGER_[OF])', effect.get('Type', '')):
                registered.append(effect)
    result = []
    for effect in registered:
        description = re.fullmatch(r'aux.Stringid\((id|\d+),\s*(\d+)\)', effect.get('Description', ''))
        if not description:
            continue  # No guessed mapping of 0/-1 to a printed paragraph.
        source = cid if description[1] == 'id' else int(description[1])
        if source != cid:
            continue
        profile = classify(effect, funcs)
        if profile:
            cost = funcs.get(effect.get('Cost', ''), '')
            if re.search(r'Duel\.DiscardHand\(tp,(?:nil|aux.TRUE|Card.IsDiscardable),1,1,REASON_(?:COST\+REASON_DISCARD|DISCARD\+REASON_COST)\)', cost):
                profile = (*profile[:3], profile[3] | 32)
            result.append((source * 16 + int(description[2]), profile))
    # A shared description with different operations is ambiguous even if only
    # one of those operations happened to match the recognised subset.
    descriptions = collections.Counter(e.get('Description', '') for e in registered)
    return [(key, p) for key, p in result if descriptions.get(f'aux.Stringid({cid},{key % 16})', 0) +
            descriptions.get(f'aux.Stringid(id,{key % 16})', 0) == 1]


def generate(scripts, output):
    profiles, conflicts = {}, set()
    with zipfile.ZipFile(scripts) as archive:
        for name in sorted(archive.namelist()):
            match = re.fullmatch(r'(?:script/)?c(\d+)\.lua', name)
            if not match:
                continue
            for key, profile in read_effects(int(match[1]), archive.read(name).decode('utf-8-sig')):
                if key in profiles and profiles[key] != profile:
                    conflicts.add(key)
                profiles[key] = profile
    for key in conflicts: profiles.pop(key, None)
    groups = collections.defaultdict(list)
    for key, profile in sorted(profiles.items()): groups[profile].append(key)
    lines = ['// Generated by tools/build_story_effect_safety.py; do not edit by hand.',
             '// Installed script.zip SHA256: ' + hashlib.sha256(scripts.read_bytes()).hexdigest(),
             '// Only exact, unambiguous Lua description IDs; no card text paragraph guesses.',
             'using System.Collections.Generic;',
             'namespace MDPro3.Plugins.Features.StoryMode', '{',
             '    internal static partial class StoryAiScriptEffects', '    {',
             '        private static Dictionary<int, Profile> Build()', '        {',
             '            var result = new Dictionary<int, Profile>();']
    for (kind, zone, minimum, flags), keys in sorted(groups.items()):
        lines.append(f'            Add(result, Kind.{kind}, {zone}, {minimum}, {flags}, new[] {{')
        for i in range(0, len(keys), 8):
            lines.append('                ' + ', '.join(map(str, keys[i:i+8])) + ',')
        lines.append('            });')
    lines += ['            return result;', '        }', '    }', '}']
    output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(f'{len(profiles)} exact effect profiles: ' + str(dict(collections.Counter(p[0] for p in profiles.values()))))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--scripts', type=Path, default=ROOT.parent/'MDPro3/Data/script.zip')
    parser.add_argument('--output', type=Path, default=ROOT/'MDPro3Plugins/Runtime/Features/StoryMode/StoryAiScriptEffects.g.cs')
    args = parser.parse_args()
    if ROOT not in args.output.resolve().parents:
        raise ValueError('Output must be inside plugins')
    generate(args.scripts, args.output)
