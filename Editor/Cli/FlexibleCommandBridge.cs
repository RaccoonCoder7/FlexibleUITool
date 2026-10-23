using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 켜져 있는 에디터가 Library/FlexibleUITool/inbox 의 명령 파일을 처리해 outbox 에 결과를 쓴다 (flexui.ps1 의 에디터 경로)
    [InitializeOnLoad]
    public static class FlexibleCommandBridge
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static double nextHeartbeat;
        private static string lastState;
        private static FlexibleGameViewShots shots;
        private static string shotsId;
        private static bool agentKitWritten;

        public static string WorkFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "FlexibleUITool");
        private static string InboxFolder => Path.Combine(WorkFolder, "inbox");
        private static string OutboxFolder => Path.Combine(WorkFolder, "outbox");
        private static string HeartbeatFile => Path.Combine(WorkFolder, "bridge.json");

        static FlexibleCommandBridge()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.update += Tick;
            EditorApplication.quitting += () => TryDelete(HeartbeatFile);
        }

        // 에이전트 진입점을 툴 위치(Assets 안 또는 패키지 캐시)와 무관하게 Library/FlexibleUITool 에 고정한다.
        // AGENTS.md 의 {{ToolPath}} 는 실제 툴 폴더(프로젝트 기준 상대 경로)로 채운다
        public static void WriteAgentKit()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string tool = FlexibleSettingsUtility.ToolFullPath;
                string toolPath = ToProjectPath(projectRoot, tool);
                Directory.CreateDirectory(WorkFolder);
                CopyIfChanged(Path.Combine(tool, "AGENTS.md"), Path.Combine(WorkFolder, "AGENTS.md"), text => text.Replace("{{ToolPath}}", toolPath));
                CopyIfChanged(Path.Combine(tool, "Cli", "flexui.ps1"), Path.Combine(WorkFolder, "flexui.ps1"), text => text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FlexibleUITool] Could not write the agent files to Library/FlexibleUITool: {e.Message}");
            }
        }

        private static string ToProjectPath(string projectRoot, string path)
        {
            string root = Path.GetFullPath(projectRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            string result = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
            return result.Replace('\\', '/');
        }

        private static void CopyIfChanged(string source, string target, Func<string, string> transform)
        {
            if (!File.Exists(source))
                return;
            string text = transform(File.ReadAllText(source, Encoding.UTF8));
            if (File.Exists(target) && File.ReadAllText(target, Encoding.UTF8) == text)
                return;
            File.WriteAllText(target, text, Utf8);
        }

        private static void Tick()
        {
            string state = GetState();
            double now = EditorApplication.timeSinceStartup;
            if (state != lastState || now >= nextHeartbeat)
            {
                WriteHeartbeat(state);
                lastState = state;
                nextHeartbeat = now + 1.0;
            }

            if (!agentKitWritten && state == "ready")
            {
                agentKitWritten = true;
                WriteAgentKit();
            }

            // 컴파일·임포트 중에는 쌓아 두고, 플레이 중에는 플레이용 명령(ping, game-view)만 처리한다
            if (!Directory.Exists(InboxFolder))
                return;

            if (shots != null)
            {
                if (shots.Tick(now))
                {
                    WriteResult(shotsId, shots.Result);
                    shots = null;
                }
                return;
            }

            string[] files = Directory.GetFiles(InboxFolder, "*.json").OrderBy(File.GetLastWriteTimeUtc).ToArray();
            string file = null;
            if (state == "ready")
                file = files.FirstOrDefault();
            else if (state == "playing" && EditorApplication.isPlaying)
                file = files.FirstOrDefault(IsPlaySafe);

            if (file != null)
                Process(file);
        }

        public static bool IsPlaySafeCommand(string command)
        {
            return command == "ping" || command == "game-view";
        }

        private static bool IsPlaySafe(string file)
        {
            try
            {
                var command = JsonUtility.FromJson<FlexibleCommand>(File.ReadAllText(file, Encoding.UTF8));
                return command != null && IsPlaySafeCommand(command.command);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetState()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "playing";
            if (EditorApplication.isCompiling)
                return "compiling";
            if (EditorApplication.isUpdating)
                return "updating";
            return "ready";
        }

        private static void Process(string file)
        {
            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
                File.Delete(file);
            }
            catch (IOException)
            {
                return;
            }

            string id = Path.GetFileNameWithoutExtension(file);

            // 플레이 중 game-view는 여러 갱신에 걸쳐 해상도 전환·대기·캡처를 반복한 뒤 응답한다
            if (EditorApplication.isPlaying && TryStartShots(text, id))
                return;
            WriteResult(id, FlexibleCommandRunner.ExecuteJson(text, id, true, out _));
        }

        private static bool TryStartShots(string text, string id)
        {
            FlexibleCommand command;
            try
            {
                command = JsonUtility.FromJson<FlexibleCommand>(text);
            }
            catch (Exception)
            {
                return false;
            }
            if (command == null || command.command != "game-view")
                return false;

            if (string.IsNullOrEmpty(command.id))
                command.id = id;
            try
            {
                shots = new FlexibleGameViewShots(command);
                shotsId = id;
            }
            catch (FlexibleCommandException e)
            {
                WriteResult(id, FlexibleCommandRunner.Error(command.id, command.command, e.Code, e.Message));
            }
            return true;
        }

        private static void WriteResult(string id, string result)
        {
            Directory.CreateDirectory(OutboxFolder);
            string temp = Path.Combine(OutboxFolder, id + ".tmp");
            string target = Path.Combine(OutboxFolder, id + ".json");
            File.WriteAllText(temp, result, Utf8);
            TryDelete(target);
            File.Move(temp, target);
        }

        private static void WriteHeartbeat(string state)
        {
            try
            {
                Directory.CreateDirectory(WorkFolder);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string json = new FlexibleJson().BeginObject()
                    .Value("pid", System.Diagnostics.Process.GetCurrentProcess().Id)
                    .Value("state", state)
                    .Value("unixTime", now.ToString())
                    .EndObject().ToString();
                File.WriteAllText(HeartbeatFile, json, Utf8);
            }
            catch (IOException)
            {
                // 스크립트가 읽는 중이면 다음 박동에 다시 쓴다
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
