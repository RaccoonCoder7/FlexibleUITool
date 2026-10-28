using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 미리보기 탭: 창 크기(또는 기기 프리셋) 비율 그대로 UI 프리팹을 실시간 렌더링한다. 프리팹 모드 편집 내용도 바로 반영된다
    [System.Serializable]
    public class FlexiblePreviewPanel
    {
        private const float InfoHeight = 46f;
        private const float MinAspect = 0.35f;
        private const float MaxAspect = 2.6f;

        private static readonly Color[] ProfileColors =
        {
            new Color(0.30f, 0.62f, 1.00f),
            new Color(0.95f, 0.66f, 0.20f),
            new Color(0.40f, 0.80f, 0.45f),
            new Color(0.85f, 0.45f, 0.85f),
            new Color(0.90f, 0.35f, 0.35f),
        };

        [SerializeField] private int presetIndex = -1;
        [SerializeField] private bool rotated;
        [SerializeField] private bool simulateSafeArea = true;
        [SerializeField] private bool showIssues = true;
        [SerializeField] private bool locked;
        [SerializeField] private string lockedPath;
        private FlexiblePreviewRenderer renderer;
        private int currentProfile = -1;
        private List<string> issues = new List<string>();
        private string targetLabel;

        // 지금 미리보기가 고른 화면 (튜토리얼 확인용)
        public string CurrentProfileId { get; private set; }

        // 미리보기 탭을 연 뒤 그린 화면들 (튜토리얼 확인용, 창 크기를 끄는 중에 그린 것도 포함)
        [System.NonSerialized] private HashSet<string> seenProfiles;
        public HashSet<string> SeenProfiles => seenProfiles ??= new HashSet<string>();

        public void OnEnable()
        {
            renderer = new FlexiblePreviewRenderer();
        }

        public void OnDisable()
        {
            renderer?.Dispose();
            renderer = null;
        }

        public void MarkDirty()
        {
            if (renderer != null)
                renderer.Dirty = true;
        }

        public void Draw()
        {
            if (renderer == null)
                OnEnable();

            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            DrawToolbar(settings);
            GUILayout.Label(T("Drag the window edge to change its shape. The layout switches automatically at each screen ratio."), EditorStyles.wordWrappedMiniLabel);

            Rect body = GUILayoutUtility.GetRect(10f, 100000f, 10f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout)
                return;

            var info = new Rect(body.x, body.yMax - InfoHeight, body.width, InfoHeight);
            body.height -= InfoHeight;
            FlexiblePreviewPreset preset = GetPreset(settings);
            Rect screen = preset != null ? FitAspect(body, preset.Aspect) : body;
            if (screen.width < 8f || screen.height < 8f)
                return;

            float aspect = screen.width / screen.height;
            currentProfile = settings.SelectProfile(aspect, currentProfile);
            FlexibleProfile profile = settings.Profiles[currentProfile];
            CurrentProfileId = profile.id;
            SeenProfiles.Add(profile.id);
            Rect safeArea = preset != null && simulateSafeArea ? preset.NormalizedSafeArea : new Rect(0f, 0f, 1f, 1f);

            if (!ResolveTarget(profile, out GameObject source, out string applyProfileId))
            {
                EditorGUI.HelpBox(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 40f),
                    T("Open a UI prefab (double-click it in the Project window) or select one."), MessageType.Info);
                return;
            }

            if (Event.current.type == EventType.Repaint)
            {
                Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(aspect, settings.ReferenceShortSide);
                float scale = EditorGUIUtility.pixelsPerPoint;
                renderer.Render(source, applyProfileId, logical, new Vector2Int(Mathf.RoundToInt(screen.width * scale), Mathf.RoundToInt(screen.height * scale)), safeArea);
                issues = renderer.CollectIssues(safeArea);
            }

            if (renderer.Texture != null)
                GUI.DrawTexture(screen, renderer.Texture, ScaleMode.StretchToFill, false);
            if (preset != null && simulateSafeArea)
                DrawUnsafeArea(screen, safeArea);
            if (showIssues && issues.Count > 0)
                DrawIssues(screen);

            DrawInfo(info, settings, aspect, profile);
        }

        private void DrawToolbar(FlexibleProfileSettings settings)
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var presetNames = new List<string> { T("Free (window size)") };
                presetNames.AddRange(settings.PreviewPresets.Select(p => T(p.name)));
                presetIndex = EditorGUILayout.Popup(presetIndex + 1, presetNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(150f)) - 1;
                using (new EditorGUI.DisabledScope(presetIndex < 0))
                    rotated = GUILayout.Toggle(rotated, T("Rotate"), EditorStyles.toolbarButton, GUILayout.Width(52f));
                using (new EditorGUI.DisabledScope(presetIndex < 0))
                    simulateSafeArea = GUILayout.Toggle(simulateSafeArea, "SafeArea", EditorStyles.toolbarButton, GUILayout.Width(64f));
                showIssues = GUILayout.Toggle(showIssues, T("Show issues"), EditorStyles.toolbarButton, GUILayout.Width(80f));

                GUILayout.FlexibleSpace();
                GUILayout.Label(targetLabel ?? "", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                locked = GUILayout.Toggle(locked, new GUIContent(T("Lock"), T("Keep showing this prefab even when the selection changes.")), EditorStyles.toolbarButton, GUILayout.Width(44f));
                if (EditorGUI.EndChangeCheck())
                {
                    lockedPath = locked ? GetCurrentSourcePath() : null;
                    MarkDirty();
                }
                if (GUILayout.Button(T("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    MarkDirty();
            }
        }

        // 프리팹 모드에서 보고 있는 화면은 편집 중인 내용 그대로, 다른 화면은 기준 화면 편집 중이면 내용에 그 화면 기록을 적용해서,
        // 다른 화면 편집 중이면 저장된 프리팹으로 그린다
        private bool ResolveTarget(FlexibleProfile profile, out GameObject source, out string applyProfileId)
        {
            source = null;
            applyProfileId = profile.id;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            string path = locked ? lockedPath : GetCurrentSourcePath();
            if (string.IsNullOrEmpty(path))
                return false;

            string name = Path.GetFileNameWithoutExtension(path);
            if (stage != null && stage.assetPath == path && !FlexibleScreenSession.IsLegacyWorkspace(stage))
            {
                if (!FlexibleScreenSession.IsReady(stage, out _))
                {
                    source = stage.prefabContentsRoot;
                    targetLabel = F("{0} (editing)", name);
                    return true;
                }

                if (FlexibleScreenSession.GetActiveProfileId(stage) == profile.id)
                {
                    source = stage.prefabContentsRoot;
                    applyProfileId = null;
                    targetLabel = F("{0} (editing)", name);
                    return true;
                }

                if (FlexibleScreenSession.IsBaseView(stage))
                {
                    source = stage.prefabContentsRoot;
                    targetLabel = F("{0} (editing)", name);
                    return true;
                }

                targetLabel = F("{0} (saved)", name);
                source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return source != null;
            }

            source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            targetLabel = name;
            return source != null && source.transform is RectTransform;
        }

        private static string GetCurrentSourcePath()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
                return workspace != null && !workspace.inPlace ? AssetDatabase.GUIDToAssetPath(workspace.sourceGuid) : stage.assetPath;
            }

            var selected = Selection.activeObject as GameObject;
            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : null;
            return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab") ? path : null;
        }

        private FlexiblePreviewPreset GetPreset(FlexibleProfileSettings settings)
        {
            if (presetIndex < 0 || presetIndex >= settings.PreviewPresets.Count)
                return null;

            FlexiblePreviewPreset preset = settings.PreviewPresets[presetIndex];
            return rotated ? preset.Rotated() : preset;
        }

        private static Rect FitAspect(Rect area, float aspect)
        {
            float width = area.width;
            float height = width / aspect;
            if (height > area.height)
            {
                height = area.height;
                width = height * aspect;
            }
            return new Rect(area.x + (area.width - width) * 0.5f, area.y + (area.height - height) * 0.5f, width, height);
        }

        private static void DrawUnsafeArea(Rect screen, Rect safe)
        {
            var shade = new Color(0f, 0f, 0f, 0.45f);
            float left = screen.width * safe.xMin;
            float right = screen.width * (1f - safe.xMax);
            float bottom = screen.height * safe.yMin;
            float top = screen.height * (1f - safe.yMax);
            EditorGUI.DrawRect(new Rect(screen.x, screen.y, screen.width, top), shade);
            EditorGUI.DrawRect(new Rect(screen.x, screen.yMax - bottom, screen.width, bottom), shade);
            EditorGUI.DrawRect(new Rect(screen.x, screen.y + top, left, screen.height - top - bottom), shade);
            EditorGUI.DrawRect(new Rect(screen.xMax - right, screen.y + top, right, screen.height - top - bottom), shade);
        }

        private void DrawIssues(Rect screen)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.55f, 0.45f) }, wordWrap = true };
            float lineHeight = 14f;
            int count = Mathf.Min(issues.Count, 6);
            var box = new Rect(screen.x + 4f, screen.yMax - (count + (issues.Count > 6 ? 1 : 0)) * lineHeight - 8f, Mathf.Min(screen.width - 8f, 320f), 0f);
            box.height = screen.yMax - 4f - box.y;
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.6f));
            for (int i = 0; i < count; i++)
                GUI.Label(new Rect(box.x + 4f, box.y + 2f + i * lineHeight, box.width - 8f, lineHeight), "⚠ " + issues[i], style);
            if (issues.Count > 6)
                GUI.Label(new Rect(box.x + 4f, box.y + 2f + count * lineHeight, box.width - 8f, lineHeight), F("{0} more", issues.Count - 6), style);
        }

        private void DrawInfo(Rect rect, FlexibleProfileSettings settings, float aspect, FlexibleProfile profile)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
            Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(aspect, settings.ReferenceShortSide);
            var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 18f),
                F("Now showing: {0}   ratio {1:0.00}   size {2:0}x{3:0}   {4} issues", FlexibleEditPanel.Name(profile), aspect, logical.x, logical.y, issues.Count), style);

            // 비율 축 위에 화면 구간을 색으로 나누고 현재 위치를 표시한다
            var bar = new Rect(rect.x + 6f, rect.y + 24f, rect.width - 12f, 14f);
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                float start = Mathf.Max(settings.Profiles[i].minAspect, MinAspect);
                float end = Mathf.Min(settings.MaxAspectOf(i), MaxAspect);
                if (end <= start)
                    continue;

                var segment = new Rect(AspectToX(bar, start), bar.y, AspectToX(bar, end) - AspectToX(bar, start), bar.height);
                EditorGUI.DrawRect(segment, ProfileColors[i % ProfileColors.Length] * (i == currentProfile ? 1f : 0.55f));
                GUI.Label(segment, FlexibleEditPanel.Name(settings.Profiles[i]), EditorStyles.centeredGreyMiniLabel);
            }

            float marker = AspectToX(bar, Mathf.Clamp(aspect, MinAspect, MaxAspect));
            EditorGUI.DrawRect(new Rect(marker - 1f, bar.y - 3f, 3f, bar.height + 6f), Color.white);
        }

        // 세로·가로가 대칭으로 보이도록 로그 스케일로 배치
        private static float AspectToX(Rect bar, float aspect)
        {
            float t = Mathf.InverseLerp(Mathf.Log(MinAspect), Mathf.Log(MaxAspect), Mathf.Log(aspect));
            return bar.x + bar.width * t;
        }
    }
}
