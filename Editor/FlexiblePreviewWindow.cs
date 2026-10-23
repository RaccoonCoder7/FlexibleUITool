using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 창 크기(또는 기기 프리셋) 비율 그대로 UI 프리팹을 실시간 렌더링하는 미리보기 창. 프리팹 모드 편집 내용도 바로 반영된다
    public class FlexiblePreviewWindow : EditorWindow
    {
        private const float ToolbarHeight = 22f;
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

        private FlexiblePreviewRenderer renderer;
        private int presetIndex = -1;
        private bool rotated;
        private bool simulateSafeArea = true;
        private bool showIssues = true;
        private bool locked;
        private string lockedPath;
        private int currentProfile = -1;
        private List<string> issues = new List<string>();
        private string targetLabel;

        [MenuItem("Tools/FlexibleUITool/Preview Window")]
        public static void Open()
        {
            var window = GetWindow<FlexiblePreviewWindow>("Flexible Preview");
            window.minSize = new Vector2(160f, 160f);
        }

        private void OnEnable()
        {
            renderer = new FlexiblePreviewRenderer();
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.undoRedoPerformed += MarkDirty;
            Selection.selectionChanged += MarkDirty;
            EditorApplication.projectChanged += MarkDirty;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            Undo.undoRedoPerformed -= MarkDirty;
            Selection.selectionChanged -= MarkDirty;
            EditorApplication.projectChanged -= MarkDirty;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            renderer?.Dispose();
            renderer = null;
        }

        private void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            MarkDirty();
        }

        private void OnStageChanged(PrefabStage stage)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (renderer != null)
                renderer.Dirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            DrawToolbar(settings);

            var body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight - InfoHeight);
            FlexiblePreviewPreset preset = GetPreset(settings);
            Rect screen = preset != null ? FitAspect(body, preset.Aspect) : body;
            if (screen.width < 8f || screen.height < 8f)
                return;

            float aspect = screen.width / screen.height;
            currentProfile = settings.SelectProfile(aspect, currentProfile);
            FlexibleProfile profile = settings.Profiles[currentProfile];
            Rect safeArea = preset != null && simulateSafeArea ? preset.NormalizedSafeArea : new Rect(0f, 0f, 1f, 1f);

            if (!ResolveTarget(profile, out GameObject source, out string applyProfileId))
            {
                EditorGUI.HelpBox(new Rect(body.x + 8f, body.y + 8f, body.width - 16f, 40f),
                    T("Select a UI prefab in the Project window or open it in Prefab Mode."), MessageType.Info);
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

            DrawInfo(new Rect(0f, position.height - InfoHeight, position.width, InfoHeight), settings, aspect, profile, applyProfileId == null);
        }

        private void DrawToolbar(FlexibleProfileSettings settings)
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var presetNames = new List<string> { T("Free (window size)") };
                presetNames.AddRange(settings.PreviewPresets.Select(p => T(p.name)));
                EditorGUI.BeginChangeCheck();
                presetIndex = EditorGUILayout.Popup(presetIndex + 1, presetNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(150f)) - 1;
                using (new EditorGUI.DisabledScope(presetIndex < 0))
                    rotated = GUILayout.Toggle(rotated, T("Rotate"), EditorStyles.toolbarButton, GUILayout.Width(52f));
                using (new EditorGUI.DisabledScope(presetIndex < 0))
                    simulateSafeArea = GUILayout.Toggle(simulateSafeArea, "SafeArea", EditorStyles.toolbarButton, GUILayout.Width(64f));
                showIssues = GUILayout.Toggle(showIssues, T("Show issues"), EditorStyles.toolbarButton, GUILayout.Width(80f));
                if (EditorGUI.EndChangeCheck())
                    Repaint();

                GUILayout.FlexibleSpace();
                GUILayout.Label(targetLabel ?? "", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                locked = GUILayout.Toggle(locked, T("Lock"), EditorStyles.toolbarButton, GUILayout.Width(44f));
                if (EditorGUI.EndChangeCheck())
                {
                    lockedPath = locked ? GetCurrentSourcePath() : null;
                    MarkDirty();
                }
                if (GUILayout.Button(T("Refresh"), EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    MarkDirty();
            }
        }

        // 작업 프리팹 편집 중이면 그 프로필 구간에서는 작업 내용을, 다른 구간에서는 원본 데이터를 보여 준다
        private bool ResolveTarget(FlexibleProfile profile, out GameObject source, out string applyProfileId)
        {
            source = null;
            applyProfileId = profile.id;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            string path = locked ? lockedPath : GetCurrentSourcePath();
            if (string.IsNullOrEmpty(path))
                return false;

            if (stage != null)
            {
                var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
                if (workspace != null && AssetDatabase.GUIDToAssetPath(workspace.sourceGuid) == path)
                {
                    if (workspace.profileId == profile.id)
                    {
                        source = stage.prefabContentsRoot;
                        applyProfileId = null;
                        targetLabel = F("{0} (in progress)", System.IO.Path.GetFileNameWithoutExtension(path));
                        return true;
                    }
                }
                else if (stage.assetPath == path)
                {
                    source = stage.prefabContentsRoot;
                    targetLabel = F("{0} (editing)", System.IO.Path.GetFileNameWithoutExtension(path));
                    return true;
                }
            }

            source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            targetLabel = System.IO.Path.GetFileNameWithoutExtension(path);
            return source != null && source.transform is RectTransform;
        }

        private static string GetCurrentSourcePath()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
                return workspace != null ? AssetDatabase.GUIDToAssetPath(workspace.sourceGuid) : stage.assetPath;
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

        private void DrawInfo(Rect rect, FlexibleProfileSettings settings, float aspect, FlexibleProfile profile, bool workspaceContent)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
            Vector2 logical = FlexiblePreviewRenderer.GetLogicalSize(aspect, settings.ReferenceShortSide);
            string content = workspaceContent ? T("  · workspace content") : "";
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 18f),
                F("Profile: {0}{1}   ratio {2:0.00}   logical {3:0}x{4:0}   {5} issues", Label(profile), content, aspect, logical.x, logical.y, issues.Count), EditorStyles.boldLabel);

            // 비율 축 위에 프로필 구간을 색으로 나누고 현재 위치를 표시한다
            var bar = new Rect(rect.x + 6f, rect.y + 24f, rect.width - 12f, 14f);
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                float start = Mathf.Max(settings.Profiles[i].minAspect, MinAspect);
                float end = Mathf.Min(settings.MaxAspectOf(i), MaxAspect);
                if (end <= start)
                    continue;

                var segment = new Rect(AspectToX(bar, start), bar.y, AspectToX(bar, end) - AspectToX(bar, start), bar.height);
                EditorGUI.DrawRect(segment, ProfileColors[i % ProfileColors.Length] * (i == currentProfile ? 1f : 0.55f));
                GUI.Label(segment, T(settings.Profiles[i].displayName), EditorStyles.centeredGreyMiniLabel);
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
