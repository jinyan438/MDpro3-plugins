"""Read the user's level-10 decks without changing saves; freeze paired core inputs."""
import argparse
import json
import random
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--samples', type=int, default=12)
parser.add_argument('--start', type=int, default=0)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
output = Path(args.output).resolve()
if root / '.selfcheck' not in output.parents:
    raise ValueError('Test output must stay inside plugins/.selfcheck')
if not 1 <= args.samples <= 100:
    raise ValueError('Samples must be between 1 and 100 per deck')
if not 0 <= args.start <= 100000:
    raise ValueError('Start must be between 0 and 100000')
progress = json.loads((root / 'StoryMode/progress.json').read_text(encoding='utf-8-sig'))
rows = []
for character, mechanic in [('0201', 'synchro'), ('0508', 'xyz'), ('0101', 'fusion'), ('0806', 'link')]:
    deck = progress['opponents'][character]['10']
    for sample in range(args.start, args.start + args.samples):
        seed = 927000 + sample
        main = list(deck['main'])
        random.Random(seed).shuffle(main)
        rows.append('\t'.join([f'{mechanic}-{character}-{sample}', str(seed), ','.join(map(str, main)), ','.join(map(str, deck['extra']))]))
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('\n'.join(rows) + '\n', encoding='utf-8')
print(f'Frozen {len(rows)} openings from 4 actual level-10 decks: {output}')
