// Robotsuz deneme: Arduino'daki buzzer mantığının tarayıcıdaki kopyası (robot_kontrol.ino, sesGuncelle).
// Sayfanın robota gönderdiği mesajları dinler ve buzzer'ın o an çalacağı sesi kare dalgayla çalar.
// Öncelik aynı: bip > korna > melodi > geri vites bipi > motor sesi. Sayılar ve melodiler .ino'dan okunur.
// Gerçek piezo kalın seslerde (motor sesinin 38-140 Hz'i) kulaklıktan çok daha cılız ve tıkırtılı duyulur.
(async () => {
  const { sabit: S, melodiler: MELODILER } = await (await fetch('/sahte/ses.json')).json();

  // ---------------------------------------------------------------- Arduino durumu
  let kornaBitis = 0, melodi = 0, notaSirasi = 0, notaBitis = 0, sesBitis = 0, melodiHz = 0;
  let geriBip = true, motorSesi = false, geriBaslangic = 0;
  let hedef = [0, 0], simdi = [0, 0], sonKomut = 0, sonRampa = 0;

  function notaBaslat(t) {
    const nota = MELODILER[melodi - 1][notaSirasi];
    if (!nota || nota[1] === 0) { melodi = 0; melodiHz = 0; return; }
    notaSirasi++;
    notaBitis = t + nota[1];
    sesBitis = t + (nota[1] > 60 ? nota[1] - 20 : nota[1]);
    melodiHz = nota[0];
  }
  function melodiCal(n, t) {
    melodi = 0; melodiHz = 0;
    if (n < 1 || n > MELODILER.length) return;
    melodi = n; notaSirasi = 0; notaBaslat(t);
  }
  function yaklas(s, h) {
    const yavasliyor = (s > 0 && h < s) || (s < 0 && h > s);
    const adim = yavasliyor ? S.YAVASLAMA_ADIMI : S.HIZLANMA_ADIMI;
    if (yavasliyor && ((s > 0 && h < 0) || (s < 0 && h > 0))) h = 0;
    return s < h ? Math.min(s + adim, h) : s > h ? Math.max(s - adim, h) : s;
  }

  // Sayfanın /kontrol'e gönderdiği her mesajı Arduino'ya giden komut gibi işle
  const gonder = WebSocket.prototype.send;
  WebSocket.prototype.send = function (veri) {
    if (typeof veri === 'string' && this.url.endsWith('/kontrol')) {
      try {
        const m = JSON.parse(veri), t = performance.now();
        if (m.t === 'surus') { hedef = [m.sol, m.sag]; sonKomut = t; }
        else if (m.t === 'korna') kornaBitis = t + Math.max(50, Math.min(1000, m.ms || 250));
        else if (m.t === 'melodi') melodiCal(m.no | 0, t);
        else if (m.t === 'motor_sesi') motorSesi = !!m.acik;
        else if (m.t === 'geri_bip') geriBip = !!m.acik;
      } catch (e) { /* robotun anlamayacağı mesaj */ }
    }
    return gonder.call(this, veri);
  };

  // ---------------------------------------------------------------- ses çıkışı
  // Korna iki tonu KORNA_ADIMI ms'de bir değiştirir; bu, ortası 460 Hz olan bir kare dalganın
  // kare dalgayla titretilmesidir (frekans modülasyonu). Motor sesinin kıpırtısı da öyle.
  let ses = null;
  function sesHazirla() {
    if (ses) { ses.ctx.resume(); return; }
    const ctx = new AudioContext();
    const osc = ctx.createOscillator(); osc.type = 'square'; osc.frequency.value = 440;
    const lfo = ctx.createOscillator(); lfo.type = 'square'; lfo.frequency.value = 1;
    const derinlik = ctx.createGain(); derinlik.gain.value = 0;
    const filtre = ctx.createBiquadFilter(); filtre.type = 'lowpass'; filtre.frequency.value = 5000;
    const kazanc = ctx.createGain(); kazanc.gain.value = 0;
    lfo.connect(derinlik).connect(osc.frequency);
    osc.connect(filtre).connect(kazanc).connect(ctx.destination);
    osc.start(); lfo.start();
    ses = { ctx, osc, lfo, derinlik, kazanc, anahtar: '' };
    rozetCiz();
  }
  addEventListener('pointerdown', sesHazirla, true);
  addEventListener('keydown', sesHazirla, true);

  function cal(hz, titresimHz = 0, titresim = 0) {
    if (!ses) return;
    const anahtar = hz + '/' + titresimHz + '/' + titresim;
    if (anahtar === ses.anahtar) return;
    ses.anahtar = anahtar;
    const t = ses.ctx.currentTime;
    ses.kazanc.gain.setTargetAtTime(hz ? 0.07 : 0, t, 0.003);
    if (hz) ses.osc.frequency.setValueAtTime(hz, t);
    ses.lfo.frequency.setValueAtTime(titresimHz || 1, t);
    ses.derinlik.gain.setValueAtTime(titresim, t);
  }

  // ---------------------------------------------------------------- loop() (her 4 ms)
  setInterval(() => {
    const t = performance.now();
    if ((hedef[0] || hedef[1]) && t - sonKomut > S.KOMUT_ZAMAN_ASIMI) hedef = [0, 0];
    while (t - sonRampa >= S.RAMPA_ADIMI_MS) {
      sonRampa = sonRampa ? sonRampa + S.RAMPA_ADIMI_MS : t;
      simdi = [yaklas(simdi[0], hedef[0]), yaklas(simdi[1], hedef[1])];
    }
    if (melodi) {
      if (sesBitis && t >= sesBitis) { melodiHz = 0; sesBitis = 0; }
      if (t >= notaBitis) notaBaslat(t);
    }
    const geriGidiyor = hedef[0] < 0 && hedef[1] < 0;
    if (!geriGidiyor) geriBaslangic = 0; else if (!geriBaslangic) geriBaslangic = t;

    if (t < kornaBitis) {
      const a = S.KORNA_HZ_1, b = S.KORNA_HZ_2;
      cal((a + b) / 2, 1000 / (2 * S.KORNA_ADIMI), Math.abs(b - a) / 2);
    } else if (melodi) {
      cal(melodiHz);
    } else if (geriBip && geriGidiyor) {
      cal((t - geriBaslangic) % (2 * S.GERI_BIP_YARIM) < S.GERI_BIP_YARIM ? S.GERI_BIP_HZ : 0);
    } else if (motorSesi) {
      const hiz = Math.max(Math.abs(simdi[0]), Math.abs(simdi[1]));
      const hz = S.MOTOR_ROLANTI_HZ + Math.floor(hiz * (S.MOTOR_EN_YUKSEK_HZ - S.MOTOR_ROLANTI_HZ) / 255);
      cal(hz * (1 + 1 / 32), 1000 / (2 * S.MOTOR_TITRESIM), hz / 32);
    } else {
      cal(0);
    }
  }, 4);

  // Konsoldan bakmak için: o an çalan ses "hz/titreşimHz/titreşim" ve sesin durumu
  window.sahteSes = () => ({ calan: ses ? ses.anahtar : 'ses kapalı', durum: ses ? ses.ctx.state : '-', melodi, hedef, simdi });

  // ---------------------------------------------------------------- rozet
  const rozet = document.createElement('div');
  rozet.style.cssText = 'position:fixed;left:50%;top:10px;transform:translateX(-50%);z-index:9;padding:6px 11px;' +
    'border-radius:999px;font:600 11px/1 ui-monospace,Consolas,monospace;letter-spacing:.08em;' +
    'background:rgba(240,138,69,.92);color:#15171a;pointer-events:none';
  function rozetCiz() {
    rozet.textContent = ses ? 'ROBOTSUZ DENEME · SES AÇIK' : 'ROBOTSUZ DENEME · SES İÇİN TIKLA';
  }
  rozetCiz();
  document.body.appendChild(rozet);
})();
