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
        private static string[] ModeLabelsX => new[] { T("Auto"), T("Stretch"), T("Stick left"), T("Stick right"), T("Center") };
        private static string[] ModeLabelsY => new[] { T("Auto"), T("Stretch"), T("Stick bottom"), T("Stick top"), T("Center") };
        private static readonly int DragHint = "FlexibleGridDrag".GetHashCode();
        private const string InsideSafeAreaKey = "FlexibleUITool.InsideSafeArea";

        [SerializeField] private bool showGrid;
        [SerializeField] private bool showTemplates;
        [SerializeField] private bool showMore;
        [SerializeField] private bool dragPlacement = true;
        [SerializeField] private FlexibleCell cell = new FlexibleCell(0, 0, 1, 1);
        [SerializeField] private FlexibleAxisMode modeX;
        [SerializeField] private FlexibleAxisMode modeY;
        [SerializeField] private int templateIndex;
        [SerializeField] private string templateName = "";
        private bool insideSafeArea = true;
        private bool dragging;
        private Vector2Int dragStart;

        public bool GridOpen => showGrid;

        public bool TemplatesOpen => showTemplates;

        // 템플릿 「적용」을 누른 횟수 (튜토리얼 확인용)
        public int TemplateApplyCount { get; private set; }

        // 아직 적용하지 않은 따라가기 방식 (튜토리얼 강조 위치용)
        public bool HasPendingMode => modeX != FlexibleAxisMode.Auto || modeY != FlexibleAxisMode.Auto;

        public void SetFoldouts(bool grid, bool templates)
        {
            showGrid = grid;
            showTemplates = templates;
            SceneView.RepaintAll();
        }

        public void ResetPlacementInput()
        {
            modeX = FlexibleAxisMode.Auto;
            modeY = FlexibleAxisMode.Auto;
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

        // 그리드로 놓은 적 있는 블록을 고르면 그때의 칸·모드를 채워 둔다
        public void OnSelectionChanged()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            RectTransform target = GetSelectedBlock((RectTransform)stage.prefabContentsRoot.transform);
            FlexibleGridPlacement placement = target != null ? session.FindPlacement(target) : null;
            if (placement == null)
                return;
            cell = placement.cell;
            modeX = placement.modeX;
            modeY = placement.modeY;
            insideSafeArea = placement.insideSafeArea;
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
            RectTransform target = GetSelectedBlock(root);
            if (target == null)
            {
                GUILayout.Label(T("Click a block in the Hierarchy or the Scene view."), EditorStyles.wordWrappedMiniLabel);
                return;
            }

            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(target.name, EditorStyles.boldLabel);
                switch (FlexibleScreenSession.GetStatus(stage, target))
                {
                    case FlexibleBlockStatus.BaseScreen:
                        GUILayout.Label(T("Base screen: changes here apply to every screen (except where a screen has its own layout)."), EditorStyles.wordWrappedMiniLabel);
                        break;
                    case FlexibleBlockStatus.SameAsBase:
                        GUILayout.Label(F("○ Same as {0}. Move it to give this screen its own layout.", Name(baseProfile)), FlexibleGui.WrapLabel);
                        break;
                    case FlexibleBlockStatus.Own:
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(FlexibleGui.Good)}>●</color> " + T("Has its own layout on this screen"), FlexibleGui.WrapLabel);
                            if (GUILayout.Button(F("Back to {0}", Name(baseProfile)), GUILayout.Width(130f)))
                                FlexibleScreenSession.ResetToBase(stage, target);
                        }
                        break;
                    case FlexibleBlockStatus.NotSavedYet:
                        GUILayout.Label(T("New block (not saved yet). After saving it appears on every screen."), EditorStyles.wordWrappedMiniLabel);
                        break;
                }

                DrawRolePopup(layout, target, settings);
            }
        }

        private static void DrawRolePopup(FlexibleLayout layout, RectTransform target, FlexibleProfileSettings settings)
        {
            var options = new List<string> { T("(none)") };
            options.AddRange(settings.Roles);
            string current = layout.GetRole(target);
            int index = string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
            if (index < 0)
            {
                options.Add(current);
                index = options.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.Popup(new GUIContent(T("Role (optional)"), T("Used by templates and the check tab. Header, Main, Footer ...")), index, options.ToArray());
            FlexibleGui.Mark("role");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Role");
                layout.SetRole(target, index == 0 ? null : options[index]);
                EditorUtility.SetDirty(layout);
            }
        }

        // ───────── 그리드 ─────────

        private void DrawGrid(PrefabStage stage, FlexibleLayout layout)
        {
            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            showGrid = EditorGUILayout.Foldout(showGrid, T("Place on a grid (optional)"), true, EditorStyles.foldoutHeader);
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

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Select a block, then drag over the blue cells in the Scene view. The block snaps to those cells."), EditorStyles.wordWrappedMiniLabel);
                DrawGridSettings(settings, profileIndex, context);
                DrawStalePlacements(context, session);
                DrawPlacement(context, session);
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

        private void DrawGridSettings(FlexibleProfileSettings settings, int profileIndex, FlexibleGridContext context)
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
            insideSafeArea = EditorGUILayout.ToggleLeft(new GUIContent(T("Keep inside the safe area (red = notch / home bar)"),
                F("SafeArea insets of the reference device: left {0:P1} · right {1:P1} · top {2:P1} · bottom {3:P1}", context.safeInsets.x, context.safeInsets.y, context.safeInsets.z, context.safeInsets.w)), insideSafeArea);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(InsideSafeAreaKey, insideSafeArea);
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

        private void DrawPlacement(FlexibleGridContext context, FlexibleWorkspace session)
        {
            RectTransform root = context.root;
            FlexibleGrid grid = context.grid;
            RectTransform target = GetSelectedBlock(root);
            if (target == null)
            {
                GUILayout.Label(T("Select the block to place."), EditorStyles.miniBoldLabel);
                return;
            }
            if (!grid.IsValid)
            {
                EditorGUILayout.HelpBox(T("Grid columns and rows must be 1 or more."), MessageType.Error);
                return;
            }

            FlexibleGridPlacement placement = session.FindPlacement(target);
            string where = placement != null && placement.insideSafeArea ? T(", inside SafeArea") : "";
            string kind;
            if (placement == null)
                kind = T("Not placed on the grid");
            else if (FlexibleWorkspaceService.IsUntouchedPlacement(placement))
                kind = F("Grid block {0}{1}", placement.cell, where);
            else if (FlexibleGridPlacementService.TryGetAdjustment(placement, out FlexibleGridAdjustment adjustment))
                kind = F("Grid {0}{1} + adjustment ({2})", placement.cell, where, adjustment);
            else
                kind = F("Manually adjusted (anchors or pivot changed from grid {0}{1}; excluded from templates)", placement.cell, where);
            EditorGUILayout.LabelField(F("{0}: {1}", target.name, kind), EditorStyles.wordWrappedMiniLabel);

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Column"), T("Row"), T("Width (cells)"), T("Height (cells)"), T("Horizontal"), T("Vertical"));
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                cell.column = EditorGUILayout.IntField(T("Column"), cell.column);
                cell.row = EditorGUILayout.IntField(T("Row"), cell.row);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                cell.width = EditorGUILayout.IntField(T("Width (cells)"), cell.width);
                cell.height = EditorGUILayout.IntField(T("Height (cells)"), cell.height);
            }
            cell = cell.Clamped(grid);
            using (new EditorGUILayout.VerticalScope())
            {
                modeX = (FlexibleAxisMode)EditorGUILayout.Popup(T("Horizontal"), (int)modeX, ModeLabelsX);
                modeY = (FlexibleAxisMode)EditorGUILayout.Popup(T("Vertical"), (int)modeY, ModeLabelsY);
            }
            FlexibleGui.Mark("grid:mode");
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            EditorGUIUtility.labelWidth = labelWidth;

            FlexibleAxisMode resolvedX = FlexibleGridMath.ResolveX(modeX, cell, grid);
            FlexibleAxisMode resolvedY = FlexibleGridMath.ResolveY(modeY, cell, grid);
            EditorGUILayout.LabelField(F("Mode to apply: horizontal {0} / vertical {1}", ModeLabelsX[(int)resolvedX], ModeLabelsY[(int)resolvedY]), EditorStyles.miniLabel);

            if (GUILayout.Button(T("Apply to selected block"), GUILayout.Height(26)))
                FlexibleGridPlacementService.Apply(context, session, target, cell, modeX, modeY, insideSafeArea);
            FlexibleGui.Mark("grid:apply");
        }

        // ───────── 템플릿 ─────────

        private void DrawTemplates(PrefabStage stage, FlexibleLayout layout)
        {
            showTemplates = EditorGUILayout.Foldout(showTemplates, T("Templates (optional)"), true, EditorStyles.foldoutHeader);
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
            Rect area = context.GetArea(insideSafeArea);
            DrawUnsafeArea(root, root.rect, context.GetArea(true));
            for (int r = 0; r < grid.rows; r++)
            for (int c = 0; c < grid.columns; c++)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, new FlexibleCell(c, r, 1, 1)),
                    new Color(0.2f, 0.6f, 1f, 0.05f), new Color(0.2f, 0.6f, 1f, 0.5f));

            if (GetSelectedBlock(root) != null)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, cell.Clamped(grid)),
                    new Color(1f, 0.6f, 0.1f, 0.15f), new Color(1f, 0.6f, 0.1f, 1f));

            if (dragPlacement)
                HandleDrag(context, session, area);
        }

        private void HandleDrag(FlexibleGridContext context, FlexibleWorkspace session, Rect area)
        {
            RectTransform root = context.root;
            FlexibleGrid grid = context.grid;
            int id = GUIUtility.GetControlID(DragHint, FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    if (GetSelectedBlock(root) != null)
                        HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && GetSelectedBlock(root) != null && TryPickCell(root, area, grid, e.mousePosition, out Vector2Int start))
                    {
                        GUIUtility.hotControl = id;
                        dragging = true;
                        dragStart = start;
                        cell = FlexibleCell.FromCorners(start, start);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        if (TryPickCell(root, area, grid, e.mousePosition, out Vector2Int current))
                            cell = FlexibleCell.FromCorners(dragStart, current);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        dragging = false;
                        e.Use();
                        RectTransform target = GetSelectedBlock(root);
                        if (target != null)
                            FlexibleGridPlacementService.Apply(context, session, target, cell, modeX, modeY, insideSafeArea);
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
