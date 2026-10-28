using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
        private static void Place(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input)
        {
            FlexibleGridAdjustment adjustment = default;
            if (input.placement != null)
                FlexibleGridPlacementService.TryGetAdjustment(input.placement, out adjustment);
            FlexibleGridPlacementService.Apply(context, session, input.target, input.cell.Clamped(context.grid), input.modeX, input.modeY, input.insideSafeArea, adjustment);
        }

        // 고른 블록 전부에 한 번에 적용하고 Undo 한 번으로 묶는다
        private static void PlaceAll(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs, System.Func<BlockInput, BlockInput> change)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (BlockInput input in inputs)
                Place(context, session, change(input));
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

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
            FlexibleAiPanel.DrawLauncher(stage, layout);
            DrawPendingLegacy(stage);
            DrawSelected(stage, layout);
            DrawGrid(stage, layout);
            DrawTemplates(stage, layout);
            DrawMore(stage, layout);
        }

        // ───────── 프리팹이 열려 있지 않을 때 ─────────

        private static void DrawNoPrefab()
        {
            if (FlexibleTutorial.Active || FlexibleAiRun.Current == FlexibleAiRun.State.Running)
                return;

            EditorGUILayout.Space(4f);
            GUILayout.Label(T("Open a UI prefab"), FlexibleGui.Title);
            GUILayout.Label(T("Double-click a UI prefab in the Project window. It opens in Prefab Mode, and the screen buttons (Portrait / Square / Landscape) appear here."), FlexibleGui.WrapLabel);

            var selected = Selection.activeObject as GameObject;
            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : null;
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
            {
                EditorGUILayout.Space(4f);
                if (GUILayout.Button(F("Open \"{0}\"", Path.GetFileNameWithoutExtension(path)), FlexibleGui.BigButton, GUILayout.Height(34f)))
                {
                    PrefabStageUtility.OpenPrefab(path);
                    GUIUtility.ExitGUI();
                }
            }
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

            GUILayout.Label(T("This prefab is not ready for screen switching yet. Press the button and it is set up automatically."), FlexibleGui.WrapLabel);
            var layout = root.GetComponent<FlexibleLayout>();
            bool stretch = IsFullStretch(rootRect);
            CheckRow(layout != null, T("Has the Flexible Layout component"));
            CheckRow(layout != null && layout.Settings != null, T("Uses the project's screen settings"));
            CheckRow(stretch, T("Fills the whole screen (anchors stretch, offsets 0)"));

            EditorGUILayout.Space(4f);
            if (GUILayout.Button(T("Set up this prefab"), FlexibleGui.BigButton, GUILayout.Height(34f)))
            {
                if (layout == null)
                    layout = Undo.AddComponent<FlexibleLayout>(root);
                Undo.RecordObject(layout, "Flexible Setup");
                if (layout.Settings == null)
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                if (!stretch)
                    FlexibleLayoutInspector.FitToParent(rootRect);
                EditorUtility.SetDirty(layout);
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

            if (stage.scene.isDirty)
                GUILayout.Label(T("● Unsaved changes. Save with Ctrl+S (switching screens also saves)."), EditorStyles.miniLabel);
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
                    string sub = p.id == settings.BaseProfileId
                        ? T("base")
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
                GUILayout.Label(targets.Count == 1 ? targets[0].name : F("{0} blocks selected", targets.Count), EditorStyles.boldLabel);
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
            }
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
            var context = new FlexibleGridContext(root, profile.grid, settings.GetSafeInsets(profileIndex));

            List<BlockInput> inputs = context.grid.IsValid
                ? GetSelectedBlocks(root).Select(t => GetInput(t, session, context)).ToList()
                : new List<BlockInput>();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Select a block, then drag over the blue cells in the Scene view. The block snaps to those cells."), EditorStyles.wordWrappedMiniLabel);
                DrawGridSettings(settings, profileIndex, context, session, inputs);
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

        private void DrawGridSettings(FlexibleProfileSettings settings, int profileIndex, FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs)
        {
            var serialized = new SerializedObject(settings);
            SerializedProperty grid = serialized.FindProperty("profiles").GetArrayElementAtIndex(profileIndex).FindPropertyRelative("grid");

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Columns"), T("Rows"), T("Margin"), T("Gutter"));
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(grid.FindPropertyRelative("columns"), new GUIContent(T("Columns")));
                    EditorGUILayout.PropertyField(grid.FindPropertyRelative("rows"), new GUIContent(T("Rows")));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(grid.FindPropertyRelative("margin"), new GUIContent(T("Margin")));
                    EditorGUILayout.PropertyField(grid.FindPropertyRelative("gutter"), new GUIContent(T("Gutter")));
                }
            }
            FlexibleGui.Mark("grid:size");
            if (EditorGUI.EndChangeCheck())
            {
                grid.FindPropertyRelative("columns").intValue = Mathf.Max(1, grid.FindPropertyRelative("columns").intValue);
                grid.FindPropertyRelative("rows").intValue = Mathf.Max(1, grid.FindPropertyRelative("rows").intValue);
                serialized.ApplyModifiedProperties();
                SceneView.RepaintAll();
            }
            EditorGUIUtility.labelWidth = labelWidth;
            GUILayout.Label(T("The grid is shared by the whole project (one per screen)."), EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            dragPlacement = EditorGUILayout.ToggleLeft(T("Place by dragging in the Scene view"), dragPlacement);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            // 블록을 고르면 그 블록들의 값(다르면 「—」), 안 고르면 새로 놓을 때의 기본값
            bool shown = inputs.Count > 0 ? inputs[0].insideSafeArea : insideSafeArea;
            EditorGUI.showMixedValue = inputs.Exists(i => i.insideSafeArea != shown);
            EditorGUI.BeginChangeCheck();
            bool inside = EditorGUILayout.ToggleLeft(new GUIContent(T("Keep inside the safe area (red = notch / home bar)"),
                F("SafeArea insets of the reference device: left {0:P1} · right {1:P1} · top {2:P1} · bottom {3:P1}", context.safeInsets.x, context.safeInsets.y, context.safeInsets.z, context.safeInsets.w)), shown);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                insideSafeArea = inside;
                EditorPrefs.SetBool(InsideSafeAreaKey, inside);
                // 칸에 놓인 선택 블록은 바로 다시 놓는다
                List<BlockInput> placed = inputs.Where(i => i.placement != null).ToList();
                if (placed.Count > 0)
                    PlaceAll(context, session, placed, i => { i.insideSafeArea = inside; return i; });
                SceneView.RepaintAll();
            }
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
            var context = new FlexibleGridContext(root, profile.grid, settings.GetSafeInsets(profileIndex));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(F("Save where each role (Header, Main, ...) sits on the grid of the {0} screen, and reuse it on other prefabs.", Name(profile)), EditorStyles.wordWrappedMiniLabel);
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
            if (profileIndex < 0 || !settings.Profiles[profileIndex].grid.IsValid)
                return;

            var context = new FlexibleGridContext(root, settings.Profiles[profileIndex].grid, settings.GetSafeInsets(profileIndex));
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
                        FlexibleGridPlacementService.Apply(context, session, input.target, dragCell, input.modeX, input.modeY, input.insideSafeArea);
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
