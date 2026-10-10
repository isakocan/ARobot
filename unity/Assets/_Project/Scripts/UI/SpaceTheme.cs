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
}
