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
            {
                reports.Add(specProfile.profile == settings.BaseProfileId
                    ? ApplyBaseFits(prefabPath, specProfile, settings, dryRun)
                    : ApplyProfile(prefabPath, specProfile, settings, dryRun));
            }
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
                // 기준 화면은 프리팹 자체라 「칸에 맞춰 줄이기」(cell 또는 pin + fit)만 받는다 (짧은·긴 세로 화면에서 다시 줄이도록 칸 영역을 기록)
                bool isBase = specProfile.profile == settings.BaseProfileId;
                if (isBase && (specProfile.blocks ?? Array.Empty<FlexibleSpecBlock>()).Any(b => !b.fit || b.reset || b.HasRect || b.HasFollow || !string.IsNullOrEmpty(b.visible)))
                {
                    errors.Add($"profile '{specProfile.profile}' is the base profile (the prefab itself): only fit blocks (cell or pin with fit: true) are allowed there; edit anything else in the prefab directly");
                    continue;
                }
                if (!isBase && FlexibleWorkspaceService.WorkspaceExists(prefabPath, specProfile.profile))
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

                    if (!block.reset && !block.HasCell && !block.HasRect && !block.HasPin && !block.HasFollow && string.IsNullOrEmpty(block.visible))
                        errors.Add($"{where}: nothing to do (set cell, rect, pin, follow, visible or reset)");
                    if ((block.HasCell ? 1 : 0) + (block.HasRect ? 1 : 0) + (block.HasPin ? 1 : 0) + (block.HasFollow ? 1 : 0) > 1)
                        errors.Add($"{where}: use only one of cell, rect, pin or follow");
                    if (block.fit && !block.HasCell && !block.HasPin)
                        errors.Add($"{where}: fit needs cell or pin");
                    if (block.scaleBy != 0f && (block.scaleBy < 0f || (block.scale != null && block.scale.Length > 0)))
                        errors.Add($"{where}: scaleBy must be positive and cannot be used with scale");
                    if ((IsKeep(block.modeX) || IsKeep(block.modeY)) && !block.HasCell)
                        errors.Add($"{where}: mode 'keep' needs cell");
                    if (block.HasFollow)
                    {
                        if (!map.TryGetValue(block.follow, out RectTransform followed))
                            errors.Add($"{where}: follow '{block.follow}' not found");
                        else if (!string.IsNullOrEmpty(block.path) && map.TryGetValue(block.path, out RectTransform follower) && follower.parent != followed.parent)
                            errors.Add($"{where}: follow '{block.follow}' must have the same parent");
                    }
                    if (block.HasPin && !TryParsePin(block.pin, out _, out _))
                        errors.Add($"{where}: unknown pin '{block.pin}' (top-left, top, top-right, left, center, right, bottom-left, bottom, bottom-right)");
                    if (block.cell != null && block.cell.Length != 0 && block.cell.Length != 4)
                        errors.Add($"{where}: cell must be [column, row, width, height]");
                    if (!TryParseMode(block.modeX, true, out _))
                        errors.Add($"{where}: unknown modeX '{block.modeX}'");
                    if (!TryParseMode(block.modeY, false, out _))
                        errors.Add($"{where}: unknown modeY '{block.modeY}'");
                    if (!string.IsNullOrEmpty(block.visible) && block.visible != "show" && block.visible != "hide")
                        errors.Add($"{where}: visible must be 'show' or 'hide'");
                    if ((block.HasCell || block.HasRect || block.HasPin || block.HasFollow) && !string.IsNullOrEmpty(block.path) && map.TryGetValue(block.path, out RectTransform placed))
                    {
                        RectTransform group = FlexiblePathUtility.GetPlaceInstead(placed);
                        if (group != null)
                            errors.Add($"{where}: placed by the layout group on '{FlexiblePathUtility.GetPath(root, group)}'; give the cell to that block instead (visible is fine)");
                    }
                }
            }

            if (errors.Count > 0)
                throw new FlexibleCommandException("SPEC_INVALID", string.Join("; ", errors));
        }

        private static FlexibleCommitReport ApplyProfile(string prefabPath, FlexibleSpecProfile specProfile, FlexibleProfileSettings settings, bool dryRun)
        {
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            string workspacePath = $"{FlexibleWorkspaceService.WorkspaceFolder}/_cli_{guid.Substring(0, 8)}_{specProfile.profile}.prefab";
            placeWarnings.Clear();
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

                FlexibleCommitReport report = FlexibleWorkspaceService.Commit(workspacePath, dryRun);
                report.warnings.AddRange(placeWarnings);
                return report;
            }
            finally
            {
                AssetDatabase.DeleteAsset(workspacePath);
            }
        }

        // 기준 화면의 칸 맞춤: 프리팹 자체를 칸에 맞춰 줄이고, 칸 영역·최대 배율을 기준 화면 기록으로 남긴다 (창에서 기준 화면에 「칸에 맞춰 줄이기」를 한 것과 같다)
        private static FlexibleCommitReport ApplyBaseFits(string prefabPath, FlexibleSpecProfile specProfile, FlexibleProfileSettings settings, bool dryRun)
        {
            var report = new FlexibleCommitReport { sourcePath = prefabPath, profileId = specProfile.profile, dryRun = dryRun };
            placeWarnings.Clear();
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var layout = root.GetComponent<FlexibleLayout>();
                if (layout == null)
                {
                    report.warnings.Add("The prefab has no FlexibleLayout.");
                    return report;
                }
                // 기존 기준 화면 맞춤은 남기고 이번 블록을 더한다 (작업 표식은 계산에만 쓰고 저장하지 않는다)
                var workspace = root.AddComponent<FlexibleWorkspace>();
                workspace.sourceGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                workspace.profileId = specProfile.profile;
                workspace.inPlace = true;
                FlexibleScreenSession.RestoreBasePlacements(layout, workspace, specProfile.profile);
                PlaceBlocks(root, prefabPath, specProfile, settings);
                FlexibleScreenSession.RecordBaseFits(layout, workspace, specProfile.profile);
                UnityEngine.Object.DestroyImmediate(workspace);
                report.recorded = layout.Entries.Count(e => e.target != null && e.Find(specProfile.profile) != null);
                report.warnings.AddRange(placeWarnings);
                if (!dryRun)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.success = true;
                return report;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 배치하면서 알게 된 주의 사항 (배경까지 줄어드는 맞춤 등). 결과의 warnings 로 돌려준다
        private static readonly List<string> placeWarnings = new List<string>();

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
            ((RectTransform)canvasObject.transform).sizeDelta = GetWorkSize(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), profile, settings);
            rootRect.SetParent(canvasObject.transform, false);
            // 맨 위가 캔버스인 프리팹은 저장된 크기·배율이 0이라 계산용으로 화면에 맞추고, 끝나면 원래 값으로 돌린다
            bool canvasRoot = root.GetComponent<Canvas>() != null;
            FlexibleRectState savedRoot = FlexibleRectState.Capture(rootRect);
            if (canvasRoot)
                FlexiblePreviewRenderer.FitCopy(rootRect);

            GameObject sourceRoot = null;
            BaseSizes baseSizes = null;
            try
            {
                var context = new FlexibleGridContext(rootRect, layout != null ? layout.GetGrid(profile.id) : profile.grid, settings.GetSafeInsets(profileIndex));
                Dictionary<string, RectTransform> map = FlexiblePathUtility.BuildMap(rootRect);
                // follow는 따라갈 블록이 먼저 자리를 잡은 뒤에 놓는다
                IEnumerable<FlexibleSpecBlock> ordered = (specProfile.blocks ?? Array.Empty<FlexibleSpecBlock>()).OrderBy(b => b.HasFollow ? 1 : 0);
                foreach (FlexibleSpecBlock block in ordered)
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
                        else if (block.HasPin)
                        {
                            // 안전 영역의 한 모서리(또는 가운데)에 세로(기준) 화면에서와 같은 거리·크기로 붙인다
                            TryParsePin(block.pin, out FlexibleAxisMode modeX, out FlexibleAxisMode modeY);
                            baseSizes ??= new BaseSizes(prefabPath, settings);
                            var whole = new FlexibleCell(0, 0, context.grid.columns, context.grid.rows);
                            FlexibleGridAdjustment adjustment = ToAdjustment(block, workspace, target);
                            string path = FlexiblePathUtility.GetPath(rootRect, target);
                            Rect? baseRect = baseSizes.GetRect(path);
                            if (baseRect.HasValue)
                            {
                                adjustment.size += KeepSizeOffset(context, target, whole, modeX, modeY, block.safeArea, baseRect.Value.size);
                                adjustment.position += PinOffset(modeX, modeY, baseRect.Value, baseSizes.Screen, context.grid.margin);
                            }
                            FlexibleGridPlacementService.Apply(context, workspace, target, whole, modeX, modeY, block.safeArea, adjustment, block.fit);
                            WarnBackdropFit(block, rootRect, target, baseSizes);
                        }
                        else if (block.HasCell)
                        {
                            TryParseMode(block.modeX, true, out FlexibleAxisMode modeX);
                            TryParseMode(block.modeY, false, out FlexibleAxisMode modeY);
                            var cell = new FlexibleCell(block.cell[0], block.cell[1], block.cell[2], block.cell[3]);
                            FlexibleGridAdjustment adjustment = ToAdjustment(block, workspace, target);
                            if (block.keepSize || block.fit)
                            {
                                baseSizes ??= new BaseSizes(prefabPath, settings);
                                FlexibleAxisMode sizeX = block.fit && modeX == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : modeX;
                                FlexibleAxisMode sizeY = block.fit && modeY == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : modeY;
                                adjustment.size += KeepSizeOffset(context, target, cell, sizeX, sizeY, block.safeArea, baseSizes.Get(FlexiblePathUtility.GetPath(rootRect, target)));
                            }
                            FlexibleGridPlacementService.Apply(context, workspace, target, cell, modeX, modeY, block.safeArea, adjustment, block.fit);
                            WarnBackdropFit(block, rootRect, target, baseSizes);
                            if (IsKeep(block.modeX) || IsKeep(block.modeY))
                            {
                                // 코드가 움직이는 축 등: 그 축만 세로(기준) 값으로 되돌린다 (칸 기록 없이 직접 지정한 배치가 된다)
                                sourceRoot ??= PrefabUtility.LoadPrefabContents(prefabPath);
                                KeepAxes(target, rootRect, (RectTransform)sourceRoot.transform, IsKeep(block.modeX), IsKeep(block.modeY));
                                workspace.placements.RemoveAll(p => p.target == target);
                            }
                        }
                        else if (block.HasFollow)
                        {
                            baseSizes ??= new BaseSizes(prefabPath, settings);
                            Follow(target, map[block.follow], baseSizes, FlexiblePathUtility.GetPath(rootRect, target), block.follow);
                            if (block.scaleBy > 0f || (block.scale != null && block.scale.Length > 0))
                                target.localScale = ToAdjustment(block, workspace, target).scale;
                            workspace.placements.RemoveAll(p => p.target == target);
                        }

                        if (block.visible == "show" || block.visible == "hide")
                            target.gameObject.SetActive(block.visible == "show");
                    }
                }
            }
            finally
            {
                baseSizes?.Dispose();
                if (sourceRoot != null)
                    PrefabUtility.UnloadPrefabContents(sourceRoot);
                if (canvasRoot)
                    savedRoot.ApplyTo(rootRect);
                rootRect.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static void WarnBackdropFit(FlexibleSpecBlock block, RectTransform root, RectTransform target, BaseSizes baseSizes)
        {
            if (!block.fit || baseSizes == null)
                return;
            string path = FlexiblePathUtility.GetPath(root, target);
            Vector2? size = baseSizes.Get(path);
            if (size.HasValue && FlexibleGridPlacementService.IsScreenBackdrop(target, size.Value, baseSizes.Screen))
                placeWarnings.Add($"{path}: {FlexibleGridPlacementService.BackdropFitWarning}");
        }

        // 크기를 지키는 축(stretch가 아닌 축)은 칸 크기 대신 세로(기준) 화면에서의 블록 크기를 쓰도록 크기 보정을 구한다
        internal static Vector2 KeepSizeOffset(FlexibleGridContext context, RectTransform target, FlexibleCell cell,
            FlexibleAxisMode modeX, FlexibleAxisMode modeY, bool insideSafeArea, Vector2? baseSize)
        {
            if (!baseSize.HasValue || !FlexibleGridMath.TryComputePlacement(target, context, cell, modeX, modeY, insideSafeArea, out FlexibleRectState slot))
                return Vector2.zero;
            FlexibleCell clamped = cell.Clamped(context.grid);
            var offset = Vector2.zero;
            if (FlexibleGridMath.ResolveX(modeX, clamped, context.grid) != FlexibleAxisMode.Stretch)
                offset.x = baseSize.Value.x - slot.sizeDelta.x;
            if (FlexibleGridMath.ResolveY(modeY, clamped, context.grid) != FlexibleAxisMode.Stretch)
                offset.y = baseSize.Value.y - slot.sizeDelta.y;
            return offset;
        }

        public static bool IsKeep(string mode)
        {
            return string.Equals((mode ?? "").Trim(), "keep", StringComparison.OrdinalIgnoreCase);
        }

        // 고른 축의 앵커·피벗·위치·크기를 세로(기준) 값으로 되돌린다
        private static void KeepAxes(RectTransform target, RectTransform root, RectTransform sourceRoot, bool x, bool y)
        {
            string path = FlexiblePathUtility.GetPath(root, target);
            if (!FlexiblePathUtility.BuildMap(sourceRoot).TryGetValue(path, out RectTransform source))
                return;
            FlexibleRectState current = FlexibleRectState.Capture(target);
            FlexibleRectState saved = FlexibleRectState.Capture(source);
            for (int i = 0; i < 2; i++)
            {
                if ((i == 0 && !x) || (i == 1 && !y))
                    continue;
                current.anchorMin[i] = saved.anchorMin[i];
                current.anchorMax[i] = saved.anchorMax[i];
                current.pivot[i] = saved.pivot[i];
                current.anchoredPosition[i] = saved.anchoredPosition[i];
                current.sizeDelta[i] = saved.sizeDelta[i];
            }
            current.ApplyTo(target);
        }

        // 같은 부모 아래 다른 블록(followed)과 세로 화면에서의 관계를 지킨다.
        // 그 블록의 60% 이상을 덮던 축은 같은 여백으로 늘어나고, 아니면 가까운 쪽 가장자리(또는 가운데)에서 같은 거리·크기를 지킨다.
        // followed와 같은 앵커를 쓰므로 화면 크기가 바뀌어도 함께 움직인다
        private static void Follow(RectTransform target, RectTransform followed, BaseSizes baseSizes, string path, string followedPath)
        {
            Rect? b = baseSizes.GetRect(path);
            Rect? r = baseSizes.GetRect(followedPath);
            if (!b.HasValue || !r.HasValue)
                return;
            FlexibleRectState state = FlexibleRectState.Capture(target);
            for (int i = 0; i < 2; i++)
            {
                float b0 = b.Value.min[i], b1 = b.Value.max[i], r0 = r.Value.min[i], r1 = r.Value.max[i];
                float aMin = followed.anchorMin[i], aMax = followed.anchorMax[i];
                float oMin = followed.offsetMin[i], oMax = followed.offsetMax[i];
                float startGap = b0 - r0, endGap = r1 - b1, centerGap = (b0 + b1) * 0.5f - (r0 + r1) * 0.5f;
                if (b1 - b0 >= (r1 - r0) * 0.6f)
                {
                    state.anchorMin[i] = aMin;
                    state.anchorMax[i] = aMax;
                    state.pivot[i] = 0.5f;
                    float min = oMin + startGap, max = oMax - endGap;
                    state.sizeDelta[i] = max - min;
                    state.anchoredPosition[i] = (min + max) * 0.5f;
                    continue;
                }
                if (Mathf.Abs(startGap) <= Mathf.Abs(endGap) && Mathf.Abs(startGap) <= Mathf.Abs(centerGap))
                {
                    state.anchorMin[i] = state.anchorMax[i] = aMin;
                    state.pivot[i] = 0f;
                    state.anchoredPosition[i] = oMin + startGap;
                }
                else if (Mathf.Abs(endGap) <= Mathf.Abs(centerGap))
                {
                    state.anchorMin[i] = state.anchorMax[i] = aMax;
                    state.pivot[i] = 1f;
                    state.anchoredPosition[i] = oMax - endGap;
                }
                else
                {
                    state.anchorMin[i] = state.anchorMax[i] = (aMin + aMax) * 0.5f;
                    state.pivot[i] = 0.5f;
                    state.anchoredPosition[i] = (oMin + oMax) * 0.5f + centerGap;
                }
                state.sizeDelta[i] = b1 - b0;
            }
            state.ApplyTo(target);
        }

        public static bool TryParsePin(string pin, out FlexibleAxisMode modeX, out FlexibleAxisMode modeY)
        {
            modeX = FlexibleAxisMode.Center;
            modeY = FlexibleAxisMode.Center;
            string[] parts = (pin ?? "").ToLowerInvariant().Split('-');
            if (parts.Length == 0 || parts.Length > 2)
                return false;
            foreach (string part in parts)
            {
                switch (part)
                {
                    case "left": modeX = FlexibleAxisMode.Start; break;
                    case "right": modeX = FlexibleAxisMode.End; break;
                    case "top": modeY = FlexibleAxisMode.End; break;
                    case "bottom": modeY = FlexibleAxisMode.Start; break;
                    case "center": if (parts.Length != 1) return false; break;
                    default: return false;
                }
            }
            return true;
        }

        // 세로 화면에서 그 가장자리까지의 거리를 지킨다 (칸 영역은 안전 영역에서 margin만큼 안쪽이라 그만큼 뺀다). 가운데 축은 화면 가운데로부터의 거리
        internal static Vector2 PinOffset(FlexibleAxisMode modeX, FlexibleAxisMode modeY, Rect block, Vector2 screen, float margin)
        {
            var offset = Vector2.zero;
            if (modeX == FlexibleAxisMode.Start)
                offset.x = block.xMin - margin;
            else if (modeX == FlexibleAxisMode.End)
                offset.x = -(screen.x - block.xMax - margin);
            else
                offset.x = block.center.x - screen.x * 0.5f;
            if (modeY == FlexibleAxisMode.Start)
                offset.y = block.yMin - margin;
            else if (modeY == FlexibleAxisMode.End)
                offset.y = -(screen.y - block.yMax - margin);
            else
                offset.y = block.center.y - screen.y * 0.5f;
            return offset;
        }

        // 저장된 프리팹(= 기준 화면)을 기준 작업 해상도로 놓았을 때의 블록 크기·위치
        internal sealed class BaseSizes : IDisposable
        {
            private readonly FlexiblePreviewRenderer renderer = new FlexiblePreviewRenderer();
            private readonly RectTransform root;

            public BaseSizes(string prefabPath, FlexibleProfileSettings settings)
            {
                FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                renderer.Prepare(asset, baseProfile.id, GetWorkSize(asset, baseProfile, settings), new Rect(0f, 0f, 1f, 1f));
                root = (RectTransform)renderer.Instance.transform;
            }

            public Vector2 Screen => root.rect.size;

            public Vector2? Get(string path)
            {
                var target = FlexiblePathUtility.FindByPath(root, path) as RectTransform;
                return target != null ? target.rect.size : (Vector2?)null;
            }

            // 화면 왼쪽 아래를 (0,0)으로 한 위치
            public Rect? GetRect(string path)
            {
                var target = FlexiblePathUtility.FindByPath(root, path) as RectTransform;
                if (target == null)
                    return null;
                Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, target);
                return new Rect(r.x - root.rect.xMin, r.y - root.rect.yMin, r.width, r.height);
            }

            // 자신과 밖으로 나가 보이는 자식(버튼 아래 글자 등)까지 덮는 영역. 화면 왼쪽 아래가 (0,0)
            public Rect? GetVisibleRect(string path)
            {
                var target = FlexiblePathUtility.FindByPath(root, path) as RectTransform;
                if (target == null)
                    return null;
                Rect local = FlexibleProfileState.VisibleBounds(target);
                Vector2 a = root.InverseTransformPoint(target.TransformPoint(local.min));
                Vector2 b = root.InverseTransformPoint(target.TransformPoint(local.max));
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - root.rect.xMin, Mathf.Min(a.y, b.y) - root.rect.yMin,
                    Mathf.Max(a.x, b.x) - root.rect.xMin, Mathf.Max(a.y, b.y) - root.rect.yMin);
            }

            public void Dispose() => renderer.Dispose();
        }

        public static Vector2 GetWorkSize(FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            float aspect = profile.workResolution.y > 0f ? profile.workResolution.x / profile.workResolution.y : 1f;
            return FlexiblePreviewRenderer.GetLogicalSize(aspect, settings.ReferenceShortSide);
        }

        // 이 프리팹을 실제로 그리는 캔버스 단위의 작업 크기
        public static Vector2 GetWorkSize(GameObject source, FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            float aspect = profile.workResolution.y > 0f ? profile.workResolution.x / profile.workResolution.y : 1f;
            return FlexiblePreviewRenderer.GetLogicalSize(source, settings, profile.id, aspect);
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

        private static FlexibleGridAdjustment ToAdjustment(FlexibleSpecBlock block, FlexibleWorkspace workspace, RectTransform target)
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
            if (block.scaleBy > 0f)
            {
                adjustment.overrideScale = true;
                adjustment.scale = FlexibleGridPlacementService.GetBaseScale(workspace, target) * block.scaleBy;
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
            mode = FlexibleAxisMode.Stretch;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                // 지정이 없으면 stretch. "auto"는 예전 버전이 내보낸 spec을 다시 적용할 때만 쓴다
                case "": case "stretch": mode = FlexibleAxisMode.Stretch; return true;
                case "auto": mode = FlexibleAxisMode.Auto; return true;
                case "start": mode = FlexibleAxisMode.Start; return true;
                case "end": mode = FlexibleAxisMode.End; return true;
                case "center": mode = FlexibleAxisMode.Center; return true;
                // 그 축은 세로(기준) 값 그대로 (코드가 그 축으로 움직이는 패널 등)
                case "keep": mode = FlexibleAxisMode.Stretch; return true;
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
                    if (state.controlSizeFitter)
                        warnings.Add($"{profile.id}/{FlexiblePathUtility.GetPath(root, entry.target)}: Content Size Fitter overrides are not exported");
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
            // 칸에 맞춰 줄이는 블록은 맞춘 배율이 칸 결과의 일부이고, 최대 배율이 원래와 다르면 scale로 내보낸다
            FlexibleGridAdjustment adjustment;
            bool fromGrid = state.fit
                ? FlexibleGridPlacementService.TryGetAdjustment(state.rect, state.gridResult, out adjustment)
                : FlexibleGridPlacementService.TryGetAdjustment(state.rect, state.gridResult, entry.target.localScale, out adjustment);
            if (state.gridOrigin && fromGrid)
            {
                FlexibleCell cell = state.gridCell;
                json.Numbers("cell", cell.column, cell.row, cell.width, cell.height)
                    .Value("modeX", ModeName(state.gridModeX))
                    .Value("modeY", ModeName(state.gridModeY))
                    .Value("safeArea", state.gridInsideSafeArea);
                if (state.fit)
                {
                    json.Value("fit", true);
                    if (Mathf.Abs(state.fitMaxScale - entry.target.localScale.x) > 0.0001f)
                        json.Numbers("scale", state.fitMaxScale);
                    adjustment.overrideScale = false;
                }
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
