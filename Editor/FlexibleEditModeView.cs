using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 에디터 정지 중에도 씬 UI를 게임 뷰 모양(세로·정사각·가로)의 배치로 보여 준다.
    // 원본은 값을 건드리지 않고 그리기만 투명하게 하고, 배치를 적용한 저장되지 않는 사본을 그 자리에 그린다
    [InitializeOnLoad]
    internal static class FlexibleEditModeView
    {
        private const string MenuPath = "Tools/FlexibleUITool/Show Screen Layouts While Stopped";
        private const string EnabledKey = "FlexibleUITool.EditModeView";
        private const string StandInName = "__FlexibleEditView";
        private const double RescanInterval = 1.0;
        private const double RebuildDelay = 0.1;

        // 사본 하나: 원본 레이아웃을 덮는 캔버스, 원본 부모 자리를 맞춘 틀, 그 안의 사본, 투명하게 만든 원본 그래픽
        private class StandIn
        {
            public FlexibleLayout source;
            public string profileId;
            public Canvas canvas;
            public RectTransform frame;
            public GameObject copy;
            public readonly List<KeyValuePair<CanvasRenderer, float>> hidden = new List<KeyValuePair<CanvasRenderer, float>>();
            public double staleSince = -1;
        }

        private static readonly Dictionary<FlexibleLayout, StandIn> standIns = new Dictionary<FlexibleLayout, StandIn>();
        private static readonly HashSet<int> ownDestroyed = new HashSet<int>();
        private static readonly List<FlexibleLayout> candidates = new List<FlexibleLayout>();
        private static bool rescan = true;
        private static double lastScan;

        static FlexibleEditModeView()
        {
            EditorApplication.update += Update;
            EditorApplication.hierarchyChanged += () => rescan = true;
            ObjectChangeEvents.changesPublished += OnChanges;
            PrefabUtility.prefabInstanceUpdated += root => MarkStale(root != null ? root.transform : null);
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode)
                    RemoveAll();
            };
            AssemblyReloadEvents.beforeAssemblyReload += RemoveAll;
            EditorSceneManager.sceneClosing += (scene, removing) => RemoveScene(scene);
            SceneView.duringSceneGui += DrawSceneNote;
            CleanUpOrphans();
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        [MenuItem(MenuPath, false, 20)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            RemoveAll();
            rescan = true;
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !Enabled)
            {
                if (standIns.Count > 0)
                    RemoveAll();
                return;
            }

            if (rescan || EditorApplication.timeSinceStartup - lastScan > RescanInterval)
                Scan();

            // 바깥 레이아웃이 사본을 그리면 안쪽은 그 사본에 포함되므로 건너뛴다 (candidates는 얕은 것부터)
            var wanted = new Dictionary<FlexibleLayout, string>();
            foreach (FlexibleLayout layout in candidates)
            {
                if (layout == null || !layout.isActiveAndEnabled || HasWantedAncestor(layout, wanted))
                    continue;
                string profileId = SelectProfile(layout);
                if (profileId != null)
                    wanted[layout] = profileId;
            }

            foreach (FlexibleLayout layout in standIns.Keys.ToList())
            {
                if (layout == null || !wanted.ContainsKey(layout))
                    Remove(layout);
            }

            double now = EditorApplication.timeSinceStartup;
            foreach (KeyValuePair<FlexibleLayout, string> pair in wanted)
            {
                standIns.TryGetValue(pair.Key, out StandIn standIn);
                bool broken = standIn != null && (standIn.canvas == null || standIn.copy == null);
                bool stale = standIn != null && standIn.staleSince >= 0 && now - standIn.staleSince >= RebuildDelay;
                if (standIn == null || broken || stale || standIn.profileId != pair.Value)
                {
                    if (standIn != null)
                        Remove(pair.Key);
                    standIn = Create(pair.Key, pair.Value);
                }
                if (standIn != null)
                    Sync(standIn);
            }
        }

        private static void Scan()
        {
            rescan = false;
            lastScan = EditorApplication.timeSinceStartup;
            candidates.Clear();
            foreach (FlexibleLayout layout in Object.FindObjectsByType<FlexibleLayout>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                GameObject go = layout.gameObject;
                if ((go.hideFlags & HideFlags.DontSave) != 0 || !go.scene.IsValid() || EditorSceneManager.IsPreviewScene(go.scene)
                    || PrefabStageUtility.GetPrefabStage(go) != null)
                    continue;
                candidates.Add(layout);
            }
            candidates.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (; t.parent != null; t = t.parent)
                depth++;
            return depth;
        }

        private static bool HasWantedAncestor(FlexibleLayout layout, Dictionary<FlexibleLayout, string> wanted)
        {
            for (Transform t = layout.transform.parent; t != null; t = t.parent)
            {
                var outer = t.GetComponent<FlexibleLayout>();
                if (outer != null && wanted.ContainsKey(outer))
                    return true;
            }
            return false;
        }

        // 게임 뷰 모양으로 고른 화면. 기준 화면이거나 그 화면에 바꾼 배치가 없으면 원본 그대로 보이므로 null
        private static string SelectProfile(FlexibleLayout layout)
        {
            FlexibleProfileSettings settings = layout.Settings;
            Canvas canvas = layout.GetComponentInParent<Canvas>();
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            if (settings == null || settings.Profiles.Count == 0 || root == null || root.renderMode == RenderMode.WorldSpace
                || !layout.TryGetSelectionSize(out Vector2 size))
                return null;

            int current = standIns.TryGetValue(layout, out StandIn shown) ? settings.IndexOf(shown.profileId) : settings.IndexOf(settings.BaseProfileId);
            string id = settings.Profiles[settings.SelectProfile(size.x / size.y, current)].id;
            if (id == settings.BaseProfileId)
                return null;
            foreach (FlexibleLayout inner in layout.GetComponentsInChildren<FlexibleLayout>(true))
            {
                if (inner.Entries.Exists(e => e.Find(id) != null))
                    return id;
            }
            return null;
        }

        private static StandIn Create(FlexibleLayout layout, string profileId)
        {
            Canvas root = layout.GetComponentInParent<Canvas>().rootCanvas;
            var canvasObject = new GameObject(StandInName, typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(canvasObject, layout.gameObject.scene);
            var frameObject = new GameObject("Frame", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            frameObject.transform.SetParent(canvasObject.transform, false);

            var standIn = new StandIn
            {
                source = layout,
                profileId = profileId,
                canvas = canvasObject.GetComponent<Canvas>(),
                frame = (RectTransform)frameObject.transform,
            };
            standIn.frame.anchorMin = standIn.frame.anchorMax = new Vector2(0.5f, 0.5f);
            standIn.frame.pivot = Vector2.zero;
            SyncCanvas(standIn.canvas, root);
            SyncFrame(standIn, root);

            // 사본은 원본과 같은 값에서 출발해 미리보기 창과 같은 순서로 배치를 적용한다 (본체 → 안쪽 프리팹 → 세이프 에리어)
            GameObject copy = Object.Instantiate(layout.gameObject, standIn.frame, false);
            copy.name = layout.gameObject.name;
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            standIn.copy = copy;

            var copyLayout = copy.GetComponent<FlexibleLayout>();
            copyLayout.ApplyProfile(profileId);
            foreach (FlexibleLayout nested in copy.GetComponentsInChildren<FlexibleLayout>(true))
            {
                if (nested != copyLayout)
                    nested.ApplyForScreen(profileId);
            }
            foreach (FlexibleSafeArea safeArea in copy.GetComponentsInChildren<FlexibleSafeArea>(true))
                safeArea.ApplyNormalized(new Rect(0f, 0f, 1f, 1f));

            foreach (CanvasRenderer renderer in layout.GetComponentsInChildren<CanvasRenderer>(true))
            {
                standIn.hidden.Add(new KeyValuePair<CanvasRenderer, float>(renderer, renderer.GetAlpha()));
                renderer.SetAlpha(0f);
            }

            standIns[layout] = standIn;
            return standIn;
        }

        // 게임 뷰 크기·원본 부모 위치가 바뀌어도 사본이 같은 자리에 오도록 매번 맞춘다
        private static void Sync(StandIn standIn)
        {
            Canvas canvas = standIn.source.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;
            Canvas root = canvas.rootCanvas;
            SyncCanvas(standIn.canvas, root);
            SyncFrame(standIn, root);
        }

        private static void SyncCanvas(Canvas canvas, Canvas root)
        {
            if (canvas.renderMode != root.renderMode)
                canvas.renderMode = root.renderMode;
            if (canvas.worldCamera != root.worldCamera)
                canvas.worldCamera = root.worldCamera;
            if (!Mathf.Approximately(canvas.planeDistance, root.planeDistance))
                canvas.planeDistance = root.planeDistance;
            if (canvas.sortingLayerID != root.sortingLayerID)
                canvas.sortingLayerID = root.sortingLayerID;
            if (canvas.sortingOrder != root.sortingOrder)
                canvas.sortingOrder = root.sortingOrder;
            if (canvas.targetDisplay != root.targetDisplay)
                canvas.targetDisplay = root.targetDisplay;
            if (canvas.pixelPerfect != root.pixelPerfect)
                canvas.pixelPerfect = root.pixelPerfect;
            if (canvas.additionalShaderChannels != root.additionalShaderChannels)
                canvas.additionalShaderChannels = root.additionalShaderChannels;
            if (!Mathf.Approximately(canvas.scaleFactor, root.scaleFactor))
                canvas.scaleFactor = root.scaleFactor;
            if (!Mathf.Approximately(canvas.referencePixelsPerUnit, root.referencePixelsPerUnit))
                canvas.referencePixelsPerUnit = root.referencePixelsPerUnit;
        }

        // 틀 = 원본 부모의 사각형 (최상위 캔버스 좌표)
        private static void SyncFrame(StandIn standIn, Canvas root)
        {
            var parent = standIn.source.transform.parent as RectTransform;
            var rootRect = (RectTransform)root.transform;
            Vector2 min, max;
            if (parent == null)
            {
                min = rootRect.rect.min;
                max = rootRect.rect.max;
            }
            else
            {
                var corners = new Vector3[4];
                parent.GetWorldCorners(corners);
                min = rootRect.InverseTransformPoint(corners[0]);
                max = rootRect.InverseTransformPoint(corners[2]);
            }
            if (standIn.frame.anchoredPosition != min)
                standIn.frame.anchoredPosition = min;
            if (standIn.frame.sizeDelta != max - min)
                standIn.frame.sizeDelta = max - min;
        }

        private static void Remove(FlexibleLayout layout)
        {
            if (layout == null)
            {
                // 원본이 지워졌으면 사본만 정리한다
                foreach (FlexibleLayout key in standIns.Keys.Where(k => k == null).ToList())
                {
                    Destroy(standIns[key]);
                    standIns.Remove(key);
                }
                return;
            }
            if (!standIns.TryGetValue(layout, out StandIn standIn))
                return;
            Destroy(standIn);
            standIns.Remove(layout);
        }

        private static void Destroy(StandIn standIn)
        {
            foreach (KeyValuePair<CanvasRenderer, float> pair in standIn.hidden)
            {
                if (pair.Key != null)
                    pair.Key.SetAlpha(pair.Value);
            }
            standIn.hidden.Clear();
            if (standIn.canvas != null)
            {
                ownDestroyed.Add(standIn.canvas.gameObject.GetInstanceID());
                Object.DestroyImmediate(standIn.canvas.gameObject);
            }
        }

        private static void RemoveAll()
        {
            foreach (StandIn standIn in standIns.Values)
                Destroy(standIn);
            standIns.Clear();
        }

        private static void RemoveScene(Scene scene)
        {
            foreach (FlexibleLayout layout in standIns.Keys.ToList())
            {
                if (layout == null || layout.gameObject.scene == scene)
                    Remove(layout);
            }
        }

        // 원본이 바뀌면(사용자 편집·되돌리기·프리팹 갱신) 잠시 뒤 사본을 다시 만든다. 사본 자신의 변화는 무시한다
        private static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (standIns.Count == 0)
                return;
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties);
                        MarkStale(properties.instanceId);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structure);
                        MarkStale(structure.instanceId);
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var hierarchy);
                        MarkStale(hierarchy.instanceId);
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var parent);
                        MarkStale(parent.instanceId);
                        break;
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                        MarkStale(created.instanceId);
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var destroyed);
                        if (!ownDestroyed.Remove(destroyed.instanceId))
                            MarkStale((Transform)null);
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                    case ObjectChangeKind.UpdatePrefabInstances:
                        MarkStale((Transform)null);
                        break;
                }
            }
        }

        private static void MarkStale(int instanceId)
        {
            Object changed = EditorUtility.InstanceIDToObject(instanceId);
            GameObject go = changed as GameObject;
            if (go == null && changed is Component component)
                go = component.gameObject;
            if (go != null && (go.hideFlags & HideFlags.DontSave) != 0)
                return;
            MarkStale(go != null ? go.transform : null);
        }

        // null이면 전부
        private static void MarkStale(Transform changed)
        {
            double now = EditorApplication.timeSinceStartup;
            foreach (StandIn standIn in standIns.Values)
            {
                if (changed == null || (standIn.source != null && changed.IsChildOf(standIn.source.transform)))
                    standIn.staleSince = now;
            }
        }

        // 사본을 보여 주는 UI 안쪽을 고르면, 씬에서 고친 값은 기준 배치에 들어간다고 알린다
        private static void DrawSceneNote(SceneView view)
        {
            Transform selected = Selection.activeTransform;
            if (standIns.Count == 0 || selected == null)
                return;
            StandIn standIn = standIns.Values.FirstOrDefault(s => s.source != null && selected.IsChildOf(s.source.transform));
            if (standIn == null)
                return;

            FlexibleProfileSettings settings = standIn.source.Settings;
            string message = F("\"{0}\" is shown with its {1} layout because of the Game view shape. Changes here go to the base layout ({2}); edit the {1} layout in the Flexible UI window.",
                standIn.source.name, Label(settings.Find(standIn.profileId)), Label(settings.Find(settings.BaseProfileId)));
            Handles.BeginGUI();
            var content = new GUIContent(message);
            GUIStyle style = EditorStyles.helpBox;
            float width = Mathf.Min(420f, view.position.width - 20f);
            float height = style.CalcHeight(content, width);
            GUI.Box(new Rect(10f, 10f, width, height), content, style);
            Handles.EndGUI();
        }

        // 스크립트 다시 읽기 등으로 남은 사본을 지우고, 그때 투명하게 남은 원본 그래픽을 되살린다
        private static void CleanUpOrphans()
        {
            bool found = false;
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas != null && canvas.name == StandInName && (canvas.gameObject.hideFlags & HideFlags.DontSave) != 0
                    && !EditorUtility.IsPersistent(canvas))
                {
                    Object.DestroyImmediate(canvas.gameObject);
                    found = true;
                }
            }
            if (!found)
                return;
            foreach (CanvasRenderer renderer in Object.FindObjectsByType<CanvasRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.GetAlpha() == 0f && (renderer.gameObject.hideFlags & HideFlags.DontSave) == 0)
                    renderer.SetAlpha(1f);
            }
        }
    }
}
