"""Tarama uygulamasının harita görünümü (inceleme ekranı), PC'de birebir.

Zemin resmi uygulamanın kendi C# koduyla üretilir (uygulama_harita/: ZeminHaritasi.cs + ZeminGeo.cs değişmeden derlenir):
turkuaz açık zemin, koyu mavi oyun alanı (güvenilir açık zemin), ince gri kenar. Resim uygulamadaki gibi ev çerçevesine
döndürülür (+x sağa, +z yukarı), koyu panel zemini üstüne konur. İşaretler uygulamadaki simge: sarı kare, koyu iç,
beyaz numara, işaretin baktığı yöne sarı çıkıntı.

    h = Harita("zemin.json", olcek=90)    # olcek: piksel / metre
    im = h.resim.copy(); h.isaretler(im, ev["isaretler"]); c, r = h.px(x, z)
"""
import json, math, os, subprocess
import cv2
import numpy as np

KLASOR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "uygulama_harita")
DLL = os.path.join(KLASOR, "cikti", "uygulama_harita.dll")
UNITY = os.environ.get("UNITY_DATA", r"C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Data")
ARKA = (22, 18, 16)                     # BGR: uygulamanın PanelKoyu (16, 18, 22)
SARI = (51, 204, 255)                   # BGR: IsaretSari (1, 0.80, 0.20)
ISARET_IC = (28, 23, 20)                # BGR: (0.08, 0.09, 0.11)


def _kaynak_degisti():
    if not os.path.exists(DLL): return True
    t = os.path.getmtime(DLL); s = os.path.join(KLASOR, "..", "..", "..", "ar_tarama", "Assets", "Scripts")
    return any(os.path.getmtime(os.path.join(s, f)) > t for f in ("ZeminHaritasi.cs", "ZeminGeo.cs", "OdaModeli.cs")) \
        or os.path.getmtime(os.path.join(KLASOR, "Program.cs")) > t


def uygulama_resmi(zemin_json, onek):
    """Uygulamanın Resim() çıktısı: (RGBA dizisi H x W x 4, satır 0 = en küçük w), bilgi sözlüğü."""
    if _kaynak_degisti():
        subprocess.run(["sh", os.path.join(KLASOR, "derle.sh")], check=True, stdout=subprocess.DEVNULL)
    # uygulamanın okuyucusu sıkışık JSON bekler ("acik":[...]); Python'un boşluklu yazdığı dosya için sıkışık kopya
    girdi = onek + "_girdi.json"
    json.dump(json.load(open(zemin_json, encoding="utf-8")), open(girdi, "w", encoding="utf-8"), separators=(",", ":"))
    subprocess.run([os.path.join(UNITY, "NetCoreRuntime", "dotnet.exe"), DLL, girdi, onek], check=True)
    b = json.load(open(onek + ".json", encoding="utf-8"))
    p = np.fromfile(onek + ".rgba", np.uint8).reshape(b["H"], b["W"], 4)
    return p, b


class Harita:
    def __init__(self, zemin_json, olcek=90.0, pay=0.4, onek=None):
        onek = onek or os.path.splitext(zemin_json)[0] + "_uygulama"
        p, b = uygulama_resmi(zemin_json, onek)
        self.bilgi = b; self.s = float(olcek)
        ca, sa, P, ou, ow, W, H = b["ca"], b["sa"], b["piksel"], b["ou"], b["ow"], b["W"], b["H"]
        # resim köşelerinin ev çerçevesindeki kutusu (uygulama: x = u ca - w sa, z = u sa + w ca)
        kose = [(ou + i * W * P, ow + j * H * P) for i in (0, 1) for j in (0, 1)]
        xs = [u * ca - w * sa for u, w in kose]; zs = [u * sa + w * ca for u, w in kose]
        self.x0, self.x1 = min(xs) - pay, max(xs) + pay; self.z0, self.z1 = min(zs) - pay, max(zs) + pay
        Wc = int(math.ceil((self.x1 - self.x0) * self.s)); Hc = int(math.ceil((self.z1 - self.z0) * self.s))
        c, r = np.meshgrid(np.arange(Wc, dtype=np.float32), np.arange(Hc, dtype=np.float32))
        x = self.x0 + (c + 0.5) / self.s; z = self.z1 - (r + 0.5) / self.s
        u = x * ca + z * sa; w = -x * sa + z * ca
        mx = ((u - ou) / P - 0.5).astype(np.float32); my = ((w - ow) / P - 0.5).astype(np.float32)
        rgba = cv2.remap(p.astype(np.float32), mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        a = rgba[..., 3:4] / 255.0
        im = rgba[..., [2, 1, 0]] * a + np.array(ARKA, np.float32) * (1 - a)
        # döndürülmüş resmin boş köşelerini at: içeriğin çevresinde 'pay' kalsın
        ys, xs = np.nonzero(rgba[..., 3] > 8); m = int(pay * self.s)
        c0, c1 = max(0, xs.min() - m), min(Wc, xs.max() + m + 1); r0, r1 = max(0, ys.min() - m), min(Hc, ys.max() + m + 1)
        self.x0 += c0 / self.s; self.z1 -= r0 / self.s
        self.resim = np.clip(im[r0:r1, c0:c1], 0, 255).astype(np.uint8)

    def px(self, x, z):
        return int(round((x - self.x0) * self.s)), int(round((self.z1 - z) * self.s))

    def isaretler(self, im, liste, boyut=34, yazi=None):
        """Uygulamadaki işaret simgesi (büyütülebilir): sarı kare + koyu iç + numara, baktığı yöne sarı çıkıntı."""
        for e in liste:
            c, r = self.px(e["x"], e["z"]); h = boyut // 2; k = max(3, boyut // 7)
            a = e.get("aci")
            if a is not None and not (isinstance(a, float) and math.isnan(a)):
                L = boyut * 1.05; dx, dy = math.sin(a), -math.cos(a)
                cv2.line(im, (c, r), (int(c + dx * (h + L)), int(r + dy * (h + L))), SARI, max(4, boyut // 5), cv2.LINE_AA)
            cv2.rectangle(im, (c - h, r - h), (c + h, r + h), SARI, -1)
            cv2.rectangle(im, (c - h + k, r - h + k), (c + h - k, r + h - k), ISARET_IC, -1)
            t = str(e["id"]); fs = boyut / 40.0; (tw, th), _ = cv2.getTextSize(t, cv2.FONT_HERSHEY_DUPLEX, fs, 2)
            cv2.putText(im, t, (c - tw // 2, r + th // 2), cv2.FONT_HERSHEY_DUPLEX, fs, (255, 255, 255), 2, cv2.LINE_AA)
        return im

    def kutu(self):
        """Tarayıcı / dış çizim için: ev çerçevesinde sol üst köşe ve ölçek."""
        return {"x0": self.x0, "z1": self.z1, "olcek": self.s, "W": self.resim.shape[1], "H": self.resim.shape[0]}


def yazi(im, metin, xy, boy=18, renk=(235, 235, 235), kalin=False):
    """Türkçe harfli yazı (PIL, Segoe UI); PIL yoksa harfleri sadeleştirip cv2."""
    try:
        from PIL import Image, ImageDraw, ImageFont
        f = ImageFont.truetype("segoeuib.ttf" if kalin else "segoeui.ttf", boy)
        pi = Image.fromarray(im[..., ::-1]); d = ImageDraw.Draw(pi)
        d.text(xy, metin, font=f, fill=tuple(int(v) for v in renk[::-1]), stroke_width=2, stroke_fill=(16, 18, 22))
        im[:] = np.asarray(pi)[..., ::-1]
    except Exception:
        cv2.putText(im, metin.translate(str.maketrans("şçığüöŞÇİĞÜÖ", "sciguoSCIGUO")), (xy[0], xy[1] + boy),
                    cv2.FONT_HERSHEY_SIMPLEX, boy / 32.0, renk, 1, cv2.LINE_AA)
    return im


if __name__ == "__main__":
    import sys
    h = Harita(sys.argv[1]); im = h.resim.copy()
    if len(sys.argv) > 3: h.isaretler(im, json.load(open(sys.argv[2], encoding="utf-8"))["isaretler"])
    cv2.imwrite(sys.argv[-1], im)
