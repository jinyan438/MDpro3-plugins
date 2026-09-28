"""Audit paired native runs; distinguish actual openings from constructed fixtures."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
plugins = Path(__file__).resolve().parents[2]
root, output = Path(args.root).resolve(), Path(args.output).resolve()
if plugins not in output.parents:
    raise ValueError('Report must stay inside plugins')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def load(label, count):
    folder = root / ('core-ai-' + label)
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    for key, file in [('cases', 'cases.tsv'), ('assembly', 'Assembly-CSharp.dll'), ('core', 'ocgcore.dll'), ('cards', 'cards.cdb')]:
        assert manifest['hashes'][key] == digest(folder / file), (label, key)
    cases = {row.split('\t')[0]: row for row in (folder / 'cases.tsv').read_text(encoding='utf-8-sig').splitlines() if row and not row.startswith('#')}
    states = {row.split('\t')[0]: row.split('\t')[1:] for row in (folder / 'endstates.tsv').read_text(encoding='utf-8-sig').splitlines()}
    results = {row.split('\t')[0]: row for row in (folder / 'results.tsv').read_text(encoding='utf-8-sig').splitlines()}
    assert len(cases) == count and cases.keys() == states.keys() == results.keys(), label
    traces, current = {}, None
    for line in (folder / 'trace.log').read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('SCENARIO '):
            assert current is None, 'incomplete previous scenario'
            current = line.split()[1]
            assert current not in traces and current in cases
            traces[current] = {'level_triggers': 0, 'number_indices': [], 'levels': [], 'endstate': states[current]}
        elif line.startswith('END '):
            assert line[4:] == results[current]
            current = None
        elif current:
            if line == 'CHAIN 0 85747929 1371966865':
                traces[current]['level_triggers'] += 1
            elif line.startswith('RESPONSE 0 AnnounceNumber '):
                traces[current]['number_indices'].append(int.from_bytes(bytes.fromhex(line.split()[-1].replace('-', '')), 'little', signed=True))
            elif line.startswith('LEVELS '):
                traces[current]['levels'].append(line[7:])
    assert current is None and traces.keys() == cases.keys(), label
    return {'manifest': manifest, 'scenarios': traces}


report = {'actual_openings_per_version': 128, 'constructed_fixtures_per_version': 9, 'native_runs': 274}
for category, name, count in [('openings', 'galaxy-level', 128), ('fixtures', 'galaxy-level-fixtures', 9)]:
    before, after = load(name + '-before', count), load(name + '-after', count)
    for key in ['cases', 'core', 'cards', 'scripts', 'harness']:
        assert before['manifest']['hashes'][key] == after['manifest']['hashes'][key], (category, key)
    assert before['manifest']['hashes']['assembly'] != after['manifest']['hashes']['assembly']
    assert before['manifest']['turns'] == after['manifest']['turns'] == 1
    assert before['manifest']['interruption'] == after['manifest']['interruption'] == 'none'
    report[category] = {'before': before, 'after': after,
        'level_triggers': [sum(s['level_triggers'] for s in run['scenarios'].values()) for run in [before, after]],
        'changed_endstate_ids': [name for name in before['scenarios'] if before['scenarios'][name]['endstate'] != after['scenarios'][name]['endstate']]}

for name, case in report['fixtures']['after']['scenarios'].items():
    if '-eight-' in name:
        assert case['number_indices'] == [1] and any('46659709:8@' in s for s in case['levels'])
        assert '63767246:' in case['endstate'][2]
    elif '-four-' in name:
        assert case['number_indices'] == [0] and any('46659709:4@' in s for s in case['levels'])
        assert '16643334:' in case['endstate'][2]
    else:
        assert case['level_triggers'] == 0 and case['number_indices'] == []
        assert case['levels'][-1].count('46659709:5@') == 2
repro = 'galaxy-0407-2'
assert report['openings']['before']['scenarios'][repro]['levels'][-1].count('46659709:5@') == 1
assert report['openings']['after']['scenarios'][repro]['levels'][-1].count('46659709:5@') == 2
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('Paired native Galaxy audit: PASS (128 actual openings + 9 constructed fixtures per version)')
print('Opening level triggers before/after:', report['openings']['level_triggers'])
print('Changed terminal state IDs:', report['openings']['changed_endstate_ids'])
