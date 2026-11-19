// SVO'nun kendi vikit omni kamerasiyla yaml dogrulamasi: bilinen yonleri izdusurup geri cevirir.
// r = f*theta modeline gore beklenen piksel ile karsilastirilir.
#include <vikit/cameras.h>
#include <vikit/cameras/camera_geometry_base.h>
#include <vikit/cameras/ncamera.h>
#include <iostream>
#include <cmath>
int main(int argc, char** argv) {
  auto ncam = vk::cameras::NCamera::loadFromYaml(argv[1]);  // svo ile ayni yukleme yolu (maske dahil)
  auto cam = ncam->getCameraShared(0);
  const double f = 180.87, cx = 322.42, cy = 319.04;
  double maxerr = 0, maxang = 0;
  for (int th = 0; th <= 97; th += 1) for (int az = 0; az < 360; az += 15) {
    double t = th * M_PI / 180, a = az * M_PI / 180;
    Eigen::Vector3d d(std::sin(t) * std::cos(a), std::sin(t) * std::sin(a), std::cos(t));  // z = optik eksen
    Eigen::Vector2d px; cam->project3(d, &px);
    Eigen::Vector2d ex(cx + f * t * std::cos(a), cy + f * t * std::sin(a));  // dir.y -> piksel +y burada ayni isaret (a olcusu pikselde)
    // piksel y asagi, kamera y yukari: dir=(x,y,z) -> piksel = c + (x,-y)/|xy|*theta*f; test yonu piksel acisiyla kurulu
    Eigen::Vector3d d2(std::sin(t) * std::cos(a), std::sin(t) * std::sin(a), std::cos(t));  // SVO/CV cercevesi: x sag, y asagi, z ileri (golgelendirici y yukari kullanir, burada y ters)
    Eigen::Vector2d px2; cam->project3(d2, &px2);
    if (az == 0 && (th % 20 == 0 || th >= 89)) std::cout << "theta " << th << ": izdusum " << px2.transpose() << " beklenen " << ex.transpose() << std::endl;
    if (th > 0 && (px2 - ex).norm() > 0.01) std::cout << "SAPMA th=" << th << " az=" << az << " px=" << px2.transpose() << " ex=" << ex.transpose() << std::endl;
    if (th > 0) maxerr = std::max(maxerr, (px2 - ex).norm());
    Eigen::Vector3d b; cam->backProject3(px2, &b);
    maxang = std::max(maxang, std::acos(std::min(1.0, b.dot(d2))) * 180 / M_PI);
  }
  std::cout << "kamera: " << cam->imageWidth() << "x" << cam->imageHeight() << "\n";
  std::cout << "ileri izdusum maks piksel hatasi (esit uzaklikli beklentiye gore, 0..97 derece): " << maxerr << " px\n";
  std::cout << "gidis-donus maks aci hatasi: " << maxang << " derece\n";
  Eigen::Vector3d b; cam->backProject3(Eigen::Vector2d(cx, cy), &b); std::cout << "merkez isin: " << b.transpose() << "\n";
  Eigen::Vector2d m1(cx + 300, cy), m2(10, 10), m3(cx + 140, cy + 40);
  std::cout << "maskeli? (cx+300,cy)=" << cam->isMasked(m1) << " (10,10)=" << cam->isMasked(m2) << " govde(cx+140,cy+40)=" << cam->isMasked(m3) << "\n";
}
