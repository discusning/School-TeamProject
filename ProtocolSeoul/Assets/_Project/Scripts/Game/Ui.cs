using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProtocolSeoul.Game
{
    // 임시 UI 도구 (보고서 9장: 회색 상자·도형 임시 그림). 디자인 시안이 오면 같은 자리에 그림만 바꿔 끼운다.
    // 위치는 부모 기준 비율(0~1)로 준다.
    public static class Ui
    {
        public static Font Font;
        static Sprite _circle;

        // 색 (2050 통제 구역 느낌 — 임시)
        public static readonly Color Bg = new(0.11f, 0.12f, 0.13f);
        public static readonly Color Booth = new(0.17f, 0.19f, 0.22f);
        public static readonly Color Desk = new(0.24f, 0.2f, 0.16f);
        public static readonly Color Paper = new(0.91f, 0.88f, 0.79f);
        public static readonly Color Ink = new(0.15f, 0.13f, 0.11f);
        public static readonly Color Terminal = new(0.03f, 0.07f, 0.05f);
        public static readonly Color TermText = new(0.45f, 0.9f, 0.55f);
        public static readonly Color PassGreen = new(0.18f, 0.49f, 0.2f);
        public static readonly Color DenyRed = new(0.72f, 0.11f, 0.11f);
        public static readonly Color Disabled = new(0.3f, 0.3f, 0.3f);
        public static readonly Color Pick = new(1f, 0.85f, 0.2f);
        public static readonly Color Light = new(0.92f, 0.92f, 0.9f);
        public static readonly Color Dim = new(0f, 0f, 0f, 0.82f);

        public static Color Solid(Color c) { c.a = 1f; return c; }

        public static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1, float pad = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        public static Image Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color c, float pad = 0f)
        {
            var img = Rect(parent, name, x0, y0, x1, y1, pad).gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static Image Circle(Transform parent, string name, float x0, float y0, float x1, float y1, Color c)
        {
            var img = Box(parent, name, x0, y0, x1, y1, c);
            img.sprite = CircleSprite();
            img.preserveAspect = false;
            return img;
        }

        public static Text Label(Transform parent, string name, float x0, float y0, float x1, float y1, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, float pad = 0f)
        {
            var t = Rect(parent, name, x0, y0, x1, y1, pad).gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string name, float x0, float y0, float x1, float y1, string text, Color bg, Color fg, int size, Action onClick, float pad = 0f)
        {
            var img = Box(parent, name, x0, y0, x1, y1, bg, pad);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            Label(img.transform, "Text", 0, 0, 1, 1, text, size, fg, TextAnchor.MiddleCenter, 4);
            return b;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float r = n / 2f - 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                    byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }
    }
}
