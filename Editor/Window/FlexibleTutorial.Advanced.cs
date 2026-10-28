using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 심화 튜토리얼: 그리드 칸·칸에 놓기·따라가기 방식·미세 조정·역할·템플릿. 장마다 따로 고를 수도 있다
    public static partial class FlexibleTutorial
    {
        private const string SingleKey = "FlexibleUITool.TutorialSingleChapter";
        private const string SnapshotKey = "FlexibleUITool.TutorialSettingsSnapshot";
        private const string Landscape = "landscape";

        private struct Chapter
        {
            public string title;
            public Step first;
            public Step last;

            public Chapter(string title, Step first, Step last)
            {
                this.title = title;
                this.first = first;
                this.last = last;
            }
        }

        private static readonly Chapter[] Chapters =
        {
            new Chapter("Grid size", Step.OpenGrid, Step.ChangeGrid),
            new Chapter("Place on cells", Step.SelectBlock, Step.DragCells),
            new Chapter("Following the screen size", Step.SetMode, Step.SeeMode),
            new Chapter("Fine-tuning by hand", Step.Nudge, Step.SaveAdjust),
            new Chapter("Roles", Step.SetRole, Step.SetRole),
            new Chapter("Templates", Step.OpenTemplates, Step.ApplyTemplate),
        };

        // 튜토리얼이 바꾸는 프로젝트 공용 설정 (그리드·템플릿). 끝나면 이 값으로 되돌린다
        [Serializable]
        private class SettingsSnapshot
        {
            public List<FlexibleGrid> grids = new List<FlexibleGrid>();
            public List<string> templates = new List<string>();
        }

        private static int ChapterOf(Step step)
        {
            for (int i = 0; i < Chapters.Length; i++)
            {
                if (step >= Chapters[i].first && step <= Chapters[i].last)
                    return i;
            }
            return -1;
        }

        // 새 연습용 프리팹을 가로 화면으로 열고, 그 장을 바로 할 수 있게 앞 장까지의 상태를 만들어 둔다
        private static void StartChapter(int chapter, bool single, FlexibleWindow window)
        {
            SessionState.SetInt(SingleKey, single ? chapter : -1);
            SnapshotSettings();
            if (!CreatePractice())
                return;

            Enter(Chapters[chapter].first, window);
            EditorApplication.delayCall += () => SetUpChapter(chapter, window);
        }

        private static void SetUpChapter(int chapter, FlexibleWindow window)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsPracticeOpen() || window == null)
                return;

            FlexibleScreenSession.ShowProfile(stage, Landscape);
            window.ShowTab(FlexibleWindow.Tab.Edit);
            FlexibleEditPanel edit = window.EditPanel;
            edit.ResetPlacementInput();
            edit.SetFoldouts(chapter >= 1 && chapter <= 3, chapter == 5);

            // Game 뷰가 가로 크기로 바뀌어 프리팹 모양이 가로가 된 뒤에 칸을 계산해야 위치가 맞는다
            double giveUp = EditorApplication.timeSinceStartup + 3.0;
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
                bool ready = FlexibleScreenSession.IsShapeMatching(current, out _);
                if (!ready && EditorApplication.timeSinceStartup < giveUp)
                    return;
                EditorApplication.update -= wait;
                PlaceForChapter(chapter);
            };
            EditorApplication.update += wait;
        }

        private static void PlaceForChapter(int chapter)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!IsPracticeOpen() || !FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
                return;

            if (chapter >= 2)
                Place(stage, layout, MainBlock, g => new FlexibleCell(1, 1, Mathf.Max(1, g.columns / 2), Mathf.Max(1, g.rows - 2)));
            if (chapter == 5)
            {
                Place(stage, layout, "Header", g => new FlexibleCell(0, 0, g.columns, 1));
                Place(stage, layout, "Footer", g => new FlexibleCell(0, g.rows - 1, g.columns, 1));
                Undo.RecordObject(layout, "Flexible Tutorial");
                foreach (string role in new[] { "Header", MainBlock, "Footer" })
                {
                    RectTransform block = FindBlock(stage, role);
                    if (block != null)
                        layout.SetRole(block, role);
                }
                EditorUtility.SetDirty(layout);
            }
            if (chapter >= 2)
                FlexibleScreenSession.Save(stage);
            SelectMain();
        }

        private static void Place(PrefabStage stage, FlexibleLayout layout, string block, Func<FlexibleGrid, FlexibleCell> cellOf)
        {
            RectTransform target = FindBlock(stage, block);
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            FlexibleProfileSettings settings = layout.Settings;
            int index = settings.IndexOf(Landscape);
            if (target == null || session == null || index < 0)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            var context = new FlexibleGridContext(root, settings.Profiles[index].grid, settings.GetSafeInsets(index));
            FlexibleGridPlacementService.Apply(context, session, target, cellOf(context.grid), FlexibleAxisMode.Stretch, FlexibleAxisMode.Stretch, true);
        }

        private static void EnterAdvanced(Step step, FlexibleWindow window)
        {
            switch (step)
            {
                case Step.AdvWelcome:
                    SessionState.EraseInt(SingleKey);
                    break;
                case Step.ChangeGrid:
                    MarkText = GridKey();
                    break;
                case Step.DragCells:
                    Mark = FlexibleGridPlacementService.ApplyCount;
                    break;
                case Step.SetMode:
                    window.EditPanel.ResetPlacementInput();
                    Mark = FlexibleGridPlacementService.ApplyCount;
                    break;
                case Step.SaveAdjust:
                    Mark = FlexibleScreenSession.SaveCount;
                    break;
                case Step.SaveTemplate:
                    window.EditPanel.SuggestTemplateName(T("Practice template"));
                    break;
                case Step.ApplyTemplate:
                    Mark = window.EditPanel.TemplateApplyCount;
                    break;
                case Step.AdvFinish:
                case Step.ChapterDone:
                    RestoreSettings();
                    break;
            }
        }

        private static string AdvancedText(Step step)
        {
            switch (step)
            {
                case Step.AdvWelcome:
                    return T("Now let's place blocks precisely.\nYou'll learn the grid (cells), how blocks follow the screen size, fine-tuning, and roles and templates. We practice on the <b>Landscape</b> screen of a fresh practice prefab.");
                case Step.OpenGrid:
                    return T("Open <b>Place on a grid</b> (orange frame).\nBlue cells (the grid) appear in the Scene view. Each screen has its own grid.");
                case Step.ChangeGrid:
                    return T("<b>Columns</b> and <b>Rows</b> set how many cells there are. <b>Margin</b> is the space at the screen edge, <b>Gutter</b> the space between cells.\nChange any number and watch the Scene view. (I'll put the grid back when the tutorial ends.)");
                case Step.SelectBlock:
                    return T("Pick the block to place. Click <b>Main</b> in the Hierarchy or the Scene view.");
                case Step.DragCells:
                    return T("Drag over the blue cells in the Scene view. When you let go, Main fits those cells exactly.\n(You can also type Column / Row / Width / Height. The block moves as soon as you finish typing.)");
                case Step.SetMode:
                    return T("Choose how the block follows the screen size.\n<b>Stretch</b>: grows with the screen. <b>Stick left / right</b>: keeps its size on that side. <b>Center</b>: keeps its size in the middle.\nChange <b>Horizontal</b> from Stretch to another mode. The block changes right away.");
                case Step.SeeMode:
                    return T("Open the <b>Preview</b> tab and make the window wider and narrower.\nStretched blocks grow with it; stuck blocks keep their size. Press <b>Got it</b> when you've seen it.");
                case Step.Nudge:
                    return T("After placing on cells you can still adjust by hand.\nChange <b>Pos X</b> or <b>Width</b> of Main in the Inspector. (To drag it in the Scene view, close <b>Place on a grid</b> first.)\nThe adjustment is kept even when the grid changes.");
                case Step.SaveAdjust:
                    return T("Nice! Now save with <b>Ctrl+S</b> (or the Save button).");
                case Step.SetRole:
                    return T("A <b>role</b> (Header, Main, ...) says what a block is for. Blocks with roles can be laid out in one go with a template.\nSelect Main and set <b>Role</b> to Main.");
                case Step.OpenTemplates:
                    return T("Open <b>Templates</b> (orange frame).");
                case Step.SaveTemplate:
                    return T("Type a name and press <b>Save current layout as template</b>.\nOnly blocks that have a role and were placed on cells are saved.");
                case Step.ApplyTemplate:
                    return T("Now use it. Move Main anywhere, then press <b>Apply</b> next to the template.\nMain goes back to its saved cells. Any prefab whose blocks have the same roles can be laid out like this in one click.");
                case Step.AdvFinish:
                    return T("Advanced done! Grid → place on cells → how blocks follow → fine-tune → roles and templates.\nThe grid settings and practice templates changed during the tutorial were put back.");
                case Step.ChapterDone:
                    return T("This part is done!\nThe grid settings and practice templates changed during the tutorial were put back.");
                default:
                    return "";
            }
        }

        private static void DrawAdvancedButtons(Step step, FlexibleWindow window)
        {
            switch (step)
            {
                case Step.AdvWelcome:
                    if (GUILayout.Button(T("Start!"), GUILayout.Height(26f)))
                    {
                        StartChapter(0, false, window);
                        GUIUtility.ExitGUI();
                    }
                    break;
                case Step.SelectBlock:
                case Step.SetMode:
                case Step.Nudge:
                case Step.SetRole:
                    if (GUILayout.Button(T("Select Main for me"), GUILayout.Height(24f)))
                        SelectMain();
                    break;
                case Step.SeeMode:
                    if (GUILayout.Button(T("Got it"), GUILayout.Height(24f)))
                        Advance(step, window);
                    break;
                case Step.AdvFinish:
                case Step.ChapterDone:
                    if (GUILayout.Button(T("Choose another tutorial"), GUILayout.Height(26f)))
                    {
                        Enter(Step.Menu, window);
                        GUIUtility.ExitGUI();
                    }
                    DrawFinishButtons(window);
                    break;
            }
        }

        private static bool AdvancedNeedsEditTab(Step step)
        {
            if (step == Step.SeeMode || ChapterOf(step) < 0)
                return false;
            return IsPracticeOpen();
        }

        private static string AdvancedHighlight(Step step, FlexibleWindow window)
        {
            FlexibleEditPanel edit = window.EditPanel;
            switch (step)
            {
                case Step.OpenGrid:
                    return "grid";
                case Step.ChangeGrid:
                    return edit.GridOpen ? "grid:size" : "grid";
                case Step.DragCells:
                    return edit.GridOpen ? null : "grid";
                case Step.SetMode:
                    if (!edit.GridOpen)
                        return "grid";
                    return "grid:mode";
                case Step.SeeMode:
                    return window.CurrentTab == FlexibleWindow.Tab.Preview ? null : "tab:" + (int)FlexibleWindow.Tab.Preview;
                case Step.SaveAdjust:
                    return "save";
                case Step.SetRole:
                    return "role";
                case Step.OpenTemplates:
                    return "templates";
                case Step.SaveTemplate:
                    return edit.TemplatesOpen ? "template:save" : "templates";
                case Step.ApplyTemplate:
                    return edit.TemplatesOpen ? "template:apply" : "templates";
                default:
                    return null;
            }
        }

        private static bool AdvancedIsDone(Step step, FlexibleWindow window, PrefabStage stage, bool practice)
        {
            if (!practice)
                return false;

            FlexibleEditPanel edit = window.EditPanel;
            RectTransform main = FindMain(stage);
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            FlexibleGridPlacement placement = main != null && session != null ? session.FindPlacement(main) : null;
            switch (step)
            {
                case Step.OpenGrid:
                    return edit.GridOpen;
                case Step.ChangeGrid:
                    return GridKey() != MarkText;
                case Step.SelectBlock:
                    return main != null && Selection.activeTransform == main;
                case Step.DragCells:
                    return placement != null && FlexibleGridPlacementService.ApplyCount > Mark;
                case Step.SetMode:
                    return placement != null && FlexibleGridPlacementService.ApplyCount > Mark
                        && (placement.modeX != FlexibleAxisMode.Stretch || placement.modeY != FlexibleAxisMode.Stretch);
                case Step.Nudge:
                    return placement != null && !FlexibleWorkspaceService.IsUntouchedPlacement(placement);
                case Step.SaveAdjust:
                    return FlexibleScreenSession.SaveCount > Mark;
                case Step.SetRole:
                    return FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout) && main != null && !string.IsNullOrEmpty(layout.GetRole(main));
                case Step.OpenTemplates:
                    return edit.TemplatesOpen;
                case Step.SaveTemplate:
                    return HasNewTemplate();
                case Step.ApplyTemplate:
                    return edit.TemplateApplyCount > Mark;
                default:
                    return false;
            }
        }

        // ───────── 공용 설정 되돌리기 ─────────

        private static FlexibleProfileSettings Settings => FlexibleSettingsUtility.GetOrCreateDefault();

        private static string TemplateKey(FlexibleTemplate template) => template.profileId + "\n" + template.name;

        private static string GridKey()
        {
            FlexibleProfileSettings settings = Settings;
            return settings == null ? "" : string.Join("|", settings.Profiles.Select(p => JsonUtility.ToJson(p.grid)));
        }

        // 튜토리얼이 처음 공용 설정을 바꾸기 전에 한 번만 기억한다
        private static void SnapshotSettings()
        {
            FlexibleProfileSettings settings = Settings;
            if (settings == null || !string.IsNullOrEmpty(SessionState.GetString(SnapshotKey, "")))
                return;

            var snapshot = new SettingsSnapshot();
            snapshot.grids.AddRange(settings.Profiles.Select(p => p.grid));
            snapshot.templates.AddRange(settings.Templates.Select(TemplateKey));
            SessionState.SetString(SnapshotKey, JsonUtility.ToJson(snapshot));
        }

        private static SettingsSnapshot LoadSnapshot()
        {
            string json = SessionState.GetString(SnapshotKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SettingsSnapshot>(json);
        }

        private static bool HasNewTemplate()
        {
            SettingsSnapshot snapshot = LoadSnapshot();
            FlexibleProfileSettings settings = Settings;
            if (snapshot == null || settings == null)
                return false;
            var before = new HashSet<string>(snapshot.templates);
            return settings.Templates.Any(t => !before.Contains(TemplateKey(t)));
        }

        // 튜토리얼 중에 바꾼 그리드를 되돌리고, 튜토리얼에서 만든 템플릿을 지운다
        private static void RestoreSettings()
        {
            SettingsSnapshot snapshot = LoadSnapshot();
            SessionState.EraseString(SnapshotKey);
            FlexibleProfileSettings settings = Settings;
            if (snapshot == null || settings == null)
                return;

            Undo.RecordObject(settings, "Flexible Tutorial");
            if (snapshot.grids.Count == settings.Profiles.Count)
            {
                for (int i = 0; i < snapshot.grids.Count; i++)
                    settings.Profiles[i].grid = snapshot.grids[i];
            }
            var before = new HashSet<string>(snapshot.templates);
            settings.Templates.RemoveAll(t => !before.Contains(TemplateKey(t)));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            SceneView.RepaintAll();
        }
    }
}
