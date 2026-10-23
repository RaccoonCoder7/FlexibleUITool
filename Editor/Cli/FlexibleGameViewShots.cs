using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlexibleUITool.EditorTools
{
    // 플레이 중 game-view: 프리셋마다 해상도를 바꾸고 화면이 따라오길 기다렸다가 캡처한다 (브리지가 매 갱신마다 Tick)
    public class FlexibleGameViewShots
    {
        private const double SettleSeconds = 1.0;

        private readonly FlexibleCommand command;
        private readonly List<string> keys;
        private readonly string folder;
        private readonly List<string> warnings = new List<string>();
        private readonly FlexibleJson shots = new FlexibleJson();
        private int index = -1;
        private double captureAt;
        private string currentName;
        private Vector2Int currentResolution;

        public string Result { get; private set; }

        public FlexibleGameViewShots(FlexibleCommand command)
        {
            this.command = command;
            keys = FlexibleCommandRunner.GetGameViewKeys(command);
            folder = string.IsNullOrEmpty(command.output)
                ? Path.Combine(FlexibleCommandBridge.WorkFolder, "renders")
                : Path.GetFullPath(command.output);
            shots.BeginArray("shots");
        }

        // 끝나면 true (Result에 응답 JSON)
        public bool Tick(double now)
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    warnings.Add("Play Mode stopped before every preset was captured.");
                    Finish();
                    return true;
                }

                if (index >= 0)
                {
                    // 에디터가 뒤에 있어 플레이가 멈춰 있어도 레이아웃·캔버스 배율이 새 해상도를 따라오게 한다
                    EditorApplication.QueuePlayerLoopUpdate();
                    RefreshCanvasScalers();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    if (now < captureAt)
                        return false;
                    Capture();
                }

                index++;
                if (index >= keys.Count)
                {
                    Finish();
                    return true;
                }

                currentResolution = FlexibleCommandRunner.SelectGameView(keys[index], out currentName);
                captureAt = now + SettleSeconds;
                return false;
            }
            catch (FlexibleCommandException e)
            {
                Result = FlexibleCommandRunner.Error(command.id, command.command, e.Code, e.Message);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Result = FlexibleCommandRunner.Error(command.id, command.command, "INTERNAL", e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        // Run In Background가 꺼진 프로젝트는 에디터가 뒤에 있으면 Update가 돌지 않아 CanvasScaler 배율이 옛 해상도에 머문다
        private static void RefreshCanvasScalers()
        {
            if (UnityEditorInternal.InternalEditorUtility.isApplicationActive || Application.runInBackground)
                return;

            foreach (CanvasScaler scaler in UnityEngine.Object.FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
            {
                if (scaler.isActiveAndEnabled)
                    HandleMethod?.Invoke(scaler, null);
            }
        }

        private static readonly System.Reflection.MethodInfo HandleMethod =
            typeof(CanvasScaler).GetMethod("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private void Capture()
        {
            string file = Path.Combine(folder, $"gameview__{index}_{FlexibleCommandRunner.Sanitize(currentName)}.png");
            bool captured = FlexibleGameViewUtility.TryCapture(file, out _);
            if (!captured)
                warnings.Add($"{currentName}: could not capture the Game view. Ask the user to look at it instead.");

            shots.BeginObject().Value("target", currentName)
                .Numbers("resolution", currentResolution.x, currentResolution.y)
                .Value("path", captured ? file : "");
            FlexibleCommandRunner.AppendRuntimeProfiles(shots);
            shots.EndObject();
        }

        private void Finish()
        {
            string list = shots.EndArray().ToString();
            var json = new FlexibleJson().BeginObject()
                .Value("id", command.id)
                .Value("command", command.command)
                .Value("ok", true)
                .Value("playing", EditorApplication.isPlaying)
                .Raw(list)
                .Strings("warnings", warnings)
                .EndObject();
            Result = json.ToString();
        }
    }
}
