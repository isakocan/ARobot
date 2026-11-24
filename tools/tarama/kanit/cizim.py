import sys, os, json, numpy as np
exec(open(os.path.join(os.path.dirname(__file__),"kanit.py")).read().split("sonuc={}")[0])
P=0.025
def raster(u):
    t=np.array(u).reshape(-1,3,2)
    if len(t)==0: return set()
    out=set()
    for tri in t:
        x0,z0=np.floor(tri.min(0)/P).astype(int); x1,z1=np.floor(tri.max(0)/P).astype(int)
        gx,gz=np.meshgrid(np.arange(x0,x1+1),np.arange(z0,z1+1)); px=(gx+0.5)*P; pz=(gz+0.5)*P
        (ax,az),(bx,bz),(cx,cz)=tri; d=(bx-ax)*(cz-az)-(bz-az)*(cx-ax)
        if abs(d)<1e-12: continue
        l1=((bx-px)*(cz-pz)-(bz-pz)*(cx-px))/d; l2=((cx-px)*(az-pz)-(cz-pz)*(ax-px))/d
        ic=(l1>=-1e-6)&(l2>=-1e-6)&(l1+l2<=1+1e-6)
        out.update(zip(gx[ic].tolist(),gz[ic].tolist()))
    return out
for tur in sys.argv[2:]:
    tz=te=tm=0; sat=[]
    for n,ad in ADLAR:
        Z,E,B,ilk,tis,sic=ref(n)
        G=raster(json.load(open(os.path.join(D,"%s-%s"%(n,tur),"ucgen.json")))["ucgen"])
        hz=set((x//2,z//2) for x,z in G)  # 5 cm hucreye
        on=sum(1 for x,z in G if (x//2,z//2) in E)*P*P
        eksik=len(Z-hz)*0.0025; dogru=sum(1 for x,z in G if (x//2,z//2) in Z)*P*P
        tz+=dogru; te+=on; tm+=eksik; sat.append("%.2f/%.2f/%.2f"%(dogru,on,eksik))
    print("%-8s ekranda dogru zemin %.1f m2 | ekranda esya/duvar ustu %.2f m2 | derinlik zemini ekranda yok %.2f m2 || %s"%(tur,tz,te,tm," ".join(sat)))
