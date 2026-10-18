"""DS1b window check: compares App Designer's window with JGraph's tree open
(rt/view/shots/<App>__jgraph.png) against the same with the control made by the
app's own code (<App>__code.png). One line per app: same, or how many pixels
differ and where (the box in window pixels). For an app that differs it writes
rt/view/diff/<App>.png: JGraph's capture, the control's, and the differing
pixels in red over a faded copy, side by side."""
import pathlib

import numpy as np
from PIL import Image

here = pathlib.Path(__file__).resolve().parent
view = here / 'rt' / 'view'
shots = view / 'shots'
out = view / 'diff'
out.mkdir(exist_ok=True)
TITLE_BAR = 32
names = sorted({p.name.split('__')[0] for p in shots.glob('*__jgraph.png')})
same = differs = 0
for app in names:
    a_path, b_path = shots / f'{app}__jgraph.png', shots / f'{app}__code.png'
    if not b_path.exists():
        print(f'{app}: no control capture')
        continue
    a = np.asarray(Image.open(a_path).convert('RGB')).astype(int)
    b = np.asarray(Image.open(b_path).convert('RGB')).astype(int)
    if a.shape != b.shape:
        print(f'{app}: window sizes differ {a.shape[1]}x{a.shape[0]} and {b.shape[1]}x{b.shape[0]}')
        differs += 1
        continue
    mask = np.abs(a - b).sum(axis=2) > 24
    mask[:TITLE_BAR] = False  # the title names each file's folder
    n = int(mask.sum())
    if n == 0:
        print(f'{app}: same')
        same += 1
        continue
    differs += 1
    ys, xs = np.nonzero(mask)
    print(f'{app}: {n} pixels differ in x {xs.min()}..{xs.max()}, y {ys.min()}..{ys.max()}')
    marked = (a * 0.35 + 165).astype(np.uint8)
    marked[mask] = (255, 0, 0)
    h, w = mask.shape
    sheet = Image.new('RGB', (w * 3 + 20, h), 'white')
    sheet.paste(Image.fromarray(a.astype(np.uint8)), (0, 0))
    sheet.paste(Image.fromarray(b.astype(np.uint8)), (w + 10, 0))
    sheet.paste(Image.fromarray(marked), (2 * w + 20, 0))
    sheet.save(out / f'{app}.png')
print(f'same {same}, differs {differs}')
