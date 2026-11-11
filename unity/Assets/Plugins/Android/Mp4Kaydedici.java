package com.robotsurus.kayit;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;

/**
 * Ekran kaydını MP4'e yazar: görüntü telefonun donanım H.264 kodlayıcısıyla, ses AAC ile (MediaCodec + MediaMuxer).
 * Unity (ScreenRecorder) kare kare RGBA verir; satırlar aşağıdan yukarıdır (Unity dokusu), burada çevrilir.
 * Tüm çağrılar aynı iş parçacığından gelir (kayıt iş parçacığı); ana iş parçacığını bekletmez.
 */
public class Mp4Kaydedici {
    private static final long BEKLE_US = 10000;

    private final MediaCodec goruntu;
    private final MediaCodec ses;          // null: sessiz
    private final MediaMuxer kap;
    private final int genislik, yukseklik, fps, sesHizi;
    private int goruntuIz = -1, sesIz = -1;
    private boolean basladi;
    private long kare, sesOrnek;
    private final MediaCodec.BufferInfo bilgi = new MediaCodec.BufferInfo();
    // kap başlamadan (iki izin biçimi de belli olmadan) çıkan paketler burada bekler
    private final ArrayList<Object[]> bekleyen = new ArrayList<>();

    public Mp4Kaydedici(String yol, int genislik, int yukseklik, int fps, int bitHizi, int sesHizi) throws IOException {
        this.genislik = genislik;
        this.yukseklik = yukseklik;
        this.fps = fps;
        this.sesHizi = sesHizi;
        MediaFormat g = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, genislik, yukseklik);
        g.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
        g.setInteger(MediaFormat.KEY_BIT_RATE, bitHizi);
        g.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
        g.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
        goruntu = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
        goruntu.configure(g, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        goruntu.start();
        if (sesHizi > 0) {
            MediaFormat s = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, sesHizi, 1);
            s.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            s.setInteger(MediaFormat.KEY_BIT_RATE, 96000);
            s.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);
            ses = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
            ses.configure(s, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            ses.start();
        } else {
            ses = null;
        }
        kap = new MediaMuxer(yol, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
    }

    /** RGBA (genislik x yukseklik x 4, satırlar aşağıdan yukarı). Kodlayıcı doluysa kare atlanır. */
    public void kareEkle(byte[] rgba) {
        int i = goruntu.dequeueInputBuffer(BEKLE_US);
        if (i < 0) { bosalt(goruntu, false); return; }
        Image img = goruntu.getInputImage(i);
        Image.Plane[] p = img.getPlanes();
        ByteBuffer y = p[0].getBuffer(), u = p[1].getBuffer(), v = p[2].getBuffer();
        int ys = p[0].getRowStride(), us = p[1].getRowStride(), vs = p[2].getRowStride();
        int up = p[1].getPixelStride(), vp = p[2].getPixelStride();
        for (int satir = 0; satir < yukseklik; satir++) {
            int kaynak = (yukseklik - 1 - satir) * genislik * 4;   // üstten başla (Unity verisi alttan)
            int yb = satir * ys;
            boolean renk = (satir & 1) == 0;
            int ub = (satir / 2) * us, vb = (satir / 2) * vs;
            for (int x = 0; x < genislik; x++) {
                int r = rgba[kaynak] & 0xFF, gr = rgba[kaynak + 1] & 0xFF, b = rgba[kaynak + 2] & 0xFF;
                kaynak += 4;
                y.put(yb + x, (byte) (((66 * r + 129 * gr + 25 * b + 128) >> 8) + 16));
                if (renk && (x & 1) == 0) {
                    u.put(ub + (x / 2) * up, (byte) (((-38 * r - 74 * gr + 112 * b + 128) >> 8) + 128));
                    v.put(vb + (x / 2) * vp, (byte) (((112 * r - 94 * gr - 18 * b + 128) >> 8) + 128));
                }
            }
        }
        goruntu.queueInputBuffer(i, 0, genislik * yukseklik * 3 / 2, kare * 1000000L / fps, 0);
        kare++;
        bosalt(goruntu, false);
    }

    /** 16 bit mono örnekler. */
    public void sesEkle(short[] ornek) {
        if (ses == null) return;
        int at = 0;
        while (at < ornek.length) {
            int i = ses.dequeueInputBuffer(BEKLE_US);
            if (i < 0) { bosalt(ses, false); continue; }
            ByteBuffer b = ses.getInputBuffer(i);
            b.clear();
            b.order(ByteOrder.LITTLE_ENDIAN);
            int n = Math.min(ornek.length - at, b.capacity() / 2);
            for (int k = 0; k < n; k++) b.putShort(ornek[at + k]);
            ses.queueInputBuffer(i, 0, n * 2, sesOrnek * 1000000L / sesHizi, 0);
            sesOrnek += n;
            at += n;
            bosalt(ses, false);
        }
    }

    private void bosalt(MediaCodec c, boolean sonuna) {
        while (true) {
            int o = c.dequeueOutputBuffer(bilgi, sonuna ? BEKLE_US : 0);
            if (o == MediaCodec.INFO_TRY_AGAIN_LATER) { if (!sonuna) return; else continue; }
            if (o == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                int iz = kap.addTrack(c.getOutputFormat());
                if (c == goruntu) goruntuIz = iz; else sesIz = iz;
                if (goruntuIz >= 0 && (ses == null || sesIz >= 0)) {
                    kap.start();
                    basladi = true;
                    for (Object[] b : bekleyen) kap.writeSampleData((Integer) b[0], (ByteBuffer) b[1], (MediaCodec.BufferInfo) b[2]);
                    bekleyen.clear();
                }
                continue;
            }
            if (o < 0) continue;
            ByteBuffer veri = c.getOutputBuffer(o);
            boolean ayar = (bilgi.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;
            if (!ayar && bilgi.size > 0 && veri != null) {
                int iz = c == goruntu ? goruntuIz : sesIz;
                if (basladi) kap.writeSampleData(iz, veri, bilgi);
                else {
                    ByteBuffer kopya = ByteBuffer.allocate(bilgi.size);
                    veri.position(bilgi.offset);
                    veri.limit(bilgi.offset + bilgi.size);
                    kopya.put(veri);
                    kopya.flip();
                    MediaCodec.BufferInfo bi = new MediaCodec.BufferInfo();
                    bi.set(0, bilgi.size, bilgi.presentationTimeUs, bilgi.flags);
                    bekleyen.add(new Object[] { iz, kopya, bi });
                }
            }
            boolean son = (bilgi.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            c.releaseOutputBuffer(o, false);
            if (son) return;
        }
    }

    private void bitir(MediaCodec c, long zaman) {
        int i;
        do { i = c.dequeueInputBuffer(BEKLE_US); if (i < 0) bosalt(c, false); } while (i < 0);
        c.queueInputBuffer(i, 0, 0, zaman, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
        bosalt(c, true);
        c.stop();
        c.release();
    }

    /** Akışları bitirir, dosyayı kapatır. */
    public void kapat() {
        bitir(goruntu, kare * 1000000L / fps);
        if (ses != null) bitir(ses, sesOrnek * 1000000L / sesHizi);
        if (basladi) kap.stop();
        kap.release();
    }
}
