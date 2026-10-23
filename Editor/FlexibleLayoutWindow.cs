using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 원본 프리팹의 프로필 관리, 작업 프리팹의 그리드 배치·역할·템플릿·반영을 담당하는 아트용 창
    public class FlexibleLayoutWindow : EditorWindow
    {
        private static string[] ModeLabelsX => new[] { T("Auto"), T("Stretch"), T("Stick left"), T("Stick right"), T("Center") };
        private static string[] ModeLabelsY => new[] { T("Auto"), T("Stretch"), T("Stick bottom"), T("Stick top"), T("Center") };
        private static readonly int DragHint = "FlexibleGridDrag".GetHashCode();

        private Vector2 scroll;
        private bool showGrid = true;
        private bool dragPlacement = true;
        private bool insideSafeArea = true;
        private FlexibleCell cell = new FlexibleCell(0, 0, 1, 1);
        private FlexibleAxisMode modeX;
        private FlexibleAxisMode modeY;
        private bool dragging;
        private Vector2Int dragStart;
        private int templateIndex;
        private string templateName = "";

        [MenuItem("Tools/FlexibleUITool/Layout Window")]
        public static void Open()
        {
            GetWindow<FlexibleLayoutWindow>("Flexible Layout");
        }

        private const string InsideSafeAreaKey = "FlexibleUITool.InsideSafeArea";

        private void OnEnable()
        {
            insideSafeArea = EditorPrefs.GetBool(InsideSafeAreaKey, true);
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnStageChanged(PrefabStage stage)
        {
            Repaint();
            SceneView.RepaintAll();
        }

        // 그리드로 배치한 적 있는 블록을 고르면 그때의 칸·모드를 채워 둔다
        private void OnSelectionChanged()
        {
            if (TryGetWorkspace(out PrefabStage stage, out FlexibleWorkspace workspace))
            {
                RectTransform target = GetSelectedBlock((RectTransform)stage.prefabContentsRoot.transform);
                FlexibleGridPlacement placement = target != null ? workspace.FindPlacement(target) : null;
                if (placement != null)
                {
                    cell = placement.cell;
                    modeX = placement.modeX;
                    modeY = placement.modeY;
                    insideSafeArea = placement.insideSafeArea;
                }
            }

            Repaint();
            SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            FlexibleWorkspace workspace = stage != null ? stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>() : null;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawLanguagePopup();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("Preview window"), EditorStyles.toolbarButton))
                    FlexiblePreviewWindow.Open();
                if (GUILayout.Button(T("Review window"), EditorStyles.toolbarButton))
                    FlexibleReviewWindow.Open();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (workspace != null)
                DrawWorkspace(stage, workspace);
            else
                DrawSource(stage);
            EditorGUILayout.EndScrollView();
        }

        private static readonly string[] LanguageNames = { "Auto", "한국어", "日本語", "English" };

        // 툴 UI 언어 (사용자별). Tools > FlexibleUITool > Language 메뉴와 같은 설정
        private static void DrawLanguagePopup()
        {
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup((int)FlexibleText.Setting, LanguageNames, EditorStyles.toolbarPopup, GUILayout.Width(72f));
            if (EditorGUI.EndChangeCheck())
                FlexibleText.Setting = (FlexibleLanguage)index;
        }

        // ───────── 원본 프리팹 ─────────

        private void DrawSource(PrefabStage stage)
        {
            if (!TryGetSource(stage, out string sourcePath, out GameObject root))
            {
                EditorGUILayout.HelpBox(T("Select a UI prefab in the Project window or open it in Prefab Mode."), MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(T("Source prefab"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(sourcePath, EditorStyles.miniLabel);

            if (root.transform is not RectTransform rootRect)
            {
                EditorGUILayout.HelpBox(T("The root is not a RectTransform."), MessageType.Error);
                return;
            }

            if (rootRect.anchorMin != Vector2.zero || rootRect.anchorMax != Vector2.one)
                EditorGUILayout.HelpBox(T("The root does not stretch with its parent (anchors are not full stretch). Its ratio stays the same when the screen changes, so profiles do not switch."), MessageType.Warning);

            var layout = root.GetComponent<FlexibleLayout>();
            if (layout == null)
            {
                if (GUILayout.Button(T("Add FlexibleLayout"), GUILayout.Height(28)))
                    ModifySource(stage, sourcePath, l => { });
                return;
            }

            if (layout.Settings == null)
            {
                EditorGUILayout.HelpBox(T("No profile settings are assigned."), MessageType.Warning);
                if (GUILayout.Button(T("Assign default profile settings")))
                    ModifySource(stage, sourcePath, l => l.Settings = FlexibleSettingsUtility.GetOrCreateDefault());
                return;
            }

            bool sourceDirty = stage != null && stage.scene.isDirty;
            if (sourceDirty)
                EditorGUILayout.HelpBox(T("The source prefab has unsaved changes. Workspaces are made from the saved source, so save it first."), MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("Profiles"), EditorStyles.boldLabel);
            FlexibleProfileSettings settings = layout.Settings;
            foreach (FlexibleProfile profile in settings.Profiles)
                DrawProfileRow(stage, sourcePath, layout, settings, profile, sourceDirty);

            if (stage != null)
            {
                EditorGUILayout.Space();
                RectTransform target = GetSelectedBlock(rootRect);
                if (target != null)
                    DrawRolePopup(layout, target, settings);
                else
                    EditorGUILayout.HelpBox(T("Select a block in the Hierarchy to set its role (Header, Main, ...). Roles are used by templates and the review."), MessageType.None);
            }
        }

        private static void DrawProfileRow(PrefabStage stage, string sourcePath, FlexibleLayout layout, FlexibleProfileSettings settings,
            FlexibleProfile profile, bool sourceDirty)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (profile.id == settings.BaseProfileId)
                {
                    EditorGUILayout.LabelField(F("{0}  — base (the prefab itself)", Label(profile)));
                    return;
                }

                List<FlexibleProfileState> states = layout.Entries.Select(e => e.Find(profile.id)).Where(s => s != null).ToList();
                int grid = states.Count(s => s.gridPlaced);
                bool exists = FlexibleWorkspaceService.WorkspaceExists(sourcePath, profile.id);
                EditorGUILayout.LabelField(F("{0}  — {1} records ({2} grid / {3} manual)", Label(profile), states.Count, grid, states.Count - grid) + (exists ? T("  · in progress") : ""));

                using (new EditorGUI.DisabledScope(sourceDirty))
                {
                    if (GUILayout.Button(exists ? T("Open workspace") : T("Create workspace"), GUILayout.Width(130)))
                    {
                        FlexibleWorkspaceService.OpenOrCreate(sourcePath, profile.id);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(states.Count == 0))
                {
                    if (GUILayout.Button(T("Delete records"), GUILayout.Width(90))
                        && EditorUtility.DisplayDialog(T("Delete records"), F("Delete {1} layout records of {0}?", Label(profile), states.Count), T("Delete"), T("Cancel")))
                    {
                        string id = profile.id;
                        ModifySource(stage, sourcePath, l =>
                        {
                            foreach (FlexibleLayoutEntry entry in l.Entries)
                                entry.states.RemoveAll(s => s.profileId == id);
                            l.Entries.RemoveAll(e => e.states.Count == 0);
                        });
                    }
                }
            }
        }

        private static bool TryGetSource(PrefabStage stage, out string path, out GameObject root)
        {
            if (stage != null)
            {
                path = stage.assetPath;
                root = stage.prefabContentsRoot;
                return true;
            }

            root = Selection.activeObject as GameObject;
            path = root != null ? AssetDatabase.GetAssetPath(root) : null;
            if (root == null || string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                return false;

            root = root.transform.root.gameObject;
            return true;
        }

        // 프리팹 모드면 열린 내용을 Undo로, 아니면 애셋을 직접 수정한다 (FlexibleLayout이 없으면 붙인다)
        private static void ModifySource(PrefabStage stage, string path, System.Action<FlexibleLayout> modify)
        {
            if (stage != null)
            {
                GameObject root = stage.prefabContentsRoot;
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout == null)
                {
                    layout = Undo.AddComponent<FlexibleLayout>(root);
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                }

                Undo.RecordObject(layout, "Flexible Layout");
                modify(layout);
                EditorUtility.SetDirty(layout);
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var layout = contents.GetComponent<FlexibleLayout>();
                if (layout == null)
                {
                    layout = contents.AddComponent<FlexibleLayout>();
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                }

                modify(layout);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
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
            index = EditorGUILayout.Popup(F("Role: {0}", target.name), index, options.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Role");
                layout.SetRole(target, index == 0 ? null : options[index]);
                EditorUtility.SetDirty(layout);
            }
        }

        // ───────── 작업 프리팹 ─────────

        private void DrawWorkspace(PrefabStage stage, FlexibleWorkspace workspace)
        {
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = GetSettings(root);
            int profileIndex = settings.IndexOf(workspace.profileId);
            FlexibleProfile profile = profileIndex >= 0 ? settings.Profiles[profileIndex] : null;
            string sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);

            EditorGUILayout.LabelField(T("Workspace"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(F("Source: {0}", sourcePath), EditorStyles.miniLabel);
                if (GUILayout.Button(T("Select source"), GUILayout.Width(90)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            }

            if (profile == null)
            {
                EditorGUILayout.HelpBox(F("Profile '{0}' is not in the settings.", workspace.profileId), MessageType.Error);
                DrawCommitButtons(stage);
                return;
            }

            EditorGUILayout.LabelField(F("Profile: {0}", Label(profile)));
            DrawAspectCheck(root, settings, profileIndex, profile, workspace);

            var context = new FlexibleGridContext(root, profile.grid, settings.GetSafeInsets(profileIndex));
            EditorGUILayout.Space();
            DrawGridSettings(settings, profileIndex, context);
            DrawStalePlacements(context, workspace);

            EditorGUILayout.Space();
            DrawPlacement(context, workspace, settings);

            EditorGUILayout.Space();
            DrawTemplates(context, profile, workspace, settings);

            EditorGUILayout.Space();
            DrawCommitButtons(stage);
        }

        private static void DrawAspectCheck(RectTransform root, FlexibleProfileSettings settings, int profileIndex, FlexibleProfile profile, FlexibleWorkspace workspace)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool auto = EditorGUILayout.ToggleLeft(T("Switch the Game view resolution when a workspace opens"), FlexibleWorkspaceStage.AutoGameView);
                if (EditorGUI.EndChangeCheck())
                    FlexibleWorkspaceStage.AutoGameView = auto;
                if (GUILayout.Button(T("Switch now"), GUILayout.Width(90)))
                    FlexibleWorkspaceStage.TrySelectWorkResolution(workspace);
            }

            Rect rect = root.rect;
            float aspect = rect.height > 0f ? rect.width / rect.height : 0f;
            float max = settings.MaxAspectOf(profileIndex);
            EditorGUILayout.LabelField(F("Current root size: {0:0} x {1:0}  (ratio {2:0.00})", rect.width, rect.height, aspect), EditorStyles.miniLabel);

            if (aspect < profile.minAspect || aspect >= max)
            {
                string range = float.IsPositiveInfinity(max) ? F("{0:0.00} or wider", profile.minAspect) : $"{profile.minAspect:0.00} ~ {max:0.00}";
                EditorGUILayout.HelpBox(F("The root ratio is outside this profile ({0}). Press [{1}] or set the Game view to a {2:0}x{3:0} ratio. (Sizes are converted to the short side automatically.)", range, T("Switch now"), profile.workResolution.x, profile.workResolution.y), MessageType.Warning);
            }
            else if (Mathf.Abs(rect.width - profile.workResolution.x) > 1f || Mathf.Abs(rect.height - profile.workResolution.y) > 1f)
            {
                EditorGUILayout.HelpBox(F("The recommended work size is {0:0}x{1:0}. Fixed-size blocks are computed from the current size.", profile.workResolution.x, profile.workResolution.y), MessageType.None);
            }
        }

        private void DrawGridSettings(FlexibleProfileSettings settings, int profileIndex, FlexibleGridContext context)
        {
            EditorGUILayout.LabelField(T("Grid (project-wide setting)"), EditorStyles.boldLabel);
            var serialized = new SerializedObject(settings);
            SerializedProperty grid = serialized.FindProperty("profiles").GetArrayElementAtIndex(profileIndex).FindPropertyRelative("grid");

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FitLabels(T("Columns"), T("Rows"), T("Margin"), T("Gutter"));
            EditorGUI.BeginChangeCheck();
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
            if (EditorGUI.EndChangeCheck())
            {
                grid.FindPropertyRelative("columns").intValue = Mathf.Max(1, grid.FindPropertyRelative("columns").intValue);
                grid.FindPropertyRelative("rows").intValue = Mathf.Max(1, grid.FindPropertyRelative("rows").intValue);
                serialized.ApplyModifiedProperties();
                SceneView.RepaintAll();
            }
            EditorGUIUtility.labelWidth = labelWidth;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                showGrid = EditorGUILayout.ToggleLeft(T("Show grid"), showGrid);
                dragPlacement = EditorGUILayout.ToggleLeft(T("Place by Scene view drag"), dragPlacement);
                if (EditorGUI.EndChangeCheck())
                    SceneView.RepaintAll();
            }

            Vector4 insets = context.safeInsets;
            EditorGUI.BeginChangeCheck();
            insideSafeArea = EditorGUILayout.ToggleLeft(T("Place inside SafeArea only (avoid notch and home indicator)"), insideSafeArea);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(InsideSafeAreaKey, insideSafeArea);
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField(F("SafeArea insets of the reference device (red area): left {0:P1} · right {1:P1} · top {2:P1} · bottom {3:P1}", insets.x, insets.y, insets.z, insets.w), EditorStyles.miniLabel);
        }

        private static void DrawStalePlacements(FlexibleGridContext context, FlexibleWorkspace workspace)
        {
            var stale = FlexibleGridPlacementService.FindStale(context, workspace);
            if (stale.Count == 0)
                return;

            EditorGUILayout.HelpBox(F("{0} grid blocks would move if placed again with the current grid or screen size. (Manually adjusted blocks are excluded.)", stale.Count), MessageType.Info);
            if (GUILayout.Button(F("Re-place grid blocks ({0})", stale.Count)))
                FlexibleGridPlacementService.ReapplyStale(workspace, stale);
        }

        private void DrawPlacement(FlexibleGridContext context, FlexibleWorkspace workspace, FlexibleProfileSettings settings)
        {
            RectTransform root = context.root;
            FlexibleGrid grid = context.grid;
            EditorGUILayout.LabelField(T("Cell placement"), EditorStyles.boldLabel);
            RectTransform target = GetSelectedBlock(root);
            if (target == null)
            {
                EditorGUILayout.HelpBox(T("Select a block to place in the Hierarchy (an object under the workspace root)."), MessageType.Info);
                return;
            }

            if (!grid.IsValid)
            {
                EditorGUILayout.HelpBox(T("Grid columns and rows must be 1 or more."), MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField(F("Target: {0}", FlexiblePathUtility.GetPath(root, target)), EditorStyles.miniLabel);
            FlexibleGridPlacement placement = workspace.FindPlacement(target);
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
            EditorGUILayout.LabelField(F("State: {0}", kind), EditorStyles.miniLabel);

            var layout = root.GetComponent<FlexibleLayout>();
            if (layout != null)
                DrawRolePopup(layout, target, settings);

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FitLabels(T("Column"), T("Row"), T("Width (cells)"), T("Height (cells)"), T("Horizontal"), T("Vertical"));
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

            modeX = (FlexibleAxisMode)EditorGUILayout.Popup(T("Horizontal"), (int)modeX, ModeLabelsX);
            modeY = (FlexibleAxisMode)EditorGUILayout.Popup(T("Vertical"), (int)modeY, ModeLabelsY);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            EditorGUIUtility.labelWidth = labelWidth;

            FlexibleAxisMode resolvedX = FlexibleGridMath.ResolveX(modeX, cell, grid);
            FlexibleAxisMode resolvedY = FlexibleGridMath.ResolveY(modeY, cell, grid);
            EditorGUILayout.LabelField(F("Mode to apply: horizontal {0} / vertical {1}", ModeLabelsX[(int)resolvedX], ModeLabelsY[(int)resolvedY]), EditorStyles.miniLabel);

            if (GUILayout.Button(T("Apply to selected block"), GUILayout.Height(26)))
                FlexibleGridPlacementService.Apply(context, workspace, target, cell, modeX, modeY, insideSafeArea);
        }

        private void DrawTemplates(FlexibleGridContext context, FlexibleProfile profile, FlexibleWorkspace workspace, FlexibleProfileSettings settings)
        {
            EditorGUILayout.LabelField(T("Templates (role-to-cell layouts)"), EditorStyles.boldLabel);
            var layout = context.root.GetComponent<FlexibleLayout>();
            if (layout == null)
                return;

            List<FlexibleTemplate> templates = settings.Templates.Where(t => t.profileId == profile.id).ToList();
            if (templates.Count > 0)
            {
                templateIndex = Mathf.Clamp(templateIndex, 0, templates.Count - 1);
                using (new EditorGUILayout.HorizontalScope())
                {
                    templateIndex = EditorGUILayout.Popup(templateIndex, templates.Select(t => F("{0} ({1} slots)", t.name, t.slots.Count)).ToArray());
                    if (GUILayout.Button(T("Apply"), GUILayout.Width(60)))
                        ApplyTemplate(context, workspace, layout, templates[templateIndex]);
                    if (GUILayout.Button(T("Delete"), GUILayout.Width(60))
                        && EditorUtility.DisplayDialog(T("Delete template"), F("Delete the template '{0}'? (shared by the project)", templates[templateIndex].name), T("Delete"), T("Cancel")))
                    {
                        FlexibleGridPlacementService.DeleteTemplate(settings, templates[templateIndex]);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField(T("No templates for this profile."), EditorStyles.miniLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                templateName = EditorGUILayout.TextField(templateName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(templateName)))
                {
                    if (GUILayout.Button(T("Save current layout as template"), GUILayout.Width(210)))
                        SaveTemplate(workspace, layout, profile, settings);
                }
            }
        }

        private void SaveTemplate(FlexibleWorkspace workspace, FlexibleLayout layout, FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            var skipped = new List<string>();
            FlexibleTemplate template = FlexibleGridPlacementService.CreateTemplate(workspace, layout, profile.id, templateName.Trim(), skipped);
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

        private static void ApplyTemplate(FlexibleGridContext context, FlexibleWorkspace workspace, FlexibleLayout layout, FlexibleTemplate template)
        {
            var missing = new List<string>();
            int applied = FlexibleGridPlacementService.ApplyTemplate(context, workspace, layout, template, missing);
            string message = F("Placed {0} blocks.", applied);
            if (missing.Count > 0)
                message += "\n" + F("Roles missing in this prefab: {0}", string.Join(", ", missing));
            EditorUtility.DisplayDialog(T("Apply template"), message, T("OK"));
        }

        private void DrawCommitButtons(PrefabStage stage)
        {
            EditorGUILayout.LabelField(T("Finish"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Commit"), GUILayout.Height(30)))
                {
                    Commit(stage);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(T("Cancel (delete)"), GUILayout.Height(30), GUILayout.Width(130))
                    && EditorUtility.DisplayDialog(T("Cancel work"), T("The workspace will be deleted. Changes that were not committed are lost."), T("Delete"), T("Cancel")))
                {
                    FlexibleWorkspaceService.Delete(stage.assetPath);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static void Commit(PrefabStage stage)
        {
            string workspacePath = stage.assetPath;
            if (stage.scene.isDirty)
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, workspacePath);
                stage.ClearDirtiness();
            }

            if (FlexibleWorkspaceService.IsSourceModified(workspacePath)
                && !EditorUtility.DisplayDialog(T("Source changed"), T("The source prefab changed after this workspace was created. Commit anyway?\n(Renamed objects are not committed.)"), T("Commit"), T("Cancel")))
                return;

            StageUtility.GoToMainStage();
            FlexibleCommitReport report = FlexibleWorkspaceService.Commit(workspacePath);
            if (report.success)
                FlexibleWorkspaceService.Delete(workspacePath);

            var message = new StringBuilder();
            message.AppendLine(report.success
                ? F("{0}\nWrote {2} records to the '{1}' profile. The workspace was deleted.", report.sourcePath, report.profileId, report.recorded)
                : T("Could not commit. The workspace was kept."));
            foreach (string warning in report.warnings.Take(15))
                message.AppendLine("· " + warning);
            if (report.warnings.Count > 15)
                message.AppendLine("· " + F("{0} more (see the Console)", report.warnings.Count - 15));
            foreach (string warning in report.warnings)
                Debug.LogWarning($"[FlexibleUITool] {warning}");

            EditorUtility.DisplayDialog(T("Commit"), message.ToString(), T("OK"));
            if (report.success)
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(report.sourcePath));
        }

        // 언어마다 라벨 길이가 달라서 가장 긴 라벨에 맞춘다
        private static float FitLabels(params string[] labels)
        {
            float width = 64f;
            foreach (string label in labels)
                width = Mathf.Max(width, EditorStyles.label.CalcSize(new GUIContent(label)).x + 6f);
            return width;
        }

        private static RectTransform GetSelectedBlock(RectTransform root)
        {
            var target = Selection.activeTransform as RectTransform;
            return target != null && target != root && target.IsChildOf(root) ? target : null;
        }

        private static bool TryGetWorkspace(out PrefabStage stage, out FlexibleWorkspace workspace)
        {
            stage = PrefabStageUtility.GetCurrentPrefabStage();
            workspace = stage != null ? stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>() : null;
            return workspace != null;
        }

        private static FlexibleProfileSettings GetSettings(RectTransform root)
        {
            var layout = root.GetComponent<FlexibleLayout>();
            return layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
        }

        // ───────── Scene 뷰 ─────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!TryGetWorkspace(out PrefabStage stage, out FlexibleWorkspace workspace))
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = GetSettings(root);
            int profileIndex = settings.IndexOf(workspace.profileId);
            if (profileIndex < 0 || !settings.Profiles[profileIndex].grid.IsValid)
                return;

            var context = new FlexibleGridContext(root, settings.Profiles[profileIndex].grid, settings.GetSafeInsets(profileIndex));
            FlexibleGrid grid = context.grid;
            Rect area = context.GetArea(insideSafeArea);
            if (showGrid)
            {
                DrawUnsafeArea(root, root.rect, context.GetArea(true));
                for (int r = 0; r < grid.rows; r++)
                for (int c = 0; c < grid.columns; c++)
                    DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, new FlexibleCell(c, r, 1, 1)),
                        new Color(0.2f, 0.6f, 1f, 0.05f), new Color(0.2f, 0.6f, 1f, 0.5f));
            }

            if (GetSelectedBlock(root) != null)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, cell.Clamped(grid)),
                    new Color(1f, 0.6f, 0.1f, 0.15f), new Color(1f, 0.6f, 0.1f, 1f));

            if (dragPlacement)
                HandleDrag(context, workspace, area);
        }

        private void HandleDrag(FlexibleGridContext context, FlexibleWorkspace workspace, Rect area)
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
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        if (TryPickCell(root, area, grid, e.mousePosition, out Vector2Int current))
                            cell = FlexibleCell.FromCorners(dragStart, current);
                        e.Use();
                        Repaint();
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
                            FlexibleGridPlacementService.Apply(context, workspace, target, cell, modeX, modeY, insideSafeArea);
                        Repaint();
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
