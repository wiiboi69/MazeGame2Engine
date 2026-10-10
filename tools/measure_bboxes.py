#!/usr/bin/env python3
"""Adds a visible-pixel bounding box ("bbox": [x0,y0,x1,y1] in source units) to every UI costume in
assets/manifest.json so the game can hit-test buttons. Needs playwright + chromium + Pillow."""
import json, os, io, sys
from PIL import Image
from playwright.sync_api import sync_playwright
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
m = json.load(open(os.path.join(ROOT, 'assets/manifest.json')))
groups = ['mainmenu', 'pause', 'settings', 'dialog', 'menudialog', 'loading', 'transition', 'overlay', 'mapbg']
with sync_playwright() as p:
    b = p.chromium.launch(args=['--allow-file-access-from-files'])
    pg = b.new_page(viewport={'width': 2400, 'height': 1600})
    for g in groups:
        for name, c in m[g].items():
            if c['kind'] != 'svg':
                continue
            path = os.path.join(ROOT, 'assets', c['file'])
            head = open(path, encoding='utf-8', errors='ignore').read(2000)
            import re
            vb = re.search(r'viewBox="([-0-9.eE ,]+)"', head)
            wh = re.search(r'<svg[^>]*?\swidth="([0-9.]+)', head), re.search(r'<svg[^>]*?\sheight="([0-9.]+)', head)
            if wh[0] and wh[1]:
                w, h = float(wh[0].group(1)), float(wh[1].group(1))
            elif vb:
                v = [float(t) for t in re.split(r'[ ,]+', vb.group(1).strip())]
                w, h = v[2], v[3]
            else:
                continue
            html = os.path.join(ROOT, 'tools', '_bbox.html')
            open(html, 'w').write(f'<body style="margin:0;background:transparent"><img id=i style="display:block;width:{w}px;height:{h}px" src="file://{path}"></body>')
            pg.goto('file://' + html)
            pg.wait_for_function("document.getElementById('i').complete")
            w, h = max(1, min(int(round(w)), 2300)), max(1, min(int(round(h)), 1500))
            png = pg.screenshot(omit_background=True, clip={'x': 0, 'y': 0, 'width': w, 'height': h})
            im = Image.open(io.BytesIO(png)).convert('RGBA')
            bb = im.getchannel('A').point(lambda a: 255 if a > 20 else 0).getbbox()
            if bb:
                c['bbox'] = [round(v, 1) for v in bb]
                c['natural'] = [im.width, im.height]
    b.close()
json.dump(m, open(os.path.join(ROOT, 'assets/manifest.json'), 'w'), indent=1)
os.remove(os.path.join(ROOT, 'tools', '_bbox.html'))
print('done')
