#!/usr/bin/env python3
"""Generates the pixel art for the Desert, Snow & ice and Castle tilesets into assets/custom/ (16x16 PNGs)."""
import random, os
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), '..', 'assets', 'custom')
T = (0, 0, 0, 0)

def new(fill=T):
    return Image.new('RGBA', (16, 16), fill)

def noise(im, base, var, seed, amount=0.35):
    r = random.Random(seed)
    px = im.load()
    for y in range(16):
        for x in range(16):
            if px[x, y][3] == 0: continue
            if r.random() < amount:
                d = r.randint(-var, var)
                px[x, y] = tuple(max(0, min(255, c + d)) for c in px[x, y][:3]) + (255,)
    return im

def rect(im, x0, y0, x1, y1, c):
    px = im.load()
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if 0 <= x < 16 and 0 <= y < 16: px[x, y] = c

def save(im, name): im.save(os.path.join(OUT, name + '.png'))

def bricks(base, mortar, seed, var=10):
    im = new(base + (255,))
    for y in (0, 5, 10, 15): rect(im, 0, y, 15, y, mortar + (255,))
    for row, y in enumerate((1, 6, 11)):
        off = 0 if row % 2 == 0 else 4
        for x in range(off, 16, 8): rect(im, x, y, x, y + 3, mortar + (255,))
    return noise(im, base, var, seed)

def platform(top, under):
    im = new()
    rect(im, 0, 0, 15, 3, top + (255,))
    rect(im, 0, 4, 15, 4, under + (255,))
    for x in range(0, 16, 4): rect(im, x, 5, x, 6, under + (255,))
    return im

def gem(c1, c2, shine):
    frames = []
    for f in range(2):
        im = new()
        px = im.load()
        pts = [(7,3),(8,3),(6,4),(7,4),(8,4),(9,4),(5,5),(6,5),(7,5),(8,5),(9,5),(10,5),(5,6),(6,6),(7,6),(8,6),(9,6),(10,6),
               (6,7),(7,7),(8,7),(9,7),(6,8),(7,8),(8,8),(9,8),(7,9),(8,9),(7,10),(8,10)]
        for p in pts: px[p] = (c1 if (p[0] + p[1]) % 2 == 0 else c2) + (255,)
        s = [(6,5),(7,4)] if f == 0 else [(9,6),(8,7)]
        for p in s: px[p] = shine + (255,)
        frames.append(im)
    return frames

# ------------------------------------------------------------------ Desert
sand = noise(new((233, 205, 130, 255)), (233, 205, 130), 14, 1, 0.5)
save(sand, 'desert_sand')
top = new((233, 205, 130, 255)); rect(top, 0, 0, 15, 2, (246, 224, 160, 255)); rect(top, 0, 3, 15, 3, (200, 165, 95, 255))
save(noise(top, (233, 205, 130), 12, 2, 0.4), 'desert_sand_top')
save(bricks((214, 170, 104), (160, 120, 70), 3), 'desert_brick')
save(platform((214, 170, 104), (160, 120, 70)), 'desert_platform')
cactus = new(); g = (70, 150, 70, 255); d = (45, 110, 55, 255)
rect(cactus, 6, 2, 9, 15, g); rect(cactus, 2, 6, 5, 7, g); rect(cactus, 2, 3, 3, 7, g); rect(cactus, 10, 8, 13, 9, g); rect(cactus, 12, 5, 13, 9, g)
rect(cactus, 8, 2, 9, 15, d)
for p in [(6,4),(9,6),(6,9),(9,12),(2,4),(13,6)]: cactus.putpixel(p, (240, 240, 200, 255))
save(cactus, 'desert_cactus')
pillar = new(); rect(pillar, 3, 0, 12, 15, (222, 186, 120, 255)); rect(pillar, 3, 0, 4, 15, (246, 224, 160, 255)); rect(pillar, 11, 0, 12, 15, (170, 130, 78, 255))
rect(pillar, 2, 0, 13, 1, (200, 160, 100, 255)); rect(pillar, 2, 14, 13, 15, (200, 160, 100, 255))
save(pillar, 'desert_pillar')
f = gem((255, 140, 60), (230, 90, 30), (255, 230, 170))
save(f[0], 'desert_gem_1'); save(f[1], 'desert_gem_2')

# ------------------------------------------------------------------ Snow & ice
snow = new((238, 244, 252, 255)); rect(snow, 0, 0, 15, 0, (255, 255, 255, 255))
save(noise(snow, (238, 244, 252), 8, 4, 0.4), 'snow_block')
ice = new((160, 215, 240, 255))
for i in range(16):
    if 3 + i < 16: ice.putpixel((i, 3 + i), (225, 245, 255, 255))
    if 9 + i < 16: ice.putpixel((i, 9 + i), (225, 245, 255, 255))
save(noise(ice, (160, 215, 240), 10, 5, 0.3), 'ice_block')
save(bricks((190, 205, 225), (130, 150, 185), 6), 'snow_brick')
save(platform((245, 250, 255), (150, 190, 225)), 'snow_platform')
ic = new(); 
for cx, h in [(3, 9), (8, 14), (12, 6)]:
    for y in range(h):
        w = max(0, 2 - y * 2 // max(1, h))
        rect(ic, cx - w // 2, y, cx + w // 2 + (1 if w else 0), y, (190, 235, 255, 255))
    ic.putpixel((cx, 1), (255, 255, 255, 255))
rect(ic, 0, 0, 15, 1, (225, 245, 255, 255))
save(ic, 'snow_icicle')
pine = new(); tg = (40, 120, 80, 255); tw = (250, 250, 255, 255)
for i, (y, w) in enumerate([(1, 1), (3, 2), (5, 3), (7, 4), (9, 5)]):
    rect(pine, 8 - w, y, 7 + w, y + 1, tg); rect(pine, 8 - w, y, 7 + w, y, tw)
rect(pine, 7, 11, 8, 15, (110, 70, 40, 255))
save(pine, 'snow_pine')
f = gem((120, 210, 255), (60, 160, 230), (255, 255, 255))
save(f[0], 'snow_gem_1'); save(f[1], 'snow_gem_2')

# ------------------------------------------------------------------ Castle
save(bricks((110, 112, 128), (60, 62, 76), 7), 'castle_brick')
save(bricks((62, 62, 80), (32, 32, 46), 8, 6), 'castle_dark')
save(platform((130, 132, 150), (70, 72, 90)), 'castle_platform')
lad = new(); rect(lad, 3, 0, 4, 15, (150, 105, 60, 255)); rect(lad, 11, 0, 12, 15, (150, 105, 60, 255))
for y in (2, 6, 10, 14): rect(lad, 3, y, 12, y, (190, 140, 80, 255))
save(lad, 'castle_ladder')
sp = new()
for cx in (2, 6, 10, 14):
    for y in range(8):
        w = y // 3
        rect(sp, cx - w // 2 - 0, 15 - y, cx + w // 2 - 0, 15 - y, (200, 205, 215, 255))
rect(sp, 0, 14, 15, 15, (90, 92, 105, 255))
save(sp, 'castle_spikes')
for i in range(3):
    t = new(); rect(t, 7, 8, 8, 15, (110, 70, 40, 255)); rect(t, 5, 7, 10, 8, (80, 80, 90, 255))
    h = [5, 6, 4][i]
    rect(t, 6, 7 - h, 9, 6, (255, 150, 30, 255)); rect(t, 7, 7 - h - 1, 8, 6, (255, 210, 60, 255)); rect(t, 7, 5, 8, 6, (255, 250, 200, 255))
    save(t, 'castle_torch_%d' % (i + 1))
bars = new(); rect(bars, 0, 2, 15, 3, (70, 72, 84, 255)); rect(bars, 0, 12, 15, 13, (70, 72, 84, 255))
for x in (2, 6, 10, 14): rect(bars, x, 0, x + 1, 15, (110, 114, 130, 255))
save(bars, 'castle_bars')
f = gem((190, 110, 255), (130, 60, 210), (255, 220, 255))
save(f[0], 'castle_gem_1'); save(f[1], 'castle_gem_2')
print('tilesets written')
