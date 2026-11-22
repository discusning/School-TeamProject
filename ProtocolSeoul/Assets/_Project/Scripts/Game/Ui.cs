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

        // ───── 받은 UI 그림 (Art/UI/Resources/UI, C037) ─────
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _art = new();

        // 그림 불러오기. border > 0 이면 테두리를 지키며 늘어나는(9칸) 그림. 파일이 없으면 null → 부르는 쪽이 색 상자로 대신함.
        public static Sprite Art(string name, float border = 0f)
        {
            string key = name + "|" + border;
            if (_art.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("UI/" + name);
            if (tex != null)
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            _art[key] = s;
            return s;
        }

        // 그림 한 장 (색 곱하기 없음)
        public static Image Picture(Transform parent, string name, float x0, float y0, float x1, float y1, Sprite sprite)
        {
            var img = Box(parent, name, x0, y0, x1, y1, Color.white);
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        // 부모 칸 안에 그림 비율(가로/세로)을 지키는 가운데 칸. 그림 위 글자 칸 위치를 그림 기준 비율로 줄 수 있음.
        public static RectTransform Fit(Transform parent, string name, float x0, float y0, float x1, float y1, float aspect)
        {
            var box = Rect(parent, name, x0, y0, x1, y1);
            var inner = Rect(box, "Fit", 0, 0, 1, 1);
            var f = inner.gameObject.AddComponent<AspectRatioFitter>();
            f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            f.aspectRatio = aspect;
            return inner;
        }

        // 테두리 그림(9칸)을 칸 높이에 맞춰 테두리 두께도 같이 줄임
        public static void Sliced(Image img, Sprite sprite)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            if (img.GetComponent<SliceFit>() == null) img.gameObject.AddComponent<SliceFit>();
        }

        // 그림 버튼: 기본·마우스 올림·클릭·비활성 4장을 바꿔 끼움. 글자는 게임 글꼴로 그림 위에 씀.
        public static Button SpriteButton(Transform parent, string name, float x0, float y0, float x1, float y1, string text, Sprite[] states, Color fg, int size, Action onClick)
        {
            var b = Button(parent, name, x0, y0, x1, y1, text, Color.white, fg, size, onClick);
            SetSprites(b, states);
            return b;
        }

        // states: 0 기본 · 1 마우스 올림 · 2 클릭 · 3 비활성 (없으면 기본)
        public static void SetSprites(Button b, Sprite[] states)
        {
            if (states == null || states[0] == null) return;
            b.image.sprite = states[0];
            b.image.color = Color.white;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState
            {
                highlightedSprite = states[1],
                pressedSprite = states[2],
                selectedSprite = states[0],
                disabledSprite = states.Length > 3 ? states[3] : states[0],
            };
        }

        // 칸에 안 들어가면 글자 크기를 줄임 (fontSize가 최대)
        public static Text Shrink(Text t, int min)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = t.fontSize;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Sprite[] ArtSet(params string[] names)
        {
            var a = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++) a[i] = Art(names[i]);
            return a;
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

    // 9칸 그림의 테두리 두께를 칸 높이 비율대로 맞춤 (원본 높이 ÷ 칸 높이)
    [RequireComponent(typeof(Image))]
    public class SliceFit : MonoBehaviour
    {
        Image _img;
        void OnEnable() => Apply();
        void OnRectTransformDimensionsChange() => Apply();
        void Apply()
        {
            if (_img == null) _img = GetComponent<Image>();
            if (_img == null || _img.sprite == null) return;
            float h = ((RectTransform)transform).rect.height;
            if (h <= 1f) return;
            float m = _img.sprite.rect.height / h;
            if (!Mathf.Approximately(_img.pixelsPerUnitMultiplier, m)) _img.pixelsPerUnitMultiplier = m;
        }
    }
}
