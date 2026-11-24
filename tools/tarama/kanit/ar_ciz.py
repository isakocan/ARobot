import sys, os, json, numpy as np
from PIL import Image, ImageDraw, ImageFont
exec(open(os.path.join(os.path.dirname(__file__),"cizim.py")).read().split("for tur in sys.argv[2:]:")[0])
KOK=sys.argv[3]
SEC=[("kayit_20261123_222624","9. tarama",[0.55,0.8,0.95]),("kayit_20261123_213303","21:33",[0.6,0.85]),("kayit_20261123_222445","8. tarama (sicramadan sonra)",[0.6,0.85])]
def Rm(q):
    x,y,z,w=q; return np.array([[1-2*(y*y+z*z),2*(x*y-w*z),2*(x*z+w*y)],[2*(x*y+w*z),1-2*(x*x+z*z),2*(y*z-w*x)],[2*(x*z-w*y),2*(y*z+w*x),1-2*(x*x+y*y)]])
def Ry(a): return np.array([[np.cos(a),0,np.sin(a)],[0,1,0],[-np.sin(a),0,np.cos(a)]])
def bul(n):
    for k in ("kayitlar_24kas","kayitlar_23kas"):
        p=os.path.join(KOK,k,"telefon","kayitlar",n)
        if os.path.isdir(p): return p
satirlar=[]
for n,ad,oranlar in SEC:
    K=bul(n); fr=[json.loads(l) for l in open(os.path.join(K,"kareler.jsonl")) if l.strip()]; fr=[f for f in fr if f.get("depth_file") and f.get("jpg")]
    poz={int(r[0]):(r[1],np.array(r[2:5])) for r in np.load(os.path.join(D,"ref_%s.npz"%n))["poz"]}
    harita={}
    for tur in ("eskiduz","son"):
        G=raster(json.load(open(os.path.join(D,"%s-%s"%(n,tur),"ucgen.json")))["ucgen"]); zy=json.load(open(os.path.join(D,"%s-%s"%(n,tur),"zemin.json")))["zemin_y"]
        harita[tur]=(G,zy)
    for o in oranlar:
        f=fr[int(o*(len(fr)-1))]
        img=Image.open(os.path.join(K,f["jpg"])).convert("RGB"); W_,H_=img.size
        fx=f["fx"]*W_/f["intrinsics_w"]; fy=f["fy"]*H_/f["intrinsics_h"]; cx=f["cx"]*W_/f["intrinsics_w"]; cy=f["cy"]*H_/f["intrinsics_h"]
        v,u=np.mgrid[0:H_,0:W_]; L=np.stack([-(v+0.5-cy)/fy,-(u+0.5-cx)/fx,np.ones((H_,W_))],-1)
        dw,dh=f["depth_w"],f["depth_h"]; dd=np.fromfile(os.path.join(K,f["depth_file"]),dtype=np.uint16).reshape(dh,dw)*0.001
        dI=dd[np.minimum((v*dh)//H_,dh-1),np.minimum((u*dw)//W_,dw-1)]
        p=np.array([f["px"],f["py"],f["pz"]]); r=Rm((f["qx"],f["qy"],f["qz"],f["qw"]))
        paneller=[np.array(img)]
        for tur in ("eskiduz","son"):
            G,zy=harita[tur]
            if tur=="son" and f["index"] in poz: A,T=poz[f["index"]]; rr=Ry(A)@r; pp=Ry(A)@p+T
            else: rr=r; pp=p
            Dw=L@rr.T; t=(zy-pp[1])/Dw[...,1]; X=pp[0]+t*Dw[...,0]; Zc=pp[2]+t*Dw[...,2]
            ok=(t>0)&((dI<=0)|(dI>t-0.10))
            gx=np.floor(X/P).astype(np.int64); gz=np.floor(Zc/P).astype(np.int64)
            ic=np.zeros((H_,W_),bool); idx=np.where(ok)
            ic[idx]=[ (a,b) in G for a,b in zip(gx[idx].tolist(),gz[idx].tolist())]
            a=np.array(img).astype(float); renk=np.array([40,230,170.])
            a[ic]=a[ic]*0.45+renk*0.55
            # kenar
            k=ic^np.roll(ic,1,0)|ic^np.roll(ic,1,1); a[k]=[255,200,40]
            paneller.append(a.astype(np.uint8))
        satir=np.concatenate(paneller,1); satirlar.append((Image.fromarray(satir),"%s  t=%.0f sn"%(ad,f["image_timestamp_s"]-fr[0]["image_timestamp_s"])))
Wt=satirlar[0][0].size[0]; Ht=sum(s.size[1]+30 for s,_ in satirlar)+40
out=Image.new("RGB",(Wt,Ht),(255,255,255)); dr=ImageDraw.Draw(out)
try: fnt=ImageFont.truetype("arial.ttf",22)
except: fnt=None
dr.text((10,8),"Kamera goruntusu   |   ESKI (telefondaki)   |   YENI     (yesil = acik zemin, sari = sinir)",fill=(0,0,0),font=fnt)
y=40
for s,b in satirlar:
    dr.text((10,y+2),b,fill=(0,0,0),font=fnt); out.paste(s,(0,y+28)); y+=s.size[1]+30
out=out.resize((Wt//2,Ht//2)); out.save(os.path.join(D,"3_ar.jpg"),quality=85); print("yazildi",out.size)
