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
        private const double MaxExtraSeconds = 3.0; // 화면이 새 해상도를 아직 못 따라왔으면 이만큼 더 기다린다

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
                    RefreshCanvasScalers(false);
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    if (now < captureAt)
                        return false;

                    // 캡처 직전: 화면이 새 해상도를 따라왔는지 확인하고, 아니면 조금 더 기다린다
                    string problem = Settle();
                    if (problem != null && now < captureAt + MaxExtraSeconds)
                        return false;
                    if (problem != null)
                        warnings.Add($"{currentName}: captured before the screen fully followed the new resolution ({problem}). Check this image against the others.");

                    // 확인한 상태로 Game 뷰를 한 번 직접 그린 뒤 읽는다 (이전 화면이 남아 있지 않게)
                    FlexibleGameViewUtility.TrySnapZoomToFit();
                    FlexibleGameViewUtility.TryRenderNow();
                    Capture();
                }

                index++;
                if (index >= keys.Count)
                {
                    Finish();
                    return true;
                }

                currentResolution = FlexibleCommandRunner.SelectGameView(keys[index], out currentName);
                FlexibleGameViewUtility.TrySnapZoomToFit();
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

        // 캔버스 배율과 FlexibleLayout 프로필을 지금 해상도에 맞춘다. 아직 안 맞는 게 있으면 이유를, 다 맞으면 null
        private string Settle()
        {
            if (FlexibleGameViewUtility.TryGetTargetSize(out Vector2Int target) && target != currentResolution)
                return $"Game view renders {target.x}x{target.y}";
            if (Screen.width != currentResolution.x || Screen.height != currentResolution.y)
                return $"screen is {Screen.width}x{Screen.height}";

            RefreshCanvasScalers(true);
            Canvas.ForceUpdateCanvases();
            FlexibleLayout[] layouts = UnityEngine.Object.FindObjectsByType<FlexibleLayout>(FindObjectsSortMode.None);
            foreach (FlexibleLayout layout in layouts)
            {
                if (layout.isActiveAndEnabled)
                    layout.Refresh();
            }
            Canvas.ForceUpdateCanvases();

            float shortSide = Mathf.Min(currentResolution.x, currentResolution.y);
            foreach (FlexibleCanvasScaler scaler in UnityEngine.Object.FindObjectsByType<FlexibleCanvasScaler>(FindObjectsSortMode.None))
            {
                var canvas = scaler.GetComponent<Canvas>();
                float reference = Mathf.Min(scaler.referenceResolution.x, scaler.referenceResolution.y);
                if (!scaler.isActiveAndEnabled || canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace || reference <= 0f)
                    continue;
                if (Mathf.Abs(canvas.scaleFactor - shortSide / reference) > 0.01f)
                    return $"canvas '{canvas.name}' scale {canvas.scaleFactor:0.###}";
            }

            foreach (FlexibleLayout layout in layouts)
            {
                if (!layout.isActiveAndEnabled || layout.Settings == null)
                    continue;
                Rect rect = ((RectTransform)layout.transform).rect;
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;
                if (layout.Settings.SelectProfile(rect.width / rect.height, layout.CurrentProfileIndex) != layout.CurrentProfileIndex)
                    return $"'{layout.name}' still shows profile '{layout.CurrentProfile?.id}'";
            }
            return null;
        }

        // Run In Background가 꺼진 프로젝트는 에디터가 뒤에 있으면 Update가 돌지 않아 CanvasScaler 배율이 옛 해상도에 머문다 (force면 항상)
        private static void RefreshCanvasScalers(bool force)
        {
            if (!force && (UnityEditorInternal.InternalEditorUtility.isApplicationActive || Application.runInBackground))
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
