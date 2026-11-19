// Harita/konum örnek görüntüleyici. Veri: ornek_hazirla.py (veri/<ad>/veri.json, video.mp4, plan.png)
// Dünya: y yukarı, metre; kaydın ilk karesinin altındaki zemin = köken; ilk bakış -z.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const $ = (s) => document.querySelector(s);
const video = $('#video');
const sahneEl = $('#sahne');
const glCanvas = $('#gl');
const planCanvas = $('#plan');
const miniCanvas = $('#mini');

let katalog = [], veri = null, planImg = null, poz = [], gorunum = 'kamera';
let kare = 0, sonPoz = null, haritaYolu = [], kosuYolu = [];
let bakYaw = 0, bakPitch = 0;
const planGorus = { merkez: null, olcek: 60 };   // piksel / metre

// ---------- three.js ----------
const renderer = new THREE.WebGLRenderer({ canvas: glCanvas, antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
const sahne = new THREE.Scene();
sahne.background = new THREE.Color(0x101215);
sahne.add(new THREE.HemisphereLight(0xffffff, 0x50555c, 1.4));
const gunes = new THREE.DirectionalLight(0xffffff, 1.6);
gunes.position.set(2, 5, 3);
sahne.add(gunes);

const robot = new THREE.Object3D();              // kameranın dünyadaki pozu
sahne.add(robot);
const bakis = new THREE.Object3D();              // kullanıcının bakış yönü (robota göre)
robot.add(bakis);
const kamKam = new THREE.PerspectiveCamera(78, 1, 0.02, 300);
bakis.add(kamKam);

const videoDoku = new THREE.VideoTexture(video);
videoDoku.colorSpace = THREE.SRGBColorSpace;
const kureGeo = new THREE.SphereGeometry(100, 96, 48);
kureGeo.scale(-1, 1, 1);
const kure = new THREE.Mesh(kureGeo, new THREE.MeshBasicMaterial({ map: videoDoku, depthTest: false, depthWrite: false }));
kure.rotation.y = -Math.PI / 2;                  // görüntü ortası = kameranın önü (-z)
kure.renderOrder = -10;
robot.add(kure);

// 3B görünüm kamerası
const ucKam = new THREE.PerspectiveCamera(55, 1, 0.05, 500);
const yorunge = new OrbitControls(ucKam, glCanvas);
yorunge.enableDamping = true;
yorunge.enabled = false;

// robot işareti (3B görünümde)
const robotIsaret = new THREE.Group();
{
  const govde = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.12, 0.28), new THREE.MeshStandardMaterial({ color: 0x2b2f36, roughness: 0.6 }));
  govde.position.y = -0.11;
  const ok = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.16, 16), new THREE.MeshStandardMaterial({ color: 0xf2a541, emissive: 0x6a3a00 }));
  ok.rotation.x = -Math.PI / 2; ok.position.set(0, -0.02, -0.2);
  const kam = new THREE.Mesh(new THREE.SphereGeometry(0.04, 16, 12), new THREE.MeshStandardMaterial({ color: 0x111111 }));
  robotIsaret.add(govde, ok, kam);
}
sahne.add(robotIsaret);

const dunya = new THREE.Group();                 // harita noktaları + yollar
sahne.add(dunya);
const arGrup = new THREE.Group();                // AR nesneleri
sahne.add(arGrup);
let bulut = null, bulutKatman = null, yolCizgi = null, gecilenCizgi = null, izgara = null;
const hareketliler = [];

// ---------- yardımcılar ----------
function b64(s, Tip) {
  const ham = atob(s); const b = new Uint8Array(ham.length);
  for (let i = 0; i < ham.length; i++) b[i] = ham.charCodeAt(i);
  return new Tip(b.buffer);
}
const golgeDoku = (() => {
  const c = document.createElement('canvas'); c.width = c.height = 128;
  const g = c.getContext('2d'); const r = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  r.addColorStop(0, 'rgba(0,0,0,0.55)'); r.addColorStop(1, 'rgba(0,0,0,0)');
  g.fillStyle = r; g.fillRect(0, 0, 128, 128);
  return new THREE.CanvasTexture(c);
})();
function golge(r) {
  const m = new THREE.Mesh(new THREE.PlaneGeometry(r * 2, r * 2), new THREE.MeshBasicMaterial({ map: golgeDoku, transparent: true, depthWrite: false }));
  m.rotation.x = -Math.PI / 2; m.position.y = 0.003;
  return m;
}
const malzeme = (renk, isik = 0, ek = {}) => new THREE.MeshStandardMaterial({ color: renk, emissive: renk, emissiveIntensity: isik, roughness: 0.45, metalness: 0.1, ...ek });

// ---------- AR nesneleri ----------
function serbestMi(x, z) {
  if (!planImg) return true;
  const p = veri.plan; const u = Math.floor((x - p.x0) / p.hucre), v = Math.floor((z - p.z0) / p.hucre);
  if (u < 0 || v < 0 || u >= p.g || v >= p.y) return false;
  const d = planPiksel.data; const i = (v * p.g + u) * 4;
  return d[i + 3] > 0 && d[i] > 180;
}
let planPiksel = null;
function yerBul(hedef, minYol, maxYol) {
  // hedefe en yakın, yoldan minYol..maxYol uzak, boş zeminli nokta
  let en = null, enD = 1e9;
  for (let r = 0.2; r < 3; r += 0.1) for (let a = 0; a < Math.PI * 2; a += Math.PI / 12) {
    const x = hedef[0] + Math.cos(a) * r, z = hedef[1] + Math.sin(a) * r;
    if (!serbestMi(x, z) || !serbestMi(x + 0.15, z) || !serbestMi(x - 0.15, z) || !serbestMi(x, z + 0.15) || !serbestMi(x, z - 0.15)) continue;
    let dy = 1e9; for (const q of haritaYolu) dy = Math.min(dy, Math.hypot(q[0] - x, q[1] - z));
    if (dy < minYol || dy > maxYol) continue;
    const d = Math.hypot(x - hedef[0], z - hedef[1]);
    if (d < enD) { enD = d; en = [x, z]; }
  }
  return en || hedef;
}
function arKur() {
  arGrup.clear(); hareketliler.length = 0;
  const Y = haritaYolu; if (Y.length < 4) return;
  const at = (f) => Y[Math.min(Y.length - 1, Math.floor(f * Y.length))];
  const isimler = [];

  // Üs: başlangıç noktasında yer halkası ve ışık sütunu
  const us = new THREE.Group();
  const halka = new THREE.Mesh(new THREE.TorusGeometry(0.32, 0.018, 12, 64), malzeme(0x6fcf97, 1.2));
  halka.rotation.x = -Math.PI / 2; halka.position.y = 0.01;
  const ic = new THREE.Mesh(new THREE.CircleGeometry(0.3, 48), new THREE.MeshBasicMaterial({ color: 0x6fcf97, transparent: true, opacity: 0.18, depthWrite: false }));
  ic.rotation.x = -Math.PI / 2; ic.position.y = 0.006;
  const sutun = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 1.6, 24, 1, true),
    new THREE.MeshBasicMaterial({ color: 0x9ff0bf, transparent: true, opacity: 0.25, depthWrite: false, side: THREE.DoubleSide }));
  sutun.position.y = 0.8;
  us.add(halka, ic, sutun);
  us.position.set(Y[0][0], 0, Y[0][1]);
  arGrup.add(us); isimler.push({ ad: 'Üs', renk: '#6fcf97', nesne: us });
  hareketliler.push((t) => { halka.scale.setScalar(1 + 0.04 * Math.sin(t * 3)); sutun.material.opacity = 0.18 + 0.08 * Math.sin(t * 2); });

  // Kristal (duran, havada süzülen)
  const kp = yerBul(at(0.3), 0.5, 1.4);
  const kristal = new THREE.Group();
  const tas = new THREE.Mesh(new THREE.OctahedronGeometry(0.11, 0), malzeme(0x7fc8ff, 0.7, { flatShading: true }));
  tas.scale.y = 1.5;
  kristal.add(tas); const kg = golge(0.18); kristal.add(kg);
  kristal.position.set(kp[0], 0, kp[1]);
  arGrup.add(kristal); isimler.push({ ad: 'Kristal', renk: '#7fc8ff', nesne: kristal });
  hareketliler.push((t) => { tas.position.y = 0.32 + 0.04 * Math.sin(t * 1.7); tas.rotation.y = t * 0.8; kg.scale.setScalar(1 - 0.15 * Math.sin(t * 1.7)); });

  // Fener kulesi (duran)
  const fp = yerBul(at(0.65), 0.5, 1.4);
  const kule = new THREE.Group();
  const ayak = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.09, 0.42, 20), malzeme(0x3a3f47, 0, { metalness: 0.5 }));
  ayak.position.y = 0.21;
  const tepe = new THREE.Mesh(new THREE.SphereGeometry(0.07, 24, 16), malzeme(0xf2a541, 2.0));
  tepe.position.y = 0.48;
  const sapka = new THREE.Mesh(new THREE.ConeGeometry(0.1, 0.08, 20), malzeme(0x2b2f36));
  sapka.position.y = 0.57;
  kule.add(ayak, tepe, sapka, golge(0.2));
  kule.position.set(fp[0], 0, fp[1]);
  arGrup.add(kule); isimler.push({ ad: 'Fener', renk: '#f2a541', nesne: kule });
  hareketliler.push((t) => { tepe.material.emissiveIntensity = 1.4 + 0.8 * Math.sin(t * 4); });

  // Dron (hareketli): yolun ortası çevresinde döner
  let cx = 0, cz = 0; for (const q of Y) { cx += q[0]; cz += q[1]; } cx /= Y.length; cz /= Y.length;
  let R = 0; for (const q of Y) R = Math.max(R, Math.hypot(q[0] - cx, q[1] - cz)); R = Math.min(Math.max(R * 0.7, 0.8), 2.5);
  const dron = new THREE.Group();
  const govde = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.04, 0.14), malzeme(0xe6e3dc, 0.1));
  dron.add(govde);
  const pervaneler = [];
  for (const [dx, dz] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) {
    const kol = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.012, 0.012), malzeme(0x2b2f36));
    kol.position.set(dx * 0.07, 0, dz * 0.07); kol.rotation.y = Math.atan2(dz, dx); dron.add(kol);
    const p = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 0.004, 24), new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.35 }));
    p.position.set(dx * 0.11, 0.025, dz * 0.11); dron.add(p); pervaneler.push(p);
  }
  const goz = new THREE.Mesh(new THREE.SphereGeometry(0.02, 12, 8), malzeme(0xeb5757, 2));
  goz.position.set(0, -0.01, -0.075); dron.add(goz);
  const dg = golge(0.16); arGrup.add(dg);
  arGrup.add(dron); isimler.push({ ad: 'Dron', renk: '#eb5757', nesne: dron });
  hareketliler.push((t) => {
    const a = t * 0.35; const x = cx + Math.cos(a) * R, z = cz + Math.sin(a) * R;
    dron.position.set(x, 0.75 + 0.06 * Math.sin(t * 2.3), z);
    dron.rotation.y = -a; dron.rotation.z = 0.12;
    pervaneler.forEach((p, i) => p.rotation.y = t * 40 + i);
    dg.position.set(x, 0.003, z); dg.scale.setScalar(1.4);
  });

  // Gezgin (hareketli, yerde zıplayarak iki nokta arasında gider)
  const g1 = yerBul(at(0.15), 0.35, 1.0), g2 = yerBul(at(0.5), 0.35, 1.0);
  const gezgin = new THREE.Group();
  const beden = new THREE.Mesh(new THREE.SphereGeometry(0.09, 24, 16), malzeme(0xb28cff, 0.35));
  beden.scale.y = 0.85;
  const g1m = new THREE.Mesh(new THREE.SphereGeometry(0.018, 10, 8), malzeme(0xffffff, 1)); g1m.position.set(-0.035, 0.03, -0.075);
  const g2m = g1m.clone(); g2m.position.x = 0.035;
  const ic2 = new THREE.Group(); ic2.add(beden, g1m, g2m);
  const gg = golge(0.14);
  gezgin.add(ic2, gg);
  arGrup.add(gezgin); isimler.push({ ad: 'Gezgin', renk: '#b28cff', nesne: gezgin });
  const uz = Math.hypot(g2[0] - g1[0], g2[1] - g1[1]) || 1;
  hareketliler.push((t) => {
    const hiz = 0.35; const s = (t * hiz) % (2 * uz); const ileri = s < uz; const d = ileri ? s : 2 * uz - s;
    const f = d / uz; const x = g1[0] + (g2[0] - g1[0]) * f, z = g1[1] + (g2[1] - g1[1]) * f;
    gezgin.position.set(x, 0, z);
    const zip = Math.abs(Math.sin(t * 6));
    ic2.position.y = 0.08 + 0.08 * zip; beden.scale.set(1 + 0.1 * (1 - zip), 0.85 - 0.1 * (1 - zip), 1 + 0.1 * (1 - zip));
    gezgin.rotation.y = Math.atan2(-(g2[0] - g1[0]) * (ileri ? 1 : -1), -(g2[1] - g1[1]) * (ileri ? 1 : -1));
    gg.scale.setScalar(1 - 0.3 * zip);
  });
  arGrup.userData.isimler = isimler;
}

// ---------- veri ----------
async function katalogYukle() {
  katalog = await (await fetch('veri/katalog.json')).json();
  const liste = $('#liste'); liste.innerHTML = '';
  for (const k of katalog) {
    const el = document.createElement('div'); el.className = 'kayit';
    const oran = Math.round(100 * k.takip / k.kare);
    el.innerHTML = `<b>${k.baslik}</b><small><span class="etiket ${k.tur}">${k.tur === 'konum' ? 'konum bulma' : 'haritalama'}</span>${oran}% kare konumlu</small>`;
    el.onclick = () => kayitAc(k.ad);
    el.dataset.ad = k.ad; liste.appendChild(el);
  }
  const ilk = new URLSearchParams(location.search).get('k') || katalog[0]?.ad;
  if (ilk) kayitAc(ilk);
}

async function kayitAc(ad) {
  $('#yukleniyor').style.display = 'flex';
  document.querySelectorAll('.kayit').forEach((e) => e.classList.toggle('secili', e.dataset.ad === ad));
  veri = await (await fetch(`veri/${ad}/veri.json`)).json();
  poz = veri.poz;
  haritaYolu = veri.harita_yolu;
  kosuYolu = []; for (const p of poz) if (p) kosuYolu.push([p[0], p[2]]);
  sonPoz = null; robot.position.set(0, 0.17, 0); robot.quaternion.identity();
  planImg = await new Promise((ok) => { const i = new Image(); i.onload = () => ok(i); i.src = `veri/${ad}/plan.png`; });
  const c = document.createElement('canvas'); c.width = planImg.width; c.height = planImg.height;
  const g = c.getContext('2d'); g.drawImage(planImg, 0, 0); planPiksel = g.getImageData(0, 0, c.width, c.height);

  // nokta bulutu (renkler sRGB -> doğrusal)
  dunya.clear();
  const P = b64(veri.nokta, Float32Array); const C = b64(veri.renk, Uint8Array);
  const renk = new Float32Array(C.length); for (let i = 0; i < C.length; i++) renk[i] = Math.pow(C[i] / 255, 2.2);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(P, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(renk, 3));
  bulut = new THREE.Points(geo, new THREE.PointsMaterial({ size: 0.014, vertexColors: true }));
  bulutKatman = new THREE.Points(geo, new THREE.PointsMaterial({ size: 3, sizeAttenuation: false, color: 0x5cff8a, depthTest: false, transparent: true, opacity: 0.8 }));
  bulutKatman.renderOrder = 5;
  dunya.add(bulut, bulutKatman);
  const yolNok = (Y, y) => Y.map((q) => new THREE.Vector3(q[0], y, q[1]));
  yolCizgi = new THREE.Line(new THREE.BufferGeometry().setFromPoints(yolNok(haritaYolu, 0.01)), new THREE.LineBasicMaterial({ color: 0x8e949c, transparent: true, opacity: 0.6 }));
  const gp = new Float32Array(kosuYolu.length * 3);
  gecilenCizgi = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: 0xf2a541 }));
  gecilenCizgi.geometry.setAttribute('position', new THREE.BufferAttribute(gp, 3));
  kosuYolu.forEach((q, i) => gp.set([q[0], 0.015, q[1]], i * 3));
  izgara = new THREE.GridHelper(20, 20, 0x3a4048, 0x262a30);
  dunya.add(yolCizgi, gecilenCizgi, izgara);

  arKur();
  // 3B kamerayı haritaya oturt
  const kutu = new THREE.Box3().setFromPoints(yolNok(haritaYolu, 0));
  const m = kutu.getCenter(new THREE.Vector3()); const r = Math.max(2, kutu.getSize(new THREE.Vector3()).length());
  yorunge.target.copy(m); ucKam.position.set(m.x + r * 0.6, r * 0.9, m.z + r * 0.9); yorunge.update();
  planGorus.merkez = null;

  video.src = `veri/${ad}/video.mp4`;
  video.playbackRate = +$('#hiz').value;
  await video.play().catch(() => {});
  seritCiz();
  $('#oran').textContent = `${Math.round(100 * veri.takip / veri.kare)}% kare konumlu`;
  $('#yukleniyor').style.display = 'none';
}

// ---------- zaman çubuğu ----------
function seritCiz() {
  const c = $('#serit'); c.width = c.clientWidth; c.height = 6;
  const g = c.getContext('2d');
  for (let x = 0; x < c.width; x++) {
    const i0 = Math.floor(x / c.width * poz.length), i1 = Math.max(i0 + 1, Math.floor((x + 1) / c.width * poz.length));
    let n = 0; for (let i = i0; i < i1; i++) if (poz[i]) n++;
    g.fillStyle = n / (i1 - i0) > 0.5 ? '#3d7a59' : '#7a3434'; g.fillRect(x, 0, 1, 6);
  }
}
$('#zaman').addEventListener('pointerdown', (e) => {
  const hedef = (ev) => { const r = $('#zaman').getBoundingClientRect(); video.currentTime = Math.max(0, Math.min(1, (ev.clientX - r.left) / r.width)) * video.duration; };
  hedef(e); const m = (ev) => hedef(ev); const u = () => { removeEventListener('pointermove', m); removeEventListener('pointerup', u); };
  addEventListener('pointermove', m); addEventListener('pointerup', u);
});
$('#oynat').onclick = () => { video.paused ? video.play() : video.pause(); };
video.addEventListener('play', () => $('#oynat').textContent = '❚❚');
video.addEventListener('pause', () => $('#oynat').textContent = '▶');
$('#hiz').onchange = (e) => video.playbackRate = +e.target.value;
addEventListener('keydown', (e) => { if (e.code === 'Space') { e.preventDefault(); $('#oynat').click(); } });

// kare eşlemesi: mümkünse videonun gerçekten gösterdiği kare
if ('requestVideoFrameCallback' in HTMLVideoElement.prototype) {
  const cb = (_, m) => { kare = Math.round(m.mediaTime * (veri?.fps || 15)); video.requestVideoFrameCallback(cb); };
  video.requestVideoFrameCallback(cb);
}

// ---------- görünüm seçimi ----------
document.querySelectorAll('#gorunum button').forEach((b) => b.onclick = () => {
  gorunum = b.dataset.g;
  document.querySelectorAll('#gorunum button').forEach((x) => x.classList.toggle('acik', x === b));
  const plan = gorunum === 'plan';
  glCanvas.style.display = plan ? 'none' : 'block'; planCanvas.style.display = plan ? 'block' : 'none';
  miniCanvas.style.display = gorunum === 'kamera' ? 'block' : 'none';
  $('#secenek').style.display = gorunum === 'kamera' ? 'flex' : 'none';
  yorunge.enabled = gorunum === 'uc';
  $('#ipucu').textContent = { kamera: 'Sürükle: etrafa bak · Teker: yakınlaş · Boşluk: durdur', uc: 'Sürükle: döndür · Sağ tık: kaydır · Teker: yakınlaş', plan: 'Sürükle: kaydır · Teker: yakınlaş' }[gorunum];
  boyutla();
});

// kamera görünümünde sürükleyerek bakış
let surukle = null;
glCanvas.addEventListener('pointerdown', (e) => { if (gorunum === 'kamera') surukle = [e.clientX, e.clientY, bakYaw, bakPitch]; });
addEventListener('pointermove', (e) => {
  if (!surukle) return; const k = kamKam.fov / glCanvas.clientHeight * Math.PI / 180;
  bakYaw = surukle[2] + (e.clientX - surukle[0]) * k; bakPitch = Math.max(-1.3, Math.min(1.3, surukle[3] + (e.clientY - surukle[1]) * k));
});
addEventListener('pointerup', () => surukle = null);
glCanvas.addEventListener('wheel', (e) => { if (gorunum !== 'kamera') return; e.preventDefault(); kamKam.fov = Math.max(30, Math.min(110, kamKam.fov + e.deltaY * 0.05)); kamKam.updateProjectionMatrix(); }, { passive: false });

// plan görünümünde kaydırma/yakınlaşma
let planSur = null;
planCanvas.addEventListener('pointerdown', (e) => planSur = [e.clientX, e.clientY, ...planGorus.merkez]);
addEventListener('pointermove', (e) => { if (planSur) planGorus.merkez = [planSur[2] - (e.clientX - planSur[0]) / planGorus.olcek, planSur[3] - (e.clientY - planSur[1]) / planGorus.olcek]; });
addEventListener('pointerup', () => planSur = null);
planCanvas.addEventListener('wheel', (e) => { e.preventDefault(); planGorus.olcek = Math.max(15, Math.min(400, planGorus.olcek * Math.exp(-e.deltaY * 0.0015))); }, { passive: false });

function boyutla() {
  const w = sahneEl.clientWidth, h = sahneEl.clientHeight;
  renderer.setSize(w, h, false);
  kamKam.aspect = ucKam.aspect = w / h; kamKam.updateProjectionMatrix(); ucKam.updateProjectionMatrix();
  for (const [c, cw, ch] of [[planCanvas, w, h], [miniCanvas, miniCanvas.clientWidth, miniCanvas.clientHeight]]) {
    c.width = cw * devicePixelRatio; c.height = ch * devicePixelRatio;
  }
  seritCiz();
}
addEventListener('resize', boyutla);

// ---------- 2B plan çizimi ----------
const ileriVek = new THREE.Vector3();
function planCiz(c, merkez, olcek, mini) {
  const g = c.getContext('2d'); const W = c.width, H = c.height, dpr = devicePixelRatio;
  g.setTransform(1, 0, 0, 1, 0, 0); g.fillStyle = '#101215'; g.fillRect(0, 0, W, H);
  if (!veri || !planImg) return;
  const s = olcek * dpr; const ox = W / 2 - merkez[0] * s, oy = H / 2 - merkez[1] * s;
  const P = (x, z) => [ox + x * s, oy + z * s];
  const p = veri.plan;
  g.imageSmoothingEnabled = true;
  g.globalAlpha = 0.95;
  g.drawImage(planImg, ox + p.x0 * s, oy + p.z0 * s, p.g * p.hucre * s, p.y * p.hucre * s);
  g.globalAlpha = 1;
  // 1 m ızgara
  g.strokeStyle = 'rgba(255,255,255,0.06)'; g.lineWidth = 1;
  const x0 = Math.floor((-ox) / s) - 1, x1 = Math.ceil((W - ox) / s) + 1, z0 = Math.floor((-oy) / s) - 1, z1 = Math.ceil((H - oy) / s) + 1;
  g.beginPath();
  for (let x = x0; x <= x1; x++) { g.moveTo(ox + x * s, 0); g.lineTo(ox + x * s, H); }
  for (let z = z0; z <= z1; z++) { g.moveTo(0, oy + z * s); g.lineTo(W, oy + z * s); }
  g.stroke();
  // haritalama yolu (soluk) ve bu kaydın geçtiği yol
  const yol = (Y, renk, kalin, son) => {
    g.strokeStyle = renk; g.lineWidth = kalin * dpr; g.lineJoin = 'round'; g.beginPath();
    const n = son ?? Y.length; for (let i = 0; i < n; i++) { const [a, b] = P(Y[i][0], Y[i][1]); i ? g.lineTo(a, b) : g.moveTo(a, b); }
    g.stroke();
  };
  yol(haritaYolu, 'rgba(142,148,156,0.45)', 1.5);
  let gecilen = 0; for (let i = 0; i <= Math.min(kare, poz.length - 1); i++) if (poz[i]) gecilen++;
  yol(kosuYolu, '#f2a541', 2.2, gecilen);
  // AR nesneleri
  for (const n of arGrup.userData.isimler || []) {
    const [a, b] = P(n.nesne.position.x, n.nesne.position.z);
    g.fillStyle = n.renk; g.beginPath(); g.arc(a, b, (mini ? 4 : 6) * dpr, 0, 7); g.fill();
    if (!mini) {
      g.font = `600 ${12 * dpr}px Segoe UI`; g.lineWidth = 3 * dpr; g.strokeStyle = '#101215'; g.fillStyle = '#ffffff';
      g.strokeText(n.ad, a + 10 * dpr, b + 4 * dpr); g.fillText(n.ad, a + 10 * dpr, b + 4 * dpr);
    }
  }
  // robot ve bakış konisi
  if (sonPoz) {
    const [a, b] = P(sonPoz[0], sonPoz[2]);
    ileriVek.set(0, 0, -1).applyQuaternion(robot.quaternion); const yon = Math.atan2(ileriVek.z, ileriVek.x);
    if (gorunum === 'kamera') {
      const by = yon - bakYaw; const ac = kamKam.fov * kamKam.aspect * Math.PI / 360;
      g.fillStyle = 'rgba(127,182,255,0.18)'; g.beginPath(); g.moveTo(a, b); g.arc(a, b, 60 * dpr, by - ac, by + ac); g.fill();
    }
    g.save(); g.translate(a, b); g.rotate(yon);
    g.fillStyle = poz[kare] ? '#f2a541' : '#eb5757'; g.strokeStyle = '#101215'; g.lineWidth = 2 * dpr;
    g.beginPath(); g.moveTo(11 * dpr, 0); g.lineTo(-7 * dpr, 7 * dpr); g.lineTo(-4 * dpr, 0); g.lineTo(-7 * dpr, -7 * dpr); g.closePath(); g.fill(); g.stroke();
    g.restore();
  }
  if (!mini) {   // ölçek çubuğu
    g.fillStyle = '#e6e3dc'; g.fillRect(W - 20 * dpr - s, 30 * dpr, s, 3 * dpr);
    g.font = `${12 * dpr}px Segoe UI`; g.textAlign = 'right'; g.fillText('1 m', W - 20 * dpr, 24 * dpr); g.textAlign = 'left';
  }
}

// ---------- döngü ----------
const saat = new THREE.Clock();
function dongu() {
  requestAnimationFrame(dongu);
  if (!veri || !bulut) return;
  if (!('requestVideoFrameCallback' in HTMLVideoElement.prototype)) kare = Math.round(video.currentTime * veri.fps);
  kare = Math.max(0, Math.min(poz.length - 1, kare));
  // kayıp karede en son bilinen poz tutulur
  let p = poz[kare];
  if (!p) { for (let i = kare; i >= 0; i--) if (poz[i]) { p = poz[i]; break; } }
  if (!p) for (let i = kare; i < poz.length; i++) if (poz[i]) { p = poz[i]; break; }
  if (p) { sonPoz = p; robot.position.set(p[0], p[1], p[2]); robot.quaternion.set(p[3], p[4], p[5], p[6]); }
  bakis.rotation.set(0, 0, 0); bakis.rotateY(-bakYaw); bakis.rotateX(-bakPitch);
  const takipte = !!poz[kare];
  $('#takip').innerHTML = `<span class="nokta ${takipte ? '' : 'kayip'}"></span>${takipte ? 'konum bulundu' : 'konum kayıp'}`;
  if (sonPoz) $('#konum').textContent = `x ${sonPoz[0].toFixed(2).replace('.', ',')} · z ${sonPoz[2].toFixed(2).replace('.', ',')} m`;
  const t = video.currentTime, d = video.duration || 1;
  $('#imlec').style.left = `${100 * t / d}%`;
  const dk = (x) => `${Math.floor(x / 60)}:${String(Math.floor(x % 60)).padStart(2, '0')}`;
  $('#sure').textContent = `${dk(t)} / ${dk(d)}`;

  const zt = saat.getElapsedTime();
  for (const f of hareketliler) f(zt);
  if (gecilenCizgi) { let n = 0; for (let i = 0; i <= kare; i++) if (poz[i]) n++; gecilenCizgi.geometry.setDrawRange(0, n); }

  if (gorunum === 'kamera') {
    kure.visible = true; robotIsaret.visible = false; arGrup.visible = $('#ar').checked && !!sonPoz;
    bulut.visible = false; bulutKatman.visible = $('#noktalar').checked; yolCizgi.visible = gecilenCizgi.visible = izgara.visible = false;
    renderer.render(sahne, kamKam);
    planCiz(miniCanvas, [sonPoz?.[0] || 0, sonPoz?.[2] || 0], 28, true);
  } else if (gorunum === 'uc') {
    kure.visible = false; robotIsaret.visible = true; arGrup.visible = true;
    robotIsaret.position.copy(robot.position); robotIsaret.quaternion.copy(robot.quaternion);
    bulut.visible = true; bulutKatman.visible = false; yolCizgi.visible = gecilenCizgi.visible = izgara.visible = true;
    yorunge.update(); renderer.render(sahne, ucKam);
  } else {
    if (!planGorus.merkez) {
      let a = 1e9, b = -1e9, c = 1e9, e = -1e9; for (const q of haritaYolu) { a = Math.min(a, q[0]); b = Math.max(b, q[0]); c = Math.min(c, q[1]); e = Math.max(e, q[1]); }
      planGorus.merkez = [(a + b) / 2, (c + e) / 2];
      planGorus.olcek = Math.min(planCanvas.clientWidth / (b - a + 3), planCanvas.clientHeight / (e - c + 3));
    }
    planCiz(planCanvas, planGorus.merkez, planGorus.olcek, false);
  }
}
boyutla();
dongu();
katalogYukle();
