"""Generates Game/Assets/Art/dragon_48.png: a project-owned (CC0) pixel-art dragon, same columns as characters_24.png.

48x48 frames, faces right (the renderer mirrors), one row:
idle0 idle1 walk0..3 work0..1 (eat, head down) atk0 (rear up) atk1 (lunge, jaws open) read0..1 (= idle) dead.
Reuses the outline and rim-shading helpers of make_characters.py. Run: python tools/art/make_dragon.py [preview.png]
"""
import sys
from pathlib import Path

from PIL import Image

import make_characters as mc

mc.F = 48  # helpers read the module-level frame size
from make_characters import Sprite, ellipse, rect, line, outline, hx, dk, lt, INK  # noqa: E402

F = 48
SCALE, BELLY, WING, BONE, HORN, EYE = hx('3f9060'), hx('e8d49a'), hx('2f5f6a'), hx('9ac0a0'), hx('ece2c6'), hx('ffd040')


def thick(x0, y0, x1, y1, w):
    pts = set()
    for o in range(w):
        pts |= line(x0, y0 + o, x1, y1 + o)
    return pts


def dragon(col):
    walk = col.startswith('walk')
    f = int(col[-1]) if walk else 0
    b = {'idle1': 1, 'atk0': -1}.get(col, -1 if walk and f in (1, 3) else 0)
    dx = 3 if col == 'atk1' else 0
    eat = col in ('work0', 'work1')
    s = Sprite()
    cx, cy = 22 + dx, 30 + b
    # wing pose: tip height (low numbers = raised)
    tip = {'idle0': 6, 'idle1': 8, 'walk0': 4, 'walk1': 14, 'walk2': 24, 'walk3': 14, 'atk0': 2, 'atk1': 18, 'work0': 10, 'work1': 10}.get(col, 6)

    def wing(ox, colour, mode):
        root = (cx - 2 + ox, cy - 5)
        tips = [(cx - 12 + ox, tip), (cx - 18 + ox, tip + 6), (cx - 20 + ox, tip + 14)]
        poly = {(x, y) for x in range(F) for y in range(F) if _inside((x + .5, y + .5), [root, *tips, (cx - 8 + ox, cy - 4)])}
        s.add(poly, colour, mode)
        for t in tips: s.add(line(root[0], root[1], *t), BONE if mode != 'shade' else dk(BONE, .3), 'flat')

    wing(5, dk(WING, .25), 'shade')  # far wing behind the body
    # tail: thick near the body, thin at the tip, a spade at the end
    sway = 1 if col == 'idle1' else 0
    s.add(thick(cx - 9, cy + 1, cx - 15, cy + 5, 3) | thick(cx - 15, cy + 5, cx - 19, cy + 3 - sway, 2) | line(cx - 19, cy + 3 - sway, cx - 20, cy - 2 - sway), SCALE, 'rim')
    s.add({(cx - 21, cy - 3 - sway), (cx - 20, cy - 3 - sway), (cx - 19, cy - 3 - sway), (cx - 20, cy - 4 - sway), (cx - 20, cy - 5 - sway)}, dk(SCALE, .2), 'flat')
    # legs (far pair darker), walking alternates diagonal pairs
    offs = {0: (2, -2), 1: (0, 0), 2: (-2, 2), 3: (0, 0)}[f] if walk else (0, 0)
    lift = {1: (True, False), 3: (False, True)}.get(f, (False, False)) if walk else (False, False)
    for x, pair, far in [(cx - 6, 1, 1), (cx + 6, 0, 1), (cx - 8, 0, 0), (cx + 4, 1, 0)]:
        o = offs[pair] * (1 if not far else -1)
        if col == 'atk1': o += 2 if pair == 0 else -1
        bottom = 44 - (2 if lift[pair] and not far else 0)
        x0 = x + (2 if far else 0)
        leg = thick(x0, cy + 3, x0 + o, bottom - 1, 1) | thick(x0 + 1, cy + 3, x0 + 1 + o, bottom - 1, 1) | thick(x0 + 2, cy + 3, x0 + 2 + o, bottom - 3, 1)
        s.add(leg, SCALE, 'shade' if far else 'rim')
        s.add(rect(x0 + o - 1, bottom, x0 + o + 2, bottom), dk(HORN, .35 if far else .15), 'flat')
    # body, belly plates, back spikes
    body = ellipse(cx, cy, 10.5, 6.2)
    s.add(body, SCALE, 'rim')
    s.add({p for p in body if p[1] >= cy + 2 and abs(p[0] + .5 - cx) < 8.5}, BELLY, 'rim', body)
    s.add({(x, cy + 3) for x in range(cx - 6, cx + 8, 3)}, dk(BELLY, .25), 'flat')
    for x in range(cx - 8, cx + 8, 3):
        s.add({(x, cy - 7), (x + 1, cy - 7), (x, cy - 8)}, dk(SCALE, .3), 'flat')
    # neck and head
    if eat:
        head = (cx + 16, 39)
    elif col == 'atk0':
        head = (cx + 13, 8 + b)
    elif col == 'atk1':
        head = (cx + 18, 17)
    else:
        head = (cx + 14, 12 + b)
    hx_, hy_ = head
    s.add(thick(cx + 6, cy - 4, hx_ - 2, hy_ + 1, 4), SCALE, 'rim')
    s.add({(x, y) for (x, y) in thick(cx + 6, cy - 1, hx_ - 2, hy_ + 3, 1)}, BELLY, 'flat')
    skull = ellipse(hx_ + .5, hy_ + .5, 4.2, 3.4)
    snout = rect(hx_ + 3, hy_, hx_ + 7, hy_ + 2)
    s.add(skull | snout, SCALE, 'rim')
    jaw = col == 'atk1'
    if jaw:
        s.add(rect(hx_ + 2, hy_ + 4, hx_ + 7, hy_ + 5), SCALE, 'rim')
        s.add(rect(hx_ + 4, hy_ + 3, hx_ + 8, hy_ + 3), hx('ff8a2a'), 'flat')
        s.add({(hx_ + 9, hy_ + 3), (hx_ + 10, hy_ + 2), (hx_ + 10, hy_ + 4)}, hx('ffe26b'), 'flat')
        s.add({(hx_ + 5, hy_ + 3), (hx_ + 7, hy_ + 3)}, hx('fff3a6'), 'flat')
    else:
        s.add(rect(hx_ + 3, hy_ + 3, hx_ + 6, hy_ + 3), dk(BELLY, .1), 'flat')
    s.add({(hx_ + 7, hy_)}, INK, 'flat')                    # nostril
    s.add({(hx_ + 1, hy_ - 1), (hx_ + 2, hy_ - 1)}, EYE, 'flat')
    s.add(line(hx_ - 2, hy_ - 2, hx_ - 6, hy_ - 6) | {(hx_ - 7, hy_ - 7)}, HORN, 'flat')
    s.add(line(hx_, hy_ - 3, hx_ - 3, hy_ - 7), HORN, 'flat')
    wing(0, WING, 'rim')                                      # near wing over the body
    return outline(s.render())


def _inside(p, poly):
    x, y, n, ok = p[0], p[1], len(poly), False
    for i in range(n):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: ok = not ok
    return ok


def to_image(img, shadow=True):
    im = Image.new('RGBA', (F, F), (0, 0, 0, 0))
    if shadow:
        for (x, y) in ellipse(23, 45.5, 13, 1.8): im.putpixel((x, y), (0, 0, 0, 70))
    for p, c in img.items(): im.putpixel(p, c)
    return im


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else str(Path(__file__).resolve().parents[2] / 'Game/Assets/Art/dragon_48.png')
    sheet = Image.new('RGBA', (F * len(mc.COLS), F), (0, 0, 0, 0))
    for c, col in enumerate(mc.COLS):
        if col == 'dead':
            body = to_image(dragon('idle0'), False).transpose(Image.FLIP_TOP_BOTTOM)
            body = body.crop(body.getbbox())
            frame = to_image({})
            frame.alpha_composite(body, ((F - body.width) // 2, 46 - body.height))
        else:
            frame = to_image(dragon({'read0': 'idle0', 'read1': 'idle1'}.get(col, col)))
        sheet.alpha_composite(frame, (c * F, 0))
    sheet.save(out)
    if len(sys.argv) > 2:
        bg = Image.new('RGBA', sheet.size, (150, 173, 99, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(sys.argv[2])


if __name__ == '__main__':
    main()
