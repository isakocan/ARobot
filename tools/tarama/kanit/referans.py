"""Kayittan bagimsiz referans: her kare kendi zeminini bulur; noktalar zemin (+-4 cm) / engel (8 cm-2 m) diye hucreye sayilir.
Olcek isinmasi bitmeden kareler alinmaz; ARCore sicramasi sureklilikle duzeltilir. Cikti: <kayit>.npz"""
import json, sys, os, numpy as np
K, OUT = sys.argv[1], sys.argv[2]
fr = [json.loads(l) for l in open(os.path.join(K, "kareler.jsonl")) if l.strip()]
fr = [f for f in fr if f.get("depth_file")]
t0 = fr[0]["image_timestamp_s"]
def R(q):
    x,y,z,w = q
    return np.array([[1-2*(y*y+z*z),2*(x*y-w*z),2*(x*z+w*y)],[2*(x*y+w*z),1-2*(x*x+z*z),2*(y*z-w*x)],[2*(x*z-w*y),2*(y*z+w*x),1-2*(x*x+y*y)]])
def Ry(a): return np.array([[np.cos(a),0,np.sin(a)],[0,1,0],[-np.sin(a),0,np.cos(a)]])
A=0.0; Tv=np.zeros(3); onc=None; sicrama=[]
isindi=False; pen=[]; F={}; O={}; N={}; ilk={}; tisinma=None; POZ=[]
for f in fr:
    t=f["image_timestamp_s"]-t0
    p=np.array([f["px"],f["py"],f["pz"]]); r=R((f["qx"],f["qy"],f["qz"],f["qw"]))
    if onc is not None:
        tp,pp,rp=onc; dt=t-tp; d=np.linalg.norm(p-pp); dy=p[1]-pp[1]
        if 1e-4<dt<=1 and ((d>0.3 and d/dt>2.5) or (abs(dy)>0.2 and abs(dy)/dt>1.2)):
            D=rp@r.T; a=np.arctan2(D[0,2]-D[2,0],D[0,0]+D[2,2])
            c=Ry(A)@pp+Tv; A+=a; Tv=c-Ry(A)@p; sicrama.append(round(t,1))
    onc=(t,p,r)
    w,h=f["depth_w"],f["depth_h"]
    dd=np.fromfile(os.path.join(K,f["depth_file"]),dtype=np.uint16).reshape(h,w)*0.001
    cf=f.get("conf_file"); g=np.fromfile(os.path.join(K,cf),dtype=np.uint8).reshape(h,w) if cf and os.path.exists(os.path.join(K,cf)) else np.full((h,w),255,np.uint8)
    fx=f["fx"]*w/f["intrinsics_w"]; fy=f["fy"]*h/f["intrinsics_h"]; cx=f["cx"]*w/f["intrinsics_w"]; cy=f["cy"]*h/f["intrinsics_h"]
    v,u=np.mgrid[0:h,0:w]; L=np.stack([-(v+0.5-cy)/fy,-(u+0.5-cx)/fx,np.ones((h,w))],-1).reshape(-1,3)
    dd=dd.reshape(-1); g=g.reshape(-1); m=(dd>0.3)&(dd<3.0)&(g>=128)
    W=(L[m]@(Ry(A)@r).T)*dd[m,None]+(Ry(A)@p+Tv)
    py=(Ry(A)@p+Tv)[1]
    ys=W[:,1]; s=ys[(ys>py-2.1)&(ys<py-0.3)]
    if len(s)<300: continue
    ed=np.arange(py-2.1,py-0.3,0.01); hst,_=np.histogram(s,bins=ed); sm=np.convolve(hst,np.ones(5),'same'); b=sm.max(); z=None
    for i in range(1,len(sm)-1):
        if sm[i]>=0.35*b and sm[i]>=sm[i-1] and sm[i]>=sm[i+1]: z=ed[i]+0.005; break
    if z is None: continue
    if not isindi:
        pen.append((t,z)); pen=[q for q in pen if t-q[0]<=3]
        zs=[q[1] for q in pen]
        if len(pen)>=8 and max(zs)-min(zs)<=0.04: isindi=True; tisinma=t
        else: continue
    hk=W[:,1]-z
    POZ.append((f['index'],A,*Tv))
    for sec,D_ in ((np.abs(hk)<0.04,F),((hk>0.15)&(hk<2.0),O),((hk>-0.04)&(hk<2.0),N)):
        c=np.unique(np.floor(W[sec][:,[0,2]]/0.05).astype(int),axis=0)
        for k in map(tuple,c):
            D_[k]=D_.get(k,0)+1
            if D_ is F and k not in ilk: ilk[k]=t
kz=set(F)|set(O)
arr=np.array([[k[0],k[1],F.get(k,0),O.get(k,0),ilk.get(k,-1),N.get(k,0)] for k in kz])
np.savez(OUT,h=arr,poz=np.array(POZ),sicrama=np.array(sicrama),isinma=tisinma if tisinma is not None else -1)
print(os.path.basename(K),"hucre",len(kz),"sicrama",sicrama,"isinma t=%.1f"%tisinma)
