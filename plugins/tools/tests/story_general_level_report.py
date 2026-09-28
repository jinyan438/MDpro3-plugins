"""Audit paired native traces without interpreting terminal counts as win rates."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
RUNS = ROOT / '.selfcheck/story'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read(label, count):
    folder = RUNS / ('core-ai-' + label)
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    for key, name in [('cases', 'cases.tsv'), ('assembly', 'Assembly-CSharp.dll'), ('core', 'ocgcore.dll'), ('cards', 'cards.cdb')]:
        assert manifest['hashes'][key] == digest(folder / name), (label, key)
    assert manifest['hashes']['scripts'] == digest(ROOT.parent / 'MDPro3/Data/script.zip')
    assert manifest['hashes']['harness'] == digest(ROOT / 'tools/tests/StoryCoreAiTests.cs')
    cases = {line.split('\t')[0]: line for line in (folder / 'cases.tsv').read_text(encoding='utf-8-sig').splitlines()}
    results = {r[0]: r for line in (folder / 'results.tsv').read_text(encoding='utf-8-sig').splitlines() if (r := line.split('\t'))}
    states = {r[0]: r[1:] for line in (folder / 'endstates.tsv').read_text(encoding='utf-8-sig').splitlines() if (r := line.split('\t'))}
    assert len(cases) == count and cases.keys() == results.keys() == states.keys()
    traces, current = {}, None
    for line in (folder / 'trace.log').read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('SCENARIO '):
            assert current is None
            current = line.split()[1]
            assert current not in traces
            traces[current] = {'chains': [], 'numbers': [], 'options': [], 'levels': [],
                               'board': results[current][5], 'endstate': states[current], 'milliseconds': int(results[current][4])}
        elif line.startswith('END '):
            assert line[4:] == '\t'.join(results[current])
            current = None
        elif current:
            if line.startswith('CHAIN 0 '): traces[current]['chains'].append(list(map(int, line.split()[2:])))
            elif line.startswith('LEVELS '): traces[current]['levels'].append(line[7:])
            elif line.startswith(('RESPONSE 0 AnnounceNumber ', 'RESPONSE 0 SelectOption ')):
                key = 'numbers' if 'AnnounceNumber' in line else 'options'
                traces[current][key].append(int.from_bytes(bytes.fromhex(line.split()[-1].replace('-', '')), 'little', signed=True))
    assert current is None and traces.keys() == cases.keys()
    return {'manifest': manifest, 'scenarios': traces}


checks = re.search(r'Story local AI tests: PASS \((\d+) checks\)', (RUNS / 'general-level-tests/results.log').read_text(encoding='utf-8-sig'))
assert checks
report = {'baseline': 'Completed Galaxy-specific version, frozen in general-level-start',
          'baseline_source_assembly_sha256': digest(RUNS / 'general-level-start/Assembly-CSharp.story.dll'),
          'current_source_assembly_sha256': digest(RUNS / 'Assembly-CSharp.story.dll'),
          'local_ai_checks': int(checks[1]), 'anonymous_arithmetic_scenarios': 60, 'extractor_test_groups': 7,
          'actual_openings_per_version': 176, 'constructed_fixtures_per_version': 51, 'native_runs': 454,
          'profiles': json.loads((RUNS / 'level-effect-profiles.json').read_text(encoding='utf8'))}
for category, label, count in [('fixtures', 'general-level', 51), ('openings', 'general-openings', 176)]:
    before, after = read(label + '-before', count), read(label + '-after', count)
    for key in ['cases', 'core', 'cards', 'scripts', 'harness']:
        assert before['manifest']['hashes'][key] == after['manifest']['hashes'][key]
    assert before['manifest']['hashes']['assembly'] != after['manifest']['hashes']['assembly']
    assert before['manifest']['turns'] == after['manifest']['turns'] == 1
    assert before['manifest']['interruption'] == after['manifest']['interruption'] == 'none'
    changed = [k for k, a in after['scenarios'].items() if Counter(a['board'].split(' | ')) != Counter(before['scenarios'][k]['board'].split(' | '))]
    report[category] = {'before': before, 'after': after, 'changed_board_ids': changed}

expected = {'self-eight': [63767246], 'self-keep-four': [84013237], 'normal-bridge': [63767246],
            'machine-plus-two': [44508094], 'machine-keep-six': [64880894], 'grave-minus-two': [64880894],
            'two-to-eight': [63767246], 'up-to-two': [73580471, 67441435], 'all-minus-one': [64880894],
            'all-keep-eight': [44508094], 'all-to-eight': [63767246], 'option-up': [44508094],
            'option-down': [64880894], 'numeric-down': [84815190]}
for name, case in report['fixtures']['after']['scenarios'].items():
    if name.startswith('level-fixture-'):
        kind = name.removeprefix('level-fixture-').rsplit('-', 1)[0]
        ids = [int(c.split(':')[0]) for c in case['board'].split(' | ') if c]
        assert Counter(ids) == Counter(expected[kind]), name
        if kind == 'option-up': assert case['options'] == [0], name
        if kind == 'option-down': assert case['options'] == [1], name
        if kind == 'numeric-down': assert case['numbers'] == [0], name
        if kind == 'all-keep-eight': assert not any(c[0] == 74741494 for c in case['chains']), name
    elif '-eight-' in name:
        assert case['numbers'] == [1] and any('46659709:8@' in s for s in case['levels']), name
        assert '63767246:' in case['board']
    elif '-four-' in name:
        assert case['numbers'] == [0] and any('46659709:4@' in s for s in case['levels']), name
        assert '16643334:' in case['board']
    else:
        assert not any(c == [85747929, 1371966865] for c in case['chains']) and case['numbers'] == [], name
        assert case['levels'][-1].count('46659709:5@') == 2, name
assert report['openings']['after']['scenarios']['galaxy-0407-2']['levels'][-1].count('46659709:5@') == 2
for version in ['before', 'after']:
    assert report['fixtures'][version]['manifest']['hashes']['assembly'] == report['openings'][version]['manifest']['hashes']['assembly']

# Differences are reported, not automatically declared improvements.
report['limits'] = ['One-turn, unopposed native openings are not full-match win rates.',
                    '35 structurally supported pure level effects; no claim of whole-card-pool semantic coverage.',
                    'Changed terminal boards retain the existing bounded heuristic evaluation and are not proven optimal.']
output = ROOT / 'STORY-AI-GENERAL-LEVEL-REPORT.json'
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
print('Generic level paired native audit: PASS (176 actual openings + 51 fixtures per version; 454 executions)')
print('Changed opening boards:', report['openings']['changed_board_ids'])
print('Changed fixture boards:', report['fixtures']['changed_board_ids'])
