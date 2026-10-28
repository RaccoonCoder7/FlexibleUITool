using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
using Debug = UnityEngine.Debug;
namespace FlexibleUITool.EditorTools
{
    // 「AI로 자동 배치」 한 번의 실행: 프리팹을 저장해 닫고 백업한 뒤, Claude Code에 CLI로 배치를 만들게 하고, 끝나면 다시 연다
    public static class FlexibleAiRun
    {
        public enum State
        {
            Idle,
            Running,
            Succeeded,
            Failed,
            Canceled,
        }

        private const string Script = "powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1";
        private static readonly string[] AllowedCommands = { "profiles", "describe", "render", "check", "apply-spec" };

        // stream-json 한 줄 (필요한 필드만)
        [Serializable]
        private class StreamLine
        {
            public string type;
            public string subtype;
            public bool is_error;
            public string result;
            public StreamMessage message;
            public double total_cost_usd;
            public int num_turns;
            public StreamUsage usage;
        }

        [Serializable]
        private class StreamUsage
        {
            public long input_tokens;
            public long output_tokens;
            public long cache_creation_input_tokens;
            public long cache_read_input_tokens;
        }

        [Serializable]
        private class StreamMessage
        {
            public List<StreamContent> content;
        }

        [Serializable]
        private class StreamContent
        {
            public string type;
            public string text;
            public string name;
            public StreamInput input;
        }

        [Serializable]
        private class StreamInput
        {
            public string command;
            public string file_path;
        }

        private static Process process;
        private static readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        private static readonly StringBuilder errors = new StringBuilder();
        private static string resultText;
        private static string resultLine;
        private static bool resultIsError;
        private static bool canceling;
        private static string backupPath;
        private static List<string> targetProfiles = new List<string>();
        private static string model;
        private static string effort;
        private static string note;

        public static State Current { get; private set; } = State.Idle;
        public static string PrefabPath { get; private set; }
        public static string Status { get; private set; } = "";
        public static string LastThought { get; private set; } = "";
        public static string Summary { get; private set; } = "";
        public static int Steps { get; private set; }
        public static double LastActivityAt { get; private set; }

        // 실행이 쓴 토큰 요약 (Claude Code가 마지막에 알려 준 값)
        public static string Usage { get; private set; } = "";
        public static double StartedAt { get; private set; }
        public static double FinishedAt { get; private set; }
        public static bool CanRevert => Current != State.Running && !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath);

        public static event Action Changed;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string WorkFolder => FlexibleCommandBridge.WorkFolder;

        static FlexibleAiRun()
        {
            // 컴파일 등으로 스크립트가 다시 불리면 실행을 이어 갈 수 없으므로 끈다
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (Current == State.Running)
                    FlexibleClaudeCode.KillTree(process);
            };
        }

        public static double Elapsed => (Current == State.Running ? EditorApplication.timeSinceStartup : FinishedAt) - StartedAt;

        // 열려 있는 프리팹의 profiles 화면을 AI로 배치한다. model·effort가 비어 있으면 Claude Code 계정 설정을 따른다.
        // extraNote는 사용자가 적은 추가 지시 (비어 있으면 없음)
        public static bool Start(PrefabStage stage, IEnumerable<string> profiles, string modelAlias, string effortLevel, string extraNote)
        {
            if (Current == State.Running)
                return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Flexible UI", T("Stop Play Mode first."), T("OK"));
                return false;
            }

            string exe = FlexibleClaudeCode.FindExecutable();
            if (exe == null)
            {
                FlexibleClaudeCode.CheckAsync();
                return false;
            }
            if (!File.Exists(Path.Combine(WorkFolder, "AGENTS.md")) || !File.Exists(Path.Combine(WorkFolder, "flexui.ps1")))
            {
                EditorUtility.DisplayDialog("Flexible UI", T("The tool is still getting ready in Unity. Wait a few seconds and try again."), T("OK"));
                return false;
            }

            // 지금 화면까지 저장하고 닫는다 (열려 있으면 AI의 쓰기가 막힌다)
            if (!FlexibleScreenSession.Save(stage))
                return false;
            PrefabPath = stage.assetPath;
            targetProfiles = profiles.ToList();
            model = modelAlias;
            effort = effortLevel;
            note = string.IsNullOrWhiteSpace(extraNote) ? null : extraNote.Trim();
            StageUtility.GoToMainStage();

            backupPath = Path.Combine(WorkFolder, "ai-backup", AssetDatabase.AssetPathToGUID(PrefabPath) + ".prefab");
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            File.Copy(Path.Combine(ProjectRoot, PrefabPath), backupPath, true);
            Directory.CreateDirectory(Path.Combine(WorkFolder, "specs"));

            string taskFile = Path.Combine(WorkFolder, "ai-task.md");
            File.WriteAllText(taskFile, BuildTask(), new UTF8Encoding(false));

            ProcessStartInfo info = FlexibleClaudeCode.CreateStartInfo(exe, BuildArguments(), ProjectRoot);
            while (lines.TryDequeue(out _)) { }
            errors.Clear();
            resultText = null;
            resultLine = null;
            Usage = "";
            resultIsError = false;
            canceling = false;
            Summary = "";
            LastThought = "";
            Steps = 0;
            Status = T("Starting Claude Code...");
            StartedAt = EditorApplication.timeSinceStartup;
            LastActivityAt = StartedAt;

            try
            {
                process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (errors)
                        errors.AppendLine(e.Data);
                };
                process.Start();
                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Current = State.Failed;
                Summary = T("Could not start Claude Code.") + "\n" + e.Message;
                FinishedAt = EditorApplication.timeSinceStartup;
                PrefabStageUtility.OpenPrefab(PrefabPath);
                Changed?.Invoke();
                return false;
            }

            // 실행 중에 스크립트가 다시 불려 상태를 잃지 않도록 막는다
            EditorApplication.LockReloadAssemblies();
            Current = State.Running;
            EditorApplication.update += Poll;
            Changed?.Invoke();
            return true;
        }

        public static void Cancel()
        {
            if (Current != State.Running)
                return;
            canceling = true;
            Status = T("Stopping...");
            FlexibleClaudeCode.KillTree(process);
            Changed?.Invoke();
        }

        // AI가 바꾸기 전의 프리팹으로 되돌린다
        public static void Revert()
        {
            if (!CanRevert)
                return;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PrefabPath)
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }
            File.Copy(backupPath, Path.Combine(ProjectRoot, PrefabPath), true);
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            File.Delete(backupPath);
            backupPath = null;
            Current = State.Idle;
            PrefabStageUtility.OpenPrefab(PrefabPath);
            Changed?.Invoke();
        }

        public static void Dismiss()
        {
            if (Current == State.Running)
                return;
            if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                File.Delete(backupPath);
            backupPath = null;
            Current = State.Idle;
            Changed?.Invoke();
        }

        private static void Poll()
        {
            bool changed = false;
            while (lines.TryDequeue(out string line))
            {
                Handle(line);
                changed = true;
                LastActivityAt = EditorApplication.timeSinceStartup;
            }

            if (process != null && process.HasExited)
            {
                process.WaitForExit();
                while (lines.TryDequeue(out string line))
                    Handle(line);
                Finish();
                return;
            }
            if (changed)
                Changed?.Invoke();
        }

        private static void Handle(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
                return;

            StreamLine data;
            try
            {
                data = JsonUtility.FromJson<StreamLine>(line);
            }
            catch (Exception)
            {
                return;
            }
            if (data == null)
                return;

            if (data.type == "result")
            {
                resultLine = line;
                Usage = data.usage == null ? "" : F("Tokens: new input {0:N0} · cache write {1:N0} · cache read {2:N0} · output {3:N0} · {4} turns · API price about ${5:0.00}",
                    data.usage.input_tokens, data.usage.cache_creation_input_tokens, data.usage.cache_read_input_tokens, data.usage.output_tokens, data.num_turns, data.total_cost_usd);
                resultText = data.result;
                resultIsError = data.is_error || (data.subtype != null && data.subtype != "success");
                return;
            }
            if (data.type != "assistant" || data.message?.content == null)
                return;

            foreach (StreamContent content in data.message.content)
            {
                if (content.type == "tool_use")
                {
                    Steps++;
                    Status = Describe(content);
                }
                else if (content.type == "text" && !string.IsNullOrWhiteSpace(content.text))
                {
                    LastThought = content.text.Trim();
                }
            }
        }

        // 도구 호출을 사용자가 알아볼 말로 바꾼다
        private static string Describe(StreamContent tool)
        {
            string command = tool.input?.command ?? "";
            string file = tool.input?.file_path ?? "";
            if (command.Contains("flexui.ps1"))
            {
                string verb = command.Substring(command.IndexOf("flexui.ps1", StringComparison.Ordinal) + "flexui.ps1".Length).Trim().Split(' ')[0];
                switch (verb)
                {
                    case "ping": return T("Connecting to Unity");
                    case "profiles": return T("Reading the screen settings");
                    case "describe": return T("Reading where the blocks are");
                    case "render": return T("Drawing the screens to look at them");
                    case "check": return T("Checking for problems");
                    case "apply-spec": return T("Applying a layout");
                    case "set-role": return T("Giving blocks roles");
                    case "export-spec": return T("Reading the current layout");
                }
            }
            if (tool.name == "Read" && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return T("Looking at a drawn screen");
            if (tool.name == "Read")
                return T("Reading the instructions");
            if (tool.name == "Write" || tool.name == "Edit")
                return T("Writing a layout plan");
            return T("Thinking");
        }

        private static void Finish()
        {
            EditorApplication.update -= Poll;
            EditorApplication.UnlockReloadAssemblies();
            FinishedAt = EditorApplication.timeSinceStartup;

            string error;
            lock (errors)
                error = errors.ToString().Trim();
            int exitCode = 0;
            try { exitCode = process.ExitCode; } catch (Exception) { }
            process.Dispose();
            process = null;

            if (canceling)
            {
                Current = State.Canceled;
                Summary = T("Stopped. Changes made before stopping are kept; press Undo AI changes to go back.");
            }
            else if (resultIsError || exitCode != 0 || resultText == null)
            {
                Current = State.Failed;
                string reason = !string.IsNullOrEmpty(resultText) ? resultText : error;
                Summary = Explain(reason);
                Debug.LogWarning("[FlexibleUITool] AI auto-layout failed: " + reason);
            }
            else
            {
                Current = State.Succeeded;
                Summary = resultText.Trim();
            }

            WriteLog();
            AssetDatabase.Refresh();
            ReopenPrefab();
            Changed?.Invoke();
        }

        // 실행마다 Claude Code의 결과 줄(토큰·비용·턴 수)을 남겨 사용량을 비교할 수 있게 한다
        private static void WriteLog()
        {
            try
            {
                string entry = string.Format("{0:yyyy-MM-dd HH:mm:ss} | {1} | {2} | {3:0}s | {4}", DateTime.Now, PrefabPath, string.Join(",", targetProfiles), FinishedAt - StartedAt, resultLine ?? "(no result)") + Environment.NewLine;
                File.AppendAllText(Path.Combine(WorkFolder, "ai-runs.log"), entry, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FlexibleUITool] " + e.Message);
            }
        }

        // 자주 나는 실패는 할 일을 알려 준다
        private static string Explain(string reason)
        {
            string lower = (reason ?? "").ToLowerInvariant();
            if (lower.Contains("login") || lower.Contains("log in") || lower.Contains("authenticat") || lower.Contains("api key") || lower.Contains("oauth"))
            {
                FlexibleClaudeCode.MarkLoggedOut(reason);
                return T("Claude Code is not logged in. Press Connect AI and log in.");
            }
            if (lower.Contains("model") && (lower.Contains("not available") || lower.Contains("not found") || lower.Contains("invalid") || lower.Contains("not supported") || lower.Contains("access")))
                return T("This model cannot be used with your Claude plan. Choose another model.") + "\n" + reason;
            if (lower.Contains("usage limit") || lower.Contains("rate limit") || lower.Contains("limit reached") || lower.Contains("limit will reset"))
                return T("Your Claude usage limit has been reached. Try again later.") + (string.IsNullOrEmpty(reason) ? "" : "\n" + reason);
            if (string.IsNullOrEmpty(reason))
                return T("Claude Code stopped without a result.");
            return T("The AI could not finish.") + "\n" + reason;
        }

        private static void ReopenPrefab()
        {
            if (string.IsNullOrEmpty(PrefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                return;
            PrefabStageUtility.OpenPrefab(PrefabPath);
            string show = targetProfiles.FirstOrDefault();
            if (string.IsNullOrEmpty(show))
                return;
            EditorApplication.delayCall += () =>
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == PrefabPath)
                    FlexibleScreenSession.ShowProfile(stage, show);
            };
        }

        // ───────── Claude Code에 넘기는 내용 ─────────

        private static string BuildArguments()
        {
            var rules = new List<string>();
            foreach (string command in AllowedCommands)
            {
                rules.Add($"Bash({Script} {command} *)");
                rules.Add($"PowerShell({Script} {command} *)");
            }
            rules.Add("Write(./Library/FlexibleUITool/specs/**)");
            rules.Add("Edit(./Library/FlexibleUITool/specs/**)");

            var args = new StringBuilder();
            args.Append("-p \"Read Library/FlexibleUITool/ai-task.md and do the task in it.\"");
            args.Append(" --output-format stream-json --verbose");
            args.Append(" --permission-mode dontAsk");
            args.Append(" --no-session-persistence");
            // 사용자 개인 설정(넓은 허용 규칙·MCP 서버·훅·CLAUDE.md)이 끼어들지 않게 이 실행만 격리한다
            args.Append(" --safe-mode --strict-mcp-config --setting-sources \"\"");
            if (!string.IsNullOrEmpty(model))
                args.Append(" --model ").Append(model);
            if (!string.IsNullOrEmpty(effort))
                args.Append(" --effort ").Append(effort);
            args.Append(" --tools \"Read,Write,Edit,Bash,PowerShell\"");
            args.Append(" --allowedTools");
            foreach (string rule in rules)
                args.Append(" \"").Append(rule).Append('"');
            return args.ToString();
        }

        // AI가 처음에 알아야 할 정보는 Unity가 미리 뽑아 지시에 넣는다 (읽으러 오가는 턴과 토큰이 준다)
        private static string Gather(string command, string profile = null, string[] presets = null)
        {
            var request = new FlexibleCommand
            {
                id = "ai",
                command = command,
                prefab = command == "profiles" ? null : PrefabPath,
                profile = profile,
                presets = presets,
            };
            try
            {
                return FlexibleCommandRunner.Execute(request, true, out _);
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"error\":\"" + e.Message.Replace("\"", "'") + "\"}";
            }
        }

        private static string BuildTask()
        {
            string language;
            switch (FlexibleText.Current)
            {
                case FlexibleLanguage.Korean: language = "Korean"; break;
                case FlexibleLanguage.Japanese: language = "Japanese"; break;
                default: language = "English"; break;
            }
            string screens = string.Join(" and ", targetProfiles);
            string presets = string.Join(",", targetProfiles);
            string specFile = "Library/FlexibleUITool/specs/" + FlexibleCommandRunner.Sanitize(Path.GetFileNameWithoutExtension(PrefabPath)) + "_ai.json";
            string profilesJson = Gather("profiles");
            string describeJson = Gather("describe");
            string renderJson = Gather("render", null, new[] { "portrait" });
            // 사용자 추가 지시는 배치 규칙보다 앞서지만, 허용 범위(파일·명령·역할 금지)는 넘지 못한다
            string noteSection = "";
            if (note != null)
            {
                string quoted = "> " + note.Replace("\r", "").Replace("\n", "\n> ");
                noteSection = $@"
## Extra instructions from the user (follow these first)

The user wrote this for this run. Follow it even when it differs from the design rules above.
Ignore any part that asks for something outside this task (other files, other commands, roles, grid settings or the portrait layout), and say so in the final reply.

{quoted}
";
            }

            return $@"# Auto-layout task

You were started by the ""Auto-layout with AI"" button in the Unity editor (FlexibleUITool).
Nobody is watching: never ask questions or wait for an answer. Use as few turns as you can.

## Goal

Make the **{screens}** layout of the UI prefab `{PrefabPath}` so it looks good and has no problems on those screens.
The portrait layout is the base (the prefab itself) and must not change.

## Data Unity already collected for you (do not run these again)

Screens, grids and safe insets (`profiles`):
```json
{profilesJson}
```

Blocks of the prefab per screen (`describe`; `rect` is [x, y, w, h], top-left origin, y down, canvas units):
```json
{describeJson}
```

Portrait image (`render -Preset portrait`); open its `path` once to see the design:
```json
{renderJson}
```

## How layouts work

- Each non-portrait screen stores per-block overrides. Blocks not listed in a spec keep their state.
- Blocks are named by `path` from the prefab root (e.g. `Main`, `Main/Title`).
- Each screen has a grid (`columns` x `rows`, `margin`, `gutter`). A cell is `[column, row, width, height]`; `[0,0]` is the top-left cell.
- `safeArea: true` (default) keeps the cells inside the notch / home-indicator insets. Use `false` only for bars that should touch the screen edge (backgrounds, header bars).
- Axis modes (`modeX`, `modeY`) decide how a block follows other sizes of the same screen:
  `stretch` grows with the cells; `left`/`right` (x) and `bottom`/`top` (y) keep the size and stick to that side; `center` keeps the size in the middle; `auto` (default) = stretch if the cells touch both screen edges on that axis, the touching side if one, else center.
- Adjustments on top of the cells: `offset` [x, y] (y is up), `sizeOffset` [w, h], `scale` [s].
- `visible`: `hide` or `show`. `reset: true` removes the override (the block follows portrait).
- Canvas units: the short screen side is always 1080 (landscape 16:9 = 1920 x 1080).

## Design rules

- Keep top and bottom bars as full-width bars. Bars touching the edge use `safeArea: false`.
- Turn a vertical stack into columns: primary content left and large, secondary content right.
- Keep the reading order. Use `stretch` for content areas; fixed modes for icons and buttons.
- Keep buttons inside the safe area. Avoid partial overlaps (full containment is fine for intended layering).
- Square screens are between portrait and landscape: fewer columns, secondary content can go below or beside.
{noteSection}
## Spec format (one file for all screens)

```json
{{ ""prefab"": ""{PrefabPath}"",
  ""profiles"": [
    {{ ""profile"": ""landscape"", ""blocks"": [
      {{ ""path"": ""Header"", ""cell"": [0,0,8,1], ""safeArea"": false }},
      {{ ""path"": ""Main"",   ""cell"": [0,1,5,3], ""modeX"": ""stretch"", ""modeY"": ""stretch"" }},
      {{ ""path"": ""Icon"",   ""cell"": [7,4,1,1], ""modeX"": ""right"", ""modeY"": ""bottom"", ""scale"": [1.2] }},
      {{ ""path"": ""Promo"",  ""visible"": ""hide"" }} ] }},
    {{ ""profile"": ""square"", ""blocks"": [ ] }} ] }}
```

Each block needs `path` and at least one of `cell`, `visible` or `reset`. On `SPEC_INVALID` nothing changed; fix every listed problem.

## Steps

1. Open the portrait image.
2. Write ONE spec for all target screens to `{specFile}` with the Write tool, then run `apply-spec -Spec {specFile}`.
3. In one turn, run `render -Prefab {PrefabPath} -Preset {presets}` and `check -Prefab {PrefabPath}`. Then open the rendered images (all in one turn).
4. If `check` reports real problems or an image looks wrong, fix the spec once and apply again, then render and check once more. Then stop.

Do not set roles, change grid settings, the portrait layout, other prefabs or any other file.

## Running commands

Run each command exactly in this form, one per tool call, with nothing before or after it (no `cd`, no pipes, no `&&`, no `;`):
`{Script} <command> [options]`
Allowed: {string.Join(", ", AllowedCommands)}. Everything else is blocked. The script prints one JSON object.
If you get `EDITOR_NOT_RUNNING`, `EDITOR_PLAYING` or `PREFAB_OPEN`, stop and say so.

## Final reply

Your last message is shown to the user as it is. It must contain only the summary, written entirely in {language} (no other language, no status lines before it):
one plain sentence per screen saying where the main blocks went, then any problem that is left.{(note == null ? "" : " Also say in one sentence how you followed the user's extra instructions.")}
No JSON, no file paths, no cell numbers, no mode names. At most 6 lines.
";
        }
    }
}
