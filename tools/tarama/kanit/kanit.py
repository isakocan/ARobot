import json, os, glob, sys, numpy as np
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
D=sys.argv[1]; TURLER=sys.argv[2:4] if len(sys.argv)>3 else ["eski","yeni"]
ADLAR=[("kayit_20261122_235207","22 Kas 23:52"),("kayit_20261123_000246","23 Kas 00:02"),("kayit_20261123_213303","23 Kas 21:33"),("kayit_20261123_222445","23 Kas 22:24 (8. tarama)"),("kayit_20261123_222624","23 Kas 22:26 (9. tarama)")]
def acik(p):
    a=json.load(open(p))["acik"]; return set(zip(a[0::2],a[1::2]))
def ref(n):
    z=np.load(os.path.join(D,"ref_%s.npz"%n)); h=z["h"]
    E={(int(r[0]),int(r[1])) for r in h if r[3]>=3 and r[3]>=0.3*r[5]}
    Z={(int(r[0]),int(r[1])) for r in h if r[2]>=2}-E

    B={(int(r[0]),int(r[1])) for r in h}
    ilk={(int(r[0]),int(r[1])):r[4] for r in h}
    return Z,E,B,ilk,float(z["isinma"]),list(z["sicrama"])
def olc(A,Z,E,B):
    n=max(len(A),1)
    return dict(alan=len(A)*0.0025, kapsama=100*len(A&Z)/max(len(Z),1), yanlis=100*len(A&E)/n, dayanaksiz=100*len(A-B)/n, yanlis_m2=len(A&E)*0.0025, dayanaksiz_m2=len(A-B)*0.0025)
def ara(n,tur):
    l=[]
    for p in glob.glob(os.path.join(D,"%s-%s"%(n,tur),"ara_*.json")):
        t=float(os.path.basename(p)[:-5].split("_")[2]); l.append((t,acik(p)))
    return sorted(l,key=lambda x:x[0])
sonuc={}
fig,ax=plt.subplots(5,3,figsize=(18,30))
for i,(n,ad) in enumerate(ADLAR):
    Z,E,B,ilk,tis,sic=ref(n)
    s={}
    for j,tur in enumerate(TURLER):
        A=acik(os.path.join(D,"%s-%s"%(n,tur),"zemin.json"))
        m=olc(A,Z,E,B)
        L=ara(n,tur); kay=0; once=None
        for t,a in L:
            if once is not None: kay+=len(once-a)
            once=a
        m["titreme"]=kay; m["son"]=L[-1][0]
        # hiz: derinlikte gorulen zeminin boyanmasi (gorulduyse, ilk gorulmeden kac sn sonra)
        gec=[]; 
        for k in Z:
            t0=ilk[k]; tb=next((t for t,a in L if t>=t0 and k in a),None)
            gec.append(tb-t0 if tb is not None else None)
        hic=sum(1 for g in gec if g is None); g2=sorted(g for g in gec if g is not None)
        m["boyanmayan"]=100*hic/max(len(gec),1); m["gec_ort"]=g2[len(g2)//2] if g2 else -1; m["gec90"]=g2[len(g2)*9//10] if g2 else -1
        m["L"]=L; s[tur]=m
        a=ax[i,j+1]
        def ciz(S_,renk,boy=4): 
            if S_: q=np.array(list(S_)); a.scatter(q[:,0]*0.05,q[:,1]*0.05,s=boy,c=renk,marker="s",linewidths=0)
        ciz(Z-A,"#cfeff0"); ciz(A&Z,"#1aa39a"); ciz(A-Z-E,"#9aa0a6"); ciz(A&E,"#e8202a",6)
        a.set_title("%s: %.1f m2 boyali\nderinlik zemininin %%%.0f'i boyali | engel ustune boya %%%.1f (%.2f m2)\nbir kez boyanip silinen %d hucre" % ("ESKI" if tur==TURLER[0] else "YENI",m["alan"],m["kapsama"],m["yanlis"],m["yanlis_m2"],kay),fontsize=11)
    a=ax[i,0]
    for S_,renk in ((B-Z-E,"#d0d0d0"),(Z,"#1ac6c0"),(E,"#ff8a1f")):
        if S_: q=np.array(list(S_)); a.scatter(q[:,0]*0.05,q[:,1]*0.05,s=4,c=renk,marker="s",linewidths=0)
    a.set_title("%s\nREFERANS (karelerden): turkuaz zemin, turuncu ustunde 15 cm-2 m bir sey var\nisinma %.0f sn%s" % (ad,tis,(", ARCore sicramasi t=%.0f sn (duzeltildi)"%sic[0]) if sic else ""),fontsize=11)
    xs=[k[0]*0.05 for k in B]; zs=[k[1]*0.05 for k in B]
    for a in ax[i]: a.set_aspect("equal"); a.set_xlim(min(xs)-0.3,max(xs)+0.3); a.set_ylim(min(zs)-0.3,max(zs)+0.3); a.tick_params(labelsize=8)
    sonuc[n]=(ad,s,tis,sic)
fig.suptitle("Eski ve yeni tarama, ayni kayitlar: koyu turkuaz = dogru boyali zemin, acik turkuaz = derinlik zemin gordu ama boyanmadi,\nkirmizi = ustunde esya/duvar olan yere boya (yanlis), gri = derinlikte dayanagi zayif boya",fontsize=14,y=0.995)
fig.tight_layout(rect=(0,0,1,0.985)); fig.savefig(os.path.join(D,"1_haritalar.png"),dpi=70)
# zaman
fig,ax=plt.subplots(5,1,figsize=(11,20))
for i,(n,(ad,s,tis,sic)) in enumerate(sonuc.items()):
    Z,E,B,ilk,_,_=ref(n); a=ax[i]
    for tur,st in ((TURLER[0],"--"),(TURLER[1],"-")):
        L=s[tur]["L"]; T=[t for t,_ in L]
        gor=[100*len(A&Z)/max(len(Z),1) for t,A in L]
        yan=[len(A&E)*0.0025 for t,A in L]
        a.plot(T,gor,st,color="#1aa39a",lw=2,label="%s: "%("ESKI" if tur==TURLER[0] else "YENI")+"derinlik zemininin boyali orani (%)")
        a2=a if i<0 else None
        a.plot(T,[100*y/max(len(Z)*0.0025,1e-6) for y in yan],st,color="#e8202a",lw=1.5,label="%s: "%("ESKI" if tur==TURLER[0] else "YENI")+"engel ustune boya (zemin alaninin %si)")
    a.axvline(tis,color="#888",lw=1); a.text(tis,95,"  olcek oturdu",fontsize=9,color="#555")
    for t in sic: a.axvline(t,color="#e8202a",lw=1); a.text(t,85,"  ARCore sicramasi",fontsize=9,color="#e8202a")
    a.set_ylim(0,100); a.set_title(ad,fontsize=12); a.set_xlabel("sn"); a.grid(alpha=0.3)
    if i==0: a.legend(fontsize=9,loc="center right")
fig.tight_layout(); fig.savefig(os.path.join(D,"2_zaman.png"),dpi=70)
# tablo
for n,(ad,s,tis,sic) in sonuc.items():
    for tur in TURLER:
        m=s[tur]; print("%-26s %-4s alan %5.2f m2 | zemin kapsama %%%3.0f | engel ustune %%%4.1f (%.2f m2) | dayanaksiz %%%4.1f | boyanma gecikmesi ortanca %.1f sn %%90 %.1f sn, hic boyanmayan %%%.0f | titreme %d" % (ad,tur,m["alan"],m["kapsama"],m["yanlis"],m["yanlis_m2"],m["dayanaksiz"],m["gec_ort"],m["gec90"],m["boyanmayan"],m["titreme"]))
