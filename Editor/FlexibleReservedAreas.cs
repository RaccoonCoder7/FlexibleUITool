using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 다른 프리팹이 차지하는 자리(메뉴 바 등): 등록·화면별 위치 계산·다른 프리팹 편집 중 Scene 뷰 표시
    [InitializeOnLoad]
    public static class FlexibleReservedAreas
    {
        private static readonly Color Fill = new Color(0.25f, 0.6f, 1f, 0.12f);
        private static readonly Color Line = new Color(0.25f, 0.6f, 1f, 0.9f);
        private static GUIStyle labelStyle;

        static FlexibleReservedAreas()
        {
            SceneView.duringSceneGui += DrawScene;
        }

        // 프리팹 파일이 바뀌면(Prefab Mode 저장, CLI apply-spec, git 되돌리기 등) 그 프리팹이 가진 자리를 다시 구한다
        private class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (string path in imported)
                {
                    if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                        OnPrefabChanged(AssetDatabase.AssetPathToGUID(path));
                }
            }
        }

        public static FlexibleProfileSettings SettingsOf(GameObject root)
        {
            var layout = root != null ? root.GetComponent<FlexibleLayout>() : null;
            return layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
        }

        public static FlexibleReservedArea Find(PrefabStage stage, RectTransform target)
        {
            FlexibleProfileSettings settings = SettingsOf(stage.prefabContentsRoot);
            string guid = AssetDatabase.AssetPathToGUID(stage.assetPath);
            string path = FlexiblePathUtility.GetPath(stage.prefabContentsRoot.transform, target);
            return settings.ReservedAreas.Find(a => a.prefabGuid == guid && a.path == path);
        }

        // 블록을 「다른 프리팹이 피할 자리」로 등록하거나 뺀다. 위치는 저장된 프리팹 기준으로 화면마다 구한다
        public static void Set(PrefabStage stage, RectTransform target, bool on)
        {
            FlexibleProfileSettings settings = SettingsOf(stage.prefabContentsRoot);
            FlexibleReservedArea existing = Find(stage, target);
            Undo.RecordObject(settings, "Flexible Reserved Area");
            if (!on)
            {
                settings.ReservedAreas.Remove(existing);
            }
            else if (existing == null)
            {
                var area = new FlexibleReservedArea
                {
                    name = target.name,
                    prefabGuid = AssetDatabase.AssetPathToGUID(stage.assetPath),
                    path = FlexiblePathUtility.GetPath(stage.prefabContentsRoot.transform, target),
                };
                settings.ReservedAreas.Add(area);
                Recompute(settings, area);
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            SceneView.RepaintAll();
        }

        // 화면 전환 저장처럼 연달아 와도 한 번만 다시 구한다
        private static void OnPrefabChanged(string guid)
        {
            // 가져오기 도중에는 에셋을 만들거나 읽지 않고, 끝난 뒤에 확인한다
            if (string.IsNullOrEmpty(guid))
                return;
            EditorApplication.delayCall -= RecomputePending;
            EditorApplication.delayCall += RecomputePending;
            pending.Add(guid);
        }

        private static readonly HashSet<string> pending = new HashSet<string>();

        private static void RecomputePending()
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            bool changed = false;
            foreach (FlexibleReservedArea area in settings.ReservedAreas)
            {
                if (!pending.Contains(area.prefabGuid))
                    continue;
                Recompute(settings, area);
                changed = true;
            }
            pending.Clear();
            if (changed)
                AssetDatabase.SaveAssetIfDirty(settings);
        }

        public static void Recompute(FlexibleProfileSettings settings, FlexibleReservedArea area)
        {
            area.rects.Clear();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(area.prefabGuid));
            if (asset != null && asset.transform is RectTransform)
            {
                using (var renderer = new FlexiblePreviewRenderer())
                {
                    foreach (FlexibleProfile profile in settings.Profiles)
                    {
                        Vector2 size = FlexiblePreviewRenderer.GetLogicalSize(profile.workResolution.x / Mathf.Max(1f, profile.workResolution.y), settings.ReferenceShortSide);
                        renderer.Prepare(asset, profile.id, size, new Rect(0f, 0f, 1f, 1f));
                        var root = (RectTransform)renderer.Instance.transform;
                        var target = FlexiblePathUtility.FindByPath(root, area.path) as RectTransform;
                        if (target == null || !target.gameObject.activeInHierarchy)
                            continue;
                        Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, target);
                        Rect screen = root.rect;
                        area.rects.Add(new FlexibleReservedRect
                        {
                            profileId = profile.id,
                            rect = new Rect((r.xMin - screen.xMin) / screen.width, (r.yMin - screen.yMin) / screen.height, r.width / screen.width, r.height / screen.height),
                        });
                        area.sortingOrder = EffectiveOrder(target, root);
                    }
                }
            }
            EditorUtility.SetDirty(settings);
        }

        // 이 프리팹 밖에서 온 자리들 (지금 화면에서 보이는 것만, 루트 좌표로)
        public static List<(FlexibleReservedArea area, Rect rect)> ForScreen(FlexibleProfileSettings settings, string prefabGuid, string profileId, Rect screen)
        {
            var result = new List<(FlexibleReservedArea, Rect)>();
            foreach (FlexibleReservedArea area in settings.ReservedAreas)
            {
                if (area.prefabGuid == prefabGuid || (area.hiddenFor != null && area.hiddenFor.Contains(prefabGuid)))
                    continue;
                Rect? n = area.Find(profileId);
                if (n.HasValue)
                    result.Add((area, new Rect(screen.xMin + n.Value.x * screen.width, screen.yMin + n.Value.y * screen.height, n.Value.width * screen.width, n.Value.height * screen.height)));
            }
            return result;
        }

        // 다른 프리팹에서 온 자리 전부 (이 프리팹에서 숨긴 것 포함)
        public static List<FlexibleReservedArea> FromOthers(FlexibleProfileSettings settings, string prefabGuid)
        {
            return settings.ReservedAreas.FindAll(a => a.prefabGuid != prefabGuid);
        }

        public static bool IsShownOver(FlexibleReservedArea area, string prefabGuid)
        {
            return area.hiddenFor == null || !area.hiddenFor.Contains(prefabGuid);
        }

        // 이 프리팹이 열려 있을 때 그 자리가 보이는지 기록한다 (게임이 숨기면 검사·Scene 표시에서 뺀다)
        public static void SetShownOver(FlexibleProfileSettings settings, FlexibleReservedArea area, string prefabGuid, bool shown)
        {
            Undo.RecordObject(settings, "Flexible Reserved Area");
            area.hiddenFor ??= new List<string>();
            area.hiddenFor.Remove(prefabGuid);
            if (!shown)
                area.hiddenFor.Add(prefabGuid);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            SceneView.RepaintAll();
        }

        public static string Label(FlexibleReservedArea area)
        {
            string prefab = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(area.prefabGuid));
            return string.IsNullOrEmpty(prefab) ? area.name : F("{0} ({1})", area.name, prefab);
        }

        // 그려지는 캔버스 순서: 순서를 따로 정한 가장 가까운 캔버스, 없으면 프리팹 맨 위 캔버스, 그것도 없으면 0
        public static int EffectiveOrder(Transform t, Transform root, Dictionary<Canvas, int> cache = null)
        {
            for (Transform c = t; c != null; c = c.parent)
            {
                var canvas = c.GetComponent<Canvas>();
                if (canvas != null && (canvas.overrideSorting || c == root))
                    return SavedOrder(canvas, cache);
                if (c == root)
                    break;
            }
            return 0;
        }

        // 안쪽 캔버스의 sortingOrder는 부모 값을 돌려줄 수 있어서 저장된 값을 읽는다
        private static int SavedOrder(Canvas canvas, Dictionary<Canvas, int> cache)
        {
            if (cache != null && cache.TryGetValue(canvas, out int cached))
                return cached;
            int order;
            using (var serialized = new SerializedObject(canvas))
                order = serialized.FindProperty("m_SortingOrder").intValue;
            if (cache != null)
                cache[canvas] = order;
            return order;
        }

        // 다른 프리팹을 편집하는 동안 그 화면에서 차지된 자리를 파란 테두리로 보여 준다
        private static void DrawScene(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
                return;
            FlexibleProfileSettings settings = SettingsOf(stage.prefabContentsRoot);
            if (settings.ReservedAreas.Count == 0)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            string guid = AssetDatabase.AssetPathToGUID(stage.assetPath);
            List<(FlexibleReservedArea area, Rect rect)> areas = ForScreen(settings, guid, FlexibleScreenSession.GetActiveProfileId(stage), root.rect);
            if (areas.Count == 0)
                return;

            if (labelStyle == null)
                labelStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Line } };
            var verts = new Vector3[4];
            foreach ((FlexibleReservedArea area, Rect rect) in areas)
            {
                verts[0] = root.TransformPoint(new Vector3(rect.xMin, rect.yMin));
                verts[1] = root.TransformPoint(new Vector3(rect.xMin, rect.yMax));
                verts[2] = root.TransformPoint(new Vector3(rect.xMax, rect.yMax));
                verts[3] = root.TransformPoint(new Vector3(rect.xMax, rect.yMin));
                Handles.DrawSolidRectangleWithOutline(verts, Fill, Line);
                Handles.Label(verts[1], F("Taken by {0}", Label(area)), labelStyle);
            }
        }
    }
}
