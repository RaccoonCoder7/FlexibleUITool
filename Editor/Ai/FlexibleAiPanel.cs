using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 편집 탭의 「AI로 자동 배치」 영역과, 실행 중·실행 후 창 맨 위에 보이는 상태 상자
    public static class FlexibleAiPanel
    {
        private const string LandscapeKey = "FlexibleUITool.AiLandscape";
        private const string SquareKey = "FlexibleUITool.AiSquare";
        private const string ModelKey = "FlexibleUITool.AiModel";
        private const string EffortKey = "FlexibleUITool.AiEffort";
        // 추가 지시는 프리팹마다 따로 기억한다 (뒤에 프리팹 GUID)
        private const string NoteKey = "FlexibleUITool.AiNote.";
        private static Vector2 noteScroll;

        // claude --model 별칭 (빈 값 = 계정 설정)
        private static readonly string[] ModelValues = { "", "sonnet", "opus", "haiku", "fable" };
        private static readonly string[] ModelLabels =
        {
            "Claude Code default",
            "Sonnet (balanced, recommended)",
            "Opus (smarter, uses more)",
            "Haiku (fastest, uses least)",
            "Fable (smartest, uses the most)",
        };

        // claude --effort 값 (빈 값 = 계정 설정)
        private static readonly string[] EffortValues = { "", "low", "medium", "high", "xhigh", "max" };
        private static readonly string[] EffortLabels =
        {
            "Claude Code default",
            "Low (fastest, uses least)",
            "Medium (recommended)",
            "High",
            "Very high",
            "Max (most careful, uses the most)",
        };

        // ───────── 편집 탭: 연결·시작 ─────────

        public static void DrawLauncher(PrefabStage stage, FlexibleLayout layout)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(T("Auto-layout with AI (Claude Code)"), EditorStyles.boldLabel);
                if (FlexibleAiRun.Current == FlexibleAiRun.State.Running)
                {
                    GUILayout.Label(T("The AI is working. See the box at the top of this window."), EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                if (!FlexibleClaudeCode.Connected)
                {
                    DrawConnect();
                    return;
                }
                DrawStart(stage, layout);
            }
        }

        private static void DrawConnect()
        {
            switch (FlexibleClaudeCode.Current)
            {
                case FlexibleClaudeCode.Status.Checking:
                    GUILayout.Label(T("Checking Claude Code on this PC..."), EditorStyles.wordWrappedMiniLabel);
                    break;

                case FlexibleClaudeCode.Status.NotInstalled:
                    GUILayout.Label(T("Claude Code is not installed on this PC. Press Install: a window opens and installs it (about a minute). Then press Check again."), FlexibleGui.WrapLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(T("Install Claude Code"), GUILayout.Height(26f)))
                            FlexibleClaudeCode.OpenInstaller();
                        if (GUILayout.Button(T("Check again"), GUILayout.Height(26f)))
                            FlexibleClaudeCode.CheckAsync();
                    }
                    DrawPathField();
                    break;

                case FlexibleClaudeCode.Status.NotLoggedIn:
                    GUILayout.Label(T("Log in to Claude once. Press Log in: a window and your browser open. Log in with your Claude account (Pro, Max, Team or Enterprise), then press Check again."), FlexibleGui.WrapLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(T("Log in"), GUILayout.Height(26f)))
                            FlexibleClaudeCode.OpenLogin();
                        if (GUILayout.Button(T("Check again"), GUILayout.Height(26f)))
                            FlexibleClaudeCode.CheckAsync();
                    }
                    break;

                default:
                    GUILayout.Label(T("The AI can make the landscape and square layouts for you. It uses Claude Code on this PC with your Claude subscription."), FlexibleGui.WrapLabel);
                    if (!string.IsNullOrEmpty(FlexibleClaudeCode.Detail) && FlexibleClaudeCode.Current == FlexibleClaudeCode.Status.Unknown)
                        EditorGUILayout.HelpBox(FlexibleClaudeCode.Detail, MessageType.Warning);
                    if (GUILayout.Button(T("Connect AI"), FlexibleGui.BigButton, GUILayout.Height(30f)))
                        FlexibleClaudeCode.CheckAsync();
                    FlexibleGui.Mark("ai:connect");
                    break;
            }
        }

        private static void DrawPathField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(T("Or choose claude.exe yourself"), EditorStyles.miniLabel);
                if (GUILayout.Button(T("Choose..."), EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    string path = EditorUtility.OpenFilePanel("claude", "", "exe,cmd");
                    if (!string.IsNullOrEmpty(path))
                    {
                        FlexibleClaudeCode.CustomPath = path;
                        FlexibleClaudeCode.CheckAsync();
                    }
                }
            }
        }

        private static void DrawStart(PrefabStage stage, FlexibleLayout layout)
        {
            FlexibleProfileSettings settings = layout.Settings;
            bool landscape = EditorPrefs.GetBool(LandscapeKey, true);
            bool square = EditorPrefs.GetBool(SquareKey, true);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(T("Screens to make:"), GUILayout.ExpandWidth(false));
                if (settings.Find("landscape") != null)
                    landscape = GUILayout.Toggle(landscape, FlexibleEditPanel.Name(settings.Find("landscape")), EditorStyles.miniButton);
                if (settings.Find("square") != null)
                    square = GUILayout.Toggle(square, FlexibleEditPanel.Name(settings.Find("square")), EditorStyles.miniButton);
                GUILayout.FlexibleSpace();
            }
            EditorPrefs.SetBool(LandscapeKey, landscape);
            EditorPrefs.SetBool(SquareKey, square);

            var profiles = new List<string>();
            if (landscape && settings.Find("landscape") != null)
                profiles.Add("landscape");
            if (square && settings.Find("square") != null)
                profiles.Add("square");

            string model = DrawChoice(T("Model"), ModelKey, ModelValues, ModelLabels, "sonnet");
            string effort = DrawChoice(T("Effort"), EffortKey, EffortValues, EffortLabels, "medium");
            GUILayout.Label(T("Smarter models and higher effort give better layouts but take longer and use more of your Claude usage."), EditorStyles.wordWrappedMiniLabel);
            string note = DrawNote(stage);

            using (new EditorGUI.DisabledScope(profiles.Count == 0))
            {
                if (GUILayout.Button(T("Auto-layout with AI"), FlexibleGui.BigButton, GUILayout.Height(32f)))
                {
                    if (Confirm(layout, profiles))
                        FlexibleAiRun.Start(stage, profiles, model, effort, note);
                    GUIUtility.ExitGUI();
                }
                FlexibleGui.Mark("ai:start");
            }
            GUILayout.Label(T("Takes a few minutes and uses your Claude usage. The prefab is saved and closed while the AI works, and opens again when it is done."), EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(FlexibleClaudeCode.Detail, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("Disconnect"), EditorStyles.miniButton, GUILayout.Width(90f)))
                {
                    FlexibleClaudeCode.Connected = false;
                    FlexibleClaudeCode.CheckAsync();
                }
            }
        }

        // 고른 값은 사용자별로 기억한다. 빈 값 = Claude Code 계정 설정을 따른다
        private static string DrawChoice(string label, string key, string[] values, string[] labels, string fallback)
        {
            string current = EditorPrefs.GetString(key, fallback);
            int index = Mathf.Max(0, System.Array.IndexOf(values, current));
            string[] shown = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                shown[i] = T(labels[i]);

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Model"), T("Effort"));
            index = EditorGUILayout.Popup(label, index, shown);
            EditorGUIUtility.labelWidth = labelWidth;
            EditorPrefs.SetString(key, values[index]);
            return values[index];
        }

        // 선택 입력: AI에게 덧붙일 지시. 비워 두면 알아서 배치한다
        private static string DrawNote(PrefabStage stage)
        {
            string key = NoteKey + AssetDatabase.AssetPathToGUID(stage.assetPath);
            string note = EditorPrefs.GetString(key, "");

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(T("Extra instructions (optional)"), EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(note.Length == 0))
                {
                    if (GUILayout.Button(T("Clear"), EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        note = "";
                        GUI.FocusControl(null);
                    }
                }
            }
            noteScroll = EditorGUILayout.BeginScrollView(noteScroll, GUILayout.Height(54f));
            note = EditorGUILayout.TextArea(note, FlexibleGui.WrapTextArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            GUILayout.Label(T("Leave it empty and the AI decides by itself. Example: \"In landscape, put the menu buttons on the right. Hide the banner on the square screen.\""), EditorStyles.wordWrappedMiniLabel);
            EditorPrefs.SetString(key, note);
            return note;
        }

        // 손으로 만든 배치를 덮어쓸 수 있으면 먼저 묻는다
        private static bool Confirm(FlexibleLayout layout, List<string> profiles)
        {
            var existing = new List<string>();
            foreach (string id in profiles)
            {
                int count = FlexibleScreenSession.CountRecords(layout, id);
                if (count > 0)
                    existing.Add(F("{0}: {1} changed", FlexibleEditPanel.Name(layout.Settings.Find(id)), count));
            }
            if (existing.Count == 0)
                return true;
            return EditorUtility.DisplayDialog(T("Auto-layout with AI"),
                T("These screens already have their own layout. The AI may change it.") + "\n" + string.Join("\n", existing) + "\n\n" + T("You can go back with Undo AI changes afterwards."),
                T("Continue"), T("Cancel"));
        }

        // ───────── 창 맨 위: 진행 상황·결과 ─────────

        public static void DrawStatus()
        {
            FlexibleAiRun.State state = FlexibleAiRun.Current;
            if (state == FlexibleAiRun.State.Idle)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string name = Path.GetFileNameWithoutExtension(FlexibleAiRun.PrefabPath ?? "");
                switch (state)
                {
                    case FlexibleAiRun.State.Running:
                        GUILayout.Label(F("AI is laying out {0}... ({1:0}s)", name, FlexibleAiRun.Elapsed), EditorStyles.boldLabel);
                        GUILayout.Label(F("{0} (step {1})", FlexibleAiRun.Status, FlexibleAiRun.Steps), FlexibleGui.WrapLabel);
                        if (!string.IsNullOrEmpty(FlexibleAiRun.LastThought))
                            GUILayout.Label(Shorten(FlexibleAiRun.LastThought, 200), EditorStyles.wordWrappedMiniLabel);
                        // 생각이 길면 한동안 아무것도 안 바뀌므로 멈춘 게 아니라고 알려 준다
                        if (EditorApplication.timeSinceStartup - FlexibleAiRun.LastActivityAt > 20.0)
                            GUILayout.Label(T("The AI is thinking. This can take a minute or two; it has not stopped."), EditorStyles.wordWrappedMiniLabel);
                        GUILayout.Label(T("Do not open this prefab until the AI is done."), EditorStyles.miniLabel);
                        if (GUILayout.Button(T("Stop"), GUILayout.Width(80f)))
                            FlexibleAiRun.Cancel();
                        break;

                    default:
                        string title = state == FlexibleAiRun.State.Succeeded ? F("AI finished {0} ({1:0}s)", name, FlexibleAiRun.Elapsed)
                            : state == FlexibleAiRun.State.Canceled ? T("AI stopped")
                            : T("AI could not finish");
                        GUILayout.Label(title, EditorStyles.boldLabel);
                        GUILayout.Label(FlexibleAiRun.Summary, FlexibleGui.WrapLabel);
                        if (!string.IsNullOrEmpty(FlexibleAiRun.Usage))
                            GUILayout.Label(FlexibleAiRun.Usage, EditorStyles.wordWrappedMiniLabel);
                        if (state == FlexibleAiRun.State.Succeeded)
                            GUILayout.Label(T("Check each screen with the screen buttons. Fix anything by hand and save, or undo everything the AI did."), EditorStyles.wordWrappedMiniLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (FlexibleAiRun.CanRevert && GUILayout.Button(T("Undo AI changes"), GUILayout.Height(24f))
                                && EditorUtility.DisplayDialog(T("Undo AI changes"), T("Put the prefab back as it was before the AI started?"), T("Undo"), T("Cancel")))
                            {
                                FlexibleAiRun.Revert();
                                GUIUtility.ExitGUI();
                            }
                            if (GUILayout.Button(state == FlexibleAiRun.State.Succeeded ? T("Keep it") : T("Close"), GUILayout.Height(24f)))
                                FlexibleAiRun.Dismiss();
                            GUILayout.FlexibleSpace();
                        }
                        break;
                }
            }
        }

        private static string Shorten(string text, int max)
        {
            text = text.Replace("\r", "").Replace("\n", " ");
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
