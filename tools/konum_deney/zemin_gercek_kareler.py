"""NİTEL deneme: SegFormer-B0 (ADE20K) gerçek robot kareleri üstünde (kokpit ekran kaydından, bindirmeli perspektif görünüm).
Bindirme (kontrol paneli altta, aynada üstte ortada, köşe düğmeleri, direksiyon/pedal) kırpılarak dışarıda bırakılır:
kare[118:500] (tüm genişlik). Model olduğu gibi çalışır; zemin = floor+rug sınıfları. Çıktı: zemin olasılığı bindirmesi (yeşil = zemin)
ve en baskın sınıfların adı. Sayısal ölçüt YOK (gerçek yer gerçeği yok).
    python tools/konum_deney/zemin_gercek_kareler.py <kare_klasoru> [cikti_klasoru]"""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zemin_ortak as zo  # noqa: E402
import zemin_model as zm  # noqa: E402


def ana():
    kl = sys.argv[1]
    cikis = sys.argv[2] if len(sys.argv) > 2 else os.path.join(zo.CIKTI, "gercek")
    os.makedirs(cikis, exist_ok=True)
    b = zm.Bolutleyici()
    torch = b.torch
    print("etiketler (zemin):", [b.id2[i] for i in b.zemin_idx], "cihaz", b.cihaz)
    dosyalar = sorted(f for f in os.listdir(kl) if f.lower().endswith((".jpg", ".png")))
    for f in dosyalar:
        im = cv2.imread(os.path.join(kl, f))
        kes = im[118:500]
        hh, ww = 384, 1568                                  # 32 katı; oran korunur (382x1600)
        x = cv2.resize(kes, (ww, hh))
        x_ = x[..., ::-1].astype(np.float32) / 255.0
        x_ = (x_ - b.ort) / b.std
        t = torch.from_numpy(np.ascontiguousarray(x_.transpose(2, 0, 1)[None])).to(b.cihaz)
        if b.cihaz == "cuda":
            t = t.half()
        with torch.no_grad():
            lg = b.model(pixel_values=t).logits
            lg = torch.nn.functional.interpolate(lg.float(), size=(hh, ww), mode="bilinear", align_corners=False)
            pr = lg.softmax(1)[0]
        zem = pr[b.zemin_idx].sum(0).cpu().numpy()
        sinif = pr.argmax(0).cpu().numpy()
        yesil = np.zeros_like(x)
        yesil[..., 1] = 255
        a = np.clip(zem, 0, 1)[..., None] * 0.55
        ov = (x * (1 - a) + yesil * a).astype(np.uint8)
        # zemin/zemin-dışı sınır çizgisi (olasılık 0,5)
        m = (zem > 0.5).astype(np.uint8)
        kont, _ = cv2.findContours(m, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(ov, kont, -1, (0, 0, 255), 2)
        ad, say = np.unique(sinif, return_counts=True)
        en = sorted(zip(say, ad), reverse=True)[:5]
        etiket = ", ".join("%s %.0f%%" % (b.id2[int(i)].split(",")[0], 100.0 * s / sinif.size) for s, i in en)
        cv2.putText(ov, etiket, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 3, cv2.LINE_AA)
        cv2.putText(ov, etiket, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 1, cv2.LINE_AA)
        cv2.imwrite(os.path.join(cikis, f.rsplit(".", 1)[0] + "_bolut.jpg"), np.vstack([x, ov]))
        print(f, "zemin orani %.2f" % float((zem > 0.5).mean()), "|", etiket)


if __name__ == "__main__":
    ana()
