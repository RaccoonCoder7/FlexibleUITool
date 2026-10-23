using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FlexibleUITool.EditorTools
{
    // 작업 프리팹을 열면 환경 캔버스에 짧은 변 기준 스케일러를 붙이고 Game 뷰를 작업 해상도로 바꾼다 (닫으면 원래 해상도로 복원)
    [InitializeOnLoad]
    public static class FlexibleWorkspaceStage
    {
        private const string AutoGameViewKey = "FlexibleUITool.AutoGameView";
        private const string PreviousGameViewKey = "FlexibleUITool.PreviousGameViewIndex";

        public static bool AutoGameView
        {
            get => EditorPrefs.GetBool(AutoGameViewKey, true);
            set => EditorPrefs.SetBool(AutoGameViewKey, value);
        }

        static FlexibleWorkspaceStage()
        {
            PrefabStage.prefabStageOpened += EnsureEnvironmentScaler;
            PrefabStage.prefabStageOpened += OnOpened;
            PrefabStage.prefabStageClosing += OnClosing;
            // 도메인 리로드 후 스테이지가 복원될 때는 열림 이벤트가 오지 않으므로 Scene 뷰 갱신 때도 확인한다
            SceneView.duringSceneGui += _ =>
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null)
                    EnsureEnvironmentScaler(stage);
            };
        }

        public static bool TrySelectWorkResolution(FlexibleWorkspace workspace)
        {
            FlexibleProfile profile = FlexibleSettingsUtility.GetOrCreateDefault().Find(workspace.profileId);
            if (profile == null)
                return false;

            int width = Mathf.RoundToInt(profile.workResolution.x);
            int height = Mathf.RoundToInt(profile.workResolution.y);
            return FlexibleGameViewUtility.TrySelectResolution(width, height, $"Flexible {profile.displayName} {width}x{height}");
        }

        private static void OnOpened(PrefabStage stage)
        {
            var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            if (workspace == null || !AutoGameView)
                return;

            // 작업 프리팹끼리 옮겨 다녀도 처음 해상도를 기억한다
            if (SessionState.GetInt(PreviousGameViewKey, -1) < 0 && FlexibleGameViewUtility.TryGetSelectedIndex(out int previous))
                SessionState.SetInt(PreviousGameViewKey, previous);
            TrySelectWorkResolution(workspace);
        }

        private static void OnClosing(PrefabStage stage)
        {
            if (stage.prefabContentsRoot == null || stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>() == null)
                return;

            int previous = SessionState.GetInt(PreviousGameViewKey, -1);
            SessionState.EraseInt(PreviousGameViewKey);
            if (previous >= 0)
                FlexibleGameViewUtility.TrySelectIndex(previous);
        }

        public static void EnsureEnvironmentScaler(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            if (root == null || root.GetComponent<FlexibleWorkspace>() == null || root.GetComponent<Canvas>() != null)
                return;

            Transform environment = root.transform.parent;
            if (environment == null || environment.GetComponent<Canvas>() == null || environment.GetComponent<CanvasScaler>() != null)
                return;

            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 reference = baseProfile != null ? baseProfile.workResolution : new Vector2(1080, 1920);

            var scaler = environment.gameObject.AddComponent<FlexibleCanvasScaler>();
            scaler.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = reference;
        }
    }
}
