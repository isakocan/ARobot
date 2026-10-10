using UnityEngine;
using UnityEngine.UI;

namespace ARaki.UI
{
    /// <summary>Kodla arayüz kurmak için küçük yardımcılar (panel, metin, çerçeve). Renkler SpaceTheme'den gelir.</summary>
    public class UiKit
    {
        public Font Title, Body, Mono;
        public Sprite Bracket, Ring;

        public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        public static readonly Vector2 TopRight = new Vector2(1f, 1f);
        public static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        public static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        public static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        public static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        public static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        public RectTransform Rt(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rt(name, parent, Center, Center, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public Image Img(RectTransform rt, Color color, Sprite sprite = null, Image.Type type = Image.Type.Simple)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            if (type == Image.Type.Sliced) img.fillCenter = false;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public Text Label(RectTransform parent, string text, Font font, int size, Color color, TextAnchor align,
                          Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            var rt = Rt("Text", parent, anchor, pivot, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static void Shadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.85f);
            s.effectDistance = new Vector2(1.2f, -1.2f);
        }

        /// <summary>Yarı saydam panel + köşe çerçevesi.</summary>
        public RectTransform Card(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color? border = null)
        {
            var rt = Rt(name, parent, anchor, pivot, pos, size);
            Img(rt, SpaceTheme.Panel);
            var frame = Stretch("Frame", rt);
            Img(frame, border ?? SpaceTheme.WithAlpha(SpaceTheme.Primary, 0.9f), Bracket, Image.Type.Sliced);
            return rt;
        }
    }
}
