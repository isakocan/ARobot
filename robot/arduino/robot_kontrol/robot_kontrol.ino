// Robot kontrol: Arduino Uno + Pololu Dual VNH5019 shield.
// Jetson'dan seri port (115200) ile satır satır komut alır:
//
//   M <sol> <sag>   motorlar, -255..255 (artı = ileri). 300 ms yenilenmezse motorlar durur.
//   S               motorları hemen durdur
//   L <0|1>         LED (pin 3 rölesi)
//   F               selektör: ışığı iki kez yakıp söndürür, eski hâline döner
//   H <0|1>         dörtlüler: ışık kapatılana kadar yanıp söner (L komutu da kapatır)
//   R <0|1>         ikinci röle (pin 5, şimdilik boşta)
//   B <hz> <ms>     buzzer (pin 11); çalan melodiyi keser
//   C <n>           n. melodiyi çal (1..MELODI_SAYISI), C 0 = sustur
//   ?               durum satırını hemen gönder
//
// Saniyede 10 kez durum satırı gönderir:
//   D <sol> <sag> <solmA> <sagmA> <ariza> <led> <melodi> <dortlu>
//   sol/sag: o anki motor değeri, mA: motor akımı, ariza: 1 = sürücü arıza bildiriyor,
//   melodi: çalan melodinin numarası (0 = yok), dortlu: 1 = dörtlüler açık
//
// Hiçbir yerde delay() yok: loop() her turda saate bakıp sırası gelen işi yapar,
// böylece motor, LED, buzzer ve seri port birbirini bekletmez.

// --- Pinler (VNH5019 shield düzeni; test edildi) ---
const uint8_t SOL_A = 2, SOL_B = 4, SOL_PWM = 9, SOL_ARIZA = 6, SOL_AKIM = A0;   // Motor 1 = sol
const uint8_t SAG_A = 7, SAG_B = 8, SAG_PWM = 10, SAG_ARIZA = 12, SAG_AKIM = A1; // Motor 2 = sag
const uint8_t LED_ROLE = 3, YEDEK_ROLE = 5, BUZZER = 11;

// Test sonuçları: ileri için B pini açılır; röleler LOW'da çeker.
const bool SOL_TERS = true, SAG_TERS = true;
const bool ROLE_ACIK = LOW;

const unsigned long KOMUT_ZAMAN_ASIMI = 300;  // ms
const unsigned long RAMPA_ADIMI_MS = 5;
const int HIZLANMA_ADIMI = 4;                  // 0 → 255 yaklaşık 320 ms (dişliler ve pil için yumuşak kalkış)
const int YAVASLAMA_ADIMI = 9;                 // 255 → 0 yaklaşık 140 ms (durmak hızlı olsun)
const int OLU_BOLGE = 50;                      // bunun altındaki PWM motoru döndürmüyor; 1..255 → 50..255
const unsigned long DURUM_ARALIGI = 100;       // ms
const int PWM_UST = 400;                       // Timer1 20 kHz için tepe değeri
const unsigned long SELEKTOR_ADIMI = 110;      // ms; röle mekanik, daha hızlı çevrilmez
const unsigned long DORTLU_ADIMI = 400;        // ms; gerçek dörtlüler gibi ~1,25 Hz

int hedefSol = 0, hedefSag = 0;   // istenen
int simdiSol = 0, simdiSag = 0;   // rampalı, motora giden
bool ledAcik = false;
uint8_t selektorKalan = 0;        // kalan yak/söndür adımı
bool dortlu = false, dortluYanik = false;
unsigned long sonKomut = 0, sonRampa = 0, sonDurum = 0, buzzerBitis = 0, selektorZamani = 0, dortluZamani = 0;
char satir[32];
uint8_t uzunluk = 0;

// ------------------------------------------------------------ melodiler
// {frekans Hz, süre ms} çiftleri; frekans 0 = sus, {0, 0} = son.
// Telifsiz: halk ezgileri, klasik müzik, borazan çağrısı ve kendi ritimlerimiz.

const uint16_t N_G4 = 392, N_A4 = 440, N_B4 = 494, N_C5 = 523, N_D5 = 587, N_E5 = 659, N_F5 = 698, N_G5 = 784, N_C6 = 1047;

const uint16_t M_ACILIS[] PROGMEM = { N_C5, 110, N_E5, 110, N_G5, 110, N_C6, 260, 0, 0 };
const uint16_t M_CUCARACHA[] PROGMEM = {     // La Cucaracha (Meksika halk ezgisi)
  N_G4, 120, N_G4, 120, N_G4, 120, N_C5, 360, N_E5, 240, 0, 120,
  N_G4, 120, N_G4, 120, N_G4, 120, N_C5, 360, N_E5, 240, 0, 240,
  N_C5, 120, N_C5, 120, N_B4, 120, N_B4, 120, N_A4, 120, N_A4, 120, N_G4, 420, 0, 0 };
const uint16_t M_NESE[] PROGMEM = {          // Neşeye Övgü (Beethoven, 9. senfoni)
  N_E5, 250, N_E5, 250, N_F5, 250, N_G5, 250, N_G5, 250, N_F5, 250, N_E5, 250, N_D5, 250,
  N_C5, 250, N_C5, 250, N_D5, 250, N_E5, 250, N_E5, 375, N_D5, 125, N_D5, 500, 0, 0 };
const uint16_t M_HUCUM[] PROGMEM = {         // Hücum borazanı
  N_G4, 150, N_C5, 150, N_E5, 150, N_G5, 300, N_E5, 150, N_G5, 600, 0, 0 };
const uint16_t M_SIREN[] PROGMEM = {
  650, 300, 950, 300, 650, 300, 950, 300, 650, 300, 950, 300, 650, 300, 950, 300, 0, 0 };
const uint16_t M_DAVUL[] PROGMEM = {         // kendi ritmimiz: bas · zil · trampet (8'lik, ~120 bpm)
  90, 70, 0, 180, 4000, 15, 0, 235, 1800, 40, 0, 210, 4000, 15, 0, 235,
  90, 70, 0, 55, 90, 70, 0, 55, 4000, 15, 0, 235, 1800, 40, 0, 210, 4000, 15, 0, 110, 4000, 15, 0, 110,
  90, 70, 0, 180, 4000, 15, 0, 235, 1800, 40, 0, 210, 4000, 15, 0, 235,
  90, 70, 0, 55, 90, 70, 0, 55, 1800, 40, 0, 85, 1800, 40, 0, 85, 1800, 40, 0, 85, 2400, 60, 0, 0 };
const uint16_t M_ROBOT[] PROGMEM = {         // kendi ezgimiz: "bip bop" robot konuşması
  1200, 70, 0, 40, 900, 70, 0, 40, 1500, 50, 0, 30, 1500, 50, 0, 120,
  700, 90, 0, 40, 1100, 60, 0, 30, 1800, 140, 0, 0 };

const uint16_t *const MELODILER[] = { M_ACILIS, M_CUCARACHA, M_NESE, M_HUCUM, M_SIREN, M_DAVUL, M_ROBOT };
const uint8_t MELODI_SAYISI = sizeof(MELODILER) / sizeof(MELODILER[0]);

uint8_t melodi = 0;               // çalan melodi (1..), 0 = yok
uint8_t notaSirasi = 0;
unsigned long notaBitis = 0, sesBitis = 0;

void melodiDurdur() {
  if (melodi) noTone(BUZZER);
  melodi = 0;
}

// Sırası gelen notayı başlatır; melodi bittiyse susar.
void notaBaslat(unsigned long simdi) {
  const uint16_t *m = MELODILER[melodi - 1] + notaSirasi * 2;
  uint16_t hz = pgm_read_word(m), ms = pgm_read_word(m + 1);
  if (ms == 0) { melodiDurdur(); return; }
  notaSirasi++;
  notaBitis = simdi + ms;
  // aynı nota art arda gelince ayrı duyulsun diye sonunda kısa bir sessizlik
  sesBitis = simdi + (ms > 60 ? ms - 20 : ms);
  if (hz) tone(BUZZER, hz); else noTone(BUZZER);
}

void melodiCal(uint8_t n) {
  melodiDurdur();
  buzzerBitis = 0;
  if (n < 1 || n > MELODI_SAYISI) return;
  melodi = n;
  notaSirasi = 0;
  notaBaslat(millis());
}

void melodiIlerlet(unsigned long simdi) {
  if (!melodi) return;
  if (sesBitis && (long)(simdi - sesBitis) >= 0) { noTone(BUZZER); sesBitis = 0; }
  if ((long)(simdi - notaBitis) >= 0) notaBaslat(simdi);
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

int yaklas(int simdi, int hedef) {
  // Sıfıra doğru (yavaşlama ya da yön değiştirme) hızlı, sıfırdan uzağa (hızlanma) yavaş
  bool yavasliyor = (simdi > 0 && hedef < simdi) || (simdi < 0 && hedef > simdi);
  int adim = yavasliyor ? YAVASLAMA_ADIMI : HIZLANMA_ADIMI;
  if (yavasliyor && ((simdi > 0 && hedef < 0) || (simdi < 0 && hedef > 0)))
    hedef = 0;   // yön değişirken önce sıfırda dur
  if (simdi < hedef) return min(simdi + adim, hedef);
  if (simdi > hedef) return max(simdi - adim, hedef);
  return simdi;
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
  Serial.println(dortlu ? 1 : 0);
}

void komutuIsle(char *k) {
  long a = 0, b = 0;
  int n = sscanf(k + 1, "%ld %ld", &a, &b);
  switch (k[0]) {
    case 'M':
      if (n == 2) {
        hedefSol = constrain(a, -255, 255);
        hedefSag = constrain(b, -255, 255);
        sonKomut = millis();
      }
      break;
    case 'S':
      motorlariDurdur();
      break;
    case 'L':
      if (n >= 1) { ledAcik = a; dortluAyarla(false); }
      break;
    case 'F':
      if (!selektorKalan && !dortlu) { selektorKalan = 4; selektorZamani = millis() - SELEKTOR_ADIMI; }
      break;
    case 'H':
      if (n >= 1) dortluAyarla(a);
      break;
    case 'R':
      if (n >= 1) digitalWrite(YEDEK_ROLE, a ? ROLE_ACIK : !ROLE_ACIK);
      break;
    case 'B':
      if (n == 2 && a > 0 && b > 0) {
        melodiDurdur();
        tone(BUZZER, constrain(a, 31, 10000));
        buzzerBitis = millis() + constrain(b, 1, 5000);
      }
      break;
    case 'C':
      if (n >= 1) melodiCal(a);
      break;
    case '?':
      durumGonder();
      break;
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
}

void loop() {
  seriOku();
  unsigned long simdi = millis();

  // Güvenlik: komut kesilirse dur
  if ((hedefSol || hedefSag) && simdi - sonKomut > KOMUT_ZAMAN_ASIMI) {
    hedefSol = hedefSag = 0;
  }

  if (simdi - sonRampa >= RAMPA_ADIMI_MS) {
    sonRampa = simdi;
    simdiSol = yaklas(simdiSol, hedefSol);
    simdiSag = yaklas(simdiSag, hedefSag);
    motorYaz(SOL_A, SOL_B, OCR1A, simdiSol, SOL_TERS);
    motorYaz(SAG_A, SAG_B, OCR1B, simdiSag, SAG_TERS);
  }

  if (buzzerBitis && (long)(simdi - buzzerBitis) >= 0) {
    noTone(BUZZER);
    buzzerBitis = 0;
  }
  melodiIlerlet(simdi);
  selektorIlerlet(simdi);
  dortluIlerlet(simdi);

  if (simdi - sonDurum >= DURUM_ARALIGI) {
    sonDurum = simdi;
    durumGonder();
  }
}
