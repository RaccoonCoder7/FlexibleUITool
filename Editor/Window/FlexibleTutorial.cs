using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 창 안에서 진행하는 튜토리얼 (기본·심화). 연습용 프리팹을 만들어 열고, 사용자가 실제로 해 보면 다음 단계로 넘어간다 (AI 불필요)
    public static partial class FlexibleTutorial
    {
        private const string StepKey = "FlexibleUITool.TutorialStep";
        private const string MarkKey = "FlexibleUITool.TutorialMark";
        private const string MarkTextKey = "FlexibleUITool.TutorialMarkText";
        private const string BasicDoneKey = "FlexibleUITool.TutorialBasicDone";
        public const string PracticeName = "TutorialPractice";
        private const string MainBlock = "Main";

        private enum Step
        {
            // 기본
            Welcome,
            PressLandscape,
            MoveMain,
            Save,
            PressPortrait,
            PressLandscapeAgain,
            Grid,
            Square,
            Preview,
            Check,
            Finish,

            // 심화
            AdvWelcome,
            OpenGrid,
            ChangeGrid,
            SelectBlock,
            DragCells,
            SetMode,
            SeeMode,
            Nudge,
            SaveAdjust,
            SetRole,
            OpenTemplates,
            SaveTemplate,
            ApplyTemplate,
            AdvFinish,

            // 고르기
            Menu,
            ChapterDone,
        }

        public static bool Active => Current >= 0;

        private static int Current
        {
            get => SessionState.GetInt(StepKey, -1);
            set => SessionState.SetInt(StepKey, value);
        }

        // 단계에 들어올 때 기억해 둔 값 (그 뒤에 바뀌었는지로 판정한다)
        private static int Mark
        {
            get => SessionState.GetInt(MarkKey, 0);
            set => SessionState.SetInt(MarkKey, value);
        }

        private static string MarkText
        {
            get => SessionState.GetString(MarkTextKey, "");
            set => SessionState.SetString(MarkTextKey, value);
        }

        public static string PracticePath => FlexibleSettingsUtility.SampleTargetFolder + "/" + PracticeName + ".prefab";

        // 기본 튜토리얼을 마친 적이 있으면 고르기 화면부터 보여 준다
        public static void Start(FlexibleWindow window)
        {
            Enter(EditorPrefs.GetBool(BasicDoneKey, false) ? Step.Menu : Step.Welcome, window);
        }

        public static void Stop()
        {
            RestoreSettings();
            SessionState.EraseInt(SingleKey);
            Current = -1;
            FlexibleGui.Highlight = null;
        }

        // 조건을 만족하면 다음 단계로. 바뀌면 true
        public static bool Update(FlexibleWindow window)
        {
            if (!Active)
            {
                FlexibleGui.Highlight = null;
                return false;
            }

            var step = (Step)Current;
            FlexibleGui.Highlight = HighlightOf(step, window);
            if (!IsDone(step, window))
                return false;

            Advance(step, window);
            return true;
        }

        private static void Advance(Step step, FlexibleWindow window)
        {
            int single = SessionState.GetInt(SingleKey, -1);
            Step next = single >= 0 && step == Chapters[single].last ? Step.ChapterDone : step + 1;
            Enter(next, window);
        }

        private static void Enter(Step step, FlexibleWindow window)
        {
            Current = (int)step;
            switch (step)
            {
                case Step.Check:
                    Mark = window.CheckRunCount;
                    break;
                case Step.Finish:
                    EditorPrefs.SetBool(BasicDoneKey, true);
                    break;
                case Step.Menu:
                    RestoreSettings();
                    SessionState.EraseInt(SingleKey);
                    break;
                default:
                    EnterAdvanced(step, window);
                    break;
            }
            FlexibleGui.Highlight = HighlightOf(step, window);
            window.Repaint();
        }

        public static void Draw(FlexibleWindow window)
        {
            if (!Active)
                return;

            var step = (Step)Current;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Header(step), FlexibleGui.HeaderLabel, GUILayout.Height(20f));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(T("Quit tutorial"), EditorStyles.miniButton, GUILayout.Width(110f), GUILayout.Height(20f)))
                    {
                        Stop();
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.Space(6f);

                string text = Text(step);
                if (NeedsEditTab(step) && window.CurrentTab != FlexibleWindow.Tab.Edit)
                    text = T("First go back to the <b>Edit</b> tab (orange frame).") + "\n" + text;
                FlexibleGui.BeginMascot(text);
                if (step == Step.Menu)
                    DrawMenu(window);
                else
                    DrawButtons(step, window);
                FlexibleGui.EndMascot();
            }
        }

        private static string Header(Step step)
        {
            if (step <= Step.Finish)
                return F("Tutorial {0} / {1}", (int)step + 1, (int)Step.Finish + 1);
            if (step == Step.Menu)
                return T("Choose a tutorial");
            int chapter = ChapterOf(step);
            if (chapter >= 0)
                return F("Advanced {0} / {1} · {2}", chapter + 1, Chapters.Length, T(Chapters[chapter].title));
            return T("Advanced tutorial");
        }

        private static string Text(Step step)
        {
            switch (step)
            {
                case Step.Welcome:
                    return T("Hi! Let's learn this tool in about 2 minutes.\nI'll make a practice prefab for you, so nothing of yours is touched.");
                case Step.PressLandscape:
                    return IsPracticeOpen()
                        ? T("This is the <b>Portrait</b> screen. It is the base of every screen.\nNow press the <b>Landscape</b> button (orange frame)!")
                        : T("Open the practice prefab first.");
                case Step.MoveMain:
                    return T("You are on the <b>Landscape</b> screen now.\nMove the <b>Main</b> block anywhere you like: drag it in the Scene view, or change Pos X in the Inspector.");
                case Step.Save:
                    return T("Nice! Now save with <b>Ctrl+S</b> (or the Save button).");
                case Step.PressPortrait:
                    return T("Press the <b>Portrait</b> button.\nMain goes back to its portrait position. The two screens are separate!");
                case Step.PressLandscapeAgain:
                    return T("Press <b>Landscape</b> again.\nYour landscape layout is still there.");
                case Step.Grid:
                    return T("(Optional) For neat alignment, open <b>Place on a grid</b>, select a block and drag over the blue cells in the Scene view.");
                case Step.Square:
                    return T("Tablets use the <b>Square</b> screen. It works the same way.\nPress the <b>Square</b> button. Blocks you don't touch keep the portrait layout.");
                case Step.Preview:
                    return T("Open the <b>Preview</b> tab. Drag the window edge to make it wide, then let go and make it narrow.\nWhen the screen name at the bottom changes, you're done.");
                case Step.Check:
                    return T("Last one: the <b>Check</b> tab shows the prefab on many phones and tablets at once.\nOpen it and press <b>Run check</b>.");
                case Step.Finish:
                    return T("All done! In the check, a red bar on a thumbnail means a problem on that device.\nJust remember: 1. Press a screen button  2. Move blocks  3. Ctrl+S\nShall I delete the practice prefab?");
                case Step.Menu:
                    return T("Which tutorial do you want to do?");
                default:
                    return AdvancedText(step);
            }
        }

        private static void DrawButtons(Step step, FlexibleWindow window)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                switch (step)
                {
                    case Step.Welcome:
                        if (GUILayout.Button(T("Start!"), GUILayout.Height(26f)))
                        {
                            SessionState.EraseInt(SingleKey);
                            if (CreatePractice())
                                Enter(Step.PressLandscape, window);
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button(T("Choose another tutorial"), GUILayout.Height(26f)))
                        {
                            Enter(Step.Menu, window);
                            GUIUtility.ExitGUI();
                        }
                        break;
                    case Step.PressLandscape:
                        if (!IsPracticeOpen() && GUILayout.Button(T("Open the practice prefab"), GUILayout.Height(24f)))
                        {
                            OpenPractice();
                            GUIUtility.ExitGUI();
                        }
                        break;
                    case Step.MoveMain:
                        if (GUILayout.Button(T("Select Main for me"), GUILayout.Height(24f)))
                            SelectMain();
                        break;
                    case Step.Grid:
                    case Step.Preview:
                        // 미리보기는 창 크기를 바꾸기 어려운 배치도 있어서 넘어갈 수 있게 둔다
                        if (GUILayout.Button(T("Skip"), GUILayout.Height(24f)))
                            Advance(step, window);
                        break;
                    case Step.Finish:
                        if (GUILayout.Button(T("Try the advanced tutorial"), GUILayout.Height(26f)))
                        {
                            Enter(Step.AdvWelcome, window);
                            GUIUtility.ExitGUI();
                        }
                        DrawFinishButtons(window);
                        break;
                    default:
                        DrawAdvancedButtons(step, window);
                        break;
                }
                GUILayout.FlexibleSpace();
            }
        }

        // 연습용 프리팹을 지우거나 남기고 끝낸다
        private static void DrawFinishButtons(FlexibleWindow window)
        {
            if (GUILayout.Button(T("Delete it and finish"), GUILayout.Height(26f)))
            {
                DeletePractice();
                Stop();
                window.ShowTab(FlexibleWindow.Tab.Edit);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(T("Keep it and finish"), GUILayout.Height(26f)))
            {
                Stop();
                window.ShowTab(FlexibleWindow.Tab.Edit);
                GUIUtility.ExitGUI();
            }
        }

        // 다시 해 보고 싶은 튜토리얼 고르기
        private static void DrawMenu(FlexibleWindow window)
        {
            if (GUILayout.Button(T("Basic: switch screens and move blocks"), GUILayout.Height(24f)))
            {
                Enter(Step.Welcome, window);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(T("Advanced: everything"), GUILayout.Height(24f)))
            {
                Enter(Step.AdvWelcome, window);
                GUIUtility.ExitGUI();
            }
            for (int i = 0; i < Chapters.Length; i++)
            {
                if (GUILayout.Button(F("Advanced {0}. {1}", i + 1, T(Chapters[i].title)), GUILayout.Height(22f)))
                {
                    StartChapter(i, true, window);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 편집 탭에 있는 버튼을 눌러야 하는 단계
        private static bool NeedsEditTab(Step step)
        {
            switch (step)
            {
                case Step.PressLandscape:
                case Step.MoveMain:
                case Step.Save:
                case Step.PressPortrait:
                case Step.PressLandscapeAgain:
                case Step.Grid:
                case Step.Square:
                    return IsPracticeOpen();
                default:
                    return AdvancedNeedsEditTab(step);
            }
        }

        private static string HighlightOf(Step step, FlexibleWindow window)
        {
            if (NeedsEditTab(step) && window.CurrentTab != FlexibleWindow.Tab.Edit)
                return "tab:" + (int)FlexibleWindow.Tab.Edit;

            switch (step)
            {
                case Step.PressLandscape:
                case Step.PressLandscapeAgain:
                    return "screen:landscape";
                case Step.PressPortrait:
                    return "screen:portrait";
                case Step.Save:
                    return "save";
                case Step.Grid:
                    return "grid";
                case Step.Preview:
                    return window.CurrentTab == FlexibleWindow.Tab.Preview ? null : "tab:" + (int)FlexibleWindow.Tab.Preview;
                case Step.Square:
                    return "screen:square";
                case Step.Check:
                    return window.CurrentTab == FlexibleWindow.Tab.Check ? "check:run" : "tab:" + (int)FlexibleWindow.Tab.Check;
                default:
                    return AdvancedHighlight(step, window);
            }
        }

        private static bool IsDone(Step step, FlexibleWindow window)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool practice = IsPracticeOpen();
            switch (step)
            {
                case Step.PressLandscape:
                case Step.PressLandscapeAgain:
                    return practice && FlexibleScreenSession.GetActiveProfileId(stage) == "landscape";
                case Step.MoveMain:
                {
                    RectTransform main = FindMain(stage);
                    return practice && main != null && FlexibleScreenSession.GetStatus(stage, main) == FlexibleBlockStatus.Own;
                }
                case Step.Save:
                    return practice && SavedMainHasLandscape();
                case Step.PressPortrait:
                    return practice && FlexibleScreenSession.IsBaseView(stage);
                case Step.Grid:
                {
                    FlexibleWorkspace session = practice ? FlexibleScreenSession.GetSession(stage) : null;
                    return session != null && session.placements.Count > 0;
                }
                case Step.Square:
                    return practice && FlexibleScreenSession.GetActiveProfileId(stage) == "square";
                case Step.Preview:
                    return window.CurrentTab == FlexibleWindow.Tab.Preview && window.PreviewSawTwoScreens;
                case Step.Check:
                    return window.CurrentTab == FlexibleWindow.Tab.Check && window.CheckRunCount > Mark;
                default:
                    return AdvancedIsDone(step, window, stage, practice);
            }
        }

        // ───────── 연습용 프리팹 ─────────

        private static bool IsPracticeOpen()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null && stage.assetPath == PracticePath && FlexibleScreenSession.IsReady(stage, out _);
        }

        private static RectTransform FindMain(PrefabStage stage)
        {
            return FindBlock(stage, MainBlock);
        }

        private static RectTransform FindBlock(PrefabStage stage, string name)
        {
            if (stage == null || stage.prefabContentsRoot == null)
                return null;
            return stage.prefabContentsRoot.transform.Find(name) as RectTransform;
        }

        private static void SelectMain()
        {
            RectTransform main = FindMain(PrefabStageUtility.GetCurrentPrefabStage());
            if (main == null)
                return;
            Selection.activeTransform = main;
            EditorGUIUtility.PingObject(main);
            FrameWholePrefab(main);
        }

        // 고른 블록만 크게 비추면 화면 전체가 안 보이므로 프리팹 전체가 들어오게 비춘다
        private static void FrameWholePrefab(RectTransform block)
        {
            SceneView view = SceneView.lastActiveSceneView;
            var root = block.root as RectTransform;
            if (view == null || root == null)
                return;

            var corners = new Vector3[4];
            root.GetWorldCorners(corners);
            var bounds = new Bounds(corners[0], Vector3.zero);
            foreach (Vector3 corner in corners)
                bounds.Encapsulate(corner);
            view.Frame(bounds, false);
        }

        private static bool SavedMainHasLandscape()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PracticePath);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null)
                return false;
            FlexibleLayoutEntry entry = layout.Entries.Find(e => e.target != null && e.target.name == MainBlock);
            return entry != null && entry.Find("landscape") != null;
        }

        // 샘플을 복사해 다른 화면 기록을 지운 연습용 프리팹을 만들고 연다 (이미 있으면 새로 만든다)
        private static bool CreatePractice()
        {
            string folder = FlexibleSettingsUtility.ImportTutorialSample(out _);
            string sample = folder != null ? folder + "/" + FlexibleSettingsUtility.SamplePrefabName + ".prefab" : null;
            if (sample == null || AssetDatabase.LoadAssetAtPath<GameObject>(sample) == null)
            {
                EditorUtility.DisplayDialog("Flexible UI", T("The tutorial sample could not be found. Reinstall the package and try again."), T("OK"));
                return false;
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PracticePath)
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PracticePath) != null)
                AssetDatabase.DeleteAsset(PracticePath);
            if (!AssetDatabase.CopyAsset(sample, PracticePath))
                return false;

            GameObject root = PrefabUtility.LoadPrefabContents(PracticePath);
            try
            {
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout != null && layout.Settings != null)
                {
                    string baseId = layout.Settings.BaseProfileId;
                    foreach (FlexibleLayoutEntry entry in layout.Entries)
                        entry.states.RemoveAll(s => s.profileId != baseId);
                    layout.Entries.RemoveAll(e => e.states.Count == 0);
                    // 연습 중 그리드를 바꿔도 다른 프리팹에 영향이 없게 전용 그리드를 쓴다
                    layout.UseOwnGrid = true;
                    layout.FillOwnGrids();
                }
                PrefabUtility.SaveAsPrefabAsset(root, PracticePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            OpenPractice();
            return true;
        }

        private static void OpenPractice()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PracticePath) == null)
            {
                CreatePractice();
                return;
            }
            PrefabStageUtility.OpenPrefab(PracticePath);
            EditorApplication.delayCall += SelectMain;
        }

        private static void DeletePractice()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PracticePath)
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PracticePath) != null)
                AssetDatabase.MoveAssetToTrash(PracticePath);
        }
    }
}
