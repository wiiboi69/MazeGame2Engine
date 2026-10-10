#!/usr/bin/env python3
"""Generates the overworld tileset (16x16 PNGs, drawn at 2x = 32px cells) into assets/overworld/."""
import random, os
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), '..', 'assets', 'overworld')
os.makedirs(OUT, exist_ok=True)
T = (0, 0, 0, 0)

def new(c=T): return Image.new('RGBA', (16, 16), c)
def rect(im, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if 0 <= x < 16 and 0 <= y < 16: im.putpixel((x, y), c)
def noise(im, var, seed, amt=0.4):
    r = random.Random(seed); px = im.load()
    for y in range(16):
        for x in range(16):
            if px[x, y][3] and r.random() < amt:
                d = r.randint(-var, var); px[x, y] = tuple(max(0, min(255, c + d)) for c in px[x, y][:3]) + (255,)
    return im
def save(im, n): im.save(os.path.join(OUT, n + '.png'))
def base(c, seed, var=10, amt=0.4): return noise(new(c + (255,)), var, seed, amt)

G = (96, 176, 72); 
save(base(G, 1), 'grass')
fl = base(G, 2)
for p, c in [((3,4),(255,240,120)),((10,3),(255,255,255)),((6,11),(255,150,170)),((13,12),(255,240,120)),((2,13),(255,255,255))]: fl.putpixel(p, c + (255,))
save(fl, 'flowers')

def tree(im, x, y, c1=(40,120,50), c2=(24,90,40)):
    rect(im, x+1, y+5, x+2, y+7, (110,70,40,255)); rect(im, x, y+1, x+3, y+4, c1 + (255,)); rect(im, x+1, y, x+2, y, c1 + (255,)); rect(im, x, y+4, x+3, y+4, c2 + (255,))
f = base(G, 3, 6)
for x, y in [(0,0),(8,1),(4,6),(11,8),(0,9),(7,11)]: tree(f, x, y)
save(f, 'forest')
pf = base((226, 236, 248), 4, 6)
for x, y in [(0,0),(8,1),(4,6),(11,8),(0,9),(7,11)]: tree(pf, x, y, (50,110,90), (30,80,70)); pf.putpixel((x+1, y+1), (255,255,255,255)); pf.putpixel((x+2, y), (255,255,255,255))
save(pf, 'pine_forest')
h = base(G, 5, 8)
for cx, cy in [(4, 9), (11, 7)]:
    for dy in range(4):
        rect(h, cx - 3 + dy, cy + dy - 3, cx + 3 - dy, cy + dy - 3, (130, 195, 90, 255))
    rect(h, cx - 3, cy + 1, cx + 3, cy + 1, (80, 140, 60, 255))
save(h, 'hills')
m = base(G, 6, 6)
for cx, base_y, hh in [(5, 14, 11), (11, 14, 8)]:
    for dy in range(hh):
        w = dy * 5 // hh + 1
        rect(m, cx - w, base_y - hh + dy, cx + w, base_y - hh + dy, (130, 120, 120, 255) if dy > 2 else (250, 250, 255, 255))
        if cx + w < 16: m.putpixel((cx + w, base_y - hh + dy), (80, 75, 85, 255))
save(m, 'mountain')
save(base((236, 214, 140), 7, 12, 0.5), 'sand')
save(base((240, 246, 255), 8, 6, 0.4), 'snow')
d = base((176, 130, 80), 9, 12, 0.5); save(d, 'path')
road = base((150, 150, 160), 10, 8, 0.3)
for y in (3, 8, 13):
    for x in range(0, 16, 5): road.putpixel(((x + y) % 16, y), (110, 110, 120, 255))
save(road, 'road')
def wave(c, hi, seed, f):
    im = base(c, seed, 6, 0.3)
    for y in (3, 9, 14):
        for x in range(16):
            if (x + y * 3 + f * 3) % 8 < 3: im.putpixel((x, y), hi + (255,))
    return im
for i in range(2):
    save(wave((70, 140, 230), (170, 215, 255), 11, i), 'water_%d' % (i + 1))
    save(wave((40, 90, 180), (90, 150, 230), 12, i), 'deep_water_%d' % (i + 1))
for i in range(2):
    lv = base((200, 70, 30), 13, 20, 0.6)
    for k in range(5):
        x, y = (k * 7 + i * 5) % 14, (k * 5 + i * 3) % 14
        rect(lv, x, y, x + 2, y + 1, (255, 200, 60, 255))
    save(lv, 'lava_%d' % (i + 1))
br = new(); rect(br, 0, 0, 15, 15, (70, 140, 230, 255)); br = noise(br, 6, 14, 0.3)
rect(br, 0, 3, 15, 12, (150, 100, 55, 255))
for x in range(0, 16, 3): rect(br, x, 3, x, 12, (110, 70, 40, 255))
rect(br, 0, 3, 15, 3, (190, 140, 80, 255)); rect(br, 0, 12, 15, 12, (90, 55, 30, 255))
save(br, 'bridge')

def on_grass(seed): return base(G, seed, 6)
t = on_grass(15)
rect(t, 3, 7, 12, 13, (225, 205, 170, 255)); rect(t, 2, 5, 13, 6, (200, 60, 60, 255)); rect(t, 4, 3, 11, 4, (200, 60, 60, 255)); rect(t, 6, 1, 9, 2, (200, 60, 60, 255))
rect(t, 7, 10, 8, 13, (110, 70, 40, 255)); rect(t, 4, 8, 5, 9, (120, 190, 240, 255)); rect(t, 10, 8, 11, 9, (120, 190, 240, 255))
save(t, 'town')
c = on_grass(16)
rect(c, 3, 6, 12, 14, (140, 140, 160, 255)); rect(c, 2, 3, 4, 14, (120, 120, 140, 255)); rect(c, 11, 3, 13, 14, (120, 120, 140, 255))
for x in (2, 4, 11, 13): rect(c, x, 2, x, 2, (120, 120, 140, 255))
rect(c, 7, 9, 8, 14, (50, 40, 60, 255)); rect(c, 6, 1, 9, 5, (200, 60, 60, 255)); c.putpixel((7, 0), (255, 240, 120, 255))
save(c, 'castle')
cv = on_grass(17)
rect(cv, 2, 5, 13, 14, (120, 115, 120, 255)); rect(cv, 3, 4, 12, 4, (150, 145, 150, 255)); rect(cv, 5, 7, 10, 14, (30, 25, 35, 255)); rect(cv, 6, 6, 9, 6, (30, 25, 35, 255))
save(cv, 'cave')
print('overworld tiles written')
