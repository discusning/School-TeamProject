using System.IO;
using System.Linq;
using ProtocolSeoul.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProtocolSeoul.EditorTools
{
    // 프로토타입 준비 (한 번만): 하루 설정 에셋 3개 + 검문소 장면 + 빌드 장면 목록.
    // 메뉴: Protocol Seoul > Setup Project / Build Windows
    // 배치: Unity.exe -batchmode -quit -projectPath ... -executeMethod ProtocolSeoul.EditorTools.ProjectSetup.SetupAndBuild
    public static class ProjectSetup
    {
        const string DaysDir = "Assets/_Project/Resources/Days";
        const string ScenePath = "Assets/_Project/Scenes/Checkpoint.unity";
        const string BuildPath = "Builds/Windows/ProtocolSeoul.exe";

        [MenuItem("Protocol Seoul/Setup Project")]
        public static void Setup()
        {
            // 1) 하루 설정 — 이미 있으면 건드리지 않음 (기획이 바꾼 값 보존)
            Directory.CreateDirectory(DaysDir);
            foreach (var day in DayConfig.Defaults())
            {
                string path = $"{DaysDir}/Day{day.Day}.asset";
                if (AssetDatabase.LoadAssetAtPath<DayConfig>(path) == null) AssetDatabase.CreateAsset(day, path);
            }
            AssetDatabase.SaveAssets();

            // 2) 검문소 장면
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ui.Bg;
            cam.transform.position = new Vector3(0, 0, -10);
            new GameObject("CheckpointGame", typeof(CheckpointGame));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 3) 빌드 장면 목록: 검문소 하나
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[ProjectSetup] 하루 설정 3개 · Checkpoint 장면 · 빌드 목록 준비 완료");
        }

        [MenuItem("Protocol Seoul/Build Windows")]
        public static void BuildWindows()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[ProjectSetup] 빌드 {report.summary.result} → {BuildPath}");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        public static void SetupAndBuild()
        {
            Setup();
            BuildWindows();
        }
    }
}
