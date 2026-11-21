"""Tarama kaydindaki karelere SegFormer-B0 (ADE20K) ile etiket verir (bilgisayarda; uygulamadakinin aynisi).

Kullanim: python etiketle.py <kayit_klasoru> [--model tools/tarama/model/segformer_b0_ade.onnx] [--kontrol]
Cikti: her kare icin etiket_XXXXXX.bin (derinlik izgarasi boyutunda, 80x45 varsayilan, sensor yonunde, 1 bayt/piksel).

Grup kodlari (C# OdaEtiket ile ayni):
  0 bilinmiyor/diger, 1 duvar, 2 zemin, 3 tavan, 4 acilis (kapi/pencere/perde/jaluzi), 5 insan (yok sayilir),
  10 sandalye, 11 masa, 12 koltuk, 13 yatak, 14 dolap/raf, 15 ekran/bilgisayar, 16 diger esya
JPG dikey ters kayitli (Unity), once duzeltilir; goruntu sensor yonunde (manzara); modele dik (portre) cevrilerek verilir, sonuc geri cevrilir.
"""
import argparse
import glob
import json
import os

import cv2
import numpy as np
import onnxruntime as ort

GRUP = {
    1: ["wall"],
    2: ["floor", "rug", "carpet"],
    3: ["ceiling"],
    4: ["door", "windowpane", "screen door", "curtain", "blind", "double door"],
    5: ["person"],
    10: ["chair", "swivel chair", "stool", "bench", "seat"],
    11: ["table", "desk", "coffee table", "kitchen island", "counter", "pool table"],
    12: ["sofa", "armchair", "ottoman", "cushion"],
    13: ["bed", "cradle"],
    14: ["cabinet", "shelf", "bookcase", "chest of drawers", "wardrobe", "buffet", "case"],
    15: ["television receiver", "monitor", "computer", "screen", "crt screen"],
    16: ["box", "refrigerator", "sink", "toilet", "bathtub", "plant", "lamp", "printer", "stove", "oven",
         "washer", "dishwasher", "microwave", "radiator", "fan", "bag", "basket", "trash can", "piano"],
}


def tablo(id2label):
    t = np.zeros(256, np.uint8)
    for i, ad in id2label.items():
        adlar = [a.strip() for a in ad.split(",")]
        for g, ns in GRUP.items():
            if any(a in ns for a in adlar):
                t[int(i)] = g
                break
    return t


DONUS = {  # gosterim yonu -> (modele verirken, geri donerken)
    "Portrait": (cv2.ROTATE_90_CLOCKWISE, cv2.ROTATE_90_COUNTERCLOCKWISE),
    "PortraitUpsideDown": (cv2.ROTATE_90_COUNTERCLOCKWISE, cv2.ROTATE_90_CLOCKWISE),
    "LandscapeLeft": (None, None),
    "LandscapeRight": (cv2.ROTATE_180, cv2.ROTATE_180),
}


def calistir(oturum, tab, rgb_bgr, yon):
    on, geri = DONUS.get(yon, DONUS["Portrait"])
    im = cv2.rotate(rgb_bgr, on) if on is not None else rgb_bgr
    x = cv2.resize(im, (512, 512), interpolation=cv2.INTER_AREA)[:, :, ::-1].astype(np.float32) / 255.0
    x = (x - (0.485, 0.456, 0.406)) / (0.229, 0.224, 0.225)
    y = oturum.run(None, {oturum.get_inputs()[0].name: x.transpose(2, 0, 1)[None].astype(np.float32)})[0][0]
    c = tab[y.argmax(0).astype(np.uint8)]
    c = cv2.resize(c, (im.shape[1], im.shape[0]), interpolation=cv2.INTER_NEAREST)
    return cv2.rotate(c, geri) if geri is not None else c


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("kayit")
    ap.add_argument("--model", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "model", "segformer_b0_ade.onnx"))
    ap.add_argument("--kontrol", action="store_true", help="birkac karede etiketi goruntu ustune cizip kontrol.png yaz")
    a = ap.parse_args()
    mdir = os.path.dirname(a.model)
    tab = tablo(json.load(open(os.path.join(mdir, "config.json"), encoding="utf-8"))["id2label"])
    oturum = ort.InferenceSession(a.model, providers=["CPUExecutionProvider"])
    kareler = [json.loads(s) for s in open(os.path.join(a.kayit, "kareler.jsonl"), encoding="utf-8") if s.strip()]
    renk = {1: (200, 200, 200), 2: (60, 180, 90), 3: (230, 160, 60), 4: (60, 120, 230), 5: (0, 0, 255)}
    kontrol = []
    for n, k in enumerate(kareler):
        im = cv2.imdecode(np.fromfile(os.path.join(a.kayit, k["jpg"]), np.uint8), cv2.IMREAD_COLOR)
        # Unity EncodeArrayToJPG satir 0'i alta yazar: kayitli JPG, ham kamera/derinlik tamponuna gore dikey ters.
        # (olculdu: ters cevrilmeden zemin etiketi derinlikte zemine %59, cevrilince %66 oturuyor)
        im = im[::-1].copy()
        c = calistir(oturum, tab, im, k.get("display_orientation", "Portrait"))
        dw, dh = k["depth_w"], k["depth_h"]
        # derinlik izgarasi merkezleri: (u+0.5)/dw ayni goruntu kesitine denk (ayni sensor yonu, ayni gorus alani)
        ys = ((np.arange(dh) + 0.5) * c.shape[0] / dh).astype(int)
        xs = ((np.arange(dw) + 0.5) * c.shape[1] / dw).astype(int)
        e = c[ys][:, xs].astype(np.uint8)
        ad = k["depth_file"].replace("derinlik_", "etiket_")
        e.tofile(os.path.join(a.kayit, ad))
        if a.kontrol and n % max(1, len(kareler) // 4) == 0 and len(kontrol) < 4:
            d = np.fromfile(os.path.join(a.kayit, k["depth_file"]), np.uint16).reshape(dh, dw).astype(np.float32) / 1000
            ov = im.copy()
            for g, r in renk.items(): ov[c == g] = r
            ov[c >= 10] = (200, 60, 200)
            ust = cv2.addWeighted(im, 0.5, ov, 0.5, 0)
            dv = cv2.applyColorMap(cv2.resize((np.clip(d, 0, 4) / 4 * 255).astype(np.uint8), (im.shape[1], im.shape[0]), interpolation=cv2.INTER_NEAREST), cv2.COLORMAP_TURBO)
            ev = np.zeros_like(im)
            for g, r in renk.items(): ev[cv2.resize(e, (im.shape[1], im.shape[0]), interpolation=cv2.INTER_NEAREST) == g] = r
            ev[cv2.resize(e, (im.shape[1], im.shape[0]), interpolation=cv2.INTER_NEAREST) >= 10] = (200, 60, 200)
            kontrol.append(cv2.resize(np.hstack([ust, dv, ev]), (1440, 270)))
        if n % 100 == 0:
            print(n, "/", len(kareler))
    if kontrol:
        cv2.imencode(".png", np.vstack(kontrol))[1].tofile(os.path.join(a.kayit, "etiket_kontrol.png"))
    print("bitti:", len(kareler), "kare")


if __name__ == "__main__":
    main()
