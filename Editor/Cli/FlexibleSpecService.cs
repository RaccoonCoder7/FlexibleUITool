using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlexibleUITool.EditorTools
{
    // 배치 스펙 적용(화면에 열지 않는 작업 프리팹 → 기존 반영 로직)과 현재 데이터의 스펙 내보내기
    public static class FlexibleSpecService
    {
        private const float Epsilon = 0.01f;

        // 모든 대상을 먼저 검증해 하나라도 틀리면 아무것도 바꾸지 않는다
        public static List<FlexibleCommitReport> Apply(string prefabPath, FlexibleSpec spec, bool dryRun)
        {
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            Validate(prefabPath, spec, settings);

            var reports = new List<FlexibleCommitReport>();
            foreach (FlexibleSpecProfile specProfile in spec.profiles)
                reports.Add(ApplyProfile(prefabPath, specProfile, settings, dryRun));
            return reports;
        }

        public static void Validate(string prefabPath, FlexibleSpec spec, FlexibleProfileSettings settings)
        {
            if (spec == null || spec.profiles == null || spec.profiles.Length == 0)
                throw new FlexibleCommandException("SPEC_INVALID", "Spec has no profiles.");

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = (RectTransform)asset.transform;
            Dictionary<string, RectTransform> map = FlexiblePathUtility.BuildMap(root);
            var layout = asset.GetComponent<FlexibleLayout>();
            var errors = new List<string>();

            foreach (FlexibleSpecProfile specProfile in spec.profiles)
            {
                int index = settings.IndexOf(specProfile.profile);
                if (index < 0)
                {
                    errors.Add($"profile '{specProfile.profile}' not found (available: {string.Join(", ", settings.Profiles.Select(p => p.id))})");
                    continue;
                }
                if (specProfile.profile == settings.BaseProfileId)
                {
                    errors.Add($"profile '{specProfile.profile}' is the base profile (the prefab itself); edit the prefab directly");
                    continue;
                }
                if (FlexibleWorkspaceService.WorkspaceExists(prefabPath, specProfile.profile))
                    errors.Add($"profile '{specProfile.profile}' has an open GUI workspace; commit or cancel it first (WORKSPACE_EXISTS)");

                foreach (FlexibleSpecBlock block in specProfile.blocks ?? Array.Empty<FlexibleSpecBlock>())
                {
                    string where = $"{specProfile.profile}/{block.Label}";
                    if (string.IsNullOrEmpty(block.path) && string.IsNullOrEmpty(block.role))
                        errors.Add($"{where}: needs 'path' or 'role'");
                    else if (!string.IsNullOrEmpty(block.path) && !map.ContainsKey(block.path))
                        errors.Add($"{where}: path not found");
                    else if (string.IsNullOrEmpty(block.path) && (layout == null || !layout.Roles.Any(r => r.target != null && r.role == block.role)))
                        errors.Add($"{where}: no block has this role");

                    if (!block.reset && !block.HasCell && !block.HasRect && string.IsNullOrEmpty(block.visible))
                        errors.Add($"{where}: nothing to do (set cell, rect, visible or reset)");
                    if (block.HasCell && block.HasRect)
                        errors.Add($"{where}: use either cell or rect, not both");
                    if (block.cell != null && block.cell.Length != 0 && block.cell.Length != 4)
                        errors.Add($"{where}: cell must be [column, row, width, height]");
                    if (!TryParseMode(block.modeX, true, out _))
                        errors.Add($"{where}: unknown modeX '{block.modeX}'");
                    if (!TryParseMode(block.modeY, false, out _))
                        errors.Add($"{where}: unknown modeY '{block.modeY}'");
                    if (!string.IsNullOrEmpty(block.visible) && block.visible != "show" && block.visible != "hide")
                        errors.Add($"{where}: visible must be 'show' or 'hide'");
                }
            }

            if (errors.Count > 0)
                throw new FlexibleCommandException("SPEC_INVALID", string.Join("; ", errors));
        }

        private static FlexibleCommitReport ApplyProfile(string prefabPath, FlexibleSpecProfile specProfile, FlexibleProfileSettings settings, bool dryRun)
        {
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            string workspacePath = $"{FlexibleWorkspaceService.WorkspaceFolder}/_cli_{guid.Substring(0, 8)}_{specProfile.profile}.prefab";
            AssetDatabase.DeleteAsset(workspacePath);
            FlexibleWorkspaceService.CreateAt(prefabPath, specProfile.profile, workspacePath);
            try
            {
                GameObject root = PrefabUtility.LoadPrefabContents(workspacePath);
                try
                {
                    PlaceBlocks(root, prefabPath, specProfile, settings);
                    PrefabUtility.SaveAsPrefabAsset(root, workspacePath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                return FlexibleWorkspaceService.Commit(workspacePath, dryRun);
            }
            finally
            {
                AssetDatabase.DeleteAsset(workspacePath);
            }
        }

        // 루트를 프로필 작업 해상도 크기의 임시 캔버스에 넣어 GUI 작업 프리팹과 같은 조건에서 배치한다
        private static void PlaceBlocks(GameObject root, string prefabPath, FlexibleSpecProfile specProfile, FlexibleProfileSettings settings)
        {
            int profileIndex = settings.IndexOf(specProfile.profile);
            FlexibleProfile profile = settings.Profiles[profileIndex];
            var rootRect = (RectTransform)root.transform;
            var workspace = root.GetComponent<FlexibleWorkspace>();
            var layout = root.GetComponent<FlexibleLayout>();

            var canvasObject = new GameObject("FlexibleSpecCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, root.scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasObject.transform).sizeDelta = GetWorkSize(profile, settings);
            rootRect.SetParent(canvasObject.transform, false);

            GameObject sourceRoot = null;
            try
            {
                var context = new FlexibleGridContext(rootRect, profile.grid, settings.GetSafeInsets(profileIndex));
                Dictionary<string, RectTransform> map = FlexiblePathUtility.BuildMap(rootRect);
                foreach (FlexibleSpecBlock block in specProfile.blocks ?? Array.Empty<FlexibleSpecBlock>())
                {
                    foreach (RectTransform target in Resolve(block, map, layout))
                    {
                        if (block.reset)
                        {
                            sourceRoot ??= PrefabUtility.LoadPrefabContents(prefabPath);
                            ResetToBase(target, rootRect, (RectTransform)sourceRoot.transform, workspace);
                            continue;
                        }

                        if (block.HasRect)
                        {
                            ToRectState(block, target).ApplyTo(target);
                            workspace.placements.RemoveAll(p => p.target == target);
                        }
                        else if (block.HasCell)
                        {
                            TryParseMode(block.modeX, true, out FlexibleAxisMode modeX);
                            TryParseMode(block.modeY, false, out FlexibleAxisMode modeY);
                            var cell = new FlexibleCell(block.cell[0], block.cell[1], block.cell[2], block.cell[3]);
                            FlexibleGridPlacementService.Apply(context, workspace, target, cell, modeX, modeY, block.safeArea, ToAdjustment(block));
                        }

                        if (block.visible == "show" || block.visible == "hide")
                            target.gameObject.SetActive(block.visible == "show");
                    }
                }
            }
            finally
            {
                if (sourceRoot != null)
                    PrefabUtility.UnloadPrefabContents(sourceRoot);
                rootRect.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        public static Vector2 GetWorkSize(FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            float aspect = profile.workResolution.y > 0f ? profile.workResolution.x / profile.workResolution.y : 1f;
            return FlexiblePreviewRenderer.GetLogicalSize(aspect, settings.ReferenceShortSide);
        }

        private static IEnumerable<RectTransform> Resolve(FlexibleSpecBlock block, Dictionary<string, RectTransform> map, FlexibleLayout layout)
        {
            if (!string.IsNullOrEmpty(block.path))
                return new[] { map[block.path] };
            return layout.Roles.Where(r => r.target != null && r.role == block.role).Select(r => r.target).ToList();
        }

        private static void ResetToBase(RectTransform target, RectTransform root, RectTransform sourceRoot, FlexibleWorkspace workspace)
        {
            string path = FlexiblePathUtility.GetPath(root, target);
            if (FlexiblePathUtility.BuildMap(sourceRoot).TryGetValue(path, out RectTransform source))
            {
                FlexibleRectState.Capture(source).ApplyTo(target);
                target.gameObject.SetActive(source.gameObject.activeSelf);
            }
            workspace.placements.RemoveAll(p => p.target == target);
        }

        private static FlexibleGridAdjustment ToAdjustment(FlexibleSpecBlock block)
        {
            var adjustment = new FlexibleGridAdjustment();
            if (block.offset != null && block.offset.Length == 2)
                adjustment.position = new Vector2(block.offset[0], block.offset[1]);
            if (block.sizeOffset != null && block.sizeOffset.Length == 2)
                adjustment.size = new Vector2(block.sizeOffset[0], block.sizeOffset[1]);
            if (block.scale != null && block.scale.Length > 0)
            {
                adjustment.overrideScale = true;
                adjustment.scale = block.scale.Length >= 3
                    ? new Vector3(block.scale[0], block.scale[1], block.scale[2])
                    : new Vector3(block.scale[0], block.scale[0], 1f);
            }
            return adjustment;
        }

        private static FlexibleRectState ToRectState(FlexibleSpecBlock block, RectTransform target)
        {
            FlexibleRectState state = FlexibleRectState.Capture(target);
            FlexibleSpecRect rect = block.rect;
            state.anchorMin = new Vector2(rect.anchorMin[0], rect.anchorMin[1]);
            state.anchorMax = new Vector2(rect.anchorMax[0], rect.anchorMax[1]);
            state.pivot = new Vector2(rect.pivot[0], rect.pivot[1]);
            state.anchoredPosition = new Vector2(rect.position[0], rect.position[1]);
            state.sizeDelta = new Vector2(rect.size[0], rect.size[1]);
            if (block.scale != null && block.scale.Length > 0)
                state.localScale = block.scale.Length >= 3 ? new Vector3(block.scale[0], block.scale[1], block.scale[2]) : new Vector3(block.scale[0], block.scale[0], 1f);
            return state;
        }

        // 가로축은 left/right, 세로축은 bottom/top 별칭도 받는다
        public static bool TryParseMode(string text, bool horizontal, out FlexibleAxisMode mode)
        {
            mode = FlexibleAxisMode.Auto;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "": case "auto": mode = FlexibleAxisMode.Auto; return true;
                case "stretch": mode = FlexibleAxisMode.Stretch; return true;
                case "start": mode = FlexibleAxisMode.Start; return true;
                case "end": mode = FlexibleAxisMode.End; return true;
                case "center": mode = FlexibleAxisMode.Center; return true;
                case "left": mode = FlexibleAxisMode.Start; return horizontal;
                case "right": mode = FlexibleAxisMode.End; return horizontal;
                case "bottom": mode = FlexibleAxisMode.Start; return !horizontal;
                case "top": mode = FlexibleAxisMode.End; return !horizontal;
                default: return false;
            }
        }

        // 현재 프로필 데이터를 스펙으로 (그리드 출신은 칸+보정, 아니면 rect 그대로)
        public static void WriteSpec(FlexibleJson json, string prefabPath, string onlyProfile, List<string> warnings)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var root = (RectTransform)asset.transform;
            var layout = asset.GetComponent<FlexibleLayout>();
            FlexibleProfileSettings settings = layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();

            json.BeginObject("spec").Value("prefab", prefabPath).BeginArray("profiles");
            foreach (FlexibleProfile profile in settings.Profiles)
            {
                if (profile.id == settings.BaseProfileId || (!string.IsNullOrEmpty(onlyProfile) && profile.id != onlyProfile))
                    continue;

                json.BeginObject().Value("profile", profile.id).BeginArray("blocks");
                foreach (FlexibleLayoutEntry entry in layout != null ? layout.Entries : new List<FlexibleLayoutEntry>())
                {
                    FlexibleProfileState state = entry.target != null ? entry.Find(profile.id) : null;
                    if (state == null)
                        continue;

                    WriteBlock(json, root, layout, entry, state);
                    if (state.controlLayoutGroup)
                        warnings.Add($"{profile.id}/{FlexiblePathUtility.GetPath(root, entry.target)}: layout group overrides are not exported");
                }
                json.EndArray().EndObject();
            }
            json.EndArray().EndObject();
        }

        private static void WriteBlock(FlexibleJson json, RectTransform root, FlexibleLayout layout, FlexibleLayoutEntry entry, FlexibleProfileState state)
        {
            json.BeginObject().Value("path", FlexiblePathUtility.GetPath(root, entry.target));
            string role = layout.GetRole(entry.target);
            if (!string.IsNullOrEmpty(role))
                json.Value("role", role);

            bool rectChanged = !state.rect.Approximately(FlexibleRectState.Capture(entry.target), Epsilon);
            if (state.gridOrigin && FlexibleGridPlacementService.TryGetAdjustment(state.rect, state.gridResult, entry.target.localScale, out FlexibleGridAdjustment adjustment))
            {
                FlexibleCell cell = state.gridCell;
                json.Numbers("cell", cell.column, cell.row, cell.width, cell.height)
                    .Value("modeX", ModeName(state.gridModeX))
                    .Value("modeY", ModeName(state.gridModeY))
                    .Value("safeArea", state.gridInsideSafeArea);
                if (adjustment.position.sqrMagnitude > 0.0001f)
                    json.Numbers("offset", adjustment.position.x, adjustment.position.y);
                if (adjustment.size.sqrMagnitude > 0.0001f)
                    json.Numbers("sizeOffset", adjustment.size.x, adjustment.size.y);
                if (adjustment.overrideScale)
                    json.Numbers("scale", adjustment.scale.x, adjustment.scale.y, adjustment.scale.z);
            }
            else if (rectChanged)
            {
                FlexibleRectState r = state.rect;
                json.BeginObject("rect")
                    .Numbers("anchorMin", r.anchorMin.x, r.anchorMin.y)
                    .Numbers("anchorMax", r.anchorMax.x, r.anchorMax.y)
                    .Numbers("pivot", r.pivot.x, r.pivot.y)
                    .Numbers("position", r.anchoredPosition.x, r.anchoredPosition.y)
                    .Numbers("size", r.sizeDelta.x, r.sizeDelta.y)
                    .EndObject();
                if ((r.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    json.Numbers("scale", r.localScale.x, r.localScale.y, r.localScale.z);
            }

            if (state.controlActive)
                json.Value("visible", state.active ? "show" : "hide");
            json.EndObject();
        }

        public static string ModeName(FlexibleAxisMode mode)
        {
            return mode.ToString().ToLowerInvariant();
        }
    }
}
