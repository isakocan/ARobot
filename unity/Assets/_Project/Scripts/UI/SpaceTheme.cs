using UnityEngine;

namespace ARaki.UI
{
    /// <summary>
    /// Arayüzün tek renk kaynağı. Tema değiştirmek (uzay → savaş sonrası → maket) için
    /// arayüz kodunu değil bu dosyayı (ileride bir ScriptableObject'i) değiştirmek yeter.
    /// </summary>
    public static class SpaceTheme
    {
        public static readonly Color Primary = new Color32(0x5F, 0xE3, 0xFF, 255);   // buz mavisi
        public static readonly Color Text = new Color32(0xE8, 0xF6, 0xFF, 255);
        public static readonly Color TextDim = new Color32(0x7F, 0xA3, 0xB5, 255);
        public static readonly Color Warning = new Color32(0xFF, 0xB4, 0x54, 255);
        public static readonly Color Danger = new Color32(0xFF, 0x5C, 0x5C, 255);
        public static readonly Color Success = new Color32(0x73, 0xFF, 0x9A, 255);
        public static readonly Color Panel = new Color32(0x05, 0x10, 0x1A, 168);
        public static readonly Color Track = new Color32(0xFF, 0xFF, 0xFF, 28);

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
    /// <summary>Yarış modunun renkleri: sinyal sarısı, kırmızı ve beyaz (zamanlama ekranı havası).</summary>
    public static class RaceTheme
    {
        public static readonly Color Primary = new Color32(0xFF, 0xC2, 0x1A, 255);   // sinyal sarısı
        public static readonly Color Accent = new Color32(0xFF, 0x3B, 0x30, 255);    // yarış kırmızısı
        public static readonly Color Text = new Color32(0xF6, 0xF6, 0xF2, 255);
        public static readonly Color TextDim = new Color32(0xA9, 0xA9, 0xA2, 255);
        public static readonly Color Good = new Color32(0x4B, 0xE3, 0x8A, 255);
        public static readonly Color Panel = new Color32(0x0C, 0x0C, 0x0F, 190);
        public static readonly Color Track = new Color32(0xFF, 0xFF, 0xFF, 30);
    }
}
