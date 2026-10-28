using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // PC에 설치된 Claude Code(claude 명령)를 찾고, 로그인 상태를 확인하고, 설치·로그인 창을 연다
    public static class FlexibleClaudeCode
    {
        public enum Status
        {
            Unknown,
            Checking,
            NotInstalled,
            NotLoggedIn,
            Ready,
        }

        private const string PathKey = "FlexibleUITool.ClaudePath";
        private const string ConnectedKey = "FlexibleUITool.AiConnected";
        private const string InstallCommand = "irm https://claude.ai/install.ps1 | iex";

        [Serializable]
        private class AuthStatus
        {
            public bool loggedIn;
            public string authMethod;
            public string email;
        }

        public static Status Current { get; private set; } = Status.Unknown;
        public static string Detail { get; private set; } = "";

        // 연결을 한 번 확인한 PC인지 (창을 열 때마다 확인하지 않도록)
        public static bool Connected
        {
            get => EditorPrefs.GetBool(ConnectedKey, false);
            set => EditorPrefs.SetBool(ConnectedKey, value);
        }

        // 직접 지정한 claude 실행 파일 경로 (비우면 자동으로 찾는다)
        public static string CustomPath
        {
            get => EditorPrefs.GetString(PathKey, "");
            set => EditorPrefs.SetString(PathKey, value ?? "");
        }

        public static event Action Changed;

        public static string FindExecutable()
        {
            if (!string.IsNullOrEmpty(CustomPath) && File.Exists(CustomPath))
                return CustomPath;

            string[] names = { "claude.exe", "claude.cmd" };
            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;
                foreach (string name in names)
                {
                    string candidate = Path.Combine(folder.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            // Unity를 켠 뒤에 설치하면 PATH에 아직 없으므로 기본 설치 위치도 본다
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] known =
            {
                Path.Combine(home, ".local", "bin", "claude.exe"),
                Path.Combine(appData, "npm", "claude.cmd"),
            };
            foreach (string candidate in known)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        // 설치 여부와 로그인 상태를 백그라운드에서 확인한다 (에디터를 멈추지 않게)
        public static void CheckAsync()
        {
            if (Current == Status.Checking)
                return;

            string exe = FindExecutable();
            if (exe == null)
            {
                SetStatus(Status.NotInstalled, "");
                return;
            }

            SetStatus(Status.Checking, "");
            Task.Run(() =>
            {
                Status status;
                string detail;
                try
                {
                    string version = Run(exe, "--version", 20000, out _).Trim();
                    string json = Run(exe, "auth status", 20000, out _);
                    var auth = JsonUtility.FromJson<AuthStatus>(ExtractJson(json));
                    status = auth != null && auth.loggedIn ? Status.Ready : Status.NotLoggedIn;
                    detail = auth != null && auth.loggedIn
                        ? $"{version} · {(string.IsNullOrEmpty(auth.email) ? auth.authMethod : auth.email)}"
                        : version;
                }
                catch (Exception e)
                {
                    status = Status.Unknown;
                    detail = e.Message;
                }
                EditorApplication.delayCall += () =>
                {
                    Connected = status == Status.Ready;
                    SetStatus(status, detail);
                };
            });
        }

        public static void MarkLoggedOut(string detail)
        {
            Connected = false;
            SetStatus(Status.NotLoggedIn, detail);
        }

        private static void SetStatus(Status status, string detail)
        {
            Current = status;
            Detail = detail ?? "";
            Changed?.Invoke();
        }

        // 공식 설치 명령을 보이는 PowerShell 창에서 실행한다 (진행 상황을 사용자가 볼 수 있게)
        public static void OpenInstaller()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -ExecutionPolicy Bypass -Command \"" + InstallCommand + "\"",
                UseShellExecute = true,
            });
        }

        // 로그인은 브라우저를 오가므로 보이는 창에서 진행한다
        public static void OpenLogin()
        {
            string exe = FindExecutable();
            if (exe == null)
                return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k \"\"" + exe + "\" auth login\"",
                UseShellExecute = true,
            });
        }

        // .cmd(npm 설치)는 cmd.exe를 거쳐 실행해야 한다
        public static ProcessStartInfo CreateStartInfo(string exe, string arguments, string workingDirectory)
        {
            bool script = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            return new ProcessStartInfo
            {
                FileName = script ? "cmd.exe" : exe,
                Arguments = script ? "/c \"\"" + exe + "\" " + arguments + "\"" : arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        private static string Run(string exe, string arguments, int timeoutMs, out int exitCode)
        {
            ProcessStartInfo info = CreateStartInfo(exe, arguments, Directory.GetCurrentDirectory());
            using (Process process = Process.Start(info))
            {
                process.StandardInput.Close();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    KillTree(process);
                    throw new TimeoutException("claude " + arguments);
                }
                exitCode = process.ExitCode;
                return output.Result + error.Result;
            }
        }

        private static string ExtractJson(string text)
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : "{}";
        }

        // claude가 띄운 하위 프로세스(PowerShell 등)까지 함께 끈다
        public static void KillTree(Process process)
        {
            try
            {
                if (process == null || process.HasExited)
                    return;
                using (Process kill = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    kill?.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                try { process.Kill(); } catch (Exception) { }
            }
        }
    }
}
