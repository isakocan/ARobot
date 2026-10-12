"""Poly Haven (CC0) modellerini ve dokularını Unity projesine indirir.

Kullanım:  python tools/fetch_polyhaven.py
Dosyalar unity/Assets/_Project/Art/Imported altına konur; zaten indirilmiş olanlar atlanır.
Lisans: https://polyhaven.com/license (CC0, atıf gerekmez).
"""
import json
import os
import re
import sys
import urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'unity', 'Assets', '_Project', 'Art', 'Imported')
API = 'https://api.polyhaven.com/files/'

MODELS = [
    # salon
    'sofa_03', 'mid_century_lounge_chair', 'wooden_bookshelf_worn', 'vintage_grandfather_clock_01', 'coffee_table_round_01',
    'Ottoman_01', 'potted_plant_04',
    # mutfak / yemek
    'round_wooden_table_02', 'painted_wooden_table', 'painted_wooden_chair_01', 'painted_wooden_chair_02',
    'painted_wooden_stool', 'painted_wooden_cabinet', 'painted_wooden_cabinet_02', 'steel_frame_shelves_01',
    'vintage_electric_kettle', 'pot_enamel_01', 'wooden_bowl_02', 'jug_01', 'metal_jug',
    # yatak odası
    'vintage_day_bed', 'painted_wooden_nightstand', 'ClassicNightstand_01', 'GothicCommode_01', 
    'ornate_mirror_01', 'chinese_screen_panels', 'wicker_basket_01',
    # banyo / koridor
    'rubber_duck_toy', 'wooden_stool_02', 'ClassicConsole_01', 'painted_wooden_bench', 'wall_clock', 'Shelf_01',
    'painted_wooden_shelves',
]

TEXTURES = {
    # unity adı: poly haven adı
    'hall_parquet': 'herringbone_parquet',
    'kitchen_tiles': 'large_floor_tiles_02',
    'bath_tiles': 'long_white_tiles',
    'bath_floor': 'grey_tiles',
    'bedroom_rug': 'poly_wool_herringbone',
    'wall_beige': 'beige_wall_001',
    'wall_yellow': 'yellow_plaster',
    'wall_painted': 'painted_plaster_wall',
}


def get(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return False
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers={'User-Agent': 'ARaki-fetch/1.0'})
    with urllib.request.urlopen(req, timeout=60) as r, open(dest, 'wb') as f:
        f.write(r.read())
    return True


def files(asset):
    req = urllib.request.Request(API + asset, headers={'User-Agent': 'ARaki-fetch/1.0'})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def fetch_model(mid):
    try:
        info = files(mid)['fbx']['1k']['fbx']
        base = os.path.join(ROOT, 'models', mid)
        get(info['url'], os.path.join(base, mid + '.fbx'))
        mats = {}
        for rel, inc in info.get('include', {}).items():
            get(inc['url'], os.path.join(base, rel))
            m = re.match(r'textures/(.+)_(diff|arm|nor_gl)_1k\.jpg$', rel)
            if m:
                key = {'diff': 'diff', 'arm': 'arm', 'nor_gl': 'nor'}[m.group(2)]
                mats.setdefault(m.group(1), {'baseColor': None})[key] = rel
        with open(os.path.join(base, 'materials.json'), 'w', encoding='utf-8') as f:
            json.dump(mats, f, indent=1)
        return mid, True
    except Exception as e:  # noqa: BLE001
        return mid, repr(e)


def fetch_texture(name, ph):
    try:
        data = files(ph)
        base = os.path.join(ROOT, 'textures', name)
        get(data['Diffuse']['1k']['jpg']['url'], os.path.join(base, name + '_diff.jpg'))
        get(data['nor_gl']['1k']['jpg']['url'], os.path.join(base, name + '_nor.jpg'))
        get(data['arm']['1k']['jpg']['url'], os.path.join(base, name + '_arm.jpg'))
        return name, True
    except Exception as e:  # noqa: BLE001
        return name, repr(e)


if __name__ == '__main__':
    with ThreadPoolExecutor(6) as ex:
        results = list(ex.map(fetch_model, MODELS)) + list(ex.map(lambda kv: fetch_texture(*kv), TEXTURES.items()))
    bad = [r for r in results if r[1] is not True]
    for name, status in results:
        print(('ok   ' if status is True else 'HATA ') + name + ('' if status is True else '  ' + status))
    sys.exit(1 if bad else 0)
