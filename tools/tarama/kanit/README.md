Tarama kaniti (24 Kasim): uygulamanin haritasini, kayittaki derinlik karelerinden bagimsiz cikarilan referansla karsilastirir.

1. `oda_test.sh <kayit> <D>/<kayit_adi>-<tur>` (ZEMIN_ARA=4 ZEMIN_ARA_JSON=1 KALEMSIZ=1; eski icin SICRAMA=0 ISINMA=0 KONTUR=duz)
2. `python referans.py <kayit> <D>/ref_<kayit_adi>.npz`: her kare kendi zeminini bulur, olcek isinmasi atlanir, ARCore sicramasi duzeltilir
3. `python kanit.py <D> eski son` (1_haritalar.png, 2_zaman.png ve tablo), `python ozet.py <D> tur...`, `python cizim.py <D> tur...` (ekrandaki cizim),
   `python ar_ciz.py <D> x <ARobot klasoru>` (kamera fotografina eski / yeni boya, 3_ar.jpg)
