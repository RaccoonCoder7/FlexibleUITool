using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 편집 탭: 화면(세로·정사각·가로) 전환, 선택한 블록 상태, 그리드 배치, 템플릿
    [System.Serializable]
    public class FlexibleEditPanel
    {
        // 선택지는 FlexibleAxisMode.Stretch부터 (Auto는 고를 수 없다)
        private static string[] ModeLabelsX => new[] { T("Stretch"), T("Stick left"), T("Stick right"), T("Center") };
        private static string[] ModeLabelsY => new[] { T("Stretch"), T("Stick bottom"), T("Stick top"), T("Center") };
        private static readonly int DragHint = "FlexibleGridDrag".GetHashCode();
        private const string InsideSafeAreaKey = "FlexibleUITool.InsideSafeArea";

        [SerializeField] private bool showGrid;
        [SerializeField] private bool showTemplates;
        [SerializeField] private bool showMore;
        [SerializeField] private bool dragPlacement = true;
        [SerializeField] private int templateIndex;
        [SerializeField] private string templateName = "";
        // 칸에 놓은 적 없는 블록에 쓰는 SafeArea 기본값 (사용자별)
        private bool insideSafeArea = true;
        private bool dragging;
        private Vector2Int dragStart;
        private FlexibleCell dragCell;

        // 블록 하나의 그리드 입력값: 칸에 놓였으면 그 기록, 아니면 지금 덮고 있는 칸 + Stretch
        private struct BlockInput
        {
            public RectTransform target;
            public FlexibleGridPlacement placement;
            public FlexibleCell cell;
            public FlexibleAxisMode modeX;
            public FlexibleAxisMode modeY;
            public bool insideSafeArea;
            // 칸에 맞춰 줄이기 (배치 기록에 남는다)
            public bool fit;
            // 이번 배치에서 크기를 지키는 축을 칸 크기 대신 세로 화면 크기로 (한 번만)
            public bool ownSize;
        }

        public bool GridOpen => showGrid;

        public bool TemplatesOpen => showTemplates;

        // 템플릿 「적용」을 누른 횟수 (튜토리얼 확인용)
        public int TemplateApplyCount { get; private set; }

        public void SetFoldouts(bool grid, bool templates)
        {
            showGrid = grid;
            showTemplates = templates;
            SceneView.RepaintAll();
        }

        public void SuggestTemplateName(string name)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                templateName = name;
        }

        public void OnEnable()
        {
            insideSafeArea = EditorPrefs.GetBool(InsideSafeAreaKey, true);
        }

        private BlockInput GetInput(RectTransform target, FlexibleWorkspace session, FlexibleGridContext context)
        {
            var input = new BlockInput { target = target, placement = session.FindPlacement(target) };
            if (input.placement != null)
            {
                input.cell = input.placement.cell.Clamped(context.grid);
                // 예전 버전의 Auto는 지금 블록이 실제로 쓰는 모드로 보여 준다
                input.modeX = FlexibleGridMath.ResolveX(input.placement.modeX, input.cell, context.grid);
                input.modeY = FlexibleGridMath.ResolveY(input.placement.modeY, input.cell, context.grid);
                input.insideSafeArea = input.placement.insideSafeArea;
                input.fit = input.placement.fit;
                return input;
            }

            input.modeX = FlexibleAxisMode.Stretch;
            input.modeY = FlexibleAxisMode.Stretch;
            input.insideSafeArea = insideSafeArea;
            input.cell = new FlexibleCell(0, 0, 1, 1);
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = context.root.InverseTransformPoint(corners[0]);
            Vector2 max = context.root.InverseTransformPoint(corners[2]);
            Rect area = context.GetArea(input.insideSafeArea);
            // 칸 경계에 딱 붙은 가장자리가 이웃 칸으로 잡히지 않게 조금 안쪽을 본다
            if (FlexibleGridMath.TryGetCell(area, context.grid, min + Vector2.one, out Vector2Int a)
                && FlexibleGridMath.TryGetCell(area, context.grid, max - Vector2.one, out Vector2Int b))
                input.cell = FlexibleCell.FromCorners(a, b);
            return input;
        }

        // 입력값대로 다시 놓는다. 손으로 옮긴 보정은 새 칸·모드 위에 그대로 얹는다
        private static void Place(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input, FlexibleSpecService.BaseSizes sizes = null)
        {
            FlexibleGridAdjustment adjustment = default;
            if (input.placement != null)
                FlexibleGridPlacementService.TryGetAdjustment(input.placement, out adjustment);
            FlexibleCell cell = input.cell.Clamped(context.grid);
            if (input.ownSize && sizes != null)
            {
                // 칸에 맞춰 줄이는 블록은 늘어나는 축도 크기를 지킨다 (가운데로)
                FlexibleAxisMode sizeX = input.fit && FlexibleGridMath.ResolveX(input.modeX, cell, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : input.modeX;
                FlexibleAxisMode sizeY = input.fit && FlexibleGridMath.ResolveY(input.modeY, cell, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : input.modeY;
                adjustment.size = FlexibleSpecService.KeepSizeOffset(context, input.target, cell, sizeX, sizeY, input.insideSafeArea,
                    sizes.Get(FlexiblePathUtility.GetPath(context.root, input.target)));
            }
            FlexibleGridPlacementService.Apply(context, session, input.target, cell, input.modeX, input.modeY, input.insideSafeArea, adjustment, input.fit);
        }

        // 고른 블록 전부에 한 번에 적용하고 Undo 한 번으로 묶는다
        private static void PlaceAll(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs, System.Func<BlockInput, BlockInput> change)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            FlexibleSpecService.BaseSizes sizes = null;
            try
            {
                foreach (BlockInput input in inputs)
                {
                    BlockInput changed = change(input);
                    if (changed.ownSize && sizes == null)
                        sizes = OpenBaseSizes(session, context.root);
                    Place(context, session, changed, sizes);
                }
            }
            finally
            {
                sizes?.Dispose();
            }
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        // 저장된 프리팹(= 세로 화면)에서 블록 크기·위치를 읽는다
        private static FlexibleSpecService.BaseSizes OpenBaseSizes(FlexibleWorkspace session, RectTransform root)
        {
            string path = AssetDatabase.GUIDToAssetPath(session.sourceGuid);
            return new FlexibleSpecService.BaseSizes(path, FlexibleReservedAreas.SettingsOf(root.gameObject));
        }

        // 안전 영역의 모서리·가장자리·가운데에 세로 화면과 같은 거리·크기로 붙인다
        private static void PinAll(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs, string pin)
        {
            FlexibleSpecService.TryParsePin(pin, out FlexibleAxisMode modeX, out FlexibleAxisMode modeY);
            var whole = new FlexibleCell(0, 0, context.grid.columns, context.grid.rows);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            using (FlexibleSpecService.BaseSizes sizes = OpenBaseSizes(session, context.root))
            {
                foreach (BlockInput input in inputs)
                {
                    Rect? rect = sizes.GetRect(FlexiblePathUtility.GetPath(context.root, input.target));
                    if (!rect.HasValue)
                        continue;
                    var adjustment = new FlexibleGridAdjustment
                    {
                        size = FlexibleSpecService.KeepSizeOffset(context, input.target, whole, modeX, modeY, input.insideSafeArea, rect.Value.size),
                        position = FlexibleSpecService.PinOffset(modeX, modeY, rect.Value, sizes.Screen, context.grid.margin),
                    };
                    FlexibleGridPlacementService.Apply(context, session, input.target, whole, modeX, modeY, input.insideSafeArea, adjustment, input.fit);
                }
            }
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        private static readonly string[] Pins = { "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right" };
        private static readonly string[] PinLabels = { "\u2196", "\u2191", "\u2197", "\u2190", "\u00b7", "\u2192", "\u2199", "\u2193", "\u2198" };

        // 고른 블록들의 값이 모두 같으면 그 값, 다르면 「—」로 보여 주는 숫자 칸. 바꾼 값을 돌려준다
        private static bool MixedIntField(string label, List<BlockInput> inputs, System.Func<BlockInput, int> get, out int value)
        {
            int first = get(inputs[0]);
            EditorGUI.showMixedValue = inputs.Exists(i => get(i) != first);
            EditorGUI.BeginChangeCheck();
            value = EditorGUILayout.DelayedIntField(label, first);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            return changed;
        }

        private static bool MixedModeField(string label, string[] labels, List<BlockInput> inputs, System.Func<BlockInput, FlexibleAxisMode> get, out FlexibleAxisMode value)
        {
            FlexibleAxisMode first = get(inputs[0]);
            EditorGUI.showMixedValue = inputs.Exists(i => get(i) != first);
            EditorGUI.BeginChangeCheck();
            // 선택지는 Stretch부터 (Auto는 고를 수 없다)
            value = (FlexibleAxisMode)(EditorGUILayout.Popup(label, (int)first - 1, labels) + 1);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            return changed;
        }

        public void Draw()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                DrawNoPrefab();
                return;
            }

            DrawBackToOuter(stage);

            if (FlexibleScreenSession.IsLegacyWorkspace(stage))
            {
                DrawLegacyWorkspace(stage);
                return;
            }

            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
            {
                DrawChecklist(stage);
                return;
            }

            DrawScreens(stage, layout);
            DrawView(stage);
            DrawOutside(stage);
            FlexibleAiPanel.DrawLauncher(stage, layout);
            DrawPendingLegacy(stage);
            DrawSelected(stage, layout);
            DrawNestedPrefabs(stage, layout);
            DrawGrid(stage, layout);
            DrawTemplates(stage, layout);
            DrawMore(stage, layout);
        }

        // ───────── 보기 (편집하는 동안만) ─────────

        [SerializeField] private bool showView = true;
        private static GUIStyle viewHeader;

        // 맨 위 블록을 편집하는 동안만 켜고 끈다 (저장되지 않음, 미리보기·검수에도 같이 적용)
        private void DrawView(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            List<Transform> items = GetViewItems(stage, root.transform);
            int overridden = items.Count(t => FlexibleScreenSession.FindView(stage, t.gameObject) != null);
            EditorGUILayout.Space(4f);
            // 굵은 제목은 한글 일부 글자만 굵게 그려져 얼룩져 보이므로 보통 굵기로
            if (viewHeader == null)
                viewHeader = new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Normal };
            showView = EditorGUILayout.Foldout(showView, overridden > 0 ? F("View while editing ({0} changed, not saved)", overridden) : T("View while editing (not saved)"), true, viewHeader);
            FlexibleGui.Mark("view");
            if (!showView)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Show or hide blocks only while you edit, for example panels that cover the screen. Nothing here is saved, on any screen. The Preview and Check tabs use the same view."), EditorStyles.wordWrappedMiniLabel);
                foreach (Transform child in items)
                {
                    GameObject go = child.gameObject;
                    bool changed = FlexibleScreenSession.FindView(stage, go) != null;
                    bool deep = child.parent != root.transform;
                    EditorGUI.BeginChangeCheck();
                    string label = deep ? FlexiblePathUtility.GetPath(root.transform, child) : go.name;
                    bool on = EditorGUILayout.ToggleLeft(label, FlexibleViewOverride.IsVisible(go), changed ? EditorStyles.boldLabel : EditorStyles.label);
                    if (EditorGUI.EndChangeCheck())
                        ShowInView(stage, root.transform, child, on);
                }
                using (new EditorGUI.DisabledScope(overridden == 0))
                {
                    if (GUILayout.Button(T("Back to the saved view")))
                        FlexibleScreenSession.ClearView(stage, true);
                }
            }
        }

        // 보기 목록: 맨 위 블록 + 더 안쪽에서 Canvas를 꺼서 숨겨 둔 패널(코드가 Canvas를 켜서 여는 방식) + 편집용으로 바꾼 것
        private static List<Transform> GetViewItems(PrefabStage stage, Transform root)
        {
            var items = new List<Transform>();
            foreach (Transform child in root)
            {
                items.Add(child);
                foreach (Canvas canvas in child.GetComponentsInChildren<Canvas>(true))
                {
                    Transform t = canvas.transform;
                    if (t != child && (!canvas.enabled || FlexibleScreenSession.FindView(stage, t.gameObject) != null))
                        items.Add(t);
                }
            }
            return items;
        }

        // 켤 때는 꺼진 위쪽 블록도 같이 켜야 보인다 (끌 때는 그것만)
        private static void ShowInView(PrefabStage stage, Transform root, Transform target, bool on)
        {
            if (on)
            {
                for (Transform t = target.parent; t != null && t != root; t = t.parent)
                {
                    if (!FlexibleViewOverride.IsVisible(t.gameObject))
                        FlexibleScreenSession.SetViewVisible(stage, t.gameObject, true);
                }
            }
            FlexibleScreenSession.SetViewVisible(stage, target.gameObject, on);
        }

        // ───────── 안쪽 프리팹으로 들어갔다가 돌아오기 ─────────

        private const string ReturnKeyPrefix = "FlexibleUITool.ReturnTo.";

        // 안쪽 프리팹을 열 때 바깥 프리팹을 기억해 두고, 안쪽에서 「돌아가기」로 바로 돌아오게 한다. 보던 화면(가로 등)으로 연다
        public static void OpenNestedPrefab(string path, PrefabStage from)
        {
            if (from != null && from.assetPath != path)
                SessionState.SetString(ReturnKeyPrefix + path, from.assetPath);
            OpenOnSameScreen(path, from);
        }

        private static void OpenOnSameScreen(string path, PrefabStage from)
        {
            FlexibleScreenSession.OpenNextOn(path, FlexibleScreenSession.GetActiveProfileId(from));
            PrefabStageUtility.OpenPrefab(path);
            // 저장 확인에서 취소하는 등으로 열리지 않았으면 다음에 열 때 쓰지 않도록 지운다
            FlexibleScreenSession.OpenNextOn(path, null);
        }

        private static void DrawBackToOuter(PrefabStage stage)
        {
            string outer = SessionState.GetString(ReturnKeyPrefix + stage.assetPath, "");
            if (string.IsNullOrEmpty(outer) || AssetDatabase.LoadAssetAtPath<GameObject>(outer) == null)
                return;
            if (GUILayout.Button(F("← Back to \"{0}\"", Path.GetFileNameWithoutExtension(outer)), EditorStyles.miniButton))
            {
                OpenOnSameScreen(outer, stage);
                GUIUtility.ExitGUI();
            }
        }

        // ───────── 안쪽 프리팹 ─────────

        // 바깥 프리팹 안에 들어 있는 프리팹 하나 (같은 프리팹 여러 개는 하나로)
        public class NestedPrefab
        {
            public string path;
            public string name;
            public bool fullScreen;
            public bool flexible;
        }

        [SerializeField] private bool showNested = true;
        private readonly Dictionary<string, bool> nestedChoice = new Dictionary<string, bool>();
        private List<NestedPrefab> nestedCache;
        private string nestedCacheStage;
        private double nestedCacheAt;

        // 열린 프리팹 안의 프리팹 중 화면을 꽉 채우는 것(전체 화면 UI)과 이미 Flexible UI인 것. 버튼 같은 부품은 뺀다
        public static List<NestedPrefab> FindNestedPrefabs(PrefabStage stage)
        {
            var result = new List<NestedPrefab>();
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            Rect screen = root.rect;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) || !(t is RectTransform rt))
                    continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !(asset.transform is RectTransform) || result.Exists(n => n.path == path))
                    continue;

                Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, rt);
                bool full = screen.width > 0f && Mathf.Abs(r.width - screen.width) < screen.width * 0.02f && Mathf.Abs(r.height - screen.height) < screen.height * 0.02f;
                bool flexible = asset.GetComponent<FlexibleLayout>() != null;
                if (full || flexible)
                    result.Add(new NestedPrefab { path = path, name = Path.GetFileNameWithoutExtension(path), fullScreen = full, flexible = flexible });
            }
            return result;
        }

        // 프리팹 파일을 직접 열어 Flexible UI로 만든다 (컴포넌트·화면 설정, 크기 맞춤 컴포넌트가 있는 캔버스면 그것도 교체)
        public static bool MakeFlexible(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var layout = contents.GetComponent<FlexibleLayout>();
                if (layout != null && layout.Settings != null)
                    return false;
                if (layout == null)
                    layout = contents.AddComponent<FlexibleLayout>();
                layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                var canvas = contents.GetComponent<Canvas>();
                if (canvas != null && contents.GetComponent<CanvasScaler>() != null)
                    FlexibleLayoutInspector.UseFlexibleScaler(canvas, false);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private void DrawNestedPrefabs(PrefabStage stage, FlexibleLayout layout)
        {
            if (nestedCache == null || nestedCacheStage != stage.assetPath || EditorApplication.timeSinceStartup - nestedCacheAt > 1.0)
            {
                nestedCache = FindNestedPrefabs(stage);
                nestedCacheStage = stage.assetPath;
                nestedCacheAt = EditorApplication.timeSinceStartup;
            }
            if (nestedCache.Count == 0)
                return;

            int pending = nestedCache.Count(n => !n.flexible);
            EditorGUILayout.Space(6f);
            showNested = EditorGUILayout.Foldout(showNested, pending > 0 ? F("Prefabs inside ({0} not Flexible UI yet)", pending) : T("Prefabs inside"), true, EditorStyles.foldoutHeader);
            if (!showNested)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Full-screen prefabs inside this one. Make them Flexible UI so each can have its own layout per screen. Small parts such as buttons are not listed; this prefab places them."), EditorStyles.wordWrappedMiniLabel);
                foreach (NestedPrefab nested in nestedCache)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (nested.flexible)
                        {
                            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(FlexibleGui.Good)}>✔</color> " + nested.name, FlexibleGui.WrapLabel);
                        }
                        else
                        {
                            bool on = !nestedChoice.TryGetValue(nested.path, out bool chosen) || chosen;
                            nestedChoice[nested.path] = EditorGUILayout.ToggleLeft(nested.name, on);
                        }
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(T("Open"), EditorStyles.miniButton, GUILayout.Width(60f)))
                        {
                            OpenNestedPrefab(nested.path, stage);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                List<string> chosenPaths = nestedCache.Where(n => !n.flexible && (!nestedChoice.TryGetValue(n.path, out bool c) || c)).Select(n => n.path).ToList();
                if (pending > 0)
                {
                    using (new EditorGUI.DisabledScope(chosenPaths.Count == 0))
                    {
                        if (GUILayout.Button(F("Make the checked prefabs Flexible UI ({0})", chosenPaths.Count), GUILayout.Height(26f)))
                        {
                            MakeNestedFlexible(stage, layout, chosenPaths);
                            GUIUtility.ExitGUI();
                        }
                    }
                    GUILayout.Label(T("Their current look becomes their portrait layout. Make their other screens by opening each one later."), EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static void MakeNestedFlexible(PrefabStage stage, FlexibleLayout layout, List<string> paths)
        {
            // 기준 화면에서 저장된 상태로 바꾼다 (안쪽 프리팹 파일이 바뀌면 열린 프리팹 안의 인스턴스도 다시 불러와진다)
            string baseId = layout.Settings.BaseProfileId;
            if (FlexibleScreenSession.GetActiveProfileId(stage) != baseId)
                FlexibleScreenSession.ShowProfile(stage, baseId);
            else if (stage.scene.isDirty)
                FlexibleScreenSession.Save(stage);

            int made = 0;
            foreach (string path in paths)
            {
                if (MakeFlexible(path))
                    made++;
            }

            // 안쪽이 Flexible UI가 되면 바깥 프리팹이 관리하는 목록이 달라지므로, 다시 불러온 뒤 한 번 저장해 목록을 새로 만든다
            EditorApplication.delayCall += () =>
            {
                PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
                if (current != null && current.assetPath == stage.assetPath)
                    FlexibleScreenSession.Save(current);
            };
            EditorUtility.DisplayDialog("Flexible UI", F("Made {0} prefabs Flexible UI.", made), T("OK"));
        }

        // ───────── 프리팹이 열려 있지 않을 때 ─────────

        private static void DrawNoPrefab()
        {
            if (FlexibleTutorial.Active || FlexibleAiRun.Current == FlexibleAiRun.State.Running)
                return;

            DrawSceneCanvasCheck();

            EditorGUILayout.Space(4f);
            GUILayout.Label(T("Open a UI prefab"), FlexibleGui.Title);
            GUILayout.Label(T("Double-click a UI prefab in the Project window, or select it (or anything inside it) in the scene and press the Open button below. It opens in Prefab Mode, and the screen buttons (Portrait / Square / Landscape) appear here."), FlexibleGui.WrapLabel);

            List<string> paths = GetSelectedPrefabPaths();
            for (int i = 0; i < paths.Count; i++)
            {
                EditorGUILayout.Space(4f);
                string label = i == 0 ? F("Open \"{0}\"", Path.GetFileNameWithoutExtension(paths[i])) : F("Open \"{0}\" (the outer prefab)", Path.GetFileNameWithoutExtension(paths[i]));
                if (GUILayout.Button(label, i == 0 ? FlexibleGui.BigButton : GUI.skin.button, GUILayout.Height(i == 0 ? 34f : 26f)))
                {
                    PrefabStageUtility.OpenPrefab(paths[i]);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 지금 선택에서 열 수 있는 UI 프리팹: Project 창의 프리팹 파일, 또는 씬에서 고른 프리팹 인스턴스.
        // 씬에서 안쪽 프리팹(다른 프리팹 안의 프리팹)을 골랐으면 안쪽 → 바깥 순서로 둘 다
        public static List<string> GetSelectedPrefabPaths()
        {
            var paths = new List<string>();
            var selected = Selection.activeObject as GameObject;
            if (selected == null)
                return paths;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (assetPath.EndsWith(".prefab"))
                    AddUiPrefab(paths, assetPath);
                return paths;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(selected))
            {
                AddUiPrefab(paths, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(PrefabUtility.GetNearestPrefabInstanceRoot(selected)));
                AddUiPrefab(paths, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(PrefabUtility.GetOutermostPrefabInstanceRoot(selected)));
            }
            return paths;
        }

        private static void AddUiPrefab(List<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path) || paths.Contains(path))
                return;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && asset.transform is RectTransform)
                paths.Add(path);
        }

        private static List<Canvas> sceneCanvases;
        private static bool showSceneList;
        private static List<string> scenePrefabs;
        private static List<RectTransform> sceneLoose;
        private static readonly Dictionary<int, bool> looseChoice = new Dictionary<int, bool>();
        private static double sceneCheckedAt;

        // 열린 씬에 아직 Flexible UI가 아닌 UI가 있으면 한 번에 바꾸는 버튼을 보여 준다
        // (최상위 캔버스의 크기 맞춤 교체 + 화면을 꽉 채우는 UI 프리팹을 Flexible UI로)
        private static void DrawSceneCanvasCheck()
        {
            if (sceneCanvases == null || EditorApplication.timeSinceStartup - sceneCheckedAt > 1.0)
            {
                sceneCanvases = FlexibleLayoutInspector.FindCanvasesToConvert();
                scenePrefabs = FindScenePrefabsToMake();
                sceneLoose = FindLooseSceneUi();
                sceneCheckedAt = EditorApplication.timeSinceStartup;
            }
            sceneCanvases.RemoveAll(c => c == null);
            sceneLoose.RemoveAll(r => r == null);
            if (sceneCanvases.Count == 0 && scenePrefabs.Count == 0 && sceneLoose.Count == 0)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(F("The open scene has UI that is not ready for portrait / square / landscape yet: {0} canvases that do not use Flexible Canvas Scaler, {1} full-screen prefabs that are not Flexible UI, {2} UI groups that are not prefabs.", sceneCanvases.Count, scenePrefabs.Count, sceneLoose.Count), MessageType.Warning);
            showSceneList = EditorGUILayout.Foldout(showSceneList, T("Show the list"), true);
            if (showSceneList)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (sceneCanvases.Count > 0)
                        GUILayout.Label(F("Canvases to switch to Flexible Canvas Scaler: {0}", string.Join(", ", sceneCanvases.Select(FlexibleLayoutInspector.CanvasLabel))), EditorStyles.wordWrappedMiniLabel);
                    if (scenePrefabs.Count > 0)
                        GUILayout.Label(F("Full-screen prefabs to make Flexible UI: {0}", string.Join(", ", scenePrefabs.Select(Path.GetFileNameWithoutExtension))), EditorStyles.wordWrappedMiniLabel);
                    if (sceneLoose.Count > 0)
                    {
                        GUILayout.Label(T("UI placed straight in the scene (not a prefab). Checked ones are saved as new prefabs and made Flexible UI; they look the same as now:"), EditorStyles.wordWrappedMiniLabel);
                        foreach (RectTransform loose in sceneLoose)
                        {
                            int id = loose.GetInstanceID();
                            bool on = !looseChoice.TryGetValue(id, out bool chosen) || chosen;
                            looseChoice[id] = EditorGUILayout.ToggleLeft(loose.parent.name + "/" + loose.name, on);
                        }
                    }
                }
            }
            if (GUILayout.Button(T("Make the scene's UI Flexible UI"), FlexibleGui.BigButton, GUILayout.Height(30f)))
            {
                List<RectTransform> chosenLoose = sceneLoose.Where(r => !looseChoice.TryGetValue(r.GetInstanceID(), out bool c) || c).ToList();
                SetUpScene(sceneCanvases, scenePrefabs, chosenLoose, true);
                sceneCanvases = null;
                GUIUtility.ExitGUI();
            }
        }

        // 화면 공간의 최상위 캔버스(프리팹이 아닌 것) 바로 아래에 프리팹이 아닌 채 놓인 UI 묶음.
        // Flexible UI는 프리팹 단위로 편집하므로 이런 UI는 화면별 배치를 만들 수 없다
        public static List<RectTransform> FindLooseSceneUi()
        {
            var result = new List<RectTransform>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                    {
                        Transform t = canvas.transform;
                        if (canvas.renderMode == RenderMode.WorldSpace || (t.parent != null && t.parent.GetComponentInParent<Canvas>(true) != null)
                            || PrefabUtility.IsPartOfAnyPrefab(canvas.gameObject))
                            continue;
                        foreach (Transform child in t)
                        {
                            if (child is RectTransform rt && IsLooseUi(rt))
                                result.Add(rt);
                        }
                    }
                }
            }
            return result;
        }

        private static bool IsLooseUi(RectTransform rt)
        {
            if (PrefabUtility.IsPartOfAnyPrefab(rt.gameObject) || rt.CompareTag("EditorOnly") || rt.gameObject.hideFlags != HideFlags.None)
                return false;
            // 안에 든 그림이 모두 프리팹 안이면 프리팹을 담는 묶음일 뿐이다
            return rt.GetComponentsInChildren<Graphic>(true).Any(g => !PrefabUtility.IsPartOfAnyPrefab(g));
        }

        // 씬 UI를 프리팹 파일로 저장하고 씬 오브젝트를 그 프리팹으로 잇는다. 모습은 그대로 둔다
        public static string SaveAsPrefab(RectTransform rt, string folder)
        {
            MakeScreenContainer(rt);
            string name = string.Join("_", rt.name.Split(Path.GetInvalidFileNameChars())).Trim();
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (name.Length > 0 ? name : "UI") + ".prefab");
            return PrefabUtility.SaveAsPrefabAssetAndConnect(rt.gameObject, path, InteractionMode.UserAction) != null ? path : null;
        }

        // 크기 0인 점에 자식을 모아 둔 묶음(가운데 정렬 그룹 등)은 화면을 꽉 채우게 바꾼다.
        // 크기 0인 부모 안에서는 모든 기준점이 한 점이므로, 자식 기준점을 그 점의 비율 위치로 옮기면 어느 화면 크기에서도 모습이 같다
        public static void MakeScreenContainer(RectTransform rt)
        {
            bool point = rt.anchorMin == rt.anchorMax && rt.sizeDelta.sqrMagnitude < 1f;
            if (!point || rt.localScale != Vector3.one || rt.localRotation != Quaternion.identity)
                return;

            List<RectTransform> children = rt.Cast<Transform>().OfType<RectTransform>().ToList();
            Undo.RecordObjects(children.Cast<Object>().Append(rt).ToArray(), "Make screen container");
            Vector2 anchor = rt.anchorMin;
            Vector2 shift = rt.anchoredPosition;
            foreach (RectTransform child in children)
            {
                Vector2 min = child.offsetMin + shift;
                Vector2 max = child.offsetMax + shift;
                child.anchorMin = anchor;
                child.anchorMax = anchor;
                child.offsetMin = min;
                child.offsetMax = max;
            }
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 새 프리팹을 둘 폴더: 씬에 이미 있는 UI 프리팹의 폴더를 기본으로 묻는다. 취소하면 null
        private static string PickPrefabFolder(List<RectTransform> loose)
        {
            string start = "Assets";
            foreach (RectTransform rt in loose)
            {
                Graphic inPrefab = rt.parent.GetComponentsInChildren<Graphic>(true).FirstOrDefault(g => PrefabUtility.IsPartOfPrefabInstance(g));
                string path = inPrefab != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inPrefab) : null;
                if (!string.IsNullOrEmpty(path))
                {
                    start = Path.GetDirectoryName(path).Replace('\\', '/');
                    break;
                }
            }
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string picked = EditorUtility.SaveFolderPanel(T("Folder for the new prefabs"), Path.Combine(projectRoot, start), "");
            if (string.IsNullOrEmpty(picked))
                return null;
            picked = picked.Replace('\\', '/');
            if (picked != Application.dataPath && !picked.StartsWith(Application.dataPath + "/"))
            {
                EditorUtility.DisplayDialog("Flexible UI", T("Pick a folder inside Assets."), T("OK"));
                return null;
            }
            return "Assets" + picked.Substring(Application.dataPath.Length);
        }

        // 열린 씬의 프리팹 인스턴스(안쪽 프리팹 포함) 중 화면을 꽉 채우는 UI 프리팹으로, 아직 Flexible UI가 아닌 것
        public static List<string> FindScenePrefabsToMake()
        {
            var paths = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(rt.gameObject) || !IsFullScreen(rt))
                            continue;
                        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rt.gameObject);
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (asset == null || !(asset.transform is RectTransform) || paths.Contains(path))
                            continue;
                        var layout = asset.GetComponent<FlexibleLayout>();
                        if (layout == null || layout.Settings == null)
                            paths.Add(path);
                    }
                }
            }
            return paths;
        }

        // 화면을 꽉 채우는지: 화면 공간의 최상위 캔버스까지 올라가며 모두 늘이기(앵커 0~1, 여백 0)인지 본다 (꺼진 오브젝트도 판단 가능)
        public static bool IsFullScreen(RectTransform target)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                var canvas = current.GetComponent<Canvas>();
                if (canvas != null && (current.parent == null || current.parent.GetComponentInParent<Canvas>(true) == null))
                    return canvas.renderMode != RenderMode.WorldSpace;
                if (!(current is RectTransform rt))
                    return false;
                bool stretch = (rt.anchorMin - Vector2.zero).sqrMagnitude < 0.0001f && (rt.anchorMax - Vector2.one).sqrMagnitude < 0.0001f
                    && rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
                if (!stretch)
                    return false;
            }
            return false;
        }

        // 확인을 받고 씬의 UI를 한 번에 바꾼다. 「프리팹까지 모두」 / 「캔버스만」 / 「취소」
        // loose: 프리팹이 아닌 씬 UI. 프리팹으로 저장한 뒤 Flexible UI로 만든다 (folder가 없으면 물어본다)
        public static void SetUpScene(List<Canvas> canvases, List<string> prefabs, List<RectTransform> loose, bool ask, string folder = null)
        {
            loose = loose ?? new List<RectTransform>();
            // 목록은 창의 「목록 보기」에 있다 (확인창은 길면 잘리므로 개수만)
            string text = F("This gets the UI in the open scene ready for portrait / square / landscape: {0} canvases switch to Flexible Canvas Scaler (phones in portrait look the same), and {1} full-screen prefabs become Flexible UI (their current look becomes the portrait layout; the prefab files are changed).\n\nSee \"Show the list\" in the window for the names.", canvases.Count, prefabs.Count);
            if (loose.Count > 0)
                text += "\n\n" + F("{0} UI groups placed straight in the scene are saved as new prefabs (you pick the folder next) and become Flexible UI. They look the same as now.", loose.Count);

            int choice = 0;
            if (ask)
            {
                if ((prefabs.Count > 0 || loose.Count > 0) && canvases.Count > 0)
                    choice = EditorUtility.DisplayDialogComplex("Flexible UI", text, T("Do all"), T("Cancel"), T("Canvases only"));
                else
                    choice = EditorUtility.DisplayDialog("Flexible UI", text, T("Do all"), T("Cancel")) ? 0 : 1;
            }
            if (choice == 1)
                return;
            if (choice == 0 && loose.Count > 0 && folder == null)
            {
                folder = PickPrefabFolder(loose);
                if (folder == null)
                    return;
            }

            // 바꾸기 전에 저장돼 있던 씬만 끝에 저장한다 (다른 미저장 작업을 같이 저장하지 않도록)
            var cleanScenes = new List<UnityEngine.SceneManagement.Scene>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !scene.isDirty && !string.IsNullOrEmpty(scene.path))
                    cleanScenes.Add(scene);
            }

            bool unsaved = canvases.Count > 0 && FlexibleLayoutInspector.ConvertCanvases(canvases);
            int made = 0;
            var newPrefabs = new List<string>();
            if (choice == 0)
            {
                foreach (RectTransform rt in loose)
                {
                    if (rt == null)
                        continue;
                    if (!cleanScenes.Contains(rt.gameObject.scene))
                        unsaved = true;
                    string path = SaveAsPrefab(rt, folder);
                    if (path != null && MakeFlexible(path))
                        newPrefabs.Add(path);
                }
                foreach (string path in prefabs)
                {
                    if (MakeFlexible(path))
                        made++;
                }
            }
            foreach (UnityEngine.SceneManagement.Scene scene in cleanScenes)
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene);
            }
            if (!ask)
                return;

            string done = F("Done: {0} canvases, {1} prefabs.", canvases.Count, made + newPrefabs.Count);
            if (newPrefabs.Count > 0)
                done += "\n" + F("New prefabs in {0}: {1}", folder, string.Join(", ", newPrefabs.Select(Path.GetFileNameWithoutExtension)));
            if (made + newPrefabs.Count > 0)
                done += "\n" + T("Next, open each prefab and make its square and landscape layouts (select it in the scene and press Open).");
            if (unsaved)
                done += "\n" + T("The scene had other unsaved changes, so it was not saved. Save it with Ctrl+S.");
            EditorUtility.DisplayDialog("Flexible UI", done, T("OK"));
        }

        // ───────── 준비 체크리스트 ─────────

        private static void DrawChecklist(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            GUILayout.Label(Path.GetFileNameWithoutExtension(stage.assetPath), FlexibleGui.Title);
            if (!(root.transform is RectTransform rootRect))
            {
                EditorGUILayout.HelpBox(T("This prefab is not UI (its root has no RectTransform), so it cannot be used."), MessageType.Error);
                return;
            }

            GUILayout.Label(T("This prefab is not ready for screen switching yet. Press the button and it is set up automatically. What you see now becomes the portrait (base) layout."), FlexibleGui.WrapLabel);
            var layout = root.GetComponent<FlexibleLayout>();
            // 맨 위가 캔버스면 화면 크기를 그대로 쓰므로 채우기 조건은 맞은 것으로 본다
            var canvas = root.GetComponent<Canvas>();
            bool stretch = canvas != null || IsFullStretch(rootRect);
            // 스케일러가 있는 캔버스(= 화면 최상위로 쓰는 캔버스)만 따진다. 다른 캔버스 안에 들어가는 캔버스는 스케일러가 동작하지 않는다
            CanvasScaler rootScaler = root.GetComponent<CanvasScaler>();
            bool scalerOk = canvas == null || rootScaler == null || rootScaler is FlexibleCanvasScaler;
            CheckRow(layout != null, T("Has the Flexible Layout component"));
            CheckRow(layout != null && layout.Settings != null, T("Uses the project's screen settings"));
            if (canvas != null)
            {
                CheckRow(true, T("It is a canvas, so it fills the screen"));
                if (rootScaler != null)
                    CheckRow(scalerOk, T("The canvas uses Flexible Canvas Scaler (the same size rule on every screen)"));
            }
            else
            {
                CheckRow(stretch, T("Fills the whole screen (anchors stretch, offsets 0)"));
                if (!stretch)
                    GUILayout.Label(T("A panel shown inside another prefab does not have to fill the screen. It still follows the screen (portrait / square / landscape)."), EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button(T("Set up this prefab"), FlexibleGui.BigButton, GUILayout.Height(34f)))
            {
                // 화면을 채우지 않는 UI는 전체 화면인지 패널인지 묻는다 (패널이면 크기를 그대로 둔다)
                bool fit = !stretch && EditorUtility.DisplayDialog(T("Set up this prefab"),
                    T("This prefab does not fill the screen. Is it a full-screen UI?\n\nFull screen: it is stretched to fill the screen.\nPanel: it keeps its size (for a panel shown inside another prefab)."),
                    T("Full screen"), T("Panel"));
                if (layout == null)
                    layout = Undo.AddComponent<FlexibleLayout>(root);
                Undo.RecordObject(layout, "Flexible Setup");
                if (layout.Settings == null)
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                if (fit)
                    FlexibleLayoutInspector.FitToParent(rootRect);
                if (canvas != null && rootScaler != null)
                    FlexibleLayoutInspector.UseFlexibleScaler(canvas);
                EditorUtility.SetDirty(layout);
                FlexibleScreenSession.Save(stage);

                // 안쪽에 전체 화면 프리팹이 있으면 같이 만들지 한 번에 묻는다
                List<string> inner = FindNestedPrefabs(stage).Where(n => !n.flexible && n.fullScreen).Select(n => n.path).ToList();
                if (inner.Count > 0 && EditorUtility.DisplayDialog(T("Set up this prefab"),
                        F("This prefab has {0} full-screen prefabs inside: {1}\nMake them Flexible UI too? Their current look becomes their portrait layout.", inner.Count, string.Join(", ", inner.Select(Path.GetFileNameWithoutExtension))),
                        T("Make them too"), T("Later")))
                    MakeNestedFlexible(stage, layout, inner);
                GUIUtility.ExitGUI();
            }
        }

        // 맨 위가 캔버스인데 일반 CanvasScaler면, 화면마다 크기 규칙이 달라지므로 바꾸게 한다
        private static void DrawScalerCheck(PrefabStage stage)
        {
            var canvas = stage.prefabContentsRoot.GetComponent<Canvas>();
            CanvasScaler scaler = stage.prefabContentsRoot.GetComponent<CanvasScaler>();
            if (canvas == null || scaler == null || scaler is FlexibleCanvasScaler)
                return;

            EditorGUILayout.HelpBox(T("This canvas does not use Flexible Canvas Scaler, so the UI size changes differently from screen to screen (for example much smaller in landscape). Phones in portrait look the same after the change."), MessageType.Warning);
            if (GUILayout.Button(T("Use Flexible Canvas Scaler"), GUILayout.Height(24f)))
            {
                FlexibleLayoutInspector.UseFlexibleScaler(canvas);
                FlexibleScreenSession.Save(stage);
                GUIUtility.ExitGUI();
            }
        }

        private static void CheckRow(bool ok, string text)
        {
            var style = new GUIStyle(EditorStyles.label) { richText = true };
            string mark = ok ? "<color=#4c4>✔</color>" : "<color=#e66>✖</color>";
            GUILayout.Label($"{mark}  {text}", style);
        }

        private static bool IsFullStretch(RectTransform rt)
        {
            return rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
                && rt.offsetMin.sqrMagnitude < 0.0001f && rt.offsetMax.sqrMagnitude < 0.0001f;
        }

        // ───────── 화면 버튼 ─────────

        private static void DrawScreens(PrefabStage stage, FlexibleLayout layout)
        {
            FlexibleProfileSettings settings = layout.Settings;
            string active = FlexibleScreenSession.GetActiveProfileId(stage);
            FlexibleProfile profile = settings.Find(active);
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Path.GetFileNameWithoutExtension(stage.assetPath), FlexibleGui.Title);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("Save (Ctrl+S)"), GUILayout.Height(22f), GUILayout.Width(110f)))
                {
                    FlexibleScreenSession.Save(stage);
                    GUIUtility.ExitGUI();
                }
                FlexibleGui.Mark("save");
            }

            GUILayout.Label(T("Which screen are you editing?"), EditorStyles.miniBoldLabel);
            DrawScreenButtons(stage, layout, 40f);

            EditorGUILayout.Space(2f);
            string message = active == settings.BaseProfileId
                ? F("<b>{0}</b> is the base screen. What you build here is used by every screen. To change only another screen, press its button.", Name(profile))
                : F("You are adjusting only the <b>{0}</b> screen. Move, resize, show or hide blocks freely. Blocks you do not touch stay the same as {1}.", Name(profile), Name(baseProfile));
            GUILayout.Label(message, FlexibleGui.Bubble);
            DrawShapeCheck(stage);
            DrawScalerCheck(stage);
            DrawScreenChanges(stage);

            if (stage.scene.isDirty)
                GUILayout.Label(T("● Unsaved changes. Save with Ctrl+S (switching screens also saves)."), EditorStyles.miniLabel);
        }

        // ───────── 이 화면에서 바뀐 블록·화면 밖 블록 ─────────

        private static bool showChanged;
        private static string infoStage;
        private static double infoAt;
        private static List<RectTransform> changedCache = new List<RectTransform>();
        private static List<RectTransform> outsideCache = new List<RectTransform>();

        // 1초마다 다시 센다 (블록 수가 많아도 창이 무거워지지 않게)
        private static void RefreshScreenInfo(PrefabStage stage)
        {
            if (infoStage == stage.assetPath && EditorApplication.timeSinceStartup - infoAt < 1.0)
                return;
            infoStage = stage.assetPath;
            infoAt = EditorApplication.timeSinceStartup;
            changedCache = FlexibleScreenSession.ChangedBlocks(stage);
            outsideCache.Clear();
            // 기준 화면에서는 경고하지 않고, 세로에서도 화면 밖이던 것(일부러 밖에 둔 것)은 빼고 알린다
            if (!FlexibleScreenSession.IsBaseView(stage))
            {
                var root = (RectTransform)stage.prefabContentsRoot.transform;
                HashSet<string> onBase = BaseOutside(stage);
                outsideCache = FlexiblePreviewRenderer.FindOutside(root).Where(t => !onBase.Contains(FlexiblePathUtility.GetPath(root, t))).ToList();
            }
        }

        private static string baseOutsideKey;
        private static HashSet<string> baseOutside = new HashSet<string>();

        // 저장된 기준(세로) 배치에서 화면 밖에 있는 것의 경로 (저장할 때마다 다시 구한다)
        private static HashSet<string> BaseOutside(PrefabStage stage)
        {
            string key = stage.assetPath + "#" + FlexibleScreenSession.SaveCount;
            if (key == baseOutsideKey)
                return baseOutside;
            baseOutsideKey = key;
            baseOutside = new HashSet<string>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null || layout.Settings == null)
                return baseOutside;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 size = FlexiblePreviewRenderer.GetLogicalSize(baseProfile.workResolution.x / Mathf.Max(1f, baseProfile.workResolution.y), settings.ReferenceShortSide);
            using (var renderer = new FlexiblePreviewRenderer())
            {
                renderer.Prepare(asset, baseProfile.id, size, new Rect(0f, 0f, 1f, 1f), null, FlexibleScreenSession.GetViewByPath(stage));
                var copyRoot = (RectTransform)renderer.Instance.transform;
                foreach (RectTransform t in FlexiblePreviewRenderer.FindOutside(copyRoot))
                    baseOutside.Add(FlexiblePathUtility.GetPath(copyRoot, t));
            }
            return baseOutside;
        }

        public static void RefreshScreenInfoNow()
        {
            infoAt = 0;
        }

        // 화면 밖 경고는 보기 목록 아래에 둔다 (목록에서 블록을 켜고 끄면 경고가 생겼다 없어져, 위에 있으면 목록이 밀린다)
        private static void DrawOutside(PrefabStage stage)
        {
            RefreshScreenInfo(stage);
            outsideCache.RemoveAll(t => t == null);

            if (outsideCache.Count > 0)
            {
                EditorGUILayout.HelpBox(F("{0} blocks stick out of the screen: {1}", outsideCache.Count, string.Join(", ", outsideCache.Take(5).Select(t => t.name)) + (outsideCache.Count > 5 ? ", …" : "")), MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(T("Fit them all into the screen"), GUILayout.Height(24f)))
                    {
                        var root = (RectTransform)stage.prefabContentsRoot.transform;
                        Undo.IncrementCurrentGroup();
                        int group = Undo.GetCurrentGroup();
                        foreach (RectTransform target in outsideCache)
                            FlexibleLayoutInspector.FitToScreen(root, target);
                        Undo.CollapseUndoOperations(group);
                        RefreshScreenInfoNow();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button(T("Select them"), GUILayout.Width(100f), GUILayout.Height(24f)))
                        Selection.objects = outsideCache.Select(t => (Object)t.gameObject).ToArray();
                }
                GUILayout.Label(T("Fitting shrinks each block evenly until it fits, then moves it inside. Check the result and adjust by hand if needed."), EditorStyles.wordWrappedMiniLabel);
            }
            DrawReservedOver(stage);
            DrawExtraIssues(stage);
        }

        // 다른 프리팹이 차지하는 자리(메뉴 바 등)가 이 프리팹 위에 보이는지. 게임이 숨기는 화면이면 끈다 (검사·Scene 표시에서 빠진다)
        private static void DrawReservedOver(PrefabStage stage)
        {
            FlexibleProfileSettings settings = FlexibleReservedAreas.SettingsOf(stage.prefabContentsRoot);
            string guid = AssetDatabase.AssetPathToGUID(stage.assetPath);
            List<FlexibleReservedArea> areas = FlexibleReservedAreas.FromOthers(settings, guid);
            if (areas.Count == 0)
                return;
            foreach (FlexibleReservedArea area in areas)
            {
                bool shown = FlexibleReservedAreas.IsShownOver(area, guid);
                EditorGUI.BeginChangeCheck();
                shown = EditorGUILayout.ToggleLeft(new GUIContent(F("{0} is shown over this prefab", FlexibleReservedAreas.Label(area)),
                    T("Turn it off if the game hides it while this prefab is open (a title screen, a full-screen conversation ...). Then its place is not drawn here and the check stops reporting buttons under it.")), shown);
                if (EditorGUI.EndChangeCheck())
                    FlexibleReservedAreas.SetShownOver(settings, area, guid, shown);
            }
        }

        // ───────── 보이지 않는 곳의 문제 (숨긴 패널·뒤집힌 크기·목록 잘림·다른 프리팹 자리) ─────────

        private const int ExtraShown = 8;
        private static List<FlexibleIssue> extraCache = new List<FlexibleIssue>();
        private static string extraKey;
        private static string extraScope;
        private static double extraAt;
        private static int changeStamp;
        private static double lastChangeAt;
        private static bool changeHooked;
        private static bool repaintScheduled;
        private static string baseExtraKey;
        private static HashSet<string> baseExtra = new HashSet<string>();

        private static string IssueKey(FlexibleIssue issue) => issue.code + "|" + string.Join("|", issue.blocks);

        // 사본을 만들어 검사하므로 무겁다: 내용·화면·보기가 바뀌고 편집이 1초 멈춘 뒤에만 다시 구한다 (끄는 동안 멈칫하지 않게).
        // 목록이 그리는 도중 바뀌지 않도록 Layout 때만 구한다
        private static void RefreshExtraIssues(PrefabStage stage)
        {
            if (!changeHooked)
            {
                changeHooked = true;
                ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
                {
                    changeStamp++;
                    lastChangeAt = EditorApplication.timeSinceStartup;
                };
            }
            if (Event.current != null && Event.current.type != EventType.Layout)
                return;
            if (FlexibleScreenSession.IsBaseView(stage))
            {
                extraCache.Clear();
                extraKey = null;
                return;
            }

            Dictionary<string, bool> view = FlexibleScreenSession.GetViewByPath(stage);
            string profileId = FlexibleScreenSession.GetActiveProfileId(stage);
            // 다른 프리팹·화면으로 바뀌면 이전 결과는 바로 지운다
            string scope = stage.assetPath + "#" + profileId;
            if (scope != extraScope)
            {
                extraScope = scope;
                extraCache.Clear();
                extraAt = 0;
            }
            string key = stage.assetPath + "#" + profileId + "#" + changeStamp + "#" + FlexibleScreenSession.SaveCount + "#"
                + string.Join(",", view.Select(p => p.Key + "=" + p.Value)) + "#"
                + FlexibleReservedAreas.SettingsOf(stage.prefabContentsRoot).ReservedAreas.Sum(a => 1 + (a.hiddenFor != null ? a.hiddenFor.Count * 7 : 0));
            if (key == extraKey)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastChangeAt < 1.0 || now - extraAt < 2.0)
            {
                RepaintLater(1.1);
                return;
            }
            extraKey = key;
            extraAt = EditorApplication.timeSinceStartup;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            HashSet<string> onBase = BaseExtra(stage);
            using (var renderer = new FlexiblePreviewRenderer())
            {
                renderer.Prepare(stage.prefabContentsRoot, null, root.rect.size, new Rect(0f, 0f, 1f, 1f), profileId);
                extraCache = renderer.CollectIssueList(new Rect(0f, 0f, 1f, 1f))
                    .Where(i => FlexibleIssue.IsExtra(i.code) && !onBase.Contains(IssueKey(i))).ToList();
            }
        }

        // 창이 가만히 있어도 잠시 뒤 다시 그려 검사가 돌게 한다
        private static void RepaintLater(double seconds)
        {
            if (repaintScheduled)
                return;
            repaintScheduled = true;
            double at = EditorApplication.timeSinceStartup + seconds;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < at)
                    return;
                EditorApplication.update -= tick;
                repaintScheduled = false;
                foreach (FlexibleWindow window in Resources.FindObjectsOfTypeAll<FlexibleWindow>())
                    window.Repaint();
            };
            EditorApplication.update += tick;
        }

        // 기준(세로) 화면에도 있던 문제는 원래 디자인이라 빼고 알린다 (저장할 때마다 다시 구한다)
        private static HashSet<string> BaseExtra(PrefabStage stage)
        {
            string key = stage.assetPath + "#" + FlexibleScreenSession.SaveCount;
            if (key == baseExtraKey)
                return baseExtra;
            baseExtraKey = key;
            baseExtra = new HashSet<string>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null || layout.Settings == null)
                return baseExtra;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 size = FlexiblePreviewRenderer.GetLogicalSize(baseProfile.workResolution.x / Mathf.Max(1f, baseProfile.workResolution.y), settings.ReferenceShortSide);
            using (var renderer = new FlexiblePreviewRenderer())
            {
                renderer.Prepare(asset, baseProfile.id, size, new Rect(0f, 0f, 1f, 1f));
                foreach (FlexibleIssue issue in renderer.CollectIssueList(new Rect(0f, 0f, 1f, 1f)))
                {
                    if (FlexibleIssue.IsExtra(issue.code))
                        baseExtra.Add(IssueKey(issue));
                }
            }
            return baseExtra;
        }

        private static void DrawExtraIssues(PrefabStage stage)
        {
            RefreshExtraIssues(stage);
            if (extraCache.Count == 0)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(F("⚠ {0} problems you cannot see right now", extraCache.Count), EditorStyles.boldLabel);
                GUILayout.Label(T("Found in panels the game opens later, in lists, and in places other prefabs take. Show a hidden panel to edit it on this screen."), EditorStyles.wordWrappedMiniLabel);
                foreach (FlexibleIssue issue in extraCache.Take(ExtraShown))
                {
                    Transform target = FlexiblePathUtility.FindByPath(root, issue.blocks[0]);
                    Transform hidden = target != null ? LowestHidden(target, root) : null;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(issue.Message, FlexibleGui.WrapLabel);
                        if (hidden != null && GUILayout.Button(new GUIContent(T("Show"), F("Shows \"{0}\" while you edit (not saved).", hidden.name)), EditorStyles.miniButton, GUILayout.Width(56f)))
                        {
                            ShowInView(stage, root, hidden, true);
                            Selection.activeTransform = target;
                        }
                        using (new EditorGUI.DisabledScope(target == null))
                        {
                            if (GUILayout.Button(T("Select"), EditorStyles.miniButton, GUILayout.Width(56f)))
                            {
                                Selection.activeTransform = target;
                                EditorGUIUtility.PingObject(target);
                            }
                        }
                    }
                }
                if (extraCache.Count > ExtraShown)
                    GUILayout.Label(F("… and {0} more. The Check tab lists all of them.", extraCache.Count - ExtraShown), EditorStyles.miniLabel);
            }
        }

        // 블록을 가리고 있는 가장 가까운 숨김 (꺼진 오브젝트 또는 Canvas가 꺼진 패널)
        private static Transform LowestHidden(Transform target, Transform root)
        {
            for (Transform t = target; t != null && t != root; t = t.parent)
            {
                if (!FlexibleViewOverride.IsVisible(t.gameObject))
                    return t;
            }
            return null;
        }

        private static void DrawScreenChanges(PrefabStage stage)
        {
            RefreshScreenInfo(stage);
            changedCache.RemoveAll(t => t == null);

            if (FlexibleScreenSession.IsBaseView(stage))
                return;
            showChanged = EditorGUILayout.Foldout(showChanged, F("Blocks with their own layout on this screen ({0})", changedCache.Count), true);
            if (!showChanged)
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (changedCache.Count == 0)
                    GUILayout.Label(T("None yet. Move, resize or hide a block to give this screen its own layout."), EditorStyles.wordWrappedMiniLabel);
                foreach (RectTransform target in changedCache.ToList())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(target.name, EditorStyles.label))
                        {
                            Selection.activeTransform = target;
                            EditorGUIUtility.PingObject(target.gameObject);
                        }
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(T("Undo this one"), EditorStyles.miniButton, GUILayout.Width(110f)))
                        {
                            FlexibleScreenSession.ResetToBase(stage, target);
                            RefreshScreenInfoNow();
                        }
                    }
                }
            }
        }

        // 화면 버튼 줄 (편집 탭·Scene 뷰 오버레이 공용)
        public static void DrawScreenButtons(PrefabStage stage, FlexibleLayout layout, float height)
        {
            FlexibleProfileSettings settings = layout.Settings;
            string active = FlexibleScreenSession.GetActiveProfileId(stage);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (FlexibleProfile p in settings.Profiles)
                {
                    bool on = p.id == active;
                    // 지금 편집 중인 화면은 저장 전 변경까지 센다
                    string sub = p.id == settings.BaseProfileId ? T("base")
                        : on ? F("{0} changed", LiveChangedCount(stage))
                        : F("{0} changed", FlexibleScreenSession.CountRecords(layout, p.id));
                    var content = new GUIContent($"{Name(p)}\n<size=10>{sub}</size>",
                        F("Edit the {0} screen ({1:0}x{2:0}). Switching screens saves the prefab.", Name(p), p.workResolution.x, p.workResolution.y));
                    Color previous = GUI.backgroundColor;
                    if (on)
                        GUI.backgroundColor = FlexibleGui.Accent;
                    bool pressed = GUILayout.Button(content, on ? FlexibleGui.BigButtonOn : FlexibleGui.BigButton, GUILayout.Height(height), GUILayout.MinWidth(60f));
                    GUI.backgroundColor = previous;
                    FlexibleGui.Mark("screen:" + p.id);
                    if (pressed && !on)
                    {
                        FlexibleScreenSession.ShowProfile(stage, p.id);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private static string liveStage;
        private static double liveAt;
        private static int liveCount;

        private static int LiveChangedCount(PrefabStage stage)
        {
            if (liveStage != stage.assetPath || EditorApplication.timeSinceStartup - liveAt > 1.0)
            {
                liveStage = stage.assetPath;
                liveAt = EditorApplication.timeSinceStartup;
                liveCount = FlexibleScreenSession.ChangedBlocks(stage).Count;
            }
            return liveCount;
        }

        public static string Name(FlexibleProfile profile)
        {
            if (profile == null)
                return "-";
            return string.IsNullOrEmpty(profile.displayName) ? profile.id : T(profile.displayName);
        }

        // ───────── 선택한 블록 ─────────

        private static void DrawSelected(PrefabStage stage, FlexibleLayout layout)
        {
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleGui.Section(T("Selected block"));
            List<RectTransform> targets = GetSelectedBlocks(root);
            if (targets.Count == 0)
            {
                GUILayout.Label(T("Click a block in the Hierarchy or the Scene view."), EditorStyles.wordWrappedMiniLabel);
                return;
            }

            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(targets.Count == 1 ? targets[0].name : F("{0} blocks selected", targets.Count), EditorStyles.boldLabel);
                    // 안쪽 프리팹(또는 그 안)을 골랐으면 그 프리팹을 바로 열 수 있게 한다
                    GameObject instanceRoot = targets.Count == 1 ? PrefabUtility.GetNearestPrefabInstanceRoot(targets[0].gameObject) : null;
                    if (instanceRoot != null)
                    {
                        string nestedPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                        GUILayout.FlexibleSpace();
                        if (!string.IsNullOrEmpty(nestedPath) && GUILayout.Button(F("Open \"{0}\"", Path.GetFileNameWithoutExtension(nestedPath)), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            OpenNestedPrefab(nestedPath, stage);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                if (targets.Count > 1)
                    GUILayout.Label(Shorten(string.Join(", ", targets.Select(t => t.name)), 120), EditorStyles.wordWrappedMiniLabel);

                // 여러 개면 상태가 모두 같을 때만 그 안내를 보이고, 다르면 화면마다 따로 둔 블록 수를 알린다
                List<FlexibleBlockStatus> statuses = targets.Select(t => FlexibleScreenSession.GetStatus(stage, t)).ToList();
                List<RectTransform> own = targets.Where((t, i) => statuses[i] == FlexibleBlockStatus.Own).ToList();
                bool same = statuses.All(s => s == statuses[0]);
                if (same && statuses[0] == FlexibleBlockStatus.BaseScreen)
                    GUILayout.Label(T("Base screen: changes here apply to every screen (except where a screen has its own layout)."), EditorStyles.wordWrappedMiniLabel);
                else if (same && statuses[0] == FlexibleBlockStatus.SameAsBase)
                    GUILayout.Label(F("○ Same as {0}. Move it to give this screen its own layout.", Name(baseProfile)), FlexibleGui.WrapLabel);
                else if (same && statuses[0] == FlexibleBlockStatus.NotSavedYet)
                    GUILayout.Label(T("New block (not saved yet). After saving it appears on every screen."), EditorStyles.wordWrappedMiniLabel);
                else if (own.Count > 0)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string text = own.Count == targets.Count ? T("Has its own layout on this screen") : F("{0} of {1} blocks have their own layout on this screen", own.Count, targets.Count);
                        GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(FlexibleGui.Good)}>●</color> " + text, FlexibleGui.WrapLabel);
                        if (GUILayout.Button(F("Back to {0}", Name(baseProfile)), GUILayout.Width(130f)))
                        {
                            foreach (RectTransform t in own)
                                FlexibleScreenSession.ResetToBase(stage, t);
                        }
                    }
                }

                DrawRolePopup(layout, targets, settings);
                DrawTransformFields(stage, targets);
                if (targets.Count == 1)
                    DrawReservedToggle(stage, targets[0]);
            }
        }

        // 메뉴 바처럼 다른 화면 위에 늘 떠 있는 블록: 다른 프리팹을 편집·검수할 때 피해야 할 자리로 등록한다
        private static void DrawReservedToggle(PrefabStage stage, RectTransform target)
        {
            bool on = FlexibleReservedAreas.Find(stage, target) != null;
            EditorGUI.BeginChangeCheck();
            on = EditorGUILayout.ToggleLeft(new GUIContent(T("Other prefabs keep clear of this"),
                T("For a bar that stays on top of other screens, such as a menu bar. While you edit other prefabs, its place is drawn in the Scene view, and the check and the AI keep buttons out of it.")), on);
            FlexibleGui.Mark("reserved");
            if (EditorGUI.EndChangeCheck())
                FlexibleReservedAreas.Set(stage, target, on);
        }

        // 지금 화면에서의 위치·크기·배율 (Inspector로 가지 않고 바로 고친다). 여러 개를 고르면 값이 다른 칸은 「—」
        private static void DrawTransformFields(PrefabStage stage, List<RectTransform> targets)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Pos X"), T("Pos Y"), T("Width"), T("Height"), T("Scale"));
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Pos X"), targets, t => t.anchoredPosition.x, (t, v) => t.anchoredPosition = new Vector2(v, t.anchoredPosition.y));
                MixedFloat(T("Pos Y"), targets, t => t.anchoredPosition.y, (t, v) => t.anchoredPosition = new Vector2(t.anchoredPosition.x, v));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Width"), targets, t => t.rect.width, (t, v) => t.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, v)));
                MixedFloat(T("Height"), targets, t => t.rect.height, (t, v) => t.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, v)));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Scale"), targets, t => t.localScale.x, (t, v) => t.localScale = new Vector3(v, v, t.localScale.z));
                var root = (RectTransform)stage.prefabContentsRoot.transform;
                bool anyOutside = targets.Any(t => FlexiblePreviewRenderer.FindOutside(root, new List<RectTransform> { t }).Count > 0);
                using (new EditorGUI.DisabledScope(!anyOutside))
                {
                    if (GUILayout.Button(new GUIContent(T("Fit into the screen"), T("Shrinks the block evenly until it fits the screen, then moves it inside.")), EditorStyles.miniButton, GUILayout.Width(130f)))
                    {
                        Undo.IncrementCurrentGroup();
                        int group = Undo.GetCurrentGroup();
                        foreach (RectTransform target in targets)
                            FlexibleLayoutInspector.FitToScreen(root, target);
                        Undo.CollapseUndoOperations(group);
                        RefreshScreenInfoNow();
                    }
                }
            }
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private static void MixedFloat(string label, List<RectTransform> targets, System.Func<RectTransform, float> get, System.Action<RectTransform, float> set)
        {
            float first = get(targets[0]);
            EditorGUI.showMixedValue = targets.Exists(t => Mathf.Abs(get(t) - first) > 0.001f);
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.DelayedFloatField(label, Mathf.Round(first * 100f) / 100f);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck())
                return;
            Undo.RecordObjects(targets.Cast<Object>().ToArray(), "Flexible Edit Block");
            foreach (RectTransform target in targets)
            {
                set(target, value);
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
            RefreshScreenInfoNow();
            SceneView.RepaintAll();
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static void DrawRolePopup(FlexibleLayout layout, List<RectTransform> targets, FlexibleProfileSettings settings)
        {
            var options = new List<string> { T("(none)") };
            options.AddRange(settings.Roles);
            List<string> roles = targets.Select(t => layout.GetRole(t)).ToList();
            string current = roles[0];
            int index = string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
            if (index < 0)
            {
                options.Add(current);
                index = options.Count - 1;
            }

            EditorGUI.showMixedValue = roles.Any(r => (r ?? "") != (current ?? ""));
            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.Popup(new GUIContent(T("Role (optional)"), T("Used by templates and the check tab. Header, Main, Footer ...")), index, options.ToArray());
            EditorGUI.showMixedValue = false;
            FlexibleGui.Mark("role");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Role");
                foreach (RectTransform t in targets)
                    layout.SetRole(t, index == 0 ? null : options[index]);
                EditorUtility.SetDirty(layout);
            }
        }

        // ───────── 그리드 ─────────

        private void DrawGrid(PrefabStage stage, FlexibleLayout layout)
        {
            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            showGrid = EditorGUILayout.Foldout(showGrid, T("Place on a grid"), true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            FlexibleGui.Mark("grid");
            if (!showGrid)
                return;

            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            FlexibleProfile profile = settings.Profiles[profileIndex];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Select a block, then drag over the blue cells in the Scene view. The block snaps to those cells."), EditorStyles.wordWrappedMiniLabel);
                DrawGridSettings(layout, profile);
                // 그리드를 바꿨을 수 있으므로 칸 계산은 그 뒤에 한다
                var context = new FlexibleGridContext(root, layout.GetGrid(profile.id), settings.GetSafeInsets(profileIndex));
                List<BlockInput> inputs = context.grid.IsValid
                    ? GetSelectedBlocks(root).Select(t => GetInput(t, session, context)).ToList()
                    : new List<BlockInput>();
                DrawStalePlacements(context, session);
                DrawPlacement(context, session, inputs);
            }
        }

        // 프리팹 모양은 Game 뷰 해상도를 따른다. 지금 화면과 모양이 다르면 알리고 맞추는 버튼을 준다
        private static void DrawShapeCheck(PrefabStage stage)
        {
            if (FlexibleScreenSession.IsShapeMatching(stage, out FlexibleProfile profile))
                return;

            EditorGUILayout.HelpBox(F("The prefab is not drawn in a {0} shape, because Unity's Game view has a different size. Press the button to set it to {1:0}x{2:0}.", Name(profile), profile.workResolution.x, profile.workResolution.y), MessageType.Warning);
            if (GUILayout.Button(T("Fix the Game view size"), GUILayout.Height(26f)))
                FlexibleScreenSession.SelectWorkResolution(profile);
        }

        // 전용 그리드를 켜면 이 프리팹의 화면별 그리드를 고칠 수 있고, 끄면 프로젝트 그리드를 읽기 전용으로 보여 준다
        private void DrawGridSettings(FlexibleLayout layout, FlexibleProfile profile)
        {
            EditorGUI.BeginChangeCheck();
            bool own = EditorGUILayout.ToggleLeft(T("Use a grid only for this prefab"), layout.UseOwnGrid);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Own Grid");
                layout.UseOwnGrid = own;
                if (own)
                    layout.FillOwnGrids();
                EditorUtility.SetDirty(layout);
                SceneView.RepaintAll();
            }
            FlexibleGui.Mark("grid:own");

            FlexibleGrid grid = layout.GetGrid(profile.id);
            using (new EditorGUI.DisabledScope(!layout.UseOwnGrid))
            {
                if (FlexibleGui.GridFields(ref grid))
                {
                    Undo.RecordObject(layout, "Flexible Own Grid");
                    layout.SetOwnGrid(profile.id, grid);
                    EditorUtility.SetDirty(layout);
                    SceneView.RepaintAll();
                }
            }
            FlexibleGui.Mark("grid:size");

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(layout.UseOwnGrid
                    ? F("This prefab's own grid for the {0} screen. Other prefabs are not affected.", Name(profile))
                    : F("Project grid for the {0} screen, used by every prefab without its own grid. Change it in Project grids, or turn on the option above.", Name(profile)), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(T("Project grids..."), EditorStyles.miniButton, GUILayout.Width(110f)))
                    FlexibleProjectGridWindow.Open();
            }

            EditorGUI.BeginChangeCheck();
            dragPlacement = EditorGUILayout.ToggleLeft(T("Place by dragging in the Scene view"), dragPlacement);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
        }

        private static void DrawStalePlacements(FlexibleGridContext context, FlexibleWorkspace session)
        {
            var stale = FlexibleGridPlacementService.FindStale(context, session);
            if (stale.Count == 0)
                return;

            EditorGUILayout.HelpBox(F("{0} grid blocks would move if placed again with the current grid or screen size. (Manually adjusted blocks are excluded.)", stale.Count), MessageType.Info);
            if (GUILayout.Button(F("Re-place grid blocks ({0})", stale.Count)))
                FlexibleGridPlacementService.ReapplyStale(session, stale);
        }

        private void DrawPlacement(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs)
        {
            if (!context.grid.IsValid)
            {
                EditorGUILayout.HelpBox(T("Grid columns and rows must be 1 or more."), MessageType.Error);
                return;
            }
            if (inputs.Count == 0)
            {
                GUILayout.Label(T("Select the block to place."), EditorStyles.miniBoldLabel);
                return;
            }

            // 블록마다 지금 상태 한 줄 (많으면 앞의 몇 개만)
            const int MaxLines = 5;
            foreach (BlockInput input in inputs.Take(MaxLines))
                EditorGUILayout.LabelField(F("{0}: {1}", input.target.name, Describe(input.placement)), EditorStyles.wordWrappedMiniLabel);
            if (inputs.Count > MaxLines)
                GUILayout.Label(F("... and {0} more", inputs.Count - MaxLines), EditorStyles.miniLabel);

            // 값을 바꾸면 고른 블록 전부에 바로 적용한다 (숫자는 입력을 마쳤을 때). 블록마다 값이 다르면 「—」
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Column"), T("Row"), T("Width (cells)"), T("Height (cells)"), T("Horizontal"), T("Vertical"));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (MixedIntField(T("Column"), inputs, i => i.cell.column, out int column))
                    PlaceAll(context, session, inputs, i => { i.cell.column = column; return i; });
                if (MixedIntField(T("Row"), inputs, i => i.cell.row, out int row))
                    PlaceAll(context, session, inputs, i => { i.cell.row = row; return i; });
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (MixedIntField(T("Width (cells)"), inputs, i => i.cell.width, out int width))
                    PlaceAll(context, session, inputs, i => { i.cell.width = width; return i; });
                if (MixedIntField(T("Height (cells)"), inputs, i => i.cell.height, out int height))
                    PlaceAll(context, session, inputs, i => { i.cell.height = height; return i; });
            }
            using (new EditorGUILayout.VerticalScope())
            {
                if (MixedModeField(T("Horizontal"), ModeLabelsX, inputs, i => i.modeX, out FlexibleAxisMode modeX))
                    PlaceAll(context, session, inputs, i => { i.modeX = modeX; return i; });
                if (MixedModeField(T("Vertical"), ModeLabelsY, inputs, i => i.modeY, out FlexibleAxisMode modeY))
                    PlaceAll(context, session, inputs, i => { i.modeY = modeY; return i; });
            }
            FlexibleGui.Mark("grid:mode");

            // 크기: 크기를 지키는 축은 칸 크기를 쓰므로, 세로 화면 크기로 되돌리거나 칸에 맞춰 줄인다
            using (new EditorGUILayout.HorizontalScope())
            {
                bool anyFixed = inputs.Exists(i => FlexibleGridMath.ResolveX(i.modeX, i.cell, context.grid) != FlexibleAxisMode.Stretch
                    || FlexibleGridMath.ResolveY(i.modeY, i.cell, context.grid) != FlexibleAxisMode.Stretch);
                using (new EditorGUI.DisabledScope(!anyFixed))
                {
                    if (GUILayout.Button(new GUIContent(T("Use its own size"), T("Sticking to an edge or centering keeps the size of the cells. This puts back the size the block has on the portrait screen.")), EditorStyles.miniButton))
                        PlaceAll(context, session, inputs, i => { i.ownSize = true; return i; });
                }
                bool shownFit = inputs[0].fit;
                EditorGUI.showMixedValue = inputs.Exists(i => i.fit != shownFit);
                EditorGUI.BeginChangeCheck();
                bool fit = EditorGUILayout.ToggleLeft(new GUIContent(T("Shrink to fit the cells"),
                    T("Keeps the portrait size and shape, and scales the block down on every screen size so it fits the cells (big pictures, popups, button rows).")), shownFit);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                    PlaceAll(context, session, inputs, i => { i.fit = fit; i.ownSize = fit; return i; });
            }

            // 모서리에 붙이기: 안전 영역의 그 모서리에서 세로 화면과 같은 거리·크기
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(T("Pin to"), T("Puts the block at the same distance from that corner or edge of the safe area as on the portrait screen, with its portrait size. Good for bars, corner buttons and centered popups.")), GUILayout.Width(EditorGUIUtility.labelWidth));
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(84f)))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            for (int col = 0; col < 3; col++)
                            {
                                int index = row * 3 + col;
                                if (GUILayout.Button(new GUIContent(PinLabels[index], Pins[index]), EditorStyles.miniButton, GUILayout.Width(26f)))
                                    PinAll(context, session, inputs, Pins[index]);
                            }
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }

            // SafeArea도 블록마다의 값이다. 바꾸면 고른 블록 전부를 그 값으로 바로 다시 놓는다 (칸에 안 놓인 블록은 지금 덮고 있는 칸에)
            bool shownSafe = inputs[0].insideSafeArea;
            EditorGUI.showMixedValue = inputs.Exists(i => i.insideSafeArea != shownSafe);
            EditorGUI.BeginChangeCheck();
            bool inside = EditorGUILayout.ToggleLeft(new GUIContent(T("Keep the selected blocks inside the safe area (red = notch / home bar)"),
                T("Turn it off for backgrounds and bars that should reach the screen edge.") + "\n"
                + F("SafeArea insets of the reference device: left {0:P1} · right {1:P1} · top {2:P1} · bottom {3:P1}", context.safeInsets.x, context.safeInsets.y, context.safeInsets.z, context.safeInsets.w)), shownSafe);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                // 마지막으로 고른 값은 다음에 새로 놓는 블록의 기본값이 된다
                insideSafeArea = inside;
                EditorPrefs.SetBool(InsideSafeAreaKey, inside);
                PlaceAll(context, session, inputs, i => { i.insideSafeArea = inside; return i; });
            }
            EditorGUIUtility.labelWidth = labelWidth;
            GUILayout.Label(inputs.Count == 1
                ? T("Changes here move the selected block right away. Save keeps them.")
                : T("Changes here apply to all selected blocks right away. Save keeps them. Dragging in the Scene view works with one block at a time."), EditorStyles.wordWrappedMiniLabel);
        }

        private static string Describe(FlexibleGridPlacement placement)
        {
            if (placement == null)
                return T("Not placed on the grid");
            string where = placement.insideSafeArea ? T(", inside SafeArea") : "";
            if (FlexibleWorkspaceService.IsUntouchedPlacement(placement))
                return F("Grid block {0}{1}", placement.cell, where);
            if (FlexibleGridPlacementService.TryGetAdjustment(placement, out FlexibleGridAdjustment adjustment))
                return F("Grid {0}{1} + adjustment ({2})", placement.cell, where, adjustment);
            return F("Manually adjusted (anchors or pivot changed from grid {0}{1}; excluded from templates)", placement.cell, where);
        }

        // ───────── 템플릿 ─────────

        private void DrawTemplates(PrefabStage stage, FlexibleLayout layout)
        {
            showTemplates = EditorGUILayout.Foldout(showTemplates, T("Templates"), true, EditorStyles.foldoutHeader);
            FlexibleGui.Mark("templates");
            if (!showTemplates)
                return;

            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            FlexibleProfile profile = settings.Profiles[profileIndex];
            var context = new FlexibleGridContext(root, layout.GetGrid(profile.id), settings.GetSafeInsets(profileIndex));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(F("Save where each role (Header, Main, ...) sits on the grid of the {0} screen, and reuse it on other prefabs.", Name(profile)), EditorStyles.wordWrappedMiniLabel);
                // 템플릿은 칸 번호로 저장되므로, 전용 그리드를 쓰면 다른 프리팹과 크기가 달라질 수 있다
                if (layout.UseOwnGrid)
                    EditorGUILayout.HelpBox(T("This prefab uses its own grid. Templates keep cell numbers, so blocks may come out a different size than in other prefabs."), MessageType.Info);
                List<FlexibleTemplate> templates = settings.Templates.Where(t => t.profileId == profile.id).ToList();
                if (templates.Count > 0)
                {
                    templateIndex = Mathf.Clamp(templateIndex, 0, templates.Count - 1);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        templateIndex = EditorGUILayout.Popup(templateIndex, templates.Select(t => F("{0} ({1} slots)", t.name, t.slots.Count)).ToArray());
                        if (GUILayout.Button(T("Apply"), GUILayout.Width(60)))
                        {
                            TemplateApplyCount++;
                            ApplyTemplate(context, session, layout, templates[templateIndex]);
                        }
                        FlexibleGui.Mark("template:apply");
                        if (GUILayout.Button(T("Delete"), GUILayout.Width(60))
                            && EditorUtility.DisplayDialog(T("Delete template"), F("Delete the template '{0}'? (shared by the project)", templates[templateIndex].name), T("Delete"), T("Cancel")))
                        {
                            FlexibleGridPlacementService.DeleteTemplate(settings, templates[templateIndex]);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(T("No templates for this screen yet."), EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    templateName = EditorGUILayout.TextField(templateName);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(templateName)))
                    {
                        if (GUILayout.Button(T("Save current layout as template"), GUILayout.Width(210)))
                            SaveTemplate(session, layout, profile, settings);
                    }
                    FlexibleGui.Mark("template:save");
                }
            }
        }

        private void SaveTemplate(FlexibleWorkspace session, FlexibleLayout layout, FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            var skipped = new List<string>();
            FlexibleTemplate template = FlexibleGridPlacementService.CreateTemplate(session, layout, profile.id, templateName.Trim(), skipped);
            string skippedText = skipped.Count > 0 ? "\n\n" + T("Excluded blocks:") + "\n· " + string.Join("\n· ", skipped) : "";
            if (template.slots.Count == 0)
            {
                EditorUtility.DisplayDialog(T("Save template"), T("Nothing to save. Only blocks that have a role and were placed on the grid go into a template.") + skippedText, T("OK"));
                return;
            }

            FlexibleGridPlacementService.SaveTemplate(settings, template);
            IEnumerable<string> saved = template.slots.Select(s => s.HasAdjustment ? F("{0} (with adjustment)", s.role) : s.role);
            EditorUtility.DisplayDialog(T("Save template"), F("Saved {1} roles to '{0}'.", template.name, template.slots.Count) + $"\n{string.Join(", ", saved)}{skippedText}", T("OK"));
            templateName = "";
        }

        private static void ApplyTemplate(FlexibleGridContext context, FlexibleWorkspace session, FlexibleLayout layout, FlexibleTemplate template)
        {
            var missing = new List<string>();
            int applied = FlexibleGridPlacementService.ApplyTemplate(context, session, layout, template, missing);
            string message = F("Placed {0} blocks.", applied);
            if (missing.Count > 0)
                message += "\n" + F("Roles missing in this prefab: {0}", string.Join(", ", missing));
            EditorUtility.DisplayDialog(T("Apply template"), message, T("OK"));
        }

        // ───────── 그 밖 ─────────

        private void DrawMore(PrefabStage stage, FlexibleLayout layout)
        {
            showMore = EditorGUILayout.Foldout(showMore, T("More"), true, EditorStyles.foldoutHeader);
            if (!showMore)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                FlexibleScreenSession.AutoGameView = EditorGUILayout.ToggleLeft(T("Change the Game view size when switching screens"), FlexibleScreenSession.AutoGameView);

                FlexibleProfileSettings settings = layout.Settings;
                foreach (FlexibleProfile p in settings.Profiles)
                {
                    if (p.id == settings.BaseProfileId)
                        continue;
                    int count = FlexibleScreenSession.CountRecords(layout, p.id);
                    using (new EditorGUI.DisabledScope(count == 0))
                    {
                        if (GUILayout.Button(F("Undo all {0} changes ({1} blocks)", Name(p), count))
                            && EditorUtility.DisplayDialog(T("Undo all changes"), F("Make every block of the {0} screen the same as {1} again? This cannot be undone.", Name(p), Name(settings.Find(settings.BaseProfileId))), T("Yes"), T("Cancel")))
                        {
                            FlexibleScreenSession.ClearProfile(stage, p.id);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (GUILayout.Button(T("Select the screen settings asset")))
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }
        }

        // ───────── 옛 작업 프리팹 ─────────

        private static void DrawLegacyWorkspace(PrefabStage stage)
        {
            var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            string sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);
            GUILayout.Label(T("Old work copy"), FlexibleGui.Title);
            GUILayout.Label(F("This is a work copy made by an older version of the tool ({0} screen of {1}). Now you edit the original prefab directly, so this copy is no longer needed.",
                workspace.profileId, Path.GetFileNameWithoutExtension(sourcePath)), FlexibleGui.WrapLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Apply it to the original and open the original"), FlexibleGui.BigButton, GUILayout.Height(32f)))
                {
                    string path = stage.assetPath;
                    if (stage.scene.isDirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, path);
                        stage.ClearDirtiness();
                    }
                    StageUtility.GoToMainStage();
                    ApplyLegacy(path, true);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(T("Discard it"), GUILayout.Height(32f), GUILayout.Width(100f))
                    && EditorUtility.DisplayDialog(T("Discard"), T("Delete this old work copy? Changes in it are lost."), T("Delete"), T("Cancel")))
                {
                    FlexibleWorkspaceService.Delete(stage.assetPath);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 원본을 열었는데 옛 버전의 작업 프리팹이 남아 있으면 알려 준다
        private static void DrawPendingLegacy(PrefabStage stage)
        {
            List<string> pending = FindLegacyWorkspaces(stage.assetPath);
            if (pending.Count == 0)
                return;

            EditorGUILayout.HelpBox(F("Unfinished work from an older version of the tool: {0}", string.Join(", ", pending.Select(LegacyProfileId))), MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Apply it")))
                {
                    if (FlexibleScreenSession.ShowProfile(stage, FlexibleScreenSession.GetActiveProfileId(stage)))
                    {
                        StageUtility.GoToMainStage();
                        foreach (string path in pending)
                            ApplyLegacy(path, false);
                        PrefabStageUtility.OpenPrefab(stage.assetPath);
                    }
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(T("Discard it"))
                    && EditorUtility.DisplayDialog(T("Discard"), T("Delete the old work copies? Changes in them are lost."), T("Delete"), T("Cancel")))
                {
                    foreach (string path in pending)
                        AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private static string LegacyProfileId(string workspacePath)
        {
            FlexibleWorkspace workspace = FlexibleWorkspaceService.LoadWorkspaceComponent(workspacePath);
            return workspace != null ? workspace.profileId : Path.GetFileNameWithoutExtension(workspacePath);
        }

        private static List<string> FindLegacyWorkspaces(string sourcePath)
        {
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(FlexibleWorkspaceService.WorkspaceFolder))
                return new List<string>();
            return AssetDatabase.FindAssets("t:Prefab", new[] { FlexibleWorkspaceService.WorkspaceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !Path.GetFileName(p).StartsWith("_cli_") && p.EndsWith("__" + guid.Substring(0, 8) + ".prefab"))
                .ToList();
        }

        private static void ApplyLegacy(string workspacePath, bool openSource)
        {
            FlexibleCommitReport report = FlexibleWorkspaceService.Commit(workspacePath);
            if (report.success)
                FlexibleWorkspaceService.Delete(workspacePath);

            var message = new StringBuilder();
            message.AppendLine(report.success ? F("Applied the {0} screen to {1}.", report.profileId, report.sourcePath) : T("Could not apply. The old work copy was kept."));
            foreach (string warning in report.warnings.Take(10))
                message.AppendLine("· " + warning);
            EditorUtility.DisplayDialog(T("Old work copy"), message.ToString(), T("OK"));
            if (report.success && openSource)
                PrefabStageUtility.OpenPrefab(report.sourcePath);
        }

        public static RectTransform GetSelectedBlock(RectTransform root)
        {
            var target = Selection.activeTransform as RectTransform;
            return target != null && target != root && target.IsChildOf(root) ? target : null;
        }

        // 고른 블록 전부 (프리팹 루트와 프리팹 밖 오브젝트는 뺀다), Hierarchy 순서
        public static List<RectTransform> GetSelectedBlocks(RectTransform root)
        {
            var picked = new HashSet<RectTransform>();
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go != null && go.transform is RectTransform rt && rt != root && rt.IsChildOf(root))
                    picked.Add(rt);
            }
            if (picked.Count <= 1)
                return picked.ToList();
            return root.GetComponentsInChildren<RectTransform>(true).Where(picked.Contains).ToList();
        }

        // ───────── Scene 뷰 그리드 ─────────

        public void OnSceneGUI(SceneView sceneView)
        {
            if (!showGrid)
                return;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
                return;
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            if (profileIndex < 0 || !layout.GetGrid(settings.Profiles[profileIndex].id).IsValid)
                return;

            var context = new FlexibleGridContext(root, layout.GetGrid(settings.Profiles[profileIndex].id), settings.GetSafeInsets(profileIndex));
            FlexibleGrid grid = context.grid;
            List<RectTransform> targets = GetSelectedBlocks(root);
            // 드래그는 블록 하나일 때만 (여러 블록을 같은 칸에 겹쳐 놓는 일은 없으므로)
            BlockInput? single = targets.Count == 1 ? GetInput(targets[0], session, context) : (BlockInput?)null;
            Rect area = context.GetArea(single?.insideSafeArea ?? insideSafeArea);
            DrawUnsafeArea(root, root.rect, context.GetArea(true));
            for (int r = 0; r < grid.rows; r++)
            for (int c = 0; c < grid.columns; c++)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, new FlexibleCell(c, r, 1, 1)),
                    new Color(0.2f, 0.6f, 1f, 0.05f), new Color(0.2f, 0.6f, 1f, 0.5f));

            // 주황 = 드래그 중인 범위, 아니면 선택 블록들이 이 화면에서 놓인 칸 (칸에 놓은 적 없으면 그리지 않는다)
            var orangeFill = new Color(1f, 0.6f, 0.1f, 0.15f);
            var orangeLine = new Color(1f, 0.6f, 0.1f, 1f);
            if (dragging)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, dragCell.Clamped(grid)), orangeFill, orangeLine);
            else
            {
                foreach (RectTransform target in targets)
                {
                    FlexibleGridPlacement placed = session.FindPlacement(target);
                    if (placed != null)
                        DrawRect(root, FlexibleGridMath.GetSlotRect(context.GetArea(placed.insideSafeArea), grid, placed.cell.Clamped(grid)), orangeFill, orangeLine);
                }
            }

            if (dragPlacement && single.HasValue)
                HandleDrag(context, session, area, single.Value);
            else
                dragging = false;
        }

        private void HandleDrag(FlexibleGridContext context, FlexibleWorkspace session, Rect area, BlockInput input)
        {
            RectTransform root = context.root;
            FlexibleGrid grid = context.grid;
            int id = GUIUtility.GetControlID(DragHint, FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && TryPickCell(root, area, grid, e.mousePosition, out Vector2Int start))
                    {
                        GUIUtility.hotControl = id;
                        dragging = true;
                        dragStart = start;
                        dragCell = FlexibleCell.FromCorners(start, start);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        if (TryPickCell(root, area, grid, e.mousePosition, out Vector2Int current))
                            dragCell = FlexibleCell.FromCorners(dragStart, current);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        dragging = false;
                        e.Use();
                        // 드래그로 칸을 새로 정하면 손으로 옮긴 보정은 버린다 (모드·SafeArea는 유지)
                        FlexibleGridPlacementService.Apply(context, session, input.target, dragCell, input.modeX, input.modeY, input.insideSafeArea, default, input.fit);
                    }
                    break;
            }
        }

        private static bool TryPickCell(RectTransform root, Rect area, FlexibleGrid grid, Vector2 mousePosition, out Vector2Int picked)
        {
            picked = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var plane = new Plane(root.forward, root.position);
            if (!plane.Raycast(ray, out float distance))
                return false;

            Vector2 local = root.InverseTransformPoint(ray.GetPoint(distance));
            return root.rect.Contains(local) && FlexibleGridMath.TryGetCell(area, grid, local, out picked);
        }

        // 대표 기기 SafeArea 밖(노치·홈 인디케이터 자리)을 붉게 칠한다
        private static void DrawUnsafeArea(RectTransform root, Rect full, Rect safe)
        {
            var fill = new Color(1f, 0.2f, 0.2f, 0.18f);
            var none = new Color(0f, 0f, 0f, 0f);
            if (safe.yMax < full.yMax)
                DrawRect(root, Rect.MinMaxRect(full.xMin, safe.yMax, full.xMax, full.yMax), fill, none);
            if (safe.yMin > full.yMin)
                DrawRect(root, Rect.MinMaxRect(full.xMin, full.yMin, full.xMax, safe.yMin), fill, none);
            if (safe.xMin > full.xMin)
                DrawRect(root, Rect.MinMaxRect(full.xMin, safe.yMin, safe.xMin, safe.yMax), fill, none);
            if (safe.xMax < full.xMax)
                DrawRect(root, Rect.MinMaxRect(safe.xMax, safe.yMin, full.xMax, safe.yMax), fill, none);
        }

        private static void DrawRect(RectTransform root, Rect rect, Color fill, Color outline)
        {
            var corners = new Vector3[]
            {
                root.TransformPoint(new Vector3(rect.xMin, rect.yMin)),
                root.TransformPoint(new Vector3(rect.xMin, rect.yMax)),
                root.TransformPoint(new Vector3(rect.xMax, rect.yMax)),
                root.TransformPoint(new Vector3(rect.xMax, rect.yMin)),
            };
            Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
        }
    }
}
