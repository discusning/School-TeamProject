using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProtocolSeoul.Game
{
    // 방문자 얼굴. 초상 그림(Art/Portraits/Resources/Portraits/pNN.png, C038)이 있으면 그림, 없으면 도형 얼굴.
    // 외부인: 같은 초상을 좌우 반전(EyeOffset ≠ 0)하거나 위로 늘림(Neck = 1, 목이 길어 보임) — 보고서 9-6.
    public static class FaceView
    {
        static readonly Color[] Skins = { new(0.96f, 0.82f, 0.68f), new(0.87f, 0.69f, 0.52f), new(0.72f, 0.53f, 0.38f), new(0.55f, 0.39f, 0.28f) };
        static readonly Color[] Hairs = { new(0.1f, 0.08f, 0.07f), new(0.38f, 0.24f, 0.12f), new(0.62f, 0.62f, 0.6f), new(0.55f, 0.15f, 0.12f) };
        static readonly Color Coat = new(0.28f, 0.33f, 0.3f);
        static readonly Color Eye = new(0.08f, 0.08f, 0.1f);
        public const float PortraitAspect = 0.75f; // 초상 그림 3:4
        const float NeckStretch = 1.12f;

        static readonly Dictionary<int, Sprite> _portraits = new();

        public static Sprite Portrait(int id)
        {
            if (id <= 0) return null;
            if (_portraits.TryGetValue(id, out var s)) return s;
            var tex = Resources.Load<Texture2D>($"Portraits/p{id:00}");
            if (tex != null) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _portraits[id] = s;
            return s;
        }

        public static void Draw(Transform area, Face f, Color bg)
        {
            Ui.Clear(area);
            var portrait = Portrait(f.Portrait);
            if (portrait != null) { DrawPortrait(area, f, portrait); return; }

            Ui.Box(area, "Bg", 0, 0, 1, 1, bg);
            Color skin = Skins[f.Skin % Skins.Length], hair = Hairs[f.Hair % Hairs.Length];
            float lift = f.Neck == 1 ? 0.1f : 0f;          // 목이 길면 머리가 올라감
            float hy0 = 0.28f + lift, hy1 = 0.84f + lift;   // 머리 위·아래

            Ui.Box(area, "Body", 0.12f, 0f, 0.88f, 0.17f, Coat);
            Ui.Box(area, "Neck", 0.42f, 0.15f, 0.58f, hy0 + 0.05f, skin * 0.92f);
            Ui.Circle(area, "Head", 0.25f, hy0, 0.75f, hy1, skin);

            switch (f.Hair % 4)
            {
                case 0: Ui.Box(area, "Hair", 0.27f, hy1 - 0.13f, 0.73f, hy1 + 0.01f, hair); break;              // 짧은 머리
                case 1: Ui.Circle(area, "Hair", 0.28f, hy1 - 0.15f, 0.72f, hy1 + 0.09f, hair); break;           // 둥근 머리
                case 2: Ui.Box(area, "Hair", 0.22f, hy0 + 0.2f, 0.33f, hy1, hair); Ui.Box(area, "Hair2", 0.67f, hy0 + 0.2f, 0.78f, hy1, hair); Ui.Box(area, "Hair3", 0.27f, hy1 - 0.08f, 0.73f, hy1 + 0.01f, hair); break; // 긴 머리
                default: Ui.Box(area, "Hat", 0.24f, hy1 - 0.1f, 0.76f, hy1 + 0.06f, new Color(0.2f, 0.22f, 0.26f)); break; // 모자
            }

            float h = hy1 - hy0;
            float ey = hy0 + h * 0.55f + f.EyeOffset * 0.07f; // 외부인: 눈이 위·아래로 어긋남
            Ui.Circle(area, "EyeL", 0.36f, ey - 0.035f, 0.43f, ey + 0.035f, Eye);
            Ui.Circle(area, "EyeR", 0.57f, ey - 0.035f, 0.64f, ey + 0.035f, Eye);
            Ui.Box(area, "Mouth", 0.44f, hy0 + h * 0.2f, 0.56f, hy0 + h * 0.24f, new Color(0.45f, 0.2f, 0.18f));
            foreach (var img in area.GetComponentsInChildren<Image>()) img.raycastTarget = false;
        }

        // 초상 그림을 칸에 꽉 채움 (비율 유지, 넘치는 쪽은 잘림). 그림 아래(어깨)를 기준으로 맞춰 위쪽 빈 벽이 먼저 잘림.
        static void DrawPortrait(Transform area, Face f, Sprite s)
        {
            if (area.GetComponent<RectMask2D>() == null) area.gameObject.AddComponent<RectMask2D>();
            var img = Ui.Picture(area, "Portrait", 0, 0, 1, 1, s);
            var rt = img.rectTransform;
            rt.pivot = new Vector2(0.5f, 0f);
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = s.rect.width / s.rect.height;
            rt.localScale = new Vector3(f.EyeOffset != 0 ? -1f : 1f, f.Neck == 1 ? NeckStretch : 1f, 1f);
        }
    }
}
