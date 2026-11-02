// Pin testi: shield'in hangi pinle neyi sürdüğünü bulmak için.
// Seri porttan (115200) satır satır komut alır; açtığı her pini bir süre sonra kendisi kapatır.
//
//   D <pin> <0|1> [ms]   dijital çıkış (ör. "D 3 1" röle/LED)
//   A <pin> <0-255> [ms] PWM çıkış (ör. "A 10 80" ~%30)
//   T <hz> <ms>          buzzer (pin 11) belirtilen frekansta öter
//   S                    hepsini durdur
//
// ms verilmezse 1000 ms. Hiçbir yerde delay() yok: loop() her turda saate bakar,
// süresi dolan pini kapatır. Aynı anda birkaç pin ve buzzer birlikte çalışabilir.

const uint8_t BUZZER = 11;
const unsigned long VARSAYILAN_MS = 1000;
const unsigned long EN_UZUN_MS = 5000;

unsigned long bitis[20];        // pin başına kapanma zamanı (0 = boşta)
unsigned long buzzerBitis = 0;
char satir[32];
uint8_t uzunluk = 0;

void pinBirak(uint8_t pin) {
  digitalWrite(pin, LOW);
  pinMode(pin, INPUT);          // açılıştaki gibi boşta bırak
  bitis[pin] = 0;
}

void hepsiniDurdur() {
  noTone(BUZZER);
  buzzerBitis = 0;
  for (uint8_t p = 2; p <= 13; p++) pinBirak(p);
}

unsigned long sure(long ms) {
  if (ms <= 0) return VARSAYILAN_MS;
  return ms > (long)EN_UZUN_MS ? EN_UZUN_MS : (unsigned long)ms;
}

void komutuIsle(char *k) {
  char tur = k[0];
  long a = 0, b = 0, c = 0;
  int n = sscanf(k + 1, "%ld %ld %ld", &a, &b, &c);

  if (tur == 'S') {
    hepsiniDurdur();
    Serial.println(F("OK durdu"));
  } else if ((tur == 'D' || tur == 'A') && n >= 2 && a >= 2 && a <= 13) {
    uint8_t pin = a;
    if (pin == BUZZER) { noTone(BUZZER); buzzerBitis = 0; }
    pinMode(pin, OUTPUT);
    if (tur == 'D') digitalWrite(pin, b ? HIGH : LOW);
    else analogWrite(pin, constrain(b, 0, 255));
    unsigned long ms = sure(n >= 3 ? c : 0);
    bitis[pin] = millis() + ms;
    Serial.print(F("OK ")); Serial.print(tur); Serial.print(' ');
    Serial.print(pin); Serial.print(' '); Serial.print(b);
    Serial.print(F(" ms=")); Serial.println(ms);
  } else if (tur == 'T' && n >= 2) {
    tone(BUZZER, constrain(a, 31, 10000));
    buzzerBitis = millis() + sure(b);
    Serial.print(F("OK T ")); Serial.println(a);
  } else {
    Serial.print(F("HATA anlasilmadi: ")); Serial.println(k);
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

void setup() {
  Serial.begin(115200);
  hepsiniDurdur();
  Serial.println(F("HAZIR pin_testi"));
}

void loop() {
  seriOku();
  unsigned long simdi = millis();
  for (uint8_t p = 2; p <= 13; p++) {
    if (bitis[p] && (long)(simdi - bitis[p]) >= 0) {
      pinBirak(p);
      Serial.print(F("KAPANDI ")); Serial.println(p);
    }
  }
  if (buzzerBitis && (long)(simdi - buzzerBitis) >= 0) {
    noTone(BUZZER);
    buzzerBitis = 0;
  }
}
