// Kullanıcının eklediği şarkılar (Kasım 2026). Nota dökümleri internetten; kulaktan yazılmış, asıllarıyla
// birebir aynı olmayabilir. Megalovania'nın kısa sürümü çıkarıldı (robsoncouto'daki uzun sürüm zaten var).
// Çevirmek için:
//   python tools/melodi_cevir.py c tools/sarkilar/eklenen.c > robot/arduino/robot_kontrol/sarkilar_ek.h

#define NOTE_C3  131
#define NOTE_CS3 139
#define NOTE_D3  147
#define NOTE_DS3 156
#define NOTE_E3  165
#define NOTE_A3  220
#define NOTE_B3  247
#define NOTE_C4  262
#define NOTE_CS4 277
#define NOTE_D4  294
#define NOTE_DS4 311
#define NOTE_E4  330
#define NOTE_F4  349
#define NOTE_FS4 370
#define NOTE_G4  392
#define NOTE_GS4 415
#define NOTE_A4  440
#define NOTE_AS4 466
#define NOTE_B4  494
#define NOTE_C5  523
#define NOTE_CS5 554
#define NOTE_D5  587
#define NOTE_DS5 622
#define NOTE_E5  659
#define NOTE_F5  698
#define NOTE_G5  784
#define NOTE_A5  880

// 1. Dance with Steel Ball Run (JoJo SBR)
int sbrMelodi[37] = {
  NOTE_E4, NOTE_G4, NOTE_A4, NOTE_A4, 0, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_C5, 0,
  NOTE_C5, NOTE_D5, NOTE_B4, NOTE_B4, 0, NOTE_A4, NOTE_G4, NOTE_A4, 0,
  NOTE_E4, NOTE_G4, NOTE_A4, NOTE_A4, 0, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_C5, 0,
  NOTE_D5, NOTE_D5, NOTE_E5, NOTE_F5, 0, NOTE_G5, NOTE_E5, NOTE_A5
};
int sbrSureleri[37] = {
  200, 200, 400, 200, 100, 200, 200, 400, 200, 100,
  200, 200, 400, 200, 100, 200, 200, 600, 400,
  200, 200, 400, 200, 100, 200, 200, 400, 200, 100,
  200, 200, 400, 400, 100, 200, 200, 800
};

// 2. Il Vento d'Oro (JoJo, Giorno)
int giornoMelodi[31] = {
  NOTE_D4, NOTE_F4, NOTE_D4, NOTE_D5, NOTE_D4, NOTE_C5, NOTE_B4, NOTE_A4,
  NOTE_D4, NOTE_F4, NOTE_D4, NOTE_GS4, NOTE_G4, NOTE_F4, NOTE_D4,
  NOTE_D4, NOTE_F4, NOTE_D4, NOTE_D5, NOTE_D4, NOTE_C5, NOTE_B4, NOTE_A4,
  NOTE_D5, NOTE_CS5, NOTE_C5, NOTE_B4, NOTE_AS4, NOTE_A4, NOTE_GS4, NOTE_G4
};
int giornoSureleri[31] = { // nota böleni: 4 = dörtlük
  4, 4, 4, 2, 4, 4, 4, 4,
  4, 4, 4, 2, 4, 4, 4,
  4, 4, 4, 2, 4, 4, 4, 4,
  4, 4, 4, 4, 4, 4, 4, 4
};

// 3. Silver for Monsters (Witcher 3)
int witcherSilverMelodi[49] = {
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_A4, 0,
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_A4, 0,
  NOTE_C5, NOTE_D5, NOTE_E5, NOTE_D5, NOTE_C5, NOTE_C5, 0,
  NOTE_B4, NOTE_C5, NOTE_D5, NOTE_C5, NOTE_B4, NOTE_B4, NOTE_A4,
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_A4, 0,
  NOTE_C5, NOTE_D5, NOTE_E5, NOTE_D5, NOTE_C5, NOTE_C5, 0,
  NOTE_E5, NOTE_D5, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_A4
};
int witcherSilverSureleri[49] = {
  150, 150, 150, 150, 300, 150, 100,
  150, 150, 150, 150, 300, 150, 100,
  150, 150, 150, 150, 300, 150, 100,
  150, 150, 150, 150, 300, 150, 300,
  150, 150, 150, 150, 300, 150, 100,
  150, 150, 150, 150, 300, 150, 100,
  150, 150, 150, 150, 150, 150, 150, 600
};

// 4. Steel for Humans (Witcher 3)
int witcherSteelMelodi[33] = {
  NOTE_D4, NOTE_D4, NOTE_F4, NOTE_G4, NOTE_A4, NOTE_A4,
  NOTE_A4S, NOTE_A4, NOTE_G4, NOTE_A4,
  NOTE_F4, NOTE_F4, NOTE_G4, NOTE_F4, NOTE_E4, NOTE_D4, 0,
  NOTE_A4, NOTE_D5, NOTE_D5, NOTE_C5, NOTE_D5,
  NOTE_A4S, NOTE_A4S, NOTE_C5, NOTE_A4S, NOTE_A4,
  NOTE_G4, NOTE_G4, NOTE_A4, NOTE_G4, NOTE_F4, NOTE_E4, NOTE_D4
};
int witcherSteelSureleri[33] = {
  200, 200, 200, 200, 400, 400,
  200, 200, 200, 400,
  200, 200, 200, 200, 200, 400, 200,
  200, 400, 400, 200, 400,
  200, 400, 400, 200, 400,
  200, 200, 200, 200, 200, 200, 600
};

// 5. Ana tema (Witcher 3)
int witcherAnaMelodi[36] = {
  NOTE_E3, NOTE_A3, NOTE_B3, NOTE_C4, NOTE_B3, NOTE_A3,
  NOTE_E3, NOTE_A3, NOTE_B3, NOTE_C4, NOTE_D4, NOTE_E4,
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_E4,
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_D5, NOTE_B4, 0,
  NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_E4,
  NOTE_F4, NOTE_E4, NOTE_D4, NOTE_C4, NOTE_B3, NOTE_A3
};
int witcherAnaSureleri[36] = {
  400, 400, 400, 600, 400, 800,
  400, 400, 400, 400, 400, 800,
  300, 300, 300, 300, 300, 600,
  300, 300, 300, 300, 600, 200,
  300, 300, 300, 300, 300, 600,
  300, 300, 300, 300, 400, 800
};

// 6. He's a Pirate (Karayip Korsanları)
int korsanMelodi[52] = {
  NOTE_D4, NOTE_D4, NOTE_D4, NOTE_D4, NOTE_D4, NOTE_D4, NOTE_D4, NOTE_D4,
  NOTE_D4, NOTE_F4, NOTE_A4, NOTE_A4, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_C5,
  NOTE_C5, NOTE_D5, NOTE_B4, NOTE_B4, NOTE_A4, NOTE_G4, NOTE_G4, NOTE_A4,
  NOTE_D4, NOTE_F4, NOTE_A4, NOTE_A4, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_C5,
  NOTE_C5, NOTE_D5, NOTE_B4, NOTE_B4, NOTE_A4, NOTE_G4, NOTE_A4,
  NOTE_D4, NOTE_F4, NOTE_A4, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_C5,
  NOTE_D5, NOTE_B4, NOTE_B4, NOTE_A4, NOTE_G4, NOTE_A4
};
int korsanSureleri[52] = {
  150, 150, 150, 150, 150, 150, 150, 150,
  150, 150, 300, 150, 150, 150, 300, 150,
  150, 150, 300, 150, 150, 150, 150, 300,
  150, 150, 300, 150, 150, 150, 300, 150,
  150, 150, 300, 150, 150, 150, 450,
  150, 150, 300, 150, 150, 300, 150,
  150, 150, 300, 150, 150, 450
};

// 7. Sadness and Sorrow (Naruto)
int narutoMelodi[30] = {
  NOTE_A3, NOTE_E4, NOTE_A4, NOTE_B4, NOTE_C5, NOTE_B4, NOTE_A4, NOTE_E4,
  NOTE_D4, NOTE_A4, NOTE_D5, NOTE_E5, NOTE_F5, NOTE_E5, NOTE_D5, NOTE_A4,
  NOTE_C4, NOTE_G4, NOTE_C5, NOTE_D5, NOTE_E5, NOTE_D5, NOTE_C5, NOTE_G4,
  NOTE_E4, NOTE_D4, NOTE_C4, NOTE_B3, NOTE_A3, 0
};
int narutoSureleri[30] = {
  400, 400, 400, 400, 600, 200, 400, 800,
  400, 400, 400, 400, 600, 200, 400, 800,
  400, 400, 400, 400, 600, 200, 400, 800,
  400, 400, 400, 400, 1200, 400
};
