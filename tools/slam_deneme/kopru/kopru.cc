// stella_vslam TCP köprüsü (tek gözlü, eşdikdörtgen). Örnekler (stella_vslam_examples/run_image_slam.cc) temel alındı.
// Sunucu kipi:   kopru --vocab V --config C [--mask M] [--port 9760] [--map-in F] [--mod harita|konum]
// Toplu kip:     kopru --vocab V --config C [--mask M] --dizin D [--cikti O] [--bas N] [--bitis N] [--map-in F] [--map-out F] [--mod ...]
//                D: 000000.jpg ... + zamanlar.csv (sira,kare,t_ms); O/poz.jsonl her kare için bir satır, O/harita.json son nokta/anahtar kare dökümü.
// Protokol (sunucu): istek = bir satır JSON ({"cmd":..., "n": yük_bayt}) + n bayt yük; yanıt = bir satır JSON.
//   feed: {"cmd":"feed","t_ms":int,"w":int,"h":int,"komut":[sol,sag],"tam":bool,"n":w*h*3}  yük = ham BGR eşdikdörtgen kare
//   state | mode {"mode":"harita"|"konum"} | save {"path"} | load {"path","mode"} | reset | quit
#include "stella_vslam/system.h"
#include "stella_vslam/config.h"
#include "stella_vslam/camera/base.h"
#include "stella_vslam/publish/frame_publisher.h"
#include "stella_vslam/publish/map_publisher.h"
#include "stella_vslam/data/keyframe.h"
#include "stella_vslam/data/landmark.h"

#include <arpa/inet.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <sys/socket.h>
#include <unistd.h>

#include <chrono>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <memory>
#include <regex>
#include <set>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#include <opencv2/core.hpp>
#include <opencv2/imgcodecs.hpp>
#include <spdlog/spdlog.h>

using clk = std::chrono::steady_clock;
static double ms_since(clk::time_point a, clk::time_point b) { return std::chrono::duration<double, std::milli>(b - a).count(); }

struct Motor {
    std::shared_ptr<stella_vslam::config> cfg;
    std::string vocab;
    std::shared_ptr<stella_vslam::system> slam;
    cv::Mat mask;
    bool mapping = true;
    long frames = 0;

    void baslat(const std::string& map_in, bool harita_kipi) {
        if (slam) {
            slam->shutdown();
            slam.reset();
        }
        slam = std::make_shared<stella_vslam::system>(cfg, vocab);
        bool need_init = true;
        if (!map_in.empty()) {
            if (!slam->load_map_database(map_in)) throw std::runtime_error("harita yuklenemedi: " + map_in);
            need_init = false;
        }
        slam->startup(need_init);
        mapping = harita_kipi;
        if (!harita_kipi) slam->disable_mapping_module();
        frames = 0;
    }
    void mod(bool harita_kipi) {
        if (harita_kipi == mapping) return;
        if (harita_kipi) slam->enable_mapping_module(); else slam->disable_mapping_module();
        mapping = harita_kipi;
    }
};

static void f3(std::ostringstream& o, double v) { char b[32]; std::snprintf(b, sizeof b, "%.4f", v); o << b; }

static void mat44(std::ostringstream& o, const stella_vslam::Mat44_t& m) {
    o << "[";
    for (int r = 0; r < 4; ++r)
        for (int c = 0; c < 4; ++c) { f3(o, m(r, c)); if (r != 3 || c != 3) o << ","; }
    o << "]";
}

static void harita_dok(Motor& m, std::ostringstream& o) {
    auto mp = m.slam->get_map_publisher();
    std::vector<std::shared_ptr<stella_vslam::data::keyframe>> kfs;
    mp->get_keyframes(kfs);
    std::vector<std::shared_ptr<stella_vslam::data::landmark>> lms;
    std::set<std::shared_ptr<stella_vslam::data::landmark>> local;
    mp->get_landmarks(lms, local);
    o << "\"noktalar\":[";
    bool ilk = true;
    for (auto& l : lms) {
        if (!l || l->will_be_erased()) continue;
        auto p = l->get_pos_in_world();
        if (!ilk) o << ",";
        ilk = false;
        o << "[";
        f3(o, p(0)); o << ","; f3(o, p(1)); o << ","; f3(o, p(2)); o << "," << local.count(l) << "," << l->id_;
        o << "]";
    }
    o << "],\"anahtar_kareler\":[";
    ilk = true;
    for (auto& k : kfs) {
        if (!k || k->will_be_erased()) continue;
        if (!ilk) o << ",";
        ilk = false;
        o << "{\"id\":" << k->id_ << ",\"t\":" << k->timestamp_ << ",\"poz_wc\":";
        mat44(o, k->get_pose_wc());
        o << ",\"gozlem\":[";
        bool ig = true;
        for (auto& lm : k->get_landmarks()) {
            if (!lm || lm->will_be_erased()) continue;
            if (!ig) o << ",";
            ig = false;
            o << lm->id_;
        }
        o << "]}";
    }
    o << "]";
}

// bir kareyi besle, yanıt JSON'unu (süslü parantezsiz gövde değil, tam nesne) döndür
static std::string besle(Motor& m, const cv::Mat& img, double t_ms, bool tam, double bayt_alma_ms, const std::string& komut_json) {
    auto t0 = clk::now();
    auto pose = m.slam->feed_monocular_frame(img, t_ms / 1000.0, m.mask);
    auto t1 = clk::now();
    m.frames++;
    auto fp = m.slam->get_frame_publisher();
    auto mp = m.slam->get_map_publisher();
    std::string st = fp->get_tracking_state();
    auto kl = fp->get_keypoints_and_landmarks();
    std::ostringstream o;
    o << "{\"ok\":true,\"t_ms\":" << (long long)t_ms << ",\"durum\":\"" << st << "\",\"izleniyor\":" << ((pose && st == "Tracking") ? "true" : "false");
    o << ",\"mod\":\"" << (m.mapping ? "harita" : "konum") << "\"";
    if (!komut_json.empty()) o << ",\"komut\":" << komut_json;
    if (pose) {
        o << ",\"poz_cw\":"; mat44(o, *pose);
        stella_vslam::Mat44_t wc = pose->inverse();
        o << ",\"kamera_konum\":["; f3(o, wc(0, 3)); o << ","; f3(o, wc(1, 3)); o << ","; f3(o, wc(2, 3)); o << "]";
    }
    int n_kp = (int)kl.first.size(), n_es = 0;
    std::ostringstream ip;
    ip << "[";
    bool ilk = true;
    for (size_t i = 0; i < kl.first.size(); ++i) {
        const auto& lm = kl.second[i];
        if (lm && !lm->will_be_erased()) {
            ++n_es;
            if (!ilk) ip << ",";
            ilk = false;
            char b[48]; std::snprintf(b, sizeof b, "[%.1f,%.1f]", kl.first[i].pt.x, kl.first[i].pt.y); ip << b;
        }
    }
    ip << "]";
    std::vector<std::shared_ptr<stella_vslam::data::keyframe>> kfs;
    unsigned nkf = mp->get_keyframes(kfs);
    std::vector<std::shared_ptr<stella_vslam::data::landmark>> lms;
    std::set<std::shared_ptr<stella_vslam::data::landmark>> local;
    unsigned nlm = mp->get_landmarks(lms, local);
    o << ",\"ipuclari\":" << ip.str() << ",\"nokta_sayisi\":" << n_kp << ",\"eslesen_sayisi\":" << n_es
      << ",\"harita_nokta_sayisi\":" << nlm << ",\"yerel_nokta_sayisi\":" << local.size() << ",\"anahtar_kare_sayisi\":" << nkf;
    if (tam) { o << ","; harita_dok(m, o); }
    auto t2 = clk::now();
    o << ",\"sureler_ms\":{\"kopru_alma\":" << bayt_alma_ms << ",\"takip\":" << ms_since(t0, t1)
      << ",\"cikarim\":" << fp->get_extraction_time_elapsed_ms() << ",\"toplam_sunucu\":" << ms_since(t0, t2) + bayt_alma_ms << "}}";
    return o.str();
}

static std::string kv(const std::string& s, const std::string& key, const std::string& def = "") {
    std::regex re("\"" + key + "\"\\s*:\\s*(\"[^\"]*\"|-?[0-9.eE+-]+|true|false)");
    std::smatch mm;
    if (std::regex_search(s, mm, re)) {
        std::string v = mm[1];
        if (!v.empty() && v[0] == '"') v = v.substr(1, v.size() - 2);
        return v;
    }
    return def;
}
static std::string komut_dizi(const std::string& s) {
    std::regex re("\"komut\"\\s*:\\s*(\\[[^\\]]*\\])");
    std::smatch mm;
    return std::regex_search(s, mm, re) ? std::string(mm[1]) : std::string();
}

static bool oku_tam(int fd, void* buf, size_t n) {
    char* p = (char*)buf;
    while (n) { ssize_t r = ::recv(fd, p, n, 0); if (r <= 0) return false; p += r; n -= r; }
    return true;
}
static bool oku_satir(int fd, std::string& s) {
    s.clear();
    char c;
    while (true) { ssize_t r = ::recv(fd, &c, 1, 0); if (r <= 0) return false; if (c == '\n') return true; s.push_back(c); }
}
static void yaz(int fd, const std::string& s) {
    std::string t = s + "\n";
    size_t k = 0;
    while (k < t.size()) { ssize_t r = ::send(fd, t.data() + k, t.size() - k, MSG_NOSIGNAL); if (r <= 0) return; k += r; }
}

static int sunucu(Motor& m, int port) {
    int ls = ::socket(AF_INET, SOCK_STREAM, 0);
    int one = 1;
    setsockopt(ls, SOL_SOCKET, SO_REUSEADDR, &one, sizeof one);
    sockaddr_in a{};
    a.sin_family = AF_INET;
    a.sin_addr.s_addr = htonl(INADDR_ANY);   // WSL localhost yönlendirmesi için; yalnız yerel kullanım
    a.sin_port = htons(port);
    if (::bind(ls, (sockaddr*)&a, sizeof a) < 0 || ::listen(ls, 1) < 0) { perror("bind/listen"); return 1; }
    std::cerr << "KOPRU_HAZIR port " << port << std::endl;
    bool cik = false;
    while (!cik) {
        int fd = ::accept(ls, nullptr, nullptr);
        if (fd < 0) continue;
        setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &one, sizeof one);
        std::string h;
        while (oku_satir(fd, h)) {
            try {
                std::string cmd = kv(h, "cmd");
                long n = std::atol(kv(h, "n", "0").c_str());
                std::vector<unsigned char> yuk(n);
                auto ta = clk::now();
                if (n > 0 && !oku_tam(fd, yuk.data(), n)) break;
                double alma = ms_since(ta, clk::now());
                if (cmd == "feed") {
                    int w = std::atoi(kv(h, "w").c_str()), hh = std::atoi(kv(h, "h").c_str());
                    cv::Mat img(hh, w, CV_8UC3, yuk.data());
                    yaz(fd, besle(m, img, std::atof(kv(h, "t_ms").c_str()), kv(h, "tam") == "true", alma, komut_dizi(h)));
                } else if (cmd == "state") {
                    std::ostringstream o; o << "{\"ok\":true,"; harita_dok(m, o); o << "}"; yaz(fd, o.str());
                } else if (cmd == "mode") {
                    m.mod(kv(h, "mode") != "konum"); yaz(fd, std::string("{\"ok\":true,\"mod\":\"") + (m.mapping ? "harita" : "konum") + "\"}");
                } else if (cmd == "save") {
                    bool ok = m.slam->save_map_database(kv(h, "path")); yaz(fd, std::string("{\"ok\":") + (ok ? "true" : "false") + "}");
                } else if (cmd == "load") {
                    m.baslat(kv(h, "path"), kv(h, "mode", "konum") != "konum"); yaz(fd, "{\"ok\":true}");
                } else if (cmd == "reset") {
                    m.baslat("", true); yaz(fd, "{\"ok\":true}");
                } else if (cmd == "quit") {
                    yaz(fd, "{\"ok\":true}"); cik = true; break;
                } else {
                    yaz(fd, "{\"ok\":false,\"hata\":\"bilinmeyen komut\"}");
                }
            } catch (const std::exception& e) {
                yaz(fd, std::string("{\"ok\":false,\"hata\":\"") + e.what() + "\"}");
            }
        }
        ::close(fd);
    }
    m.slam->shutdown();
    return 0;
}

static int toplu(Motor& m, const std::string& dizin, const std::string& cikti, long bas, long bitis, const std::string& map_out, bool gercek_zaman) {
    std::ifstream f(dizin + "/zamanlar.csv");
    std::string satir;
    std::getline(f, satir);
    std::vector<std::pair<std::string, double>> kareler;
    while (std::getline(f, satir)) {
        if (satir.empty()) continue;
        int s; long kn; double t;
        if (std::sscanf(satir.c_str(), "%d,%ld,%lf", &s, &kn, &t) == 3) {
            char b[512]; std::snprintf(b, sizeof b, "%s/%06d.jpg", dizin.c_str(), s);
            kareler.push_back({b, t});
        }
    }
    if (bitis < 0 || bitis > (long)kareler.size()) bitis = kareler.size();
    std::ofstream poz(cikti + "/poz.jsonl");
    auto t_bas = clk::now();
    double toplam_takip = 0;
    for (long i = bas; i < bitis; ++i) {
        if (gercek_zaman) {   // kare zamanına göre bekle: eşzamansız harita modülü gerçek kullanımdaki gibi iş yapabilsin
            auto hedef = t_bas + std::chrono::microseconds((long long)((kareler[i].second - kareler[bas].second) * 1000.0));
            if (clk::now() < hedef) std::this_thread::sleep_until(hedef);
        }
        cv::Mat img = cv::imread(kareler[i].first, cv::IMREAD_COLOR);
        if (img.empty()) { poz << "{\"ok\":false,\"i\":" << i << "}\n"; continue; }
        std::string j = besle(m, img, kareler[i].second, false, 0.0, "");
        poz << "{\"i\":" << i << "," << j.substr(1) << "\n";
        if (i % 200 == 0) { std::cerr << "kare " << i << "/" << bitis << std::endl; poz.flush(); }
    }
    double wall = ms_since(t_bas, clk::now()) / 1000.0;
    std::cerr << "toplu bitti: " << (bitis - bas) << " kare, " << wall << " sn, " << (bitis - bas) / wall << " kare/sn" << std::endl;
    // mapleme eşzamansız: kısa bekle, sonra dök
    std::this_thread::sleep_for(std::chrono::seconds(3));
    while (m.slam->loop_BA_is_running()) std::this_thread::sleep_for(std::chrono::milliseconds(100));
    std::ofstream hd(cikti + "/harita.json");
    std::ostringstream o; o << "{"; harita_dok(m, o); o << "}"; hd << o.str();
    if (!map_out.empty()) { bool ok = m.slam->save_map_database(map_out); std::cerr << "harita kaydi " << (ok ? "tamam" : "BASARISIZ") << std::endl; }
    m.slam->shutdown();
    return 0;
}

int main(int argc, char** argv) {
    std::string vocab, config, mask, map_in, map_out, dizin, cikti = ".", mod = "harita";
    int port = 9760; long bas = 0, bitis = -1;
    std::string loglevel = "warn"; bool gercek_zaman = false;
    for (int i = 1; i + 1 < argc; i += 2) {
        std::string k = argv[i], v = argv[i + 1];
        if (k == "--vocab") vocab = v; else if (k == "--config") config = v; else if (k == "--mask") mask = v;
        else if (k == "--map-in") map_in = v; else if (k == "--map-out") map_out = v; else if (k == "--dizin") dizin = v;
        else if (k == "--cikti") cikti = v; else if (k == "--port") port = std::atoi(v.c_str()); else if (k == "--bas") bas = std::atol(v.c_str());
        else if (k == "--bitis") bitis = std::atol(v.c_str()); else if (k == "--mod") mod = v; else if (k == "--log") loglevel = v; else if (k == "--gercek-zaman") gercek_zaman = (v == "1");
        else { std::cerr << "bilinmeyen secenek " << k << std::endl; return 2; }
    }
    spdlog::set_pattern("[%H:%M:%S.%e] %^[%L] %v%$");
    spdlog::set_level(spdlog::level::from_str(loglevel));
    Motor m;
    m.cfg = std::make_shared<stella_vslam::config>(config);
    m.vocab = vocab;
    if (!mask.empty()) m.mask = cv::imread(mask, cv::IMREAD_GRAYSCALE);
    try {
        m.baslat(map_in, mod != "konum");
        return dizin.empty() ? sunucu(m, port) : toplu(m, dizin, cikti, bas, bitis, map_out, gercek_zaman);
    } catch (const std::exception& e) {
        std::cerr << "HATA: " << e.what() << std::endl;
        return 1;
    }
}
