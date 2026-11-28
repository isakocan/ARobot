"""Jetson TensorRT FP32 / FP16 XFeat çıktıları, PC PyTorch'a göre: aynı noktalar mı, tanımlayıcılar ne kadar benzer,
PC çıktısıyla karşılıklı eşleşme oranı."""
import json, sys
import numpy as np, torch
m = torch.hub.load("verlab/accelerated_features", "XFeat", pretrained=True, top_k=1500, trust_repo=True, verbose=False)
m.dev = torch.device("cpu"); m.net = m.net.cpu().eval()
x = torch.from_numpy(np.load("pan_416x1440.npy"))[None, None]
gercek = m.net
with torch.no_grad():
    ref = m.detectAndCompute(x, top_k=1500)[0]


def yukle(ad):
    d = {o["name"]: o for o in json.load(open(ad))}
    return tuple(torch.tensor(np.array(d[n]["values"], np.float32).reshape([int(v) for v in str(d[n]["dimensions"]).split("x")])) for n in ("feats", "kpts", "heat"))


for p in sys.argv[1:]:
    cik = yukle("cikti_%s.json" % p)
    m._modules.pop("net", None); object.__setattr__(m, "net", lambda _x, c=cik: c)   # ağ yerine TensorRT çıktısı
    with torch.no_grad():
        o = m.detectAndCompute(x, top_k=1500)[0]
    object.__delattr__(m, "net"); m.net = gercek
    a, b = ref["keypoints"].numpy(), o["keypoints"].numpy()
    dd = np.linalg.norm(a[:, None] - b[None], axis=2); j = dd.argmin(1); ayni = dd[np.arange(len(a)), j] < 1.0
    cos = (ref["descriptors"][ayni] * o["descriptors"][j[ayni]]).sum(1).numpy()
    s = ref["descriptors"] @ o["descriptors"].T; i1 = s.argmax(1); i2 = s.argmax(0)
    ok = (i2[i1] == torch.arange(len(i1))) & (s.max(1).values > 0.82)
    print("%s: %d nokta, PC ile ayni yerde %%%.1f, ayni noktalarda tanimlayici benzerligi ortanca %.4f en kotu %.4f, PC ile karsilikli eslesme %d/%d" % (
        p, len(b), 100 * ayni.mean(), np.median(cos), cos.min(), int(ok.sum()), len(a)))
