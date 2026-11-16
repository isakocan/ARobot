"""Parametre çalışması yapılandırma listesi. Her varyant: yaml (Bolum.anahtar=deger listesi) ve donusum (ekdortgen_donustur.Donusturucu seçenekleri, codec).
Taban: ayar_ekdortgen.yaml + varsayılan dönüşüm (1280x640, f çarpanı 1, taban maske). Not: bu stella sürümünde max_num_keypoints YOK;
nokta sayısı Preprocessing.min_size (ızgara hücre alanı, piksel kare) ile, FAST eşikleri ve katman sayısıyla belirlenir. min_size 800 ~ 28 px hücre ~ 900 nokta/kare."""
from collections import OrderedDict

V = OrderedDict()
def ekle(ad, yaml=(), donusum=None, grup="", hiz=1.0, ikili=None, ekstra=""):
    V[ad] = dict(yaml=list(yaml), donusum=donusum or {}, grup=grup, hiz=hiz)
    if ikili:
        V[ad]["ikili"] = ikili
    if ekstra:
        V[ad]["ekstra"] = ekstra

ekle("taban", grup="taban")
# 1. nokta sayısı ve dedektör
for ms in (1600, 400, 200, 100):
    ekle("nokta_min_size_%d" % ms, ["Preprocessing.min_size=%d" % ms], grup="1_nokta")
ekle("fast_10_4", ["Feature.ini_fast_threshold=10", "Feature.min_fast_threshold=4"], grup="1_nokta")
ekle("fast_30_10", ["Feature.ini_fast_threshold=30", "Feature.min_fast_threshold=10"], grup="1_nokta")
ekle("katman_12_olcek1.1", ["Feature.scale_factor=1.1", "Feature.num_levels=12"], grup="1_nokta")
ekle("katman_6_olcek1.3", ["Feature.scale_factor=1.3", "Feature.num_levels=6"], grup="1_nokta")
# 2. giriş boyutu
ekle("boyut_1600x800", ["Camera.cols=1600", "Camera.rows=800"], dict(g=1600, y=800, interp="cubic"), grup="2_boyut")
ekle("boyut_1920x960", ["Camera.cols=1920", "Camera.rows=960"], dict(g=1920, y=960, interp="cubic"), grup="2_boyut")
# 3. CLAHE
ekle("clahe_2", donusum=dict(clahe=(2.0, 8)), grup="3_clahe")
ekle("clahe_4", donusum=dict(clahe=(4.0, 8)), grup="3_clahe")
# 4. maske
ekle("maske_kutupsuz", ["Preprocessing.mask_rectangles=[]"], grup="4_maske")
ekle("maske_dar", donusum=dict(maske_modu="dar"), grup="4_maske")
ekle("maske_dar_kutupsuz", ["Preprocessing.mask_rectangles=[]"], dict(maske_modu="dar"), grup="4_maske")
# 5. izdüşüm
for fo in (0.94, 0.97, 1.03, 1.06):
    ekle("odak_%+d" % round((fo - 1) * 100), donusum=dict(f_olcek=fo), grup="5_izdusum")
for lens in (1, 2):
    for ek, d in (("x+5", (5.0, 0.0)), ("x-5", (-5.0, 0.0)), ("y+5", (0.0, 5.0)), ("y-5", (0.0, -5.0))):
        ekle("merkez_%d_%s" % (lens, ek), donusum={("m%d" % lens): d}, grup="5_izdusum")
# 6. haritalama eşikleri
ekle("harita_baseline_0.01", ["Mapping.baseline_dist_thr_ratio=0.01"], grup="6_harita")
ekle("harita_baseline_0.005", ["Mapping.baseline_dist_thr_ratio=0.005"], grup="6_harita")
ekle("harita_artik_gozlem_0.8", ["Mapping.redundant_obs_ratio_thr=0.8"], grup="6_harita")
ekle("harita_residual_0.4", ["Mapping.residual_deg_thr=0.4"], grup="6_harita")
ekle("harita_residual_0.1", ["Mapping.residual_deg_thr=0.1"], grup="6_harita")
ekle("baslatma_paralaks_3", ["Initializer.parallax_deg_threshold=3.0"], grup="6_harita")
# 7. sıkıştırma
ekle("h264_4M_taban", donusum=dict(codec="h264_4M"), grup="7_h264")
# --- 2. tur: ince merkez taraması ve birleşim ---
for ek, d in (("x+2", (2.0, 0.0)), ("x-2", (-2.0, 0.0)), ("y-2", (0.0, -2.0)), ("y+2", (0.0, 2.0)), ("y+8", (0.0, 8.0))):
    ekle("merkez_1_%s" % ek, donusum={"m1": d}, grup="5_izdusum_ince")
ekle("kombine_clahe4_katman12_min200", ["Feature.scale_factor=1.1", "Feature.num_levels=12", "Preprocessing.min_size=200"], dict(clahe=(4.0, 8)), grup="8_kombine")
# --- 3. tur: patlama incelemesi (rastgelelik kaynağı ve çare) ---
ekle("tohum_sabit", ["Initializer.use_fixed_seed=true"], grup="9_patlama")
ekle("hiz_0.5", grup="9_patlama", hiz=0.5)
ekle("hiz_2", grup="9_patlama", hiz=2.0)
ekle("baslat_ransac_500", ["Initializer.num_ransac_iterations=500"], grup="9_patlama")
ekle("baslat_min_ucgen_150", ["Initializer.min_num_triangulated_pts=150", "Initializer.min_num_valid_pts=150"], grup="9_patlama")
ekle("baslat_paralaks_2", ["Initializer.parallax_deg_threshold=2.0"], grup="9_patlama")
# --- 4. tur: başlatma bekçisi (kopru2: nokta sayısı oranı < 1,2 ise sıfırla) ---
KOPRU2 = "kopru2/build/kopru"
ekle("bekci_taban", grup="10_bekci", ikili=KOPRU2, ekstra="--bekci 1.2")
ekle("bekci_min_size_400", ["Preprocessing.min_size=400"], grup="10_bekci", ikili=KOPRU2, ekstra="--bekci 1.2")
ekle("bekci_min_size_200", ["Preprocessing.min_size=200"], grup="10_bekci", ikili=KOPRU2, ekstra="--bekci 1.2")
# --- 5. tur: kazanan birleşimleri (başlatma paralaksı 2 derece + nokta artırma) ---
ekle("birlesim_paralaks2", ["Initializer.parallax_deg_threshold=2.0"], grup="11_birlesim")
ekle("birlesim_kombine_paralaks2", ["Initializer.parallax_deg_threshold=2.0", "Feature.scale_factor=1.1", "Feature.num_levels=12", "Preprocessing.min_size=200"], dict(clahe=(4.0, 8)), grup="11_birlesim")
ekle("birlesim_min100_paralaks2", ["Initializer.parallax_deg_threshold=2.0", "Preprocessing.min_size=100"], grup="11_birlesim")
