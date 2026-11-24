"""Rebuild the CC0 environment atlases with Python and Pillow, from any cwd."""
from pathlib import Path
from random import Random

from PIL import Image, ImageDraw
from make_characters import INK, dk, hx, lt, mix

OUT = Path(__file__).resolve().parents[2] / "Game/Assets/Art"


def tile(index):
    im = Image.new("RGBA", (16, 16))
    d = ImageDraw.Draw(im)
    rng = Random(940 + index)
    bases = ["91a85f"] * 3 + ["637e50", "898580", "dfebdf", "6aa8a2", "467f95", "916542"]
    if index < len(bases):
        base = hx(bases[index])
        d.rectangle((0, 0, 15, 15), fill=base)
        for _ in range(6):
            x, y = rng.randrange(15), rng.randrange(16)
            d.line((x, y, x + 1, y), fill=dk(base, .07))
        if index in (0, 1, 2, 3):
            for _ in range(2):
                x, y = rng.randrange(1, 13), rng.randrange(2, 15)
                d.line([(x, y-1), (x+1, y), (x+2, y-2)], fill=dk(base, .14))
                d.point((x+2, y-2), fill=lt(base, .24))
        elif index == 4:
            for x, y in ((2, 8), (10, 3), (11, 14)):
                d.polygon([(x-2,y), (x,y-3), (x+2,y-3), (x+4,y)], fill=dk(base,.18))
                d.line((x-2,y,x,y-3,x+2,y-3), fill=lt(base,.28))
        elif index == 5:
            d.line((4,8,7,8,9,6), fill="#c8d9d9")
        elif index == 8:
            for y in range(2, 16, 4):
                d.line((0,y,15,y), fill="#714d38")
                d.line((1,y+1,14,y+1), fill="#af8150")
    elif index < 13:
        # A transparent lip lies on the water side of each adjacent land edge.
        for x in range(16):
            depth = int(4 <= x < 9)
            d.line((x,0,x,depth), fill="#b8b288")
            d.point((x,depth+1), fill="#aad0b9")
        im = im.transpose([Image.Transpose.ROTATE_90, Image.Transpose.ROTATE_180,
                           Image.Transpose.ROTATE_270][index-10]) if index > 9 else im
    else:
        frame = index-13
        d.line((4+frame,8,7+frame,8), fill="#a0cbbe")
        d.line((8+frame,7,9+frame,7), fill="#c4dec7")
    return im


def prop(index):
    im = Image.new("RGBA", (32,32))
    d = ImageDraw.Draw(im)
    def oval(box, color):
        d.ellipse(box, fill=color)
    def poly(points, color):
        d.polygon(points, fill=color)
    if index in (0,1):
        poly([(13,29),(14,17),(18,17),(18,26),(21,29)], "#805838")
        d.line((15,19,15,28),fill="#bc8952")
        if index == 0:
            for x,y,r in [(9,16,6),(21,15,7),(14,10,8),(8,12,5),(18,8,5)]:
                oval((x-r,y-r,x+r,y+r),"#507344")
                oval((x-r,y-r,x+r-2,y+r-3),"#789744")
                d.arc((x-r+1,y-r+1,x+r-3,y+r-3),180,275,fill="#b2bf66")
        else:
            for y,w in [(23,11),(17,9),(11,6)]:
                poly([(16,y-11),(16+w,y-1),(16+w-3,y-1),(16+w-2,y+1),(16-w,y)],"#3b6554")
                poly([(16,y-10),(16,y-4),(13,y-2),(14,y-1),(17-w,y-1)],"#77966b")
    elif index == 2:
        poly([(10,28),(12,23),(12,19),(20,19),(21,25),(24,28)],"#856047")
        oval((11,17,21,22),"#d2a06b")
        d.arc((13,18,19,21),0,320,fill="#936748")
    elif index in (3,4):
        for x,y,r in [(9,25,4),(20,24,5),(14,22,5)]:
            oval((x-r,y-r,x+r,y+r),"#577a49")
            d.arc((x-r+1,y-r+1,x+r-1,y+r-1),185,280,fill="#98ac65")
        if index == 3:
            for x,y in [(8,24),(15,21),(19,25),(13,26)]:
                oval((x,y,x+2,y+2),"#ae4755")
                d.point((x,y),fill="#f49781")
    elif index in (5,6):
        for x,y in [(10,29),(16,27),(21,29)]:
            tall = 6 if index == 6 else 0
            d.line((x,y,x,y-4-tall),fill="#688149")
            poly([(x,y-2),(x-4,y-6),(x-1,y-5)],"#8ca457")
            poly([(x,y-3),(x+3,y-7),(x+3,y-5)],"#b5b85c")
            if tall:
                for j in range(3):
                    d.line((x-2,y-8-j*2,x,y-6-j*2,x+2,y-9-j*2),fill="#e4b55d")
    else:
        roof = "#537895" if index == 7 else "#925064"
        d.rectangle((4,15,27,28),fill="#b98a5c")
        for x in (5,16,26):
            d.rectangle((x,15,x+1,28),fill="#775440")
        for y in (20,25):
            d.line((6,y,25,y),fill="#966d4f")
        d.rectangle((13,19,20,28),fill="#5b4842")
        d.rectangle((14,20,19,27),fill="#8f694a")
        d.line((14,26,19,21),fill="#c39464")
        d.rectangle((6,18,10,22),fill="#574a4b")
        d.line((8,19,8,21),fill="#efbc77")
        poly([(1,15),(8,5),(23,5),(30,15),(28,17),(2,17)],roof)
        for y,left in ((8,7),(11,5),(14,3)):
            d.line((left,y,31-left,y),fill=dk(hx(roof[1:]),.25))
            for x in range(left+2,30-left,6):
                d.point((x,y-1),fill=lt(hx(roof[1:]),.25))
    # Match the character sheet's lit upper-left edges and ink silhouette.
    original = im.copy()
    for y in range(32):
        for x in range(32):
            c = original.getpixel((x,y))
            def solid(dx,dy):
                return 0 <= x+dx < 32 and 0 <= y+dy < 32 and original.getpixel((x+dx,y+dy))[3]
            if c[3]:
                if not solid(1,0) or not solid(0,1):
                    im.putpixel((x,y),dk(c,.28))
                elif not solid(-1,0) or not solid(0,-1):
                    im.putpixel((x,y),lt(c,.16))
            else:
                for dx,dy in ((0,-1),(-1,0),(1,0),(0,1)):
                    if solid(dx,dy):
                        im.putpixel((x,y),mix(original.getpixel((x+dx,y+dy)),INK,.72))
                        break
    shadow = Image.new("RGBA", im.size)
    ImageDraw.Draw(shadow).ellipse((6,27,25,30),fill=(26,16,30,55))
    shadow.alpha_composite(im)
    return shadow


if __name__ == "__main__":
    for name, size, count, draw in [("tiles",16,16,tile), ("props",32,9,prop)]:
        sheet = Image.new("RGBA",(size*count,size))
        for i in range(count):
            sheet.paste(draw(i),(size*i,0))
        sheet.save(OUT / f"environment_{name}.png")
