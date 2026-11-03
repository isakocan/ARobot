// Robot kontrol: Arduino Uno + Pololu Dual VNH5019 shield.
// Jetson'dan seri port (115200) ile satır satır komut alır:
//
//   M <sol> <sag>   motorlar, -255..255 (artı = ileri). 300 ms yenilenmezse motorlar durur.
//   S               motorları hemen durdur
//   L <0|1>         LED (pin 3 rölesi)
//   F               selektör: ışığı iki kez yakıp söndürür, eski hâline döner
//   H <0|1>         dörtlüler: ışık kapatılana kadar yanıp söner (L komutu da kapatır)
//   R <0|1>         ikinci röle (pin 5, şimdilik boşta)
//   K <ms> [ses]    korna, ms boyunca; ses: 0 düz (varsayılan), 1 kalın, 2 tiz, 3 titrek iki ton,
//                   4 dü-düt (kesik iki öttürme), 5 La Cucaracha (basınca bir kez çalar)
//   B <hz> <ms>     tek ton bip
//   C <n>           n. melodiyi çal (1..MELODI_SAYISI), C 0 = sustur
//   X <n>           kısa ses efekti (EFEKTLER; EF_ sabitleri): vites, foto, kayıt, kapanış...
//   E <0|1>         motor sesi (hıza göre perdesi değişen uğultu)
//   V <0|1>         geri vites bipi (iki motor da geri giderken)
//   ?               durum satırını hemen gönder
//   N               nabız: köprü, bağlı bir sürücü (uygulama / sayfa) varken 250 ms'de bir gönderir
//
// Saniyede 10 kez durum satırı gönderir:
//   D <sol> <sag> <solmA> <sagmA> <ariza> <led> <melodi> <dortlu> <motorSesi> <geriBip> <sikisma>
//   sol/sag: o anki motor değeri, mA: motor akımı, ariza: 1 = sürücü arıza bildiriyor,
//   melodi: çalan melodinin numarası (0 = yok), sikisma: akım koruması devrede (1 sol, 2 sağ, 3 ikisi),
//   diğerleri 0/1
//
// Korumalar: komut 300 ms gelmezse motorlar durur; bağlantı (nabız) 1,5 sn gelmezse sesler susar,
// sürerken koptuysa dörtlüler yanar (bağlantı gelince ya da 1 dk sonra söner); bir motorun akımı
// AKIM_SINIR_MA'yı 800 ms aşarsa (teker sıkıştı) iki motor 1,5 sn durdurulur.
//
// Hiçbir yerde delay() yok: loop() her turda saate bakıp sırası gelen işi yapar,
// böylece motor, LED, buzzer ve seri port birbirini bekletmez.
//
// Tek buzzer var; aynı anda birden çok ses istenirse öncelik:
//   bip > korna > efekt > melodi > geri vites bipi > motor sesi
// Kendiliğinden çalan efektler: açılışta "açılış", ilk sürücü bağlanınca / bağlantı geri gelince "bağlandı",
// bağlantı kopunca "koptu", akım koruması devreye girince "alarm".

// --- Pinler (VNH5019 shield düzeni; test edildi) ---
const uint8_t SOL_A = 2, SOL_B = 4, SOL_PWM = 9, SOL_ARIZA = 6, SOL_AKIM = A0;   // Motor 1 = sol
const uint8_t SAG_A = 7, SAG_B = 8, SAG_PWM = 10, SAG_ARIZA = 12, SAG_AKIM = A1; // Motor 2 = sag
const uint8_t LED_ROLE = 3, YEDEK_ROLE = 5, BUZZER = 11;

// Test sonuçları: ileri için B pini açılır; röleler LOW'da çeker.
const bool SOL_TERS = true, SAG_TERS = true;
const bool ROLE_ACIK = LOW;

const unsigned long KOMUT_ZAMAN_ASIMI = 300;  // ms
// Bu kadar hiç satır (nabız dahil) gelmezse bağlantı kopmuş sayılır ve sesler susar: motor sesi, melodi,
// korna. Işık olduğu gibi kalır; robot son birkaç saniyede gidiyorduysa dörtlüler kendiliğinden yanar.
const unsigned long BAGLANTI_ZAMAN_ASIMI = 1500;  // ms
const unsigned long OTO_DORTLU_SURE = 60000;      // ms; kendiliğinden yanan dörtlü en çok bu kadar yanar
// Akım koruması. Sıkışma (stall) akımı henüz ölçülmedi; tahmini değer, ölçünce güncellenecek.
const long AKIM_SINIR_MA = 2500;
const unsigned long AKIM_SURE = 800;              // ms; kalkıştaki kısa tepe sayılmasın
const unsigned long AKIM_BEKLEME = 1500;          // ms; koruma devreye girince motorlar bu kadar durur
const unsigned long AKIM_ARALIGI = 20;            // ms; akım bu sıklıkla okunur
const unsigned long RAMPA_ADIMI_MS = 5;
const int HIZLANMA_ADIMI = 4;                  // 0 → 255 yaklaşık 320 ms (dişliler ve pil için yumuşak kalkış)
const int YAVASLAMA_ADIMI = 9;                 // 255 → 0 yaklaşık 140 ms (durmak hızlı olsun)
const uint8_t YON_BEKLEME = 16;                // adım (×5 ms = 80 ms): yön değişirken sıfırda frenli bekle
const int OLU_BOLGE = 50;                      // bunun altındaki PWM motoru döndürmüyor; 1..255 → 50..255
const unsigned long DURUM_ARALIGI = 100;       // ms
const int PWM_UST = 400;                       // Timer1 20 kHz için tepe değeri
const unsigned long SELEKTOR_ADIMI = 110;      // ms; röle mekanik, daha hızlı çevrilmez
const unsigned long DORTLU_ADIMI = 500;        // ms; dakikada 60 yanıp sönme (arabalarda 60-120)

// Ses
// Korna sesleri. Tek buzzer aynı anda iki ton çalamaz; düz tek ton kare dalga olduğu için zaten
// pürüzlüdür ve kornaya en çok benzeyendir. 3 numara eski sesimiz: iki ton 4 ms'de bir değişir.
const uint16_t KORNA_HZ[] = { 440, 330, 660 };
const uint16_t DUDUT_HZ = 415;                       // 4: dü-düt (170 ms öt, 60 ms sus, 170 ms öt, 120 ms sus)
const uint16_t KORNA_HZ_1 = 420, KORNA_HZ_2 = 500;
const unsigned long KORNA_ADIMI = 4;                // ms
const uint16_t GERI_BIP_HZ = 1100;
const unsigned long GERI_BIP_YARIM = 450;            // ms çalar, ms susar
const uint16_t MOTOR_ROLANTI_HZ = 38;                // duruyorken
const uint16_t MOTOR_EN_YUKSEK_HZ = 140;             // tam güçte
const unsigned long MOTOR_TITRESIM = 30;             // ms; perde hafifçe oynar, düz vızıltı gibi durmaz

int hedefSol = 0, hedefSag = 0;   // istenen
int simdiSol = 0, simdiSag = 0;   // rampalı, motora giden
uint8_t bekleSol = 0, bekleSag = 0;  // yön değişiminde sıfırda kalan adım
bool ledAcik = false;
uint8_t selektorKalan = 0;        // kalan yak/söndür adımı
bool dortlu = false, dortluYanik = false;
bool motorSesi = false, geriBip = true;
unsigned long sonSatir = 0;      // herhangi bir komutun (nabız dahil) geldiği son an
unsigned long sonHareket = 0;    // sıfırdan farklı motor komutunun geldiği son an
bool otoDortlu = false;          // dörtlüyü bağlantı kopunca biz yaktık
unsigned long otoDortluBaslangic = 0;
unsigned long sonAkim = 0, akimSolBaslangic = 0, akimSagBaslangic = 0, korumaBitis = 0;
uint8_t sikisma = 0;             // 1 sol, 2 sağ
unsigned long sonKomut = 0, sonRampa = 0, sonDurum = 0, selektorZamani = 0, dortluZamani = 0;
unsigned long bipBitis = 0, kornaBitis = 0, geriBaslangic = 0;
uint16_t bipHz = 0;
uint8_t kornaSesi = 0;
unsigned long kornaBaslangic = 0;
char satir[32];
uint8_t uzunluk = 0;

// ------------------------------------------------------------ melodiler
// {frekans Hz, süre ms} çiftleri; frekans 0 = sus, {0, 0} = son.
// Buzzer (piezo) kalın seslerde kısık kaldığı için bazı ezgiler bir oktav yukarı taşındı.

const uint16_t N_E4 = 330, N_G4 = 392, N_GS4 = 415, N_A4 = 440, N_AS4 = 466, N_B4 = 494,
               N_C5 = 523, N_CS5 = 554, N_D5 = 587, N_DS5 = 622, N_E5 = 659, N_F5 = 698, N_FS5 = 740,
               N_G5 = 784, N_GS5 = 831, N_A5 = 880, N_AS5 = 932, N_B5 = 988,
               N_C6 = 1047, N_CS6 = 1109, N_D6 = 1175, N_DS6 = 1245, N_E6 = 1319, N_F6 = 1397,
               N_FS6 = 1480, N_G6 = 1568;

const uint16_t M_ACILIS[] PROGMEM = { N_C5, 110, N_E5, 110, N_G5, 110, N_C6, 260, 0, 0 };

const uint16_t M_TETRIS[] PROGMEM = {        // Korobeiniki (Rus halk şarkısı, Tetris)
  N_E5, 400, N_B4, 200, N_C5, 200, N_D5, 400, N_C5, 200, N_B4, 200,
  N_A4, 400, N_A4, 200, N_C5, 200, N_E5, 400, N_D5, 200, N_C5, 200,
  N_B4, 600, N_C5, 200, N_D5, 400, N_E5, 400, N_C5, 400, N_A4, 400, N_A4, 400, 0, 400,
  0, 200, N_D5, 400, N_F5, 200, N_A5, 400, N_G5, 200, N_F5, 200,
  N_E5, 600, N_C5, 200, N_E5, 400, N_D5, 200, N_C5, 200,
  N_B4, 400, N_B4, 200, N_C5, 200, N_D5, 400, N_E5, 400, N_C5, 400, N_A4, 400, N_A4, 400, 0, 0 };

const uint16_t M_MARIO[] PROGMEM = {         // Super Mario Bros. ana tema (başı)
  N_E5, 150, N_E5, 150, 0, 150, N_E5, 150, 0, 150, N_C5, 150, N_E5, 300, N_G5, 300, 0, 300, N_G4, 300, 0, 300,
  N_C5, 450, N_G4, 150, 0, 300, N_E4, 450, N_A4, 300, N_B4, 300, N_AS4, 150, N_A4, 300,
  N_G4, 200, N_E5, 200, N_G5, 200, N_A5, 300, N_F5, 150, N_G5, 150, 0, 150, N_E5, 300, N_C5, 150, N_D5, 150, N_B4, 450, 0, 0 };

const uint16_t M_IMPARATORLUK[] PROGMEM = {  // Star Wars, İmparatorluk Marşı (bir oktav yukarı)
  N_G5, 500, N_G5, 500, N_G5, 500, N_DS5, 350, N_AS5, 150, N_G5, 500, N_DS5, 350, N_AS5, 150, N_G5, 1000,
  N_D6, 500, N_D6, 500, N_D6, 500, N_DS6, 350, N_AS5, 150, N_FS5, 500, N_DS5, 350, N_AS5, 150, N_G5, 1000,
  N_G6, 500, N_G5, 350, N_G5, 150, N_G6, 500, N_FS6, 350, N_F6, 150, N_E6, 125, N_DS6, 125, N_E6, 250, 0, 250,
  N_GS5, 250, N_CS6, 500, N_C6, 350, N_B5, 150, N_AS5, 125, N_A5, 125, N_AS5, 250, 0, 250,
  N_DS5, 250, N_FS5, 500, N_DS5, 350, N_G5, 150, N_AS5, 500, N_G5, 350, N_AS5, 150, N_D6, 1000, 0, 0 };

const uint16_t M_GOT[] PROGMEM = {           // Game of Thrones ana tema (bir oktav yukarı)
  N_G5, 450, N_C5, 450, N_DS5, 225, N_F5, 225, N_G5, 450, N_C5, 450, N_DS5, 225, N_F5, 225,
  N_G5, 450, N_C5, 450, N_E5, 225, N_F5, 225, N_G5, 450, N_C5, 450, N_E5, 225, N_F5, 225,
  N_G5, 1350, N_C5, 1350, N_DS5, 225, N_F5, 225, N_G5, 900, N_C5, 900, N_DS5, 225, N_F5, 225, N_D5, 1800,
  N_F5, 1350, N_AS4, 1350, N_DS5, 225, N_D5, 225, N_F5, 900, N_AS4, 900, N_DS5, 225, N_D5, 225, N_C5, 1800, 0, 0 };

const uint16_t M_NOKIA[] PROGMEM = {         // Gran Vals (Tárrega), Nokia zil sesi
  N_E6, 120, N_D6, 120, N_FS5, 240, N_GS5, 240, N_CS6, 120, N_B5, 120, N_D5, 240, N_E5, 240,
  N_B5, 120, N_A5, 120, N_CS5, 240, N_E5, 240, N_A5, 600, 0, 0 };

const uint16_t M_SIREN[] PROGMEM = {
  650, 300, 950, 300, 650, 300, 950, 300, 650, 300, 950, 300, 650, 300, 950, 300, 0, 0 };

// Oyun ve film müzikleri: tools/melodi_cevir.py ile hazır nota dökümlerinden üretildi
#include "sarkilar.h"
#include "sarkilar_ek.h"

// Numara = listedeki sıra (1'den). Satır sonundaki "// grup|ad" yorumlarını Unity uygulaması ve robotsuz
// deneme okuyup şarkı menüsünü kurar; şarkı eklerken yorumunu da yaz.
const uint16_t *const MELODILER[] = {
  M_ACILIS,        // |Açılış
  M_MEGALOVANIA,   // Oyun|Megalovania (Undertale)
  M_MARIO,         // Oyun|Süper Mario
  M_TETRIS,        // Oyun|Tetris
  M_IMPARATORLUK,  // Film / dizi|İmparatorluk Marşı
  M_GOT,           // Film / dizi|Game of Thrones
  M_HEDWIG,        // Film / dizi|Hedwig (Harry Potter)
  M_PEMBE_PANTER,  // Film / dizi|Pembe Panter
  M_KORSAN,        // Film / dizi|He's a Pirate (Karayip Korsanları)
  M_TAKEONME,      // Pop|Take On Me
  M_RICK,          // Pop|Never Gonna Give You Up
  M_NOKIA,         // Pop|Nokia
  M_SIREN,         // Efekt|Siren
};
const uint8_t MELODI_SAYISI = sizeof(MELODILER) / sizeof(MELODILER[0]);

// Kısa ses efektleri (şarkı listesinde görünmez). Biçim melodilerle aynı: hz, ms çiftleri; hz 0 = sessizlik.
const uint16_t E_ACILIS[] PROGMEM = { 523, 90, 659, 90, 784, 90, 1047, 220, 0, 0 };
const uint16_t E_BAGLANDI[] PROGMEM = { 1047, 70, 0, 40, 1319, 130, 0, 0 };
const uint16_t E_KOPTU[] PROGMEM = { 330, 260, 0, 0 };
const uint16_t E_KAPANIS[] PROGMEM = { 1047, 120, 784, 120, 523, 280, 0, 0 };
const uint16_t E_VITES[] PROGMEM = { 1800, 8, 0, 0 };
const uint16_t E_ALARM[] PROGMEM = { 1760, 80, 0, 60, 1760, 80, 0, 60, 1760, 80, 0, 0 };
const uint16_t E_FOTO[] PROGMEM = { 2600, 12, 0, 50, 1800, 20, 0, 0 };
const uint16_t E_KAYIT_BASLA[] PROGMEM = { 880, 80, 1319, 130, 0, 0 };
const uint16_t E_KAYIT_BITTI[] PROGMEM = { 1319, 80, 880, 130, 0, 0 };
const uint16_t E_CUCARACHA[] PROGMEM = { 392, 110, 0, 20, 392, 110, 0, 20, 392, 110, 523, 220, 0, 30, 659, 300, 0, 0 };
const uint16_t *const EFEKTLER[] = {
  E_ACILIS, E_BAGLANDI, E_KOPTU, E_KAPANIS, E_VITES, E_ALARM, E_FOTO, E_KAYIT_BASLA, E_KAYIT_BITTI, E_CUCARACHA,
};
const uint8_t EF_ACILIS = 1, EF_BAGLANDI = 2, EF_KOPTU = 3, EF_KAPANIS = 4, EF_VITES = 5, EF_ALARM = 6,
              EF_FOTO = 7, EF_KAYIT_BASLA = 8, EF_KAYIT_BITTI = 9, EF_CUCARACHA = 10;
const uint8_t EFEKT_SAYISI = sizeof(EFEKTLER) / sizeof(EFEKTLER[0]);

const uint16_t *efekt = 0;        // çalan efekt, 0 = yok
uint16_t efektSira = 0, efektHz = 0;
unsigned long efektBitis = 0;

void efektAdim(unsigned long simdi) {
  uint16_t hz = pgm_read_word(efekt + efektSira * 2), ms = pgm_read_word(efekt + efektSira * 2 + 1);
  if (ms == 0) { efekt = 0; efektHz = 0; return; }
  efektSira++;
  efektBitis = simdi + ms;
  efektHz = hz;
}

void efektCal(uint8_t n) {
  efekt = 0;
  efektHz = 0;
  if (n < 1 || n > EFEKT_SAYISI) return;
  efekt = EFEKTLER[n - 1];
  efektSira = 0;
  efektAdim(millis());
}

void efektIlerlet(unsigned long simdi) {
  if (efekt && (long)(simdi - efektBitis) >= 0) efektAdim(simdi);
}

uint8_t melodi = 0;               // çalan melodi (1..), 0 = yok
uint16_t notaSirasi = 0;
uint16_t melodiHz = 0;            // melodinin şu an istediği ses (0 = sessizlik)
unsigned long notaBitis = 0, sesBitis = 0;

// Sırası gelen notayı başlatır; melodi bittiyse susar.
void notaBaslat(unsigned long simdi) {
  const uint16_t *m = MELODILER[melodi - 1] + notaSirasi * 2;
  uint16_t hz = pgm_read_word(m), ms = pgm_read_word(m + 1);
  if (ms == 0) { melodi = 0; melodiHz = 0; return; }
  notaSirasi++;
  notaBitis = simdi + ms;
  // aynı nota art arda gelince ayrı duyulsun diye sonunda kısa bir sessizlik
  sesBitis = simdi + (ms > 60 ? ms - 20 : ms);
  melodiHz = hz;
}

void melodiCal(uint8_t n) {
  melodi = 0;
  melodiHz = 0;
  if (n < 1 || n > MELODI_SAYISI) return;
  melodi = n;
  notaSirasi = 0;
  notaBaslat(millis());
}

void melodiIlerlet(unsigned long simdi) {
  if (!melodi) return;
  if (sesBitis && (long)(simdi - sesBitis) >= 0) { melodiHz = 0; sesBitis = 0; }
  if ((long)(simdi - notaBitis) >= 0) notaBaslat(simdi);
}

// ------------------------------------------------------------ ses (tek buzzer, öncelikli)

uint16_t calanHz = 0;

void sesYaz(uint16_t hz) {
  if (hz == calanHz) return;      // aynı sesi yeniden başlatma
  calanHz = hz;
  if (hz) tone(BUZZER, hz); else noTone(BUZZER);
}

void sesGuncelle(unsigned long simdi) {
  bool geriGidiyor = hedefSol < 0 && hedefSag < 0;
  if (!geriGidiyor) geriBaslangic = 0;
  else if (!geriBaslangic) geriBaslangic = simdi;

  uint16_t hz = 0;
  if (bipBitis && (long)(simdi - bipBitis) < 0) {
    hz = bipHz;
  } else if (kornaBitis && (long)(simdi - kornaBitis) < 0 && kornaSesi != 5) {
    if (kornaSesi == 4) {
      unsigned long t = (simdi - kornaBaslangic) % 520;
      hz = t < 170 || (t >= 230 && t < 400) ? DUDUT_HZ : 0;
    } else hz = kornaSesi < 3 ? KORNA_HZ[kornaSesi] : (simdi / KORNA_ADIMI) % 2 ? KORNA_HZ_1 : KORNA_HZ_2;
  } else if (efekt) {
    hz = efektHz;
  } else if (melodi) {
    hz = melodiHz;
  } else if (geriBip && geriGidiyor) {
    hz = (simdi - geriBaslangic) % (2 * GERI_BIP_YARIM) < GERI_BIP_YARIM ? GERI_BIP_HZ : 0;
  } else if (motorSesi) {
    int hiz = max(abs(simdiSol), abs(simdiSag));
    hz = MOTOR_ROLANTI_HZ + (long)hiz * (MOTOR_EN_YUKSEK_HZ - MOTOR_ROLANTI_HZ) / 255;
    if ((simdi / MOTOR_TITRESIM) % 2) hz += hz / 16;
  }
  sesYaz(hz);
}

// ------------------------------------------------------------ ışık

void ledYaz(bool acik) { digitalWrite(LED_ROLE, acik ? ROLE_ACIK : !ROLE_ACIK); }

void dortluIlerlet(unsigned long simdi) {
  if (!dortlu || simdi - dortluZamani < DORTLU_ADIMI) return;
  dortluZamani = simdi;
  dortluYanik = !dortluYanik;
  ledYaz(dortluYanik);
}

void dortluAyarla(bool acik) {
  dortlu = acik;
  selektorKalan = 0;
  if (acik) { dortluYanik = false; dortluZamani = millis() - DORTLU_ADIMI; }
  else ledYaz(ledAcik);   // kapanınca ışık eski hâline döner
}

void selektorIlerlet(unsigned long simdi) {
  if (!selektorKalan || simdi - selektorZamani < SELEKTOR_ADIMI) return;
  selektorZamani = simdi;
  selektorKalan--;
  // tek adımlarda ters, çift adımlarda asıl hâl: son adımda ışık eski hâline döner
  ledYaz(selektorKalan % 2 ? !ledAcik : ledAcik);
}

// ------------------------------------------------------------ motor

void pwmKur() {
  // Timer1: pin 9 (OC1A) ve 10 (OC1B), faz doğru PWM, 16 MHz / (2 * 400) = 20 kHz
  TCCR1A = _BV(COM1A1) | _BV(COM1B1);
  TCCR1B = _BV(WGM13) | _BV(CS10);
  ICR1 = PWM_UST;
  OCR1A = 0;
  OCR1B = 0;
}

void motorYaz(uint8_t a, uint8_t b, volatile uint16_t &ocr, int deger, bool ters) {
  if (ters) deger = -deger;
  if (deger > 0)      { digitalWrite(a, HIGH); digitalWrite(b, LOW); }
  else if (deger < 0) { digitalWrite(a, LOW);  digitalWrite(b, HIGH); }
  else                { digitalWrite(a, LOW);  digitalWrite(b, LOW); }  // fren
  // Ölü bölgeyi atla: küçük komutlar da tekerleği döndürsün
  long guc = deger ? OLU_BOLGE + (long)abs(deger) * (255 - OLU_BOLGE) / 255 : 0;
  ocr = (uint16_t)(guc * PWM_UST / 255);
}

int yaklas(int simdi, int hedef, uint8_t &bekle) {
  // Sıfıra doğru (yavaşlama ya da yön değiştirme) hızlı, sıfırdan uzağa (hızlanma) yavaş
  bool yavasliyor = (simdi > 0 && hedef < simdi) || (simdi < 0 && hedef > simdi);
  int adim = yavasliyor ? YAVASLAMA_ADIMI : HIZLANMA_ADIMI;
  bool yonDegisiyor = yavasliyor && ((simdi > 0 && hedef < 0) || (simdi < 0 && hedef > 0));
  if (yonDegisiyor) hedef = 0;   // yön değişirken önce sıfırda dur
  // Hızlı ileri-geri (ör. W/S'ye art arda basmak): sıfırda frenleyip tekerin durmasını bekle, sonra ters
  // yöne geç. Dönmekte olan motora ters gerilim vermek akımı ve dişlilere gelen darbeyi büyütür.
  if (simdi == 0 && bekle) { bekle--; return 0; }
  int yeni = simdi < hedef ? min(simdi + adim, hedef) : simdi > hedef ? max(simdi - adim, hedef) : simdi;
  if (yonDegisiyor && yeni == 0) bekle = YON_BEKLEME;
  return yeni;
}

void motorlariDurdur() {
  hedefSol = hedefSag = simdiSol = simdiSag = 0;
  motorYaz(SOL_A, SOL_B, OCR1A, 0, SOL_TERS);
  motorYaz(SAG_A, SAG_B, OCR1B, 0, SAG_TERS);
}

long akimMa(uint8_t pin) {
  // VNH5019 akım çıkışı ~140 mV/A; 5 V / 1024 adım → adım başına ~34 mA
  return (long)analogRead(pin) * 5000L / 1024 * 1000 / 140;
}

// ------------------------------------------------------------ komutlar

void durumGonder() {
  bool ariza = digitalRead(SOL_ARIZA) == LOW || digitalRead(SAG_ARIZA) == LOW;
  Serial.print(F("D "));
  Serial.print(simdiSol); Serial.print(' ');
  Serial.print(simdiSag); Serial.print(' ');
  Serial.print(akimMa(SOL_AKIM)); Serial.print(' ');
  Serial.print(akimMa(SAG_AKIM)); Serial.print(' ');
  Serial.print(ariza ? 1 : 0); Serial.print(' ');
  Serial.print(ledAcik ? 1 : 0); Serial.print(' ');
  Serial.print(melodi); Serial.print(' ');
  Serial.print(dortlu ? 1 : 0); Serial.print(' ');
  Serial.print(motorSesi ? 1 : 0); Serial.print(' ');
  Serial.print(geriBip ? 1 : 0); Serial.print(' ');
  Serial.println(sikisma);
}

// Teker sıkışınca motor çok akım çeker ve ısınır: akım AKIM_SURE boyunca sınırın üstündeyse iki motoru
// AKIM_BEKLEME kadar durdur. Bu sürede gelen hareket komutları sıfır sayılır.
void akimKoru(unsigned long simdi) {
  if (korumaBitis && (long)(simdi - korumaBitis) >= 0) { korumaBitis = 0; sikisma = 0; }
  if (simdi - sonAkim < AKIM_ARALIGI) return;
  sonAkim = simdi;
  bool solYuksek = simdiSol && akimMa(SOL_AKIM) > AKIM_SINIR_MA;
  bool sagYuksek = simdiSag && akimMa(SAG_AKIM) > AKIM_SINIR_MA;
  if (!solYuksek) akimSolBaslangic = 0; else if (!akimSolBaslangic) akimSolBaslangic = simdi;
  if (!sagYuksek) akimSagBaslangic = 0; else if (!akimSagBaslangic) akimSagBaslangic = simdi;
  uint8_t s = (akimSolBaslangic && simdi - akimSolBaslangic > AKIM_SURE ? 1 : 0) |
              (akimSagBaslangic && simdi - akimSagBaslangic > AKIM_SURE ? 2 : 0);
  if (s) {
    sikisma = s;
    korumaBitis = simdi + AKIM_BEKLEME;
    akimSolBaslangic = akimSagBaslangic = 0;
    hedefSol = hedefSag = 0;
    efektCal(EF_ALARM);
  }
}

void komutuIsle(char *k) {
  long a = 0, b = 0;
  int n = sscanf(k + 1, "%ld %ld", &a, &b);
  unsigned long simdi = millis();
  sonSatir = simdi;
  switch (k[0]) {
    case 'M':
      if (n == 2) {
        hedefSol = constrain(a, -255, 255);
        hedefSag = constrain(b, -255, 255);
        if (korumaBitis) hedefSol = hedefSag = 0;   // akım koruması sürüyor
        if (a || b) sonHareket = simdi;
        sonKomut = simdi;
      }
      break;
    case 'S':
      motorlariDurdur();
      break;
    case 'L':
      if (n >= 1) { ledAcik = a; dortluAyarla(false); otoDortlu = false; }
      break;
    case 'F':
      if (!selektorKalan && !dortlu) { selektorKalan = 4; selektorZamani = simdi - SELEKTOR_ADIMI; }
      break;
    case 'H':
      if (n >= 1) { dortluAyarla(a); otoDortlu = false; }
      break;
    case 'R':
      if (n >= 1) digitalWrite(YEDEK_ROLE, a ? ROLE_ACIK : !ROLE_ACIK);
      break;
    case 'K':
      if (n >= 1 && a > 0) {
        bool yeni = !kornaBitis || (long)(simdi - kornaBitis) >= 0;   // basılı tutarken gelen yenileme değil
        kornaBitis = simdi + constrain(a, 1, 2000);
        kornaSesi = n == 2 ? constrain(b, 0, 5) : 0;
        if (yeni) {
          kornaBaslangic = simdi;
          if (kornaSesi == 5) efektCal(EF_CUCARACHA);
        }
      }
      break;
    case 'B':
      if (n == 2 && a > 0 && b > 0) {
        bipHz = constrain(a, 31, 10000);
        bipBitis = simdi + constrain(b, 1, 5000);
      }
      break;
    case 'C':
      if (n >= 1) melodiCal(a);
      break;
    case 'X':
      if (n >= 1) efektCal(a);
      break;
    case 'E':
      if (n >= 1) motorSesi = a;
      break;
    case 'V':
      if (n >= 1) geriBip = a;
      break;
    case '?':
      durumGonder();
      break;
    case 'N':
      break;   // yalnızca sonSatir güncellenir
  }
}

void seriOku() {
  while (Serial.available()) {
    char ch = Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      satir[uzunluk] = 0;
      if (uzunluk) komutuIsle(satir);
      uzunluk = 0;
    } else if (uzunluk < sizeof(satir) - 1) {
      satir[uzunluk++] = ch;
    }
  }
}

// ------------------------------------------------------------ ana döngü

void setup() {
  // Röleler kapalı başlasın (LOW'da çektikleri için önce HIGH yaz)
  digitalWrite(LED_ROLE, !ROLE_ACIK);
  digitalWrite(YEDEK_ROLE, !ROLE_ACIK);
  pinMode(LED_ROLE, OUTPUT);
  pinMode(YEDEK_ROLE, OUTPUT);

  pinMode(SOL_A, OUTPUT); pinMode(SOL_B, OUTPUT); pinMode(SOL_PWM, OUTPUT);
  pinMode(SAG_A, OUTPUT); pinMode(SAG_B, OUTPUT); pinMode(SAG_PWM, OUTPUT);
  pinMode(SOL_ARIZA, INPUT); pinMode(SAG_ARIZA, INPUT);  // shield'de çekme direnci var
  pwmKur();
  motorlariDurdur();

  Serial.begin(115200);
  Serial.println(F("HAZIR robot_kontrol"));
  efektCal(EF_ACILIS);
}

void loop() {
  seriOku();
  unsigned long simdi = millis();

  // Güvenlik: komut kesilirse dur
  if ((hedefSol || hedefSag) && simdi - sonKomut > KOMUT_ZAMAN_ASIMI) {
    hedefSol = hedefSag = 0;
  }
  // Güvenlik: bağlantı (nabız) kesilirse sesler sussun
  bool kopuk = simdi - sonSatir > BAGLANTI_ZAMAN_ASIMI;
  if (kopuk && (motorSesi || melodi || kornaBitis || bipBitis)) {
    motorSesi = false;
    melodi = 0;
    melodiHz = 0;
    kornaBitis = bipBitis = 0;
  }
  // Bağlantı sesi: ilk sürücü bağlanınca / bağlantı geri gelince "bağlandı", kopunca "koptu"
  static bool bagliydi = false;
  bool bagli = sonSatir && !kopuk;
  if (bagli != bagliydi) {
    bagliydi = bagli;
    efektCal(bagli ? EF_BAGLANDI : EF_KOPTU);
  }
  // Sürerken bağlantı koparsa dörtlüler yanar; bağlantı gelince (ya da 1 dk sonra) söner
  if (kopuk && !otoDortlu && !dortlu && sonHareket && simdi - sonHareket < BAGLANTI_ZAMAN_ASIMI + 2000) {
    dortluAyarla(true);
    otoDortlu = true;
    otoDortluBaslangic = simdi;
  }
  if (otoDortlu && (!kopuk || simdi - otoDortluBaslangic > OTO_DORTLU_SURE)) {
    otoDortlu = false;
    dortluAyarla(false);
  }
  akimKoru(simdi);

  if (simdi - sonRampa >= RAMPA_ADIMI_MS) {
    sonRampa = simdi;
    simdiSol = yaklas(simdiSol, hedefSol, bekleSol);
    simdiSag = yaklas(simdiSag, hedefSag, bekleSag);
    motorYaz(SOL_A, SOL_B, OCR1A, simdiSol, SOL_TERS);
    motorYaz(SAG_A, SAG_B, OCR1B, simdiSag, SAG_TERS);
  }

  melodiIlerlet(simdi);
  efektIlerlet(simdi);
  sesGuncelle(simdi);
  selektorIlerlet(simdi);
  dortluIlerlet(simdi);

  if (simdi - sonDurum >= DURUM_ARALIGI) {
    sonDurum = simdi;
    durumGonder();
  }
}
