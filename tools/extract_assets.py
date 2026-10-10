#!/usr/bin/env python3
"""Extracts assets, levels and tile tables from the original Scratch project (.sb3).

Usage: python3 tools/extract_assets.py path/to/maze_game_2_engine.sb3

Outputs (relative to the repo root):
  assets/manifest.json        costume metadata (file, kind, bitmap resolution, rotation centre)
  assets/<group>/...          costume files (svg / png)
  assets/audio/sfx/*.wav      16-bit PCM mono sound effects (ffmpeg used for conversion)
  assets/audio/music/*.ogg    music (re-encoded from the original mp3s)
  levels/level_NNN.json       levels converted to the JSON format (NNN = original LEVEL # = list index)
  tools/tile_tables.json      tile shape/group/recipe/keymap tables -> src/MazeGame.Core/Generated/BuiltinTiles.g.cs
"""
import json, os, re, shutil, subprocess, sys, tempfile, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sb3 = sys.argv[1]
tmp = tempfile.mkdtemp()
with zipfile.ZipFile(sb3) as z:
    z.extractall(tmp)
proj = json.load(open(os.path.join(tmp, 'project.json')))
targets = {t['name']: t for t in proj['targets']}
stage = proj['targets'][0]

def safe(s):
    return re.sub(r'[^A-Za-z0-9_.-]+', '_', s)

manifest = {}
def export_costumes(target, group, dest, only=None):
    t = targets[target]
    os.makedirs(os.path.join(ROOT, 'assets', dest), exist_ok=True)
    out = {}
    for i, c in enumerate(t['costumes'], 1):
        if only and c['name'] not in only:
            continue
        ext = c['dataFormat']
        fname = f"{i:03d}_{safe(c['name'])}.{ext}"
        shutil.copy(os.path.join(tmp, c['md5ext']), os.path.join(ROOT, 'assets', dest, fname))
        out[str(i) if group == 'tiles' else c['name']] = {
            'index': i, 'name': c['name'], 'file': f'{dest}/{fname}', 'kind': ext,
            'res': c.get('bitmapResolution') or 1,
            'cx': c['rotationCenterX'], 'cy': c['rotationCenterY']}
    manifest[group] = out

export_costumes('Stage', 'stage', 'backgrounds/stage')
export_costumes('Tiles', 'tiles', 'tiles')
export_costumes('player', 'player', 'sprites/player')
export_costumes('Enemy', 'enemy', 'sprites/enemy')
export_costumes('Background', 'background', 'backgrounds')
export_costumes('particles', 'particles', 'sprites/particles')
export_costumes('pause menu Engine', 'pause', 'ui/pause')
export_costumes('menu//main menu Engine', 'mainmenu', 'ui/mainmenu')
export_costumes('menu//main menu dilog', 'dialog', 'ui/dialog')
export_costumes('menu//Menu', 'settings', 'ui/settings')
export_costumes('Sprite2', 'transition', 'ui/transition')
export_costumes('overlay', 'overlay', 'ui/overlay')
export_costumes('loading_screen', 'loading', 'ui/loading')
export_costumes('menu dilog', 'menudialog', 'ui/menudialog')
export_costumes('map_background', 'mapbg', 'ui/mapbg')
json.dump(manifest, open(os.path.join(ROOT, 'assets/manifest.json'), 'w'), indent=1)

# ---------------------------------------------------------------- audio
def ima_adpcm_to_pcm(path, out):
    """Decode the single IMA-ADPCM wav (format 0x11) with ffmpeg (supports it natively)."""
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', path, '-ac', '1', '-c:a', 'pcm_s16le', out], check=True)

sfx_dir = os.path.join(ROOT, 'assets/audio/sfx'); os.makedirs(sfx_dir, exist_ok=True)
mus_dir = os.path.join(ROOT, 'assets/audio/music'); os.makedirs(mus_dir, exist_ok=True)
seen = set()
for t in proj['targets']:
    for s in t['sounds']:
        key = (s['name'], s['md5ext'])
        if key in seen: continue
        seen.add(key)
        src = os.path.join(tmp, s['md5ext'])
        name = safe(s['name']).lower()
        if s['md5ext'].endswith('.mp3'):
            # re-encode to Ogg Vorbis (44.1 kHz stereo): the originals are 24/32 kHz MPEG-2/1 mp3s
            subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', src, '-ar', '44100', '-ac', '2',
                            '-c:a', 'libvorbis', '-q:a', '4', os.path.join(mus_dir, name + '.ogg')], check=True)
        else:
            dst = os.path.join(sfx_dir, f"{name}.wav")
            if os.path.exists(dst):  # several sprites own a different sound called 'pop'/'Coin'
                dst = os.path.join(sfx_dir, f"{name}_{s['md5ext'][:6]}.wav")
            ima_adpcm_to_pcm(src, dst)

# ---------------------------------------------------------------- levels
lists = {v[0]: v[1] for v in targets['Level Store']['lists'].values()}
store = lists['level store']
os.makedirs(os.path.join(ROOT, 'levels'), exist_ok=True)
for i, s in enumerate(store, 1):
    if s:
        open(os.path.join(ROOT, 'levels', f'level_{i:03d}.mgl'), 'w').write(str(s))

# ---------------------------------------------------------------- tile tables
sl = {v[0]: v[1] for v in stage['lists'].values()}
ed = {v[0]: v[1] for v in targets['Editor']['lists'].values()}
def cs(a):
    return ', '.join('"%s"' % str(x).replace('\\', '\\\\') for x in a)
json.dump({'Shape': ['', *map(str, sl['TILE SHAPE'])], 'Group': ['', *map(str, sl['TILE GROUPS'])],
           'Keymap': ['', *map(str, ed['tile keymap'])], 'Recipes': ['', *map(str, ed['tile recipes'])]},
          open(os.path.join(ROOT, 'tools/tile_tables.json'), 'w'), indent=0)
os.makedirs(os.path.join(ROOT, 'src/MazeGame.Core/Generated'), exist_ok=True)
subprocess.run([sys.executable, os.path.join(ROOT, 'tools/tile_ids.py')], check=True)
subprocess.run([sys.executable, os.path.join(ROOT, 'tools/convert_levels.py')], check=True)
print('tile count', len(sl['TILE SHAPE']), 'groups', len(sl['TILE GROUPS']), 'keymap', len(ed['tile keymap']), 'recipes', len(ed['tile recipes']))
print('levels', [i for i, s in enumerate(store, 1) if s])
shutil.rmtree(tmp)
