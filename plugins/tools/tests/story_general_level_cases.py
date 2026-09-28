"""Freeze real level-10 openings and explicitly labelled level-effect fixtures."""
import json
from pathlib import Path
import random

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.selfcheck/story'
progress = json.loads((ROOT / 'StoryMode/progress.json').read_text(encoding='utf-8-sig'))
openings = []
for character, mechanic, samples in [('0407', 'galaxy', 128), ('0201', 'synchro', 12),
                                       ('0401', 'xyz', 12), ('0512', 'fusion', 12), ('0013', 'link', 12)]:
    deck = progress['opponents'][character]['10']
    for sample in range(samples):
        seed = (928000 if character == '0407' else 927000) + sample
        main = list(deck['main']); random.Random(seed).shuffle(main)
        openings.append('\t'.join([f'{mechanic}-{character}-{sample}', str(seed), ','.join(map(str, main)), ','.join(map(str, deck['extra']))]))
(OUT / 'general-level-openings.tsv').write_text('\n'.join(openings) + '\n', encoding='utf8')

# field, grave, guaranteed initial hand, extra. Filler traps do not develop bodies.
# These are rule-mechanics fixtures, not claims about a legal competitive deck.
fixtures = [
    ('self-eight', [98555327, 89631139], [], [], [63767246]),
    ('self-keep-four', [98555327, 863795], [], [], [84013237]),
    ('normal-bridge', [89631139], [], [98555327], [63767246]),
    ('machine-plus-two', [83334932, 62327910], [], [], [44508094]),
    ('machine-keep-six', [83334932, 62327910], [], [], [64880894]),
    ('grave-minus-two', [63977008, 73081602], [50482813], [], [64880894]),
    ('two-to-eight', [29092121, 29092121], [], [], [63767246]),
    ('up-to-two', [8129306, 67441435], [], [], [73580471]),
    ('all-minus-one', [63977008, 32012841], [], [74741494], [64880894]),
    ('all-keep-eight', [63977008, 32012841], [], [74741494], [44508094]),
    ('all-to-eight', [88774734, 89631139], [], [], [63767246]),
    ('option-up', [1315120, 32012841], [11234702], [], [44508094]),
    ('option-down', [1315120, 32012841], [11234702], [], [64880894]),
    ('numeric-down', [64880894, 37675907], [60283232], [], [64880894, 37675907, 84815190]),
]
rows = []
for label, field, grave, hand, extra in fixtures:
    for seed in range(928301, 928304):
        main = hand + field + grave + [44095762] * 35
        for cid in extra:
            if cid in field: main.remove(cid)
        rows.append('\t'.join([f'level-fixture-{label}-{seed}', str(seed), ','.join(map(str, main)),
                               ','.join(map(str, extra)), ','.join(map(str, field)), ','.join(map(str, grave))]))
rows += (OUT / 'galaxy-level-fixtures.tsv').read_text(encoding='utf8').splitlines()
(OUT / 'general-level-fixtures.tsv').write_text('\n'.join(rows) + '\n', encoding='utf8')
print(f'Frozen {len(openings)} real openings and {len(rows)} constructed fixtures')
