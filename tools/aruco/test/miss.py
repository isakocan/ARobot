import json,numpy as np,collections,cv2,sys
V=sys.argv[1]
gercek={m["ad"]:m for m in json.load(open(V+"/gercek.json"))}
det=collections.defaultdict(list)
for l in open(V+"/sonuc.txt"):
    p=l.split(); det[p[0]].append(np.array(p[4:12],float).reshape(4,2).mean(0))
miss=[]
for ad,m in gercek.items():
    for g in m["isaretler"]:
        gc=np.array(g["kose"]).reshape(4,2).mean(0)
        if not any(np.linalg.norm(t-gc)<0.3*g["kenar"] for t in det.get(ad,[])): miss.append((ad,g))
print(len(miss))
tiles=[]
for ad,g in miss[:16]:
    im=np.fromfile(V+"/"+ad+".gray",np.uint8).reshape(540,960)
    k=np.array(g["kose"]).reshape(4,2); c=k.mean(0).astype(int); r=int(g["kenar"]*0.9)+10
    x0,y0=max(c[0]-r,0),max(c[1]-r,0); t=im[y0:y0+2*r,x0:x0+2*r]; t=cv2.resize(t,(200,200)); cv2.putText(t,f"{g['kenar']:.0f}",(5,20),0,0.6,255,2); tiles.append(t)
rows=[np.hstack(tiles[i:i+4]) for i in range(0,16,4)]
cv2.imwrite(V+"/miss.png",np.vstack(rows))
