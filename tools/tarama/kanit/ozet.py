import sys, os
exec(open(os.path.join(os.path.dirname(__file__),"kanit.py")).read().split("sonuc={}")[0])
for tur in sys.argv[2:]:
    t=[0]*6; satir=[]
    for n,ad in ADLAR:
        Z,E,B,ilk,tis,sic=ref(n)
        A=acik(os.path.join(D,"%s-%s"%(n,tur),"zemin.json")); m=olc(A,Z,E,B)
        L=ara(n,tur); kay=0; once=None
        for tt,a in L:
            if once is not None: kay+=len(once-a)
            once=a
        satir.append("%2.0f/%4.2f/%4d"%(m["kapsama"],m["yanlis_m2"],kay))
        t[0]+=m["kapsama"]/5; t[1]+=m["yanlis_m2"]; t[2]+=kay; t[3]+=m["alan"]
    print("%-10s kapsama ort %%%.1f | engel ustune toplam %.2f m2 | titreme %5d | alan %.1f m2 || %s"%(tur,t[0],t[1],t[2],t[3]," ".join(satir)))
