"""Freeze actual Kaito openings and separate, explicitly constructed level fixtures."""
import argparse
from collections import Counter
import json
from pathlib import Path
import random

parser = argparse.ArgumentParser()
parser.add_argument('--output-root', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
output = Path(args.output_root).resolve()
if output != root / '.selfcheck' and root / '.selfcheck' not in output.parents:
    raise ValueError('Outputs must stay inside plugins/.selfcheck')
output.mkdir(parents=True, exist_ok=True)
deck = json.loads((root / 'StoryMode/progress.json').read_text(encoding='utf-8-sig'))['opponents']['0407']['10']
rows = []
for sample in range(128):
    seed = 928000 + sample
    main = list(deck['main'])
    random.Random(seed).shuffle(main)
    rows.append('\t'.join([f'galaxy-0407-{sample}', str(seed), ','.join(map(str, main)), ','.join(map(str, deck['extra']))]))
(output / 'galaxy-level-cases-128.tsv').write_text('\n'.join(rows) + '\n', encoding='utf-8')

# These are small mid-combo fixtures, not legal 40-card opening samples or win-rate
# measurements. Use only physical copies available in the character's actual deck;
# omit other routes to test the core's real optional trigger and numeric protocol.
rows = []
available = Counter(deck['main'] + deck['extra'])
for name, partner, boss in [('eight', 93717133, 63767246), ('four', 97639441, 16643334), ('keep-five', 46659709, 58069384)]:
    for seed in [928002, 928003, 928004]:
        main = [partner, 46659709, 27204311, 14558128, 23434538, 10045474,
                24224830, 24224830, 12580478, 12580478, 18144508, 65681983, 24299458]
        extra = [85747929, boss]
        if Counter(main + extra) - available:
            raise ValueError('Fixture is not a physical subset of Kaito level 10')
        rows.append('\t'.join([f'galaxy-fixture-{name}-{seed}', str(seed), ','.join(map(str, main)),
                               ','.join(map(str, extra)), f'85747929,{partner}']))
(output / 'galaxy-level-fixtures.tsv').write_text('\n'.join(rows) + '\n', encoding='utf-8')
print('Frozen 128 actual openings and 9 separate mid-combo fixtures.')
