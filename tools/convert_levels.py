#!/usr/bin/env python3
"""Converts the original Scratch level strings (levels/level_NNN.mgl) to the JSON format (levels/level_NNN.json).

Usage: python3 tools/convert_levels.py [levels-dir]       (the .mgl files are moved to tools/legacy_levels/)
"""
import json, os, re, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tile_ids

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IDS = tile_ids.id_map()
ENTITY_PROPS = {29: 'walker', 30: 'danger', 46: 'npc', 47: 'end_box', 68: 'piranha', 77: 'door', 78: 'pipe', 79: 'door_wide'}


def decode(text, number=0, existing=()):
    pos = 0

    def token():
        nonlocal pos
        j = text.find('_', pos)
        if j < 0: j = len(text)
        t = text[pos:j]; pos = min(len(text), j + 1)
        return t

    def num(t, d=0):
        try: return int(t)
        except ValueError: return d

    if token() != '1': return None
    w, h = num(token()), num(token())
    bg = token(); token(); token()
    backdrop = num(token(), 1); token(); token()
    flags = num(token()); camera = num(token()) & 0xFF
    for _ in range(9): token()
    token(); token(); token(); token(); token()
    name = token(); difficulty = num(token()); song = token()
    for _ in range(4): token()
    custom = num(token()); extra = num(token())
    tiles = [[2] * w for _ in range(h)]   # tiles[y][x]
    filled, cx, cy = 0, 0, 0
    while filled < w * h and pos < len(text):
        m = re.compile(r'\d*').match(text, pos); digits = m.group(); pos = m.end()
        tile = int(digits) if digits else 2
        letter = text[pos] if pos < len(text) else ''; pos += 1
        run = ord(letter) - 96 if 'a' <= letter <= 'z' else 1
        for _ in range(run):
            if filled >= w * h: break
            tiles[cy][cx] = tile or 2
            filled += 1; cx += 1
            if cx >= w: cx = 0; cy += 1
    count = num(token())
    ents = []
    for _ in range(count):
        if pos >= len(text): break
        idx, typ, param = num(token()), num(token()), token()
        if idx < 1 or typ not in ENTITY_PROPS: continue
        e = {'type': ENTITY_PROPS[typ], 'x': (idx - 1) // h, 'y': (idx - 1) % h}
        if typ == 46 and param:
            try: e['props'] = {'text': bytes.fromhex(param).decode('utf-8')}
            except ValueError: pass
        elif typ in (77, 78, 79) and param.isdigit():
            e['props'] = {'target': 'level_%03d' % int(param)}
        ents.append(e)
    rows = []
    for y in range(h - 1, -1, -1):
        parts, i = [], 0
        row = tiles[y]
        while i < w:
            j = i
            while j < w and row[j] == row[i]: j += 1
            tid = IDS.get(row[i], 'air')
            parts.append(tid if j - i == 1 else f'{tid}*{j - i}')
            i = j
        rows.append(' '.join(parts))
    lid = 'level_%03d' % number
    names = {56: 'Secret room'}
    out = {'format': 3, 'id': lid, 'name': names.get(number, 'Level %d' % (number - 1)), 'width': w, 'height': h, 'background': bg, 'backdrop': backdrop,
           'parallax': bool(flags & 1), 'underwater': bool(flags & 2)}
    if flags & ~3: out['flagBits'] = flags & ~3
    for key, n in (('next', number + 1), ('left', number - 1), ('right', number + 1)):
        if n in existing: out[key] = 'level_%03d' % n
    out.update({'camera': camera, 'song': song, 'difficulty': difficulty, 'custom': custom, 'extraFlags': extra,
                'tiles': rows, 'entities': ents})
    return out


if __name__ == '__main__':
    d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'levels')
    src = sys.argv[2] if len(sys.argv) > 2 else d          # folder with the level_NNN.mgl files
    legacy = os.path.join(ROOT, 'tools', 'legacy_levels'); os.makedirs(legacy, exist_ok=True)
    files = {int(m.group(1)): f for f in sorted(os.listdir(src)) if (m := re.fullmatch(r'level_(\d+)\.mgl', f))}
    existing = {n for n in files if n != 1}
    for n, f in sorted(files.items()):
        text = open(os.path.join(src, f)).read().strip()
        if n == 1:
            start = re.match(r'gs_(\d+)_', text)
            if start: json.dump({'startLevel': 'level_%03d' % int(start.group(1))}, open(os.path.join(d, 'game.json'), 'w'), indent=2)
        else:
            lv = decode(text, n, existing)
            if lv is None: print('skip', f); continue
            open(os.path.join(d, lv['id'] + '.json'), 'w').write(json.dumps(lv, indent=2) + '\n')
            print(lv['id'], lv['name'], lv['width'], 'x', lv['height'], len(lv['entities']), 'entities')
        if os.path.abspath(src) != os.path.abspath(legacy):
            shutil.move(os.path.join(src, f), os.path.join(legacy, f))
