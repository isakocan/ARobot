// PatchMatch (3rd/patch_match_dense) sürücüsü: stella'sız, hazır pozlarla. Girdi: yogun_girdi_hazirla.py klasörü.
// Kullanım: surucu <girdi_klasoru> <cikti_klasoru> [min_stereo_score=0]
// Çıktı: <cikti>/cikti_JJJ_depth.f32 (320x640 float, ışın boyunca mesafe), cikti_JJJ_nokta.bin (N kez float x,y,z + uint8 r,g,b),
//        cikti.txt (JJJ görünüm_sırası_varsayımı n_nokta). JJJ = kütüphaneden çıkış sırası; hangi görünüme ait olduğu
//        çatalın dense_module.cc eşlemesine göre (ilk çıkış = görünüm numDropped()), analiz betiği doğrular.
#include "patch_match.hpp"
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <chrono>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <sstream>

static std::vector<double> read_nums(const std::string &p) {
    std::ifstream f(p); std::vector<double> v; double x;
    while (f >> x) v.push_back(x);
    return v;
}

int main(int argc, char **argv) {
    if (argc < 3) { std::cerr << "kullanim: surucu girdi cikti [min_stereo_score]\n"; return 1; }
    std::string in = argv[1], out = argv[2];
    auto cfg = std::make_shared<dense::PatchMatch::Config>();
    cfg->min_patch_standard_deviation = 0; cfg->patch_size = 7; cfg->patchmatch_iterations = 4; cfg->min_score = 0.1;
    cfg->min_consistent_views = 3; cfg->depthmap_queue_size = 5; cfg->depthmap_same_depth_threshold = 0.08f;
    cfg->min_views = 1; cfg->pointcloud_queue_size = 4; cfg->pointcloud_same_depth_threshold = 0.08f;
    cfg->min_stereo_score = argc > 3 ? atof(argv[3]) : 0.0;
    dense::PatchMatch pm(cfg);
    std::vector<std::string> names;
    { std::ifstream f(in + "/index.txt"); std::string l; while (std::getline(f, l)) { std::istringstream s(l); std::string n; s >> n; names.push_back(n); } }
    const int drop = pm.numDropped(), prerun = pm.numPrerun();
    std::cout << "gorunum " << names.size() << " numDropped " << drop << " numPrerun " << prerun << " min_stereo_score " << cfg->min_stereo_score << std::endl;
    std::ofstream idx(out + "/cikti.txt");
    auto t0 = std::chrono::steady_clock::now();
    int j = 0, rejected = 0;
    for (size_t k = 0; k < names.size(); k++) {
        auto P = read_nums(in + "/" + names[k] + "_poz.txt");
        cv::Matx33d R(P[0], P[1], P[2], P[3], P[4], P[5], P[6], P[7], P[8]);
        cv::Vec3d t(P[9], P[10], P[11]);
        cv::Mat img = cv::imread(in + "/" + names[k] + ".png", cv::IMREAD_COLOR);
        cv::Mat mask = cv::imread(in + "/" + names[k] + "_maske.png", cv::IMREAD_GRAYSCALE);
        auto S = read_nums(in + "/" + names[k] + "_seyrek.txt");
        std::vector<cv::Vec3d> sparse;
        for (size_t i = 0; i + 2 < S.size(); i += 3) sparse.emplace_back(S[i], S[i + 1], S[i + 2]);
        auto rv = pm.addView(R, t, img, mask, sparse);
        if (rv != dense::PatchMatch::SUCCESS) { rejected++; std::cout << "addView reddedildi gorunum " << k << ": " << dense::PatchMatch::retval_to_cstr(rv) << std::endl; continue; }
        // çatalın dense_module'ü gibi: her başarılı addView sonrası çıktı varsa al
        cv::Mat depth; std::vector<cv::Vec3d> pts; std::vector<cv::Vec3b> col;
        while (pm.getDepth(depth, pts, col, false) == dense::PatchMatch::SUCCESS) {
            char b[64]; snprintf(b, sizeof b, "/cikti_%03d", j);
            cv::Mat d32; depth.convertTo(d32, CV_32F);
            std::ofstream fd(out + b + "_depth.f32", std::ios::binary); fd.write((const char *)d32.data, d32.total() * sizeof(float));
            std::ofstream fp(out + b + "_nokta.bin", std::ios::binary);
            for (size_t i = 0; i < pts.size(); i++) {
                float xyz[3] = {(float)pts[i][0], (float)pts[i][1], (float)pts[i][2]};
                fp.write((const char *)xyz, 12); fp.write((const char *)&col[i][0], 3);
            }
            idx << j << " " << (j + drop) << " " << pts.size() << " " << depth.rows << "x" << depth.cols << " tip" << depth.type() << "\n";
            std::cout << "cikti " << j << " (gorunum~" << j + drop << ") nokta " << pts.size() << " eklenen_gorunum " << k << std::endl;
            j++;
        }
    }
    // kuyrukta kalan çıktılar: toplam beklenen = görünüm - (depthmap_queue+pointcloud_queue) (kütüphanenin count kapısı)
    const int beklenen = (int)(names.size() - rejected) - (int)(cfg->depthmap_queue_size + cfg->pointcloud_queue_size);
    for (int w = 0; w < 6000 && j < beklenen; w++) {
        cv::Mat depth; std::vector<cv::Vec3d> pts; std::vector<cv::Vec3b> col;
        if (pm.getDepth(depth, pts, col, false) == dense::PatchMatch::SUCCESS) {
            char b[64]; snprintf(b, sizeof b, "/cikti_%03d", j);
            cv::Mat d32; depth.convertTo(d32, CV_32F);
            std::ofstream fd(out + b + "_depth.f32", std::ios::binary); fd.write((const char *)d32.data, d32.total() * sizeof(float));
            std::ofstream fp(out + b + "_nokta.bin", std::ios::binary);
            for (size_t i = 0; i < pts.size(); i++) {
                float xyz[3] = {(float)pts[i][0], (float)pts[i][1], (float)pts[i][2]};
                fp.write((const char *)xyz, 12); fp.write((const char *)&col[i][0], 3);
            }
            idx << j << " " << (j + drop) << " " << pts.size() << " " << depth.rows << "x" << depth.cols << " tip" << depth.type() << std::endl;
            std::cout << "cikti " << j << " (gorunum~" << j + drop << ") nokta " << pts.size() << " (kuyruk)" << std::endl;
            j++;
        } else std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    std::cout << "beklenen cikti " << beklenen << std::endl;
    double sn = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    std::cout << "toplam_sn " << sn << " cikti " << j << " reddedilen " << rejected << std::endl;
    return 0;
}
