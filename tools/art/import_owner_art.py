"""Imports the owner's AI-generated character art (Assets/Source/owner-art-2026-11-25/*.jpg) into one game sheet.

Pipeline (deterministic, Pillow + NumPy only):
1. find the art-pixel grid of each upscaled JPEG (pitch about 8.3 px; phase found by edge alignment)
2. take the median colour of the centre of every grid cell (removes JPEG noise) → clean native pixel art
3. key out the magenta background and cut the sprites apart (connected components on the cell grid)
4. derive animation frames from the given poses (bob, lean, lunge, lying) into the game's 13 columns:
   idle0 idle1 walk0..3 work0..1 atk0..1 read0..1 dead
Output: Game/Assets/Art/owner_sheet.png (frames of FRAME×FRAME art px, bottom-centred) and a row map printed for the renderer.
Run: python tools/art/import_owner_art.py [preview.png]
"""
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Assets/Source/owner-art-2026-11-25'
OUT = ROOT / 'Game/Assets/Art/owner_sheet.png'
FRAME = 112
COLS = ['idle0', 'idle1', 'walk0', 'walk1', 'walk2', 'walk3', 'work0', 'work1', 'atk0', 'atk1', 'read0', 'read1', 'dead']


def is_bg(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (r - g > 100) & (b - g > 100) & (r > 150) & (b > 150)


def find_grid(im):
    """Pitch and phase of the art-pixel grid, from colour edges along rows and columns."""
    a = im.astype(int)
    ex = (np.abs(np.diff(a, axis=1)).sum(2) > 60).sum(0).astype(float)  # vertical edges per x
    ey = (np.abs(np.diff(a, axis=0)).sum(2) > 60).sum(1).astype(float)
    best = None
    for p in np.arange(7.6, 9.2, 0.01):
        sx = max(sum(ex[int(round(ph + k * p))] for k in range(int((len(ex) - ph) / p) - 1)) for ph in np.arange(0, p, 0.25)) if False else None
    # coarse-to-fine search on x, reuse pitch for y
    def score(e, p, ph):
        idx = np.round(ph + np.arange(0, (len(e) - ph - 1) / p) * p).astype(int) - 1
        idx = idx[(idx >= 0) & (idx < len(e))]
        return e[idx].sum() / max(1, len(idx))
    for p in np.arange(7.6, 9.2, 0.005):
        for ph in np.arange(0, p, 0.5):
            s = score(ex, p, ph)
            if best is None or s > best[0]: best = (s, p, ph)
    _, p, phx = best
    fine = max(((score(ex, p, ph), ph) for ph in np.arange(phx - 0.5, phx + 0.5, 0.05)))
    phx = fine[1]
    phy = max(((score(ey, p, ph), ph) for ph in np.arange(0, p, 0.05)))[1]
    return p, phx, phy


def to_cells(im, p, phx, phy):
    h, w = im.shape[:2]
    nx, ny = int((w - phx) / p), int((h - phy) / p)
    out = np.zeros((ny, nx, 3), np.uint8)
    q = 0.25  # sample the central half of each cell
    for j in range(ny):
        y0, y1 = int(phy + (j + q) * p), int(phy + (j + 1 - q) * p)
        for i in range(nx):
            x0, x1 = int(phx + (i + q) * p), int(phx + (i + 1 - q) * p)
            block = im[y0:max(y1, y0 + 1), x0:max(x1, x0 + 1)].reshape(-1, 3)
            out[j, i] = np.median(block, axis=0)
    return out


def components(mask, min_cells=60):
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    comps = []
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]: continue
            q, pts = deque([(y, x)]), []
            seen[y, x] = True
            while q:
                cy, cx = q.popleft()
                pts.append((cy, cx))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            q.append((ny, nx))
            if len(pts) >= min_cells: comps.append(pts)
    return comps


def cut(cells, mask, pts):
    ys, xs = zip(*pts)
    y0, y1, x0, x1 = min(ys), max(ys) + 1, min(xs), max(xs) + 1
    rgba = np.zeros((y1 - y0, x1 - x0, 4), np.uint8)
    sub = mask[y0:y1, x0:x1]
    rgba[..., :3] = cells[y0:y1, x0:x1]
    rgba[..., 3] = np.where(sub, 255, 0)
    # JPEG halo: only border cells (touching the background) that are strongly magenta-tinted become outline ink.
    # Purple or pink skin inside the sprite (demons) is far less saturated toward magenta and is left alone.
    r, g, b = rgba[..., 0].astype(int), rgba[..., 1].astype(int), rgba[..., 2].astype(int)
    solid = rgba[..., 3] > 0
    pad = np.pad(solid, 1, constant_values=False)
    border = solid & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    tinted = border & (r - g > 90) & (b - g > 90)
    rgba[tinted, :3] = (34, 22, 36)
    return Image.fromarray(rgba, 'RGBA'), x0


def sprites(name, expected):
    im = np.asarray(Image.open(SRC / f'{name}.jpg').convert('RGB'))
    p, phx, phy = find_grid(im)
    cells = to_cells(im, p, phx, phy)
    mask = ~is_bg(cells.astype(int))
    comps = components(mask)
    if len(comps) < expected:  # touching sprites (dragons): split the widest at its thinnest column
        comps.sort(key=lambda c: -(max(x for _, x in c) - min(x for _, x in c)))
        wide = comps.pop(0)
        xs = [x for _, x in wide]
        lo, hi = min(xs), max(xs)
        counts = {x: 0 for x in range(lo, hi + 1)}
        for _, x in wide: counts[x] += 1
        mid = min(range(lo + (hi - lo) // 4, hi - (hi - lo) // 4), key=lambda x: counts[x])
        comps += [[pt for pt in wide if pt[1] < mid], [pt for pt in wide if pt[1] >= mid]]
    out = sorted((cut(cells, mask, c) for c in comps), key=lambda t: t[1])
    print(f'{name}: pitch {p:.3f}, {len(out)} sprites, sizes {[s.size for s, _ in out]}')
    assert len(out) == expected, (name, len(out))
    return [s for s, _ in out]


# ---------------------------------------------------------------- frames from poses
def place(sprite, dx=0, dy=0, shear=0.0):
    """Bottom-centre a sprite in a FRAME×FRAME cell; shear leans the top by `shear` px (feet fixed)."""
    w, h = sprite.size
    if shear:
        sprite = sprite.transform((w + int(abs(shear)) * 2, h), Image.AFFINE,
                                  (1, shear / h, -abs(shear) - shear, 0, 1, 0), Image.NEAREST)
        w = sprite.size[0]
    frame = Image.new('RGBA', (FRAME, FRAME))
    frame.alpha_composite(sprite, ((FRAME - w) // 2 + dx, FRAME - 4 - h + dy))
    return frame


def lying(sprite, flip):
    body = sprite.transpose(Image.FLIP_TOP_BOTTOM) if flip else sprite.rotate(90, expand=True)
    body = body.crop(body.getbbox())
    if body.width > FRAME: body = body.resize((FRAME, max(1, body.height * FRAME // body.width)), Image.NEAREST)
    return place(body)


def person_frames(s):
    return {'idle0': place(s), 'idle1': place(s, dy=1), 'walk0': place(s, shear=2), 'walk1': place(s, dy=-2),
            'walk2': place(s, shear=-2), 'walk3': place(s, dy=-2), 'work0': place(s, dy=1, shear=3), 'work1': place(s, dy=3, shear=6),
            'atk0': place(s, dx=-2, shear=-4), 'atk1': place(s, dx=4, shear=6), 'read0': place(s, dy=2), 'read1': place(s, dy=2, shear=1),
            'dead': lying(s, False)}


def animal_frames(s, hop=False):
    up = (-6, -10) if hop else (-2, -2)
    return {'idle0': place(s), 'idle1': place(s, dy=1), 'walk0': place(s, shear=1), 'walk1': place(s, dy=up[0]),
            'walk2': place(s, shear=-1), 'walk3': place(s, dy=up[1] if hop else up[0]), 'work0': place(s, dy=2, shear=4),
            'work1': place(s, dy=3, shear=6), 'atk0': place(s, dx=-3, dy=2), 'atk1': place(s, dx=6, shear=4),
            'read0': place(s), 'read1': place(s, dy=1), 'dead': lying(s, True)}


def dragon_frames(a, b, c):
    return {'idle0': place(a), 'idle1': place(b), 'walk0': place(c), 'walk1': place(a, dy=-2), 'walk2': place(c, dx=2),
            'walk3': place(b, dy=-2), 'work0': place(a, dy=2, shear=5), 'work1': place(a, dy=3, shear=8),
            'atk0': place(b, dx=-3, shear=-5), 'atk1': place(c, dx=6, shear=8), 'read0': place(a), 'read1': place(b),
            'dead': lying(a, True)}


def main():
    humans = sprites('humans', 5)      # farmer, woman, elder, scholar, guard
    demons = sprites('demons', 4)      # villager, woman, elder, mage
    animals = sprites('animals', 4)    # sheep, deer, rabbit, wolf
    dragons = sprites('dragon', 3)
    rows = [('human-farmer', person_frames(humans[0])), ('human-woman', person_frames(humans[1])),
            ('human-elder', person_frames(humans[2])), ('human-scholar', person_frames(humans[3])),
            ('human-guard', person_frames(humans[4])),
            ('demon-villager', person_frames(demons[0])), ('demon-woman', person_frames(demons[1])),
            ('demon-elder', person_frames(demons[2])), ('demon-mage', person_frames(demons[3])),
            ('sheep', animal_frames(animals[0])), ('deer', animal_frames(animals[1])),
            ('rabbit', animal_frames(animals[2], hop=True)), ('wolf', animal_frames(animals[3])),
            ('dragon', dragon_frames(*dragons))]
    sheet = Image.new('RGBA', (FRAME * len(COLS), FRAME * len(rows)))
    for r, (_, frames) in enumerate(rows):
        for c, col in enumerate(COLS):
            sheet.alpha_composite(frames[col], (c * FRAME, r * FRAME))
    sheet.save(OUT)
    print('rows:', ', '.join(f'{i}={n}' for i, (n, _) in enumerate(rows)))
    print('native heights (art px):', {n: f[ 'idle0'].getbbox()[3] - f['idle0'].getbbox()[1] for n, f in rows})
    if len(sys.argv) > 1:
        bg = Image.new('RGBA', sheet.size, (150, 173, 99, 255))
        bg.alpha_composite(sheet)
        bg.convert('RGB').save(sys.argv[1])


if __name__ == '__main__':
    main()
