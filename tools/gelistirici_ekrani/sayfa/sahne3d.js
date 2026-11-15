"use strict";
/* Bağımlılıksız küçük WebGL sahne çizici (CDN yok, çevrimdışı çalışır).
   z yukarı, metre. Nokta kümeleri artımlıdır: büyüyen Float32Array + bufferSubData (50 bin+ nokta rahat).
   Katman türleri (katman tanımındaki tur3d): noktalar | parca | cizgi | kameralar | zemin. Çizim API'si arkauc.py "3B BÖLÜMÜ" ile aynıdır. */

/* turbo renk eşlemesi (Google, polinom yaklaşımı) ve tablo */
const RENK_HARITALARI = (() => {
  const kes = (x) => Math.max(0, Math.min(1, x));
  const turbo = (t) => {
    t = kes(t);
    const r = 0.13572138 + t * (4.61539260 + t * (-42.66032258 + t * (132.13108234 + t * (-152.94239396 + t * 59.28637943))));
    const g = 0.09140261 + t * (2.19418839 + t * (4.84296658 + t * (-14.18503333 + t * (4.27729857 + t * 2.82956604))));
    const b = 0.10667330 + t * (12.64194608 + t * (-60.58204836 + t * (110.36276771 + t * (-89.90310912 + t * 27.34824973))));
    return [kes(r), kes(g), kes(b)];
  };
  const tablo = (f) => { const T = new Float32Array(256 * 3); for (let i = 0; i < 256; i++){ const c = f(i / 255); T[i*3] = c[0]; T[i*3+1] = c[1]; T[i*3+2] = c[2]; } return T; };
  return {turbo: tablo(turbo), gri: tablo((t) => [t, t, t])};
})();
function renkTablosu(ad){ return RENK_HARITALARI[ad] || RENK_HARITALARI.turbo; }
function hexRenk(h){ return [parseInt(h.slice(1, 3), 16) / 255, parseInt(h.slice(3, 5), 16) / 255, parseInt(h.slice(5, 7), 16) / 255]; }

/* 4x4 matrisler (sütun öncelikli) */
const M4 = {
  carp(a, b){ const o = new Float32Array(16); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++){ let s = 0; for (let k = 0; k < 4; k++) s += a[k*4 + r] * b[c*4 + k]; o[c*4 + r] = s; } return o; },
  persp(fov, en, yakin, uzak){ const f = 1 / Math.tan(fov / 2), o = new Float32Array(16); o[0] = f / en; o[5] = f; o[10] = (uzak + yakin) / (yakin - uzak); o[11] = -1; o[14] = 2 * uzak * yakin / (yakin - uzak); return o; },
  bak(e, h, u){
    let fx = h[0] - e[0], fy = h[1] - e[1], fz = h[2] - e[2]; let n = Math.hypot(fx, fy, fz); fx /= n; fy /= n; fz /= n;
    let sx = fy * u[2] - fz * u[1], sy = fz * u[0] - fx * u[2], sz = fx * u[1] - fy * u[0]; n = Math.hypot(sx, sy, sz); sx /= n; sy /= n; sz /= n;
    const ux = sy * fz - sz * fy, uy = sz * fx - sx * fz, uz = sx * fy - sy * fx;
    const o = new Float32Array(16);
    o[0] = sx; o[4] = sy; o[8] = sz; o[1] = ux; o[5] = uy; o[9] = uz; o[2] = -fx; o[6] = -fy; o[10] = -fz;
    o[12] = -(sx * e[0] + sy * e[1] + sz * e[2]); o[13] = -(ux * e[0] + uy * e[1] + uz * e[2]); o[14] = fx * e[0] + fy * e[1] + fz * e[2]; o[15] = 1;
    return o;
  },
};

class Sahne3D {
  constructor(tuval){
    this.c = tuval;
    this.gl = tuval.getContext("webgl", {antialias: true, alpha: false}) || tuval.getContext("experimental-webgl");
    this.ok = !!this.gl;
    this.gorus = {az: 200 * Math.PI / 180, el: 38 * Math.PI / 180, uzak: 6, hedef: [0, 0, 0], otomatik: true};
    this.katmanlar = new Map();      // id -> {tur, ...}
    this.guncel = null;              // güncel kamera
    this.doku = null;                // {tex, x0, y0, w, h, ver}
    this.sinir = {min: [Infinity, Infinity, 0], max: [-Infinity, -Infinity, 0.2]};
    this.nokta_sayisi = 0;
    this.kamera_sayisi = 0;
    this.esik = 0;
    if (!this.ok) return;
    const gl = this.gl;
    const derle = (tur, kod) => { const s = gl.createShader(tur); gl.shaderSource(s, kod); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; };
    const program = (vs, fs) => { const p = gl.createProgram(); gl.attachShader(p, derle(gl.VERTEX_SHADER, vs)); gl.attachShader(p, derle(gl.FRAGMENT_SHADER, fs)); gl.linkProgram(p); if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p)); return p; };
    this.pN = program(
      "attribute vec3 p;attribute vec3 c;attribute float gv;uniform mat4 vp;uniform float boyut;uniform float dpr;uniform float esik;varying vec3 rc;void main(){gl_Position=vp*vec4(p,1.);if(gv<esik)gl_Position=vec4(2.,2.,2.,1.);rc=c;gl_PointSize=clamp(boyut*dpr*(2.2/max(gl_Position.w,.5)),1.5*dpr,boyut*dpr*2.2);}",
      "precision mediump float;varying vec3 rc;void main(){vec2 d=gl_PointCoord-.5;if(dot(d,d)>.25)discard;gl_FragColor=vec4(rc,1.);}");
    this.pL = program(
      "attribute vec3 p;attribute vec3 c;uniform mat4 vp;varying vec3 rc;void main(){gl_Position=vp*vec4(p,1.);rc=c;}",
      "precision mediump float;varying vec3 rc;uniform float alfa;void main(){gl_FragColor=vec4(rc,alfa);}");
    this.pT = program(
      "attribute vec3 p;attribute vec2 t;uniform mat4 vp;varying vec2 rt;void main(){gl_Position=vp*vec4(p,1.);rt=t;}",
      "precision mediump float;varying vec2 rt;uniform sampler2D tx;void main(){vec4 k=texture2D(tx,rt);gl_FragColor=vec4(k.rgb,k.a*.9);}");
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    this.zeminBuf = gl.createBuffer();
    this.izgaraBuf = gl.createBuffer();
    this.anlikBuf = gl.createBuffer();
    this.dokuBuf = gl.createBuffer();
  }

  sifirla(){
    if (!this.ok) return;
    for (const k of this.katmanlar.values()) if (k.buf) this.gl.deleteBuffer(k.buf);
    this.katmanlar.clear();
    this.guncel = null;
    this.sinir = {min: [Infinity, Infinity, 0], max: [-Infinity, -Infinity, 0.2]};
    this.nokta_sayisi = 0; this.kamera_sayisi = 0;
    this.gorus.otomatik = true;
  }

  _katman(id, tur, renk){
    let k = this.katmanlar.get(id);
    if (!k){
      k = {tur, renk: hexRenk(renk || "#ffffff"), veri: new Float32Array(7 * 4096), n: 0, buf: this.gl.createBuffer(), yuklenen: 0, kapasite_gl: 0, kameralar: [], kirli: false};
      this.katmanlar.set(id, k);
    }
    return k;
  }

  _sinirGenislet(x, y, z){
    const s = this.sinir;
    if (x < s.min[0]) s.min[0] = x; if (x > s.max[0]) s.max[0] = x;
    if (y < s.min[1]) s.min[1] = y; if (y > s.max[1]) s.max[1] = y;
    if (z < s.min[2]) s.min[2] = z; if (z > s.max[2]) s.max[2] = z;
  }

  /* arka uçtan gelen bir "ekle" işlemi; tanim = katman tanımı (tur3d, renk, lejant) */
  ekle(op, tanim){
    if (!this.ok || !tanim) return;
    const tur = tanim.tur3d;
    if (tur === "kameralar"){
      const k = this._katman(op.katman, tur, tanim.renk);
      for (const c of op.kameralar || []){ k.kameralar.push(c); this.kamera_sayisi++; this._sinirGenislet(c.x, c.y, c.z || 0); }
      k.kirli = true;
      return;
    }
    const xyz = op.xyz || [];
    const n = Math.floor(xyz.length / 3);
    if (!n) return;
    const k = this._katman(op.katman, tur, tanim.renk);
    if (k.n + n > k.veri.length / 7){ const y = new Float32Array(Math.max(k.veri.length * 2, (k.n + n) * 7)); y.set(k.veri.subarray(0, k.n * 7)); k.veri = y; k.yuklenen = 0; }
    const lej = tanim.lejant, T = lej ? renkTablosu(lej.harita) : null;
    const lo = lej ? lej.aralik[0] : 0, hi = lej ? lej.aralik[1] : 1;
    for (let i = 0; i < n; i++){
      const o = (k.n + i) * 7;
      const x = xyz[i*3], y = xyz[i*3+1], z = xyz[i*3+2];
      k.veri[o] = x; k.veri[o+1] = y; k.veri[o+2] = z;
      let r = k.renk[0], g = k.renk[1], b = k.renk[2];
      if (op.renk){ r = op.renk[i*3]; g = op.renk[i*3+1]; b = op.renk[i*3+2]; }
      else if (op.deger && T){ const j = Math.max(0, Math.min(255, Math.round((op.deger[i] - lo) / (hi - lo) * 255))); r = T[j*3]; g = T[j*3+1]; b = T[j*3+2]; }
      k.veri[o+3] = r; k.veri[o+4] = g; k.veri[o+5] = b; k.veri[o+6] = op.guven ? op.guven[i] : 1;
      this._sinirGenislet(x, y, z);
    }
    k.n += n;
    if (tur === "noktalar") this.nokta_sayisi += n;
    k.kirli = true;
  }

  guncelKamera(c){ this.guncel = c; if (c) this._sinirGenislet(c.x, c.y, c.z || 0); }

  dokuKoy(tuval, op){
    if (!this.ok || !tuval || !op) return;
    const gl = this.gl;
    if (!this.doku) this.doku = {tex: gl.createTexture()};
    const d = this.doku;
    gl.bindTexture(gl.TEXTURE_2D, d.tex);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, tuval);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    d.x0 = op.x0; d.y0 = op.y0; d.w = op.w * op.hucre; d.h = op.h * op.hucre;
    this._sinirGenislet(d.x0, d.y0, 0); this._sinirGenislet(d.x0 + d.w, d.y0 + d.h, 0);
    const x0 = d.x0, y0 = d.y0, x1 = x0 + d.w, y1 = y0 + d.h;
    // doku satır 0 = kuzey (y1): v=0 -> y1
    const q = new Float32Array([x0, y0, -0.001, 0, 1,  x1, y0, -0.001, 1, 1,  x1, y1, -0.001, 1, 0,   x0, y0, -0.001, 0, 1,  x1, y1, -0.001, 1, 0,  x0, y1, -0.001, 0, 0]);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.dokuBuf);
    gl.bufferData(gl.ARRAY_BUFFER, q, gl.STATIC_DRAW);
  }

  /* --- kamera denetimi --- */
  sigdir(){
    const s = this.sinir;
    if (!isFinite(s.min[0])) return;
    const g = this.gorus;
    g.hedef = [(s.min[0] + s.max[0]) / 2, (s.min[1] + s.max[1]) / 2, 0.05];
    const yaricap = Math.max(Math.hypot(s.max[0] - s.min[0], s.max[1] - s.min[1]) / 2, 0.8);
    g.uzak = Math.min(Math.max(yaricap * 2.2, 1.5), 80);
  }
  doner(dx, dy){ const g = this.gorus; g.otomatik = false; g.az -= dx * 0.008; g.el = Math.max(0.05, Math.min(1.5, g.el + dy * 0.008)); }
  kaydir(dx, dy){
    const g = this.gorus; g.otomatik = false;
    const k = g.uzak * 0.0016;
    const sx = Math.cos(g.az), sy = -Math.sin(g.az);       // ekranda sağ
    const fx = -Math.sin(g.az), fy = -Math.cos(g.az);      // bakış yönü (yatay)
    g.hedef[0] += (-dx * sx + dy * fx) * k; g.hedef[1] += (-dx * sy + dy * fy) * k;
  }
  yakinlas(oran){ const g = this.gorus; g.otomatik = false; g.uzak = Math.min(Math.max(g.uzak * oran, 0.4), 300); }

  _vp(en){
    const g = this.gorus;
    const e = [g.hedef[0] + g.uzak * Math.cos(g.el) * Math.sin(g.az), g.hedef[1] + g.uzak * Math.cos(g.el) * Math.cos(g.az), g.hedef[2] + g.uzak * Math.sin(g.el)];
    return M4.carp(M4.persp(0.9, en, 0.05, 600), M4.bak(e, g.hedef, [0, 0, 1]));
  }

  _yukle(buf, veri, k){
    const gl = this.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    if (!k){ gl.bufferData(gl.ARRAY_BUFFER, veri, gl.DYNAMIC_DRAW); return; }
    if (k.yuklenen === 0 || k.veri.length > k.kapasite_gl){
      gl.bufferData(gl.ARRAY_BUFFER, k.veri, gl.DYNAMIC_DRAW); k.kapasite_gl = k.veri.length; k.yuklenen = k.n;
    } else if (k.n > k.yuklenen){
      gl.bufferSubData(gl.ARRAY_BUFFER, k.yuklenen * 28, k.veri.subarray(k.yuklenen * 7, k.n * 7)); k.yuklenen = k.n;
    }
  }

  _ciz(prog, buf, mod, ilk, adet, ek){
    const gl = this.gl;
    gl.useProgram(prog);
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    const p = gl.getAttribLocation(prog, "p"), c = gl.getAttribLocation(prog, "c");
    gl.enableVertexAttribArray(p); gl.vertexAttribPointer(p, 3, gl.FLOAT, false, 28, 0);
    gl.enableVertexAttribArray(c); gl.vertexAttribPointer(c, 3, gl.FLOAT, false, 28, 12);
    const gv = gl.getAttribLocation(prog, "gv");
    if (gv >= 0){ gl.enableVertexAttribArray(gv); gl.vertexAttribPointer(gv, 1, gl.FLOAT, false, 28, 24); }
    if (ek) ek(prog);
    gl.drawArrays(mod, ilk, adet);
  }

  _cizgiVeri(list){ // [[x,y,z,r,g,b],...] -> Float32Array
    return new Float32Array(list.map((r) => r.concat(1)).flat());
  }

  _kameraCizgileri(c, renk, olcek){
    const a = (c.yon || 0) * Math.PI / 180, s = 0.16 * olcek, w = 0.10 * olcek, h = 0.075 * olcek;
    const f = [Math.sin(a), Math.cos(a), 0], r = [Math.cos(a), -Math.sin(a), 0];
    const A = [c.x, c.y, c.z || 0];
    const K = (sg, su) => [A[0] + f[0] * s + r[0] * w * sg, A[1] + f[1] * s + r[1] * w * sg, A[2] + f[2] * s + h * su];
    const k = [K(-1, 1), K(1, 1), K(1, -1), K(-1, -1)];
    const out = [];
    const ekle = (p, q) => { out.push([p[0], p[1], p[2], renk[0], renk[1], renk[2]], [q[0], q[1], q[2], renk[0], renk[1], renk[2]]); };
    for (const q of k) ekle(A, q);
    for (let i = 0; i < 4; i++) ekle(k[i], k[(i + 1) % 4]);
    return out;
  }

  ciz(){
    if (!this.ok) return;
    const gl = this.gl, c = this.c;
    const dpr = Math.min(window.devicePixelRatio || 1, 2.5);
    const W = Math.round(c.clientWidth * dpr), H = Math.round(c.clientHeight * dpr);
    if (c.width !== W || c.height !== H){ c.width = W; c.height = H; }
    if (this.gorus.otomatik) this.sigdir();
    gl.viewport(0, 0, W, H);
    gl.clearColor(0.024, 0.031, 0.043, 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    const vp = this._vp(W / Math.max(H, 1));
    const vpU = (prog) => gl.uniformMatrix4fv(gl.getUniformLocation(prog, "vp"), false, vp);
    // 1) zemin ızgarası ve eksenler
    const g = this.gorus, adim = this._guzelAdim(g.uzak / 6);
    const cx = Math.round(g.hedef[0] / adim) * adim, cy = Math.round(g.hedef[1] / adim) * adim, N = 12;
    const L = [];
    for (let i = -N; i <= N; i++){
      const buyuk = (Math.round((cx + i * adim) / adim) % 5 === 0);
      const t = buyuk ? 0.2 : 0.11;
      const x = cx + i * adim, y = cy + i * adim, d = N * adim;
      L.push([x, cy - d, 0, t, t + .02, t + .06], [x, cy + d, 0, t, t + .02, t + .06], [cx - d, y, 0, t, t + .02, t + .06], [cx + d, y, 0, t, t + .02, t + .06]);
    }
    L.push([0, 0, 0, 1, .3, .3], [0.5, 0, 0, 1, .3, .3], [0, 0, 0, .3, 1, .4], [0, 0.5, 0, .3, 1, .4], [0, 0, 0, .35, .6, 1], [0, 0, 0.5, .35, .6, 1]);
    gl.useProgram(this.pL);
    this._yukle(this.izgaraBuf, this._cizgiVeri(L), null);
    this._ciz(this.pL, this.izgaraBuf, gl.LINES, 0, L.length, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "alfa"), 1); });
    // 2) doluluk dokusu
    if (this.doku && this.katmanAcik("doluluk3d")){
      gl.useProgram(this.pT);
      vpU(this.pT);
      gl.bindBuffer(gl.ARRAY_BUFFER, this.dokuBuf);
      const p = gl.getAttribLocation(this.pT, "p"), t = gl.getAttribLocation(this.pT, "t");
      gl.enableVertexAttribArray(p); gl.vertexAttribPointer(p, 3, gl.FLOAT, false, 20, 0);
      gl.enableVertexAttribArray(t); gl.vertexAttribPointer(t, 2, gl.FLOAT, false, 20, 12);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.doku.tex);
      gl.uniform1i(gl.getUniformLocation(this.pT, "tx"), 0);
      gl.depthMask(false);
      gl.drawArrays(gl.TRIANGLES, 0, 6);
      gl.depthMask(true);
    }
    // 3) katmanlar
    for (const [id, k] of this.katmanlar){
      if (!this.katmanAcik(id)) continue;
      if (k.tur === "noktalar"){
        this._yukle(k.buf, null, k);
        this._ciz(this.pN, k.buf, gl.POINTS, 0, k.n, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "boyut"), 3.2); gl.uniform1f(gl.getUniformLocation(p, "esik"), this.esik || 0); gl.uniform1f(gl.getUniformLocation(p, "dpr"), dpr); });
      } else if (k.tur === "parca"){
        this._yukle(k.buf, null, k);
        this._ciz(this.pL, k.buf, gl.LINES, 0, k.n, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "alfa"), 0.55); });
      } else if (k.tur === "cizgi"){
        this._yukle(k.buf, null, k);
        this._ciz(this.pL, k.buf, gl.LINE_STRIP, 0, k.n, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "alfa"), 1); });
        this._ciz(this.pN, k.buf, gl.POINTS, 0, k.n, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "boyut"), 1.6); gl.uniform1f(gl.getUniformLocation(p, "esik"), 0); gl.uniform1f(gl.getUniformLocation(p, "dpr"), dpr); });
      } else if (k.tur === "kameralar"){
        if (k.kirli || !k.cizgi){
          const lst = [];
          for (const cm of k.kameralar) lst.push(...this._kameraCizgileri(cm, k.renk, 1));
          k.cizgi = this._cizgiVeri(lst); k.cizgiN = lst.length; k.kirli = false;
          if (!k.cbuf) k.cbuf = gl.createBuffer();
          this._yukle(k.cbuf, k.cizgi, null);
        }
        if (k.cizgiN) this._ciz(this.pL, k.cbuf, gl.LINES, 0, k.cizgiN, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "alfa"), 0.9); });
      }
    }
    // 4) güncel kamera: vurgulu, derinlik testsiz
    if (this.guncel && this.katmanAcik("kamera3d")){
      const lst = this._kameraCizgileri(this.guncel, hexRenk("#ffb347"), 1.5);
      this._yukle(this.anlikBuf, this._cizgiVeri(lst), null);
      gl.disable(gl.DEPTH_TEST);
      this._ciz(this.pL, this.anlikBuf, gl.LINES, 0, lst.length, (p) => { vpU(p); gl.uniform1f(gl.getUniformLocation(p, "alfa"), 1); });
      gl.enable(gl.DEPTH_TEST);
    }
  }

  _guzelAdim(h){ for (const a of [0.1, 0.25, 0.5, 1, 2, 5, 10, 20]) if (a >= h) return a; return 50; }
  katmanAcik(id){ return this.acik ? !!this.acik[id] : true; }
  bilgi(){ return this.nokta_sayisi + " nokta · " + this.kamera_sayisi + " anahtar kare"; }
}
