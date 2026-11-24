using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 그리드 배치 결과에 더하는 보정 (위치·크기는 캔버스 논리 단위, 스케일은 지정 시 그 값으로 덮어씀)
    public struct FlexibleGridAdjustment
    {
        public Vector2 position;
        public Vector2 size;
        public bool overrideScale;
        public Vector3 scale;

        public bool IsZero => position.sqrMagnitude < 0.0001f && size.sqrMagnitude < 0.0001f && !overrideScale;

        public static FlexibleGridAdjustment From(FlexibleTemplateSlot slot)
        {
            return new FlexibleGridAdjustment { position = slot.positionOffset, size = slot.sizeOffset, overrideScale = slot.overrideScale, scale = slot.scale };
        }

        public override string ToString()
        {
            string text = F("position {0:0.#},{1:0.#} / size {2:0.#},{3:0.#}", position.x, position.y, size.x, size.y);
            return overrideScale ? text + F(" / scale {0:0.##}", scale.x) : text;
        }
    }

    // 작업 프리팹 안에서의 그리드 배치(기록 포함)·템플릿 저장/적용
    public static class FlexibleGridPlacementService
    {
        public const string BackdropFitWarning = "fit also shrinks this block's own background, which covers the whole portrait screen (a dim or page background), so the edges of the screen are left uncovered. Keep the block as it is and fit the parts inside it instead.";

        // 화면을 다 덮는 배경(막·페이지 배경)이 자기 자신에 붙은 블록 (세로 화면에서의 크기로 본다). 칸에 맞춰 줄이면 배경도 같이 줄어든다
        public static bool IsScreenBackdrop(RectTransform target, Vector2 baseSize, Vector2 baseScreen)
        {
            var graphic = target.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic == null || !graphic.enabled || !(graphic.color.a > 0.05f) || baseScreen.x <= 0f || baseScreen.y <= 0f)
                return false;
            return baseSize.x >= baseScreen.x * 0.95f && baseSize.y >= baseScreen.y * 0.95f;
        }

        private static string baseMapSource;
        private static Dictionary<string, RectTransform> baseMap;

        // 원본 프리팹 구조는 창이 그려질 때마다 다시 읽지 않는다. 애셋이 바뀌면 다시 읽는다
        static FlexibleGridPlacementService()
        {
            EditorApplication.projectChanged += () => baseMap = null;
        }

        // 배치하면서 그리드 기록을 남긴다 (반영 시 그리드/수동 블록 구분에 쓰인다)
        public static bool Apply(FlexibleGridContext context, FlexibleWorkspace workspace, RectTransform target,
            FlexibleCell cell, FlexibleAxisMode modeX, FlexibleAxisMode modeY, bool insideSafeArea)
        {
            return Apply(context, workspace, target, cell, modeX, modeY, insideSafeArea, default);
        }

        // 기록은 순수 그리드 결과로 남기고, 실제 값에는 보정을 더한다 (그래서 보정이 있으면 수동 블록이 된다)
        // 칸에 놓은 횟수 (튜토리얼 확인용)
        public static int ApplyCount { get; private set; }

        public static bool Apply(FlexibleGridContext context, FlexibleWorkspace workspace, RectTransform target,
            FlexibleCell cell, FlexibleAxisMode modeX, FlexibleAxisMode modeY, bool insideSafeArea, FlexibleGridAdjustment adjustment, bool fit = false)
        {
            ApplyCount++;
            FlexibleCell clamped = cell.Clamped(context.grid);
            // 칸에 맞춰 줄이는 블록은 크기를 지키는 축이어야 한다 (늘어나는 축은 가운데로)
            if (fit)
            {
                modeX = FlexibleGridMath.ResolveX(modeX, clamped, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : modeX;
                modeY = FlexibleGridMath.ResolveY(modeY, clamped, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : modeY;
            }
            if (!FlexibleGridMath.TryComputePlacement(target, context, clamped, modeX, modeY, insideSafeArea, out FlexibleRectState state))
                return false;

            // 순수 그리드 결과의 스케일은 원본 값으로 둔다. 스케일 지정이 없으면 지금 스케일을 유지하고, 원본과 다르면 보정으로 남는다
            Vector3 currentScale = target.localScale;
            state.localScale = GetBaseScale(workspace, target);

            FlexibleRectState adjusted = state;
            adjusted.anchoredPosition += adjustment.position;
            adjusted.sizeDelta += adjustment.size;
            adjusted.localScale = adjustment.overrideScale ? adjustment.scale : currentScale;

            Undo.RecordObjects(new Object[] { target, workspace }, "Flexible Grid Placement");
            adjusted.ApplyTo(target);

            // 칸 영역(늘어나는 앵커)과 최대 배율을 기록하고, 지금 크기에 맞는 배율을 결과와 실제 값 모두에 쓴다.
            // 크기는 레이아웃을 다시 계산한 뒤의 실제 크기로 잰다 (Content Size Fitter가 내용에 맞춰 늘린 높이 등)
            var area = new FlexibleRectState();
            float maxScale = adjustment.overrideScale ? adjustment.scale.x : state.localScale.x;
            if (fit && FlexibleGridMath.TryComputeSlotState(target, context.root, FlexibleGridMath.GetFitSlotRect(context.GetArea(insideSafeArea), context.grid, clamped),
                    FlexibleAxisMode.Stretch, FlexibleAxisMode.Stretch, out area)
                && target.parent is RectTransform parent)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(target);
                Vector2 room = Vector2.Scale(parent.rect.size, area.anchorMax - area.anchorMin);
                Vector2 size = FlexibleProfileState.FitSize(target);
                float scale = Mathf.Min(maxScale, room.x / Mathf.Max(1f, size.x), room.y / Mathf.Max(1f, size.y));
                state.localScale = adjusted.localScale = new Vector3(scale, scale, adjusted.localScale.z);
                target.localScale = adjusted.localScale;
            }
            else
            {
                fit = false;
            }
            RecordPrefabInstance(target);

            workspace.placements.RemoveAll(p => p.target == target || p.target == null);
            workspace.placements.Add(new FlexibleGridPlacement
            {
                target = target,
                cell = clamped,
                modeX = modeX,
                modeY = modeY,
                insideSafeArea = insideSafeArea,
                result = state,
                fit = fit,
                fitAnchorMin = area.anchorMin,
                fitAnchorMax = area.anchorMax,
                fitMaxScale = maxScale,
            });
            return true;
        }

        // 그리드 배치 뒤 위치·크기·스케일만 바꿨으면 칸+보정으로 표현할 수 있고, 앵커·피벗을 바꿨으면 표현할 수 없다
        public static bool TryGetAdjustment(FlexibleGridPlacement placement, out FlexibleGridAdjustment adjustment)
        {
            adjustment = default;
            if (placement.target == null)
                return false;

            // 칸에 맞춰 줄이는 블록은 맞춘 배율이 결과의 일부다
            if (placement.fit)
                return TryGetAdjustment(FlexibleRectState.Capture(placement.target), placement.result, out adjustment);
            var workspace = placement.target.GetComponentInParent<FlexibleWorkspace>(true);
            return TryGetAdjustment(FlexibleRectState.Capture(placement.target), placement.result, GetBaseScale(workspace, placement.target), out adjustment);
        }

        // 기록된 그리드 결과의 스케일은 믿지 않고 원본 스케일과 비교한다 (수정 전 데이터에는 이전 보정 스케일이 섞여 있을 수 있다)
        public static bool TryGetAdjustment(FlexibleRectState current, FlexibleRectState grid, Vector3 baseScale, out FlexibleGridAdjustment adjustment)
        {
            grid.localScale = baseScale;
            return TryGetAdjustment(current, grid, out adjustment);
        }

        public static bool TryGetAdjustment(FlexibleRectState current, FlexibleRectState grid, out FlexibleGridAdjustment adjustment)
        {
            adjustment = default;
            if (!Near(current.anchorMin, grid.anchorMin) || !Near(current.anchorMax, grid.anchorMax) || !Near(current.pivot, grid.pivot))
                return false;

            adjustment.position = current.anchoredPosition - grid.anchoredPosition;
            adjustment.size = current.sizeDelta - grid.sizeDelta;
            adjustment.overrideScale = (current.localScale - grid.localScale).sqrMagnitude > 0.000001f;
            adjustment.scale = current.localScale;
            return true;
        }

        // 그리드 배치 후 손대지 않은 블록 중, 현재 그리드·화면 크기로 다시 계산하면 위치가 달라지는 것
        public static List<(FlexibleGridPlacement placement, FlexibleRectState expected)> FindStale(FlexibleGridContext context, FlexibleWorkspace workspace)
        {
            var stale = new List<(FlexibleGridPlacement, FlexibleRectState)>();
            if (!context.grid.IsValid)
                return stale;

            foreach (FlexibleGridPlacement placement in workspace.placements)
            {
                if (!FlexibleWorkspaceService.IsUntouchedPlacement(placement))
                    continue;
                if (!FlexibleGridMath.TryComputePlacement(placement.target, context, placement.cell, placement.modeX, placement.modeY,
                        placement.insideSafeArea, out FlexibleRectState expected))
                    continue;

                expected.localScale = placement.result.localScale;
                if (!expected.Approximately(placement.result, 0.01f))
                    stale.Add((placement, expected));
            }
            return stale;
        }

        // 작업 프리팹 블록에 대응하는 원본 프리팹 블록의 스케일 (못 찾으면 지금 스케일)
        // Content Size Fitter가 크기를 내용에 맞춰 정하는 블록: 칸 크기를 줘도 덮어쓰므로 칸에 맞춰 줄여서만 놓을 수 있다
        public static bool IsContentSized(RectTransform target)
        {
            var fitter = target != null ? target.GetComponent<UnityEngine.UI.ContentSizeFitter>() : null;
            return fitter != null && fitter.enabled
                && (fitter.horizontalFit != UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained || fitter.verticalFit != UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained);
        }

        public static Vector3 GetBaseScale(FlexibleWorkspace workspace, RectTransform target)
        {
            if (workspace == null || target == null)
                return target != null ? target.localScale : Vector3.one;

            string sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);
            var source = string.IsNullOrEmpty(sourcePath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null || source.transform is not RectTransform sourceRoot)
                return target.localScale;

            string path = workspace.links.Find(l => l.target == target)?.sourcePath ?? FlexiblePathUtility.GetPath(workspace.transform, target);
            if (baseMap == null || baseMapSource != sourcePath)
            {
                baseMap = FlexiblePathUtility.BuildMap(sourceRoot);
                baseMapSource = sourcePath;
            }
            if (baseMap.TryGetValue(path, out RectTransform baseTarget) && baseTarget == null)
            {
                // 원본이 다시 임포트되어 옛 객체가 사라졌으면 다시 읽는다
                baseMap = FlexiblePathUtility.BuildMap(sourceRoot);
                baseMap.TryGetValue(path, out baseTarget);
            }
            return baseTarget != null ? baseTarget.localScale : target.localScale;
        }

        public static void ReapplyStale(FlexibleWorkspace workspace, List<(FlexibleGridPlacement placement, FlexibleRectState expected)> stale)
        {
            Undo.RecordObjects(stale.Select(s => (Object)s.placement.target).Append(workspace).ToArray(), "Flexible Grid Reapply");
            foreach (var (placement, expected) in stale)
            {
                expected.ApplyTo(placement.target);
                placement.result = expected;
                RecordPrefabInstance(placement.target);
            }
        }

        // 역할이 있고 그리드로 배치한 블록이 슬롯이 된다 (손으로 고친 만큼은 보정으로 저장, 역할당 하나). 빠진 블록은 이유를 남긴다
        public static FlexibleTemplate CreateTemplate(FlexibleWorkspace workspace, FlexibleLayout layout, string profileId, string name, List<string> skipped)
        {
            var template = new FlexibleTemplate { name = name, profileId = profileId };
            foreach (FlexibleRoleTag tag in layout.Roles)
            {
                if (tag.target == null || string.IsNullOrEmpty(tag.role))
                    continue;

                FlexibleGridPlacement placement = workspace.FindPlacement(tag.target);
                if (placement == null)
                {
                    skipped?.Add(F("{0} ({1}): never placed on the grid", tag.role, tag.target.name));
                    continue;
                }

                if (template.slots.Any(s => s.role == tag.role))
                {
                    skipped?.Add(F("{0} ({1}): this role is already included", tag.role, tag.target.name));
                    continue;
                }

                if (!TryGetAdjustment(placement, out FlexibleGridAdjustment adjustment))
                {
                    skipped?.Add(F("{0} ({1}): anchors or pivot changed after grid placement, so it cannot be saved as cells + adjustment", tag.role, tag.target.name));
                    continue;
                }

                template.slots.Add(new FlexibleTemplateSlot
                {
                    role = tag.role,
                    cell = placement.cell,
                    modeX = placement.modeX,
                    modeY = placement.modeY,
                    insideSafeArea = placement.insideSafeArea,
                    positionOffset = adjustment.position,
                    sizeOffset = adjustment.size,
                    overrideScale = adjustment.overrideScale,
                    scale = adjustment.scale,
                });
            }
            return template;
        }

        public static void SaveTemplate(FlexibleProfileSettings settings, FlexibleTemplate template)
        {
            Undo.RecordObject(settings, "Flexible Template");
            settings.Templates.RemoveAll(t => t.profileId == template.profileId && t.name == template.name);
            settings.Templates.Add(template);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        public static void DeleteTemplate(FlexibleProfileSettings settings, FlexibleTemplate template)
        {
            Undo.RecordObject(settings, "Flexible Template");
            settings.Templates.Remove(template);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        // 같은 역할의 블록이 여럿이면 모두 같은 칸에 놓인다
        public static int ApplyTemplate(FlexibleGridContext context, FlexibleWorkspace workspace, FlexibleLayout layout,
            FlexibleTemplate template, List<string> missingRoles)
        {
            int applied = 0;
            foreach (FlexibleTemplateSlot slot in template.slots)
            {
                List<RectTransform> targets = layout.Roles.Where(r => r.role == slot.role && r.target != null).Select(r => r.target).ToList();
                if (targets.Count == 0)
                    missingRoles?.Add(slot.role);

                foreach (RectTransform target in targets)
                {
                    if (Apply(context, workspace, target, slot.cell, slot.modeX, slot.modeY, slot.insideSafeArea, FlexibleGridAdjustment.From(slot)))
                        applied++;
                }
            }
            return applied;
        }

        private static bool Near(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) <= 0.0001f && Mathf.Abs(a.y - b.y) <= 0.0001f;
        }

        private static void RecordPrefabInstance(RectTransform target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
