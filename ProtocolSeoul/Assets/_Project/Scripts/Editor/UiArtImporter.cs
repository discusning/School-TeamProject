using UnityEditor;

namespace ProtocolSeoul.EditorTools
{
    // 받은 UI 그림 (Assets/_Project/Art/UI)·방문자 초상 (Assets/_Project/Art/Portraits) 가져오기 설정: 스프라이트 · 압축 없음.
    // 그림은 원본이 크고(500~2100px) 화면에서 줄여 쓰므로 밉맵 + 선형 필터로 줄였을 때 선이 끊기지 않게 함.
    // 초상(1086×1448, 30장)은 화면에서 세로 500px 안팎이라 최대 1024로 줄여 가져옴 (메모리 절약).
    public class UiArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            bool ui = path.Contains("/_Project/Art/UI/"), portrait = path.Contains("/_Project/Art/Portraits/");
            if (!ui && !portrait) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = portrait ? 1024 : 4096;
            ti.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
