using UnityEngine;
using UnityEngine.UI;

namespace ProtocolSeoul.Game
{
    // 임시 얼굴 그림 (도형). 외부인은 눈 위치·목 길이가 다름 — 보고서 9-6 "외형 변형은 색·도형을 확실히 다르게".
    // 디자인 시안이 오면 이 클래스만 그림(스프라이트) 조합으로 바꾸면 된다.
    public static class FaceView
    {
        static readonly Color[] Skins = { new(0.96f, 0.82f, 0.68f), new(0.87f, 0.69f, 0.52f), new(0.72f, 0.53f, 0.38f), new(0.55f, 0.39f, 0.28f) };
        static readonly Color[] Hairs = { new(0.1f, 0.08f, 0.07f), new(0.38f, 0.24f, 0.12f), new(0.62f, 0.62f, 0.6f), new(0.55f, 0.15f, 0.12f) };
        static readonly Color Coat = new(0.28f, 0.33f, 0.3f);
        static readonly Color Eye = new(0.08f, 0.08f, 0.1f);

        public static void Draw(Transform area, Face f, Color bg)
        {
            Ui.Clear(area);
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
    }
}
