"""Generates Game/Assets/Art/characters_24.png: project-owned pixel-art character sheet (CC0, made by this script).

Layout: 24x24 frames, columns = COLS (all sprites face right; the renderer mirrors for left), rows = ROWS.
Deterministic: run `python tools/art/make_characters.py [preview.png]` to rebuild; an optional second path gets a 4x preview.
"""
import math
import sys
from PIL import Image

F = 24
COLS = ['idle0', 'idle1', 'walk0', 'walk1', 'walk2', 'walk3', 'work0', 'work1', 'atk0', 'atk1', 'read0', 'read1', 'dead']
DARK, LIGHT, INK = (34, 20, 44), (255, 246, 226), (26, 16, 30)


def hx(h): return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)
def mix(c, d, t): return tuple(int(round(c[i] * (1 - t) + d[i] * t)) for i in range(3)) + (255,)
def dk(c, t=0.32): return mix(c, DARK, t)
def lt(c, t=0.2): return mix(c, LIGHT, t)
def lum(c): return 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]


def inside(p): return 0 <= p[0] < F and 0 <= p[1] < F
def ellipse(cx, cy, rx, ry): return {(x, y) for x in range(F) for y in range(F) if ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 <= 1}
def rect(x0, y0, x1, y1): return {(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)}


def line(x0, y0, x1, y1, w=1):
    pts, n = set(), max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(n + 1):
        x, y = round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n)
        pts.add((x, y))
        if w == 2: pts.add((x + 1, y) if abs(y1 - y0) >= abs(x1 - x0) else (x, y + 1))
    return pts


class Sprite:
    def __init__(self): self.layers = []

    def add(self, pix, color, mode='rim', shape=None):
        pix = {p for p in pix if inside(p)}
        self.layers.append((pix, color, mode, shape or pix))

    def render(self):
        img = {}
        for pix, c, mode, shape in self.layers:
            for (x, y) in pix:
                col = c
                if mode == 'rim':
                    if (x + 1, y) not in shape or (x, y + 1) not in shape: col = dk(c, 0.28)
                    elif (x - 1, y) not in shape or (x, y - 1) not in shape: col = lt(c, 0.16)
                elif mode == 'shade': col = dk(c, 0.3)
                img[(x, y)] = col
        return img


def outline(img):
    out = dict(img)
    for (x, y), c in img.items():
        for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if n in img or not inside(n): continue
            cand = mix(c, INK, 0.72)
            if n not in out or lum(cand) < lum(out[n]): out[n] = cand
    return out


def to_image(img, shadow=(7, 16)):
    im = Image.new('RGBA', (F, F), (0, 0, 0, 0))
    if shadow:
        for (x, y) in ellipse((shadow[0] + shadow[1] + 1) / 2, 22, (shadow[1] - shadow[0] + 1) / 2, 1.3):
            im.putpixel((x, y), (0, 0, 0, 70))
    for p, c in img.items(): im.putpixel(p, c)
    return im


def lying(im, flip_vertical):
    """Dead pose: rotate (humanoids) or flip legs-up (animals), bottom-aligned with a shadow."""
    body = im.transpose(Image.FLIP_TOP_BOTTOM) if flip_vertical else im.rotate(90, expand=True)
    box = body.getbbox()
    body = body.crop(box)
    out = to_image({}, shadow=(5, 18))
    out.alpha_composite(body, ((F - body.width) // 2, 22 - body.height + (1 if flip_vertical else 0)))
    return out


# ---------------------------------------------------------------- humanoids
HUMAN_SKIN = ['f2c9a0', 'e0a878', 'b8805a', '8a5a3a']
HUMAN_HAIR = ['5a3a22', '2a2026', 'd0a050', 'a04a28', 'e8e0d0']
HUMAN_TUNIC = ['3f6fb8', '4f8f4a', 'b04a3a', '8a6ab0', 'c08a30', '3f8a8a']
HUMAN_PANTS = ['5a4a3a', '3a3a4a', '6a5a40']
DEMON_SKIN = ['d0505a', 'b04070', '9a4a8a', 'c86040']
DEMON_TUNIC = ['3a2030', '2a2a3a', '5a2030', '40304a']


def human_palette(i):
    return dict(skin=hx(HUMAN_SKIN[i % 4]), hair=hx(HUMAN_HAIR[(i * 3 + i // 4) % 5]), tunic=hx(HUMAN_TUNIC[(i * 5) % 6]),
                pants=hx(HUMAN_PANTS[i % 3]), style=['short', 'long', 'hat', 'short', 'long', 'short', 'hat', 'long'][i], demon=False)


def demon_palette(i):
    return dict(skin=hx(DEMON_SKIN[i]), hair=hx(['2a1420', '1a1a2a', '3a1a1a', '201a30'][i]), tunic=hx(DEMON_TUNIC[i]),
                pants=hx('2a2026'), style='short', demon=True)


STAND = dict(b=0, feet=[(10, 21), (12, 21)], lift=[False, False], hands=[(10, 16), (13, 16)])
POSES = {
    'idle0': STAND,
    'idle1': dict(STAND, b=1, hands=[(10, 17), (13, 17)]),
    'walk0': dict(b=0, feet=[(8, 21), (15, 21)], lift=[False, False], hands=[(14, 16), (10, 16)]),
    'walk1': dict(b=-1, feet=[(11, 20), (12, 21)], lift=[True, False], hands=[(11, 15), (12, 15)]),
    'walk2': dict(b=0, feet=[(15, 21), (8, 21)], lift=[False, False], hands=[(9, 16), (15, 16)]),
    'walk3': dict(b=-1, feet=[(11, 21), (12, 20)], lift=[False, True], hands=[(11, 15), (12, 15)]),
    'work0': dict(b=0, feet=[(9, 21), (14, 21)], lift=[False, False], hands=[(13, 9), (14, 9)], tool=('hoe', (13, 9), (8, 2))),
    'work1': dict(b=1, feet=[(9, 21), (14, 21)], lift=[False, False], hands=[(15, 15), (16, 15)], tool=('hoe', (16, 15), (21, 20))),
    'atk0': dict(b=0, feet=[(8, 21), (15, 21)], lift=[False, False], hands=[(12, 10), (13, 10)], tool=('sword', (12, 9), (8, 3))),
    'atk1': dict(b=0, feet=[(8, 21), (15, 21)], lift=[False, False], hands=[(16, 13), (17, 13)], tool=('sword', (18, 13), (22, 13))),
    'read0': dict(b=3, sit=True, hands=[(14, 17), (15, 17)], page=False),
    'read1': dict(b=3, sit=True, hands=[(14, 17), (15, 17)], page=True),
}


def humanoid(pal, pose):
    s, b = Sprite(), pose['b']
    skin, hair, tunic, pants = pal['skin'], pal['hair'], pal['tunic'], pal['pants']
    sleeve = tunic if not pal['demon'] else dk(tunic, 0.1)
    if pal['demon']:  # tail behind everything
        s.add(line(8, 17 + b, 5, 19) | line(5, 19, 4, 16) | {(3, 15), (4, 15), (5, 15), (4, 14)}, dk(skin, 0.15), 'flat')
    sit = pose.get('sit', False)
    # legs (back leg first, darker)
    if sit:
        s.add(line(11, 20, 15, 20, 2) | rect(16, 18, 16, 20), pants, 'shade')
        s.add(line(12, 21, 16, 21) | rect(17, 18, 17, 21), pants, 'flat')
        s.add({(17, 18), (17, 19)}, hx('4a3020'), 'flat')
    else:
        for i, (hip, (fx, fy)) in enumerate(zip([(10, 17 + b), (12, 17 + b)], pose['feet'])):
            leg = line(hip[0], hip[1], fx, fy - 1, 2)
            s.add(leg, pants, 'shade' if i == 0 else 'flat')
            s.add(rect(fx, fy, fx + 2, fy), dk(hx('5a3a26'), 0.25 if i == 0 else 0), 'flat')
    # back arm
    shoulder = [(10, 13 + b), (12, 13 + b)]
    def arm(i, mode):
        pts = sorted(line(shoulder[i][0], shoulder[i][1], *pose['hands'][i], 2), key=lambda p: (p[0] - shoulder[i][0]) ** 2 + (p[1] - shoulder[i][1]) ** 2)
        s.add(set(pts[:-2]), sleeve, mode)
        s.add(set(pts[-2:]), skin, mode)
    arm(0, 'shade')
    # torso: tunic, skirt, belt
    torso = rect(9, 12 + b, 14, 17 + b) | rect(9, 18 + b, 15, 18 + b) if not sit else rect(9, 12 + b, 14, 17 + b)
    s.add(torso, tunic, 'rim')
    s.add(rect(9, 16 + b, 14, 16 + b), hx('6a4428'), 'flat')
    s.add({(13, 16 + b)}, hx('e8c050'), 'flat')
    # head
    hy = 7.5 + b
    head = ellipse(12.5, hy, 4.6, 4.6)
    face = {p for p in head if p[0] >= 12 and p[1] >= hy - 1}
    if pal['demon']: face = {p for p in head if p[0] >= 11 and p[1] >= hy - 2}
    s.add(face, skin, 'rim', head)
    hairpix = head - face
    if pal['style'] == 'long': hairpix |= rect(8, int(hy), 10, int(hy) + 5)
    s.add(hairpix, hair, 'rim', head | hairpix)
    look = 1 if sit else 0
    eye = hx('ffd040') if pal['demon'] else INK
    s.add({(15, int(hy) + 1 + look), (15, int(hy) + 2 + look)}, eye, 'flat')
    s.add({(12, int(hy) + 2)}, dk(skin, 0.25), 'flat')  # ear
    if pal['style'] == 'hat':
        s.add(rect(7, int(hy) - 4, 17, int(hy) - 4), hx('d8b860'), 'flat')
        s.add(ellipse(12.5, hy - 5.5, 3.6, 2.2) - rect(0, int(hy) - 4, F, F), hx('e0c070'), 'rim')
        s.add(rect(9, int(hy) - 5, 15, int(hy) - 5), hx('a05030'), 'flat')
    if pal['demon']:
        bone = hx('e8dcc0')
        s.add({(10, 3 + b), (9, 2 + b), (9, 1 + b)}, bone, 'flat')
        s.add({(14, 3 + b), (15, 2 + b), (16, 1 + b)}, bone, 'flat')
        s.add({(9, 1 + b), (16, 1 + b)}, dk(bone, 0.35), 'flat')
    # tools and book
    tool = pose.get('tool')
    if tool:
        kind, (x0, y0), (x1, y1) = tool
        if kind == 'hoe':
            s.add(line(x0, y0, x1, y1), hx('a0703c'), 'flat')
            s.add(rect(x1 - 1, y1 - 1, x1 + 1, y1) if y1 > y0 else rect(x1 - 1, y1, x1, y1 + 1), hx('b8c0c8'), 'rim')
        else:
            blade = hx('dfe6ee') if not pal['demon'] else hx('b090c0')
            s.add(line(x0, y0, x1, y1), blade, 'flat')
            s.add({(x0, y0)}, hx('e8c050'), 'flat')
            guard = {(x0, y0 - 1), (x0, y0 + 1)} if y0 == y1 else {(x0 - 1, y0), (x0 + 1, y0)}
            s.add(guard, hx('c09030'), 'flat')
    if sit:
        cover = hx('7a3a2a') if not pal['demon'] else hx('3a2a5a')
        s.add(rect(15, 14 + b - 3 + 3, 19, 17 + b - 3 + 3) - set(), cover, 'flat')
        pages = rect(16, 15, 18, 16)
        s.add(pages, hx('f4ecd8'), 'flat')
        s.add({(17, 15), (17, 16)}, hx('c8b898'), 'flat')
        if pose['page']: s.add({(18, 14), (19, 13)}, hx('fffaf0'), 'flat')
    arm(1, 'flat')
    return outline(s.render())


# ---------------------------------------------------------------- animals
def quadruped(kind, col):
    walk = col.startswith('walk')
    f = int(col[-1]) if walk else 0
    b = (-1 if f in (1, 3) else 0) if walk and kind in ('deer', 'wolf') else 0
    if col == 'idle1': b = 0
    dx = 2 if (kind == 'wolf' and col == 'atk1') else 0
    if kind == 'wolf' and col == 'atk0': b = 1
    eat = col in ('work0', 'work1')
    s = Sprite()
    if kind == 'rabbit':
        hop = {'walk0': 0, 'walk1': -2, 'walk2': -3, 'walk3': -1}.get(col, 0)
        b = hop
        fur, belly = hx('b89878'), hx('ece0cc')
        body = ellipse(11, 18 + b, 3.4, 2.5)
        hxp, hyp = (15.5, 18) if eat else (15, 16 + b)
        s.add({(7, 17 + b), (8, 17 + b), (7, 18 + b)}, hx('f8f4ec'), 'flat')
        legs = rect(9, 20 + b, 12, 20 + b) if hop else rect(9, 21, 12, 21)
        s.add(legs, dk(fur, 0.15), 'flat')
        s.add(body, fur, 'rim')
        s.add({p for p in body if p[1] >= 19 + b and p[0] >= 11}, belly, 'flat')
        head = ellipse(hxp, hyp, 2.2, 2.0)
        s.add(head, fur, 'rim')
        ey = int(hyp)
        ears = rect(int(hxp) - 1, ey - 5, int(hxp) - 1, ey - 2) | rect(int(hxp), ey - 5 + (1 if col == 'idle1' else 0), int(hxp), ey - 2)
        s.add(ears, fur, 'flat')
        s.add({(int(hxp), ey - 4 + (1 if col == 'idle1' else 0))}, hx('e8a0a0'), 'flat')
        s.add({(int(hxp) + 1, ey - 1 + 1)}, INK, 'flat')
        s.add({(int(hxp) + 2, ey + 1)}, hx('d07070'), 'flat')
        s.add(rect(14, 21, 15, 21) if not hop else rect(14, 20 + b, 15, 20 + b), dk(fur, 0.1), 'flat')
        return outline(s.render())

    spec = {
        'sheep': dict(body=(11, 15, 5.6, 3.6), coat=hx('f0ece0'), legc=hx('3a3030'), legtop=17, head=(17.5, 12.5), eathead=(18.5, 17), headr=(2.2, 2.4), face=hx('5a4440')),
        'deer': dict(body=(11, 13, 5.2, 2.8), coat=hx('b07840'), legc=hx('8a5a30'), legtop=15, head=(18.5, 5.5), eathead=(19, 16.5), headr=(2.2, 1.7), face=hx('b07840')),
        'wolf': dict(body=(11, 15, 5.6, 2.6), coat=hx('7c7c88'), legc=hx('6a6a76'), legtop=16, head=(17.5, 12.5), eathead=(18.5, 16), headr=(2.6, 2.2), face=hx('7c7c88')),
    }[kind]
    cx, cy, rx, ry = spec['body']
    cx += dx
    cy += b
    coat = spec['coat']
    # legs: (x, far?) pairs; walking alternates diagonal pairs
    offs = {0: (1, -1), 1: (0, 0), 2: (-1, 1), 3: (0, 0)}[f] if walk else (0, 0)
    lift = {1: (True, False), 3: (False, True)}.get(f, (False, False)) if walk else (False, False)
    legx = [(int(cx) - 3, 1, 1), (int(cx) + 3, 0, 1), (int(cx) - 4, 0, 0), (int(cx) + 2, 1, 0)]  # (x, pair, far)
    for x, pair, far in legx:
        o = offs[pair] * (1 if far == 0 else -1)
        bottom = 21 - (1 if lift[pair] and far == 0 else 0)
        if kind == 'wolf' and col == 'atk1': o += 1 if pair == 1 else -1
        leg = line(x + (1 if far else 0), spec['legtop'] + b, x + (1 if far else 0) + o, bottom, 1)
        s.add(leg, spec['legc'], 'shade' if far else 'flat')
        s.add({(x + (1 if far else 0) + o, bottom)}, dk(spec['legc'], 0.4), 'flat')
    if kind == 'wolf':  # bushy tail
        wag = 1 if col == 'idle1' else 0
        tail = line(int(cx) - 5, int(cy) - 1, int(cx) - 8, int(cy) + 2 - wag, 2)
        s.add(tail, coat, 'rim')
        s.add({(int(cx) - 8, int(cy) + 2 - wag), (int(cx) - 8, int(cy) + 3 - wag)}, hx('d8d8e0'), 'flat')
    if kind == 'deer':
        s.add({(int(cx) - 5, int(cy) - 2), (int(cx) - 6, int(cy) - 2), (int(cx) - 6, int(cy) - 1 + (1 if col == 'idle1' else 0))}, hx('f4f0e8'), 'flat')
    body = ellipse(cx, cy, rx, ry)
    s.add(body, coat, 'rim')
    if kind == 'sheep':  # wool curls
        s.add({p for p in body if (p[0] * 3 + p[1] * 5) % 7 == 0 and (p[0] + 1, p[1]) in body and (p[0], p[1] + 1) in body}, lt(coat, 0.5), 'flat')
    else:  # light belly
        s.add({p for p in body if p[1] >= cy + ry - 1.5 and abs(p[0] + .5 - cx) < rx - 1.5}, lt(coat, 0.45), 'flat')
    hxp, hyp = spec['eathead'] if eat else spec['head']
    if kind == 'sheep' and col == 'idle1': hyp += 1
    if kind == 'wolf' and col == 'atk0': hyp += 1.5
    hxp += dx
    hyp += b if not eat else 0
    # neck
    if kind == 'deer':
        s.add(line(int(cx) + 3, int(cy) - 1, int(hxp) - 1, int(hyp) + 1, 2), coat, 'rim')
    elif kind == 'wolf':
        s.add(line(int(cx) + 3, int(cy) - 1, int(hxp) - 1, int(hyp), 2), coat, 'rim')
    head = ellipse(hxp, hyp, *spec['headr'])
    s.add(head, spec['face'], 'rim')
    hx_, hy_ = int(hxp), int(hyp)
    if kind == 'sheep':
        s.add({(hx_ - 1, hy_ - 2), (hx_, hy_ - 2), (hx_ - 1, hy_ - 3)}, lt(coat, 0.3), 'flat')
        s.add({(hx_ - 2, hy_ - 1), (hx_ - 3, hy_)}, spec['face'], 'flat')  # ear
        s.add({(hx_ + 1, hy_)}, hx('f0e0c0'), 'flat')
    elif kind == 'deer':
        s.add({(hx_ + 2, hy_ + (0 if not eat else 0)), (hx_ + 3, hy_)}, coat, 'rim')
        s.add({(hx_ + 3, hy_)}, INK, 'flat')
        s.add({(hx_ - 1, hy_ - 2), (hx_ - 2, hy_ - 2)}, coat, 'flat')  # ear
        if not eat:
            antler = hx('e0d0a8')
            s.add({(hx_, hy_ - 2), (hx_, hy_ - 3), (hx_ - 1, hy_ - 4), (hx_ + 1, hy_ - 4), (hx_ - 1, hy_ - 5)}, antler, 'flat')
        s.add({(hx_ + 1, hy_ - 1 + (1 if eat else 0))}, INK, 'flat')
    elif kind == 'wolf':
        snout = rect(hx_ + 2, hy_, hx_ + 3, hy_ + 1)
        s.add(snout, coat, 'rim')
        s.add({(hx_ + 4, hy_)}, INK, 'flat')
        if col == 'atk1':
            s.add({(hx_ + 3, hy_ + 2), (hx_ + 2, hy_ + 2)}, hx('c03838'), 'flat')
            s.add({(hx_ + 3, hy_ + 1)}, hx('f8f8f0'), 'flat')
        s.add({(hx_ - 1, hy_ - 2), (hx_ - 1, hy_ - 3), (hx_, hy_ - 2)}, coat, 'flat')  # ears
        s.add({(hx_ + 1, hy_ - 2), (hx_ + 1, hy_ - 3)}, dk(coat, 0.2), 'flat')
        s.add({(hx_ + 1, hy_ - 1 + (1 if eat else 0))}, hx('f0c040'), 'flat')
    return outline(s.render())


ROWS = [('human', i) for i in range(8)] + [('demon', i) for i in range(4)] + [('sheep', 0), ('deer', 0), ('rabbit', 0), ('wolf', 0)]


def frame(kind, variant, col):
    if kind in ('human', 'demon'):
        pal = human_palette(variant) if kind == 'human' else demon_palette(variant)
        if col == 'dead': return lying(to_image(humanoid(pal, POSES['idle0']), None), False)
        sit = col.startswith('read')
        return to_image(humanoid(pal, POSES[col]), (8, 18) if sit else (7, 16))
    if col == 'dead': return lying(to_image(quadruped(kind, 'idle0'), None), True)
    mapped = {'read0': 'idle0', 'read1': 'idle1', 'atk0': 'walk0' if kind != 'wolf' else 'atk0', 'atk1': 'walk2' if kind != 'wolf' else 'atk1'}.get(col, col)
    return to_image(quadruped(kind, mapped), (5, 17) if kind != 'rabbit' else (7, 15))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else 'Game/Assets/Art/characters_24.png'
    sheet = Image.new('RGBA', (F * len(COLS), F * len(ROWS)), (0, 0, 0, 0))
    for r, (kind, v) in enumerate(ROWS):
        for c, col in enumerate(COLS):
            sheet.alpha_composite(frame(kind, v, col), (c * F, r * F))
    sheet.save(out)
    if len(sys.argv) > 2:
        bg = Image.new('RGBA', sheet.size, (150, 173, 99, 255))
        bg.alpha_composite(sheet)
        bg.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(sys.argv[2])


if __name__ == '__main__':
    main()
