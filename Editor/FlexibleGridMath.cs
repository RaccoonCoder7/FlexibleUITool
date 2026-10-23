using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 배치 계산에 필요한 루트·그리드·SafeArea 여백(정규화 좌,우,위,아래) 묶음
    public readonly struct FlexibleGridContext
    {
        public readonly RectTransform root;
        public readonly FlexibleGrid grid;
        public readonly Vector4 safeInsets;

        public FlexibleGridContext(RectTransform root, FlexibleGrid grid, Vector4 safeInsets)
        {
            this.root = root;
            this.grid = grid;
            this.safeInsets = safeInsets;
        }

        // SafeArea 안쪽만 쓰면 대표 기기의 노치·홈 인디케이터 영역을 빼고 그리드를 나눈다
        public Rect GetArea(bool insideSafeArea)
        {
            Rect r = root.rect;
            if (!insideSafeArea)
                return r;
            return Rect.MinMaxRect(r.xMin + r.width * safeInsets.x, r.yMin + r.height * safeInsets.w,
                r.xMax - r.width * safeInsets.y, r.yMax - r.height * safeInsets.z);
        }
    }

    // 그리드 칸 ↔ RectTransform 앵커·피벗·크기 변환
    public static class FlexibleGridMath
    {
        // 그리드 영역(루트 로컬 좌표, y 위쪽 +) 안의 칸 영역
        public static Rect GetSlotRect(Rect area, FlexibleGrid grid, FlexibleCell cell)
        {
            Vector2 cellSize = GetCellSize(area, grid);
            float x0 = area.xMin + grid.margin + cell.column * (cellSize.x + grid.gutter);
            float x1 = x0 + cell.width * cellSize.x + (cell.width - 1) * grid.gutter;
            float yTop = area.yMax - grid.margin - cell.row * (cellSize.y + grid.gutter);
            float yBottom = yTop - (cell.height * cellSize.y + (cell.height - 1) * grid.gutter);
            return Rect.MinMaxRect(x0, yBottom, x1, yTop);
        }

        // 그리드 영역 밖(여백·SafeArea 밖)을 눌러도 가장 가까운 칸으로 맞춘다
        public static bool TryGetCell(Rect area, FlexibleGrid grid, Vector2 localPoint, out Vector2Int cell)
        {
            cell = default;
            Vector2 cellSize = GetCellSize(area, grid);
            float stepX = cellSize.x + grid.gutter;
            float stepY = cellSize.y + grid.gutter;
            if (stepX <= 0f || stepY <= 0f)
                return false;

            int column = Mathf.FloorToInt((localPoint.x - area.xMin - grid.margin + grid.gutter * 0.5f) / stepX);
            int row = Mathf.FloorToInt((area.yMax - grid.margin - localPoint.y + grid.gutter * 0.5f) / stepY);
            cell = new Vector2Int(Mathf.Clamp(column, 0, grid.columns - 1), Mathf.Clamp(row, 0, grid.rows - 1));
            return true;
        }

        // Auto: 양끝에 닿으면 Stretch, 한쪽만 닿으면 그쪽에 붙고, 안 닿으면 Center
        public static FlexibleAxisMode ResolveX(FlexibleAxisMode mode, FlexibleCell cell, FlexibleGrid grid)
        {
            return Resolve(mode, cell.column == 0, cell.column + cell.width == grid.columns);
        }

        // 세로축 Start = 아래, End = 위
        public static FlexibleAxisMode ResolveY(FlexibleAxisMode mode, FlexibleCell cell, FlexibleGrid grid)
        {
            return Resolve(mode, cell.row + cell.height == grid.rows, cell.row == 0);
        }

        // 칸 영역을 대상의 부모 좌표계로 옮겨 축별 모드대로 앵커·피벗·크기를 정한다
        public static void ApplySlot(RectTransform target, RectTransform root, Rect slotInRoot, FlexibleAxisMode modeX, FlexibleAxisMode modeY)
        {
            if (TryComputeSlotState(target, root, slotInRoot, modeX, modeY, out FlexibleRectState state))
                state.ApplyTo(target);
        }

        public static bool TryComputeSlotState(RectTransform target, RectTransform root, Rect slotInRoot, FlexibleAxisMode modeX, FlexibleAxisMode modeY,
            out FlexibleRectState state)
        {
            state = FlexibleRectState.Capture(target);
            if (target.parent is not RectTransform parent)
                return false;

            Vector2 a = parent.InverseTransformPoint(root.TransformPoint(slotInRoot.min));
            Vector2 b = parent.InverseTransformPoint(root.TransformPoint(slotInRoot.max));
            Rect parentRect = parent.rect;

            ResolveAxis(modeX, Mathf.Min(a.x, b.x) - parentRect.xMin, Mathf.Max(a.x, b.x) - parentRect.xMin, parentRect.width,
                out float minX, out float maxX, out float pivotX, out float sizeX);
            ResolveAxis(modeY, Mathf.Min(a.y, b.y) - parentRect.yMin, Mathf.Max(a.y, b.y) - parentRect.yMin, parentRect.height,
                out float minY, out float maxY, out float pivotY, out float sizeY);

            state.anchorMin = new Vector2(minX, minY);
            state.anchorMax = new Vector2(maxX, maxY);
            state.pivot = new Vector2(pivotX, pivotY);
            state.sizeDelta = new Vector2(sizeX, sizeY);
            state.anchoredPosition = Vector2.zero;
            return true;
        }

        public static bool TryComputePlacement(RectTransform target, FlexibleGridContext context, FlexibleCell cell,
            FlexibleAxisMode modeX, FlexibleAxisMode modeY, bool insideSafeArea, out FlexibleRectState state)
        {
            FlexibleGrid grid = context.grid;
            FlexibleCell clamped = cell.Clamped(grid);
            Rect slot = GetSlotRect(context.GetArea(insideSafeArea), grid, clamped);
            return TryComputeSlotState(target, context.root, slot, ResolveX(modeX, clamped, grid), ResolveY(modeY, clamped, grid), out state);
        }

        private static Vector2 GetCellSize(Rect area, FlexibleGrid grid)
        {
            float width = (area.width - grid.margin * 2f - (grid.columns - 1) * grid.gutter) / grid.columns;
            float height = (area.height - grid.margin * 2f - (grid.rows - 1) * grid.gutter) / grid.rows;
            return new Vector2(width, height);
        }

        private static FlexibleAxisMode Resolve(FlexibleAxisMode mode, bool touchesStart, bool touchesEnd)
        {
            if (mode != FlexibleAxisMode.Auto)
                return mode;
            if (touchesStart && touchesEnd)
                return FlexibleAxisMode.Stretch;
            if (touchesStart)
                return FlexibleAxisMode.Start;
            if (touchesEnd)
                return FlexibleAxisMode.End;
            return FlexibleAxisMode.Center;
        }

        private static void ResolveAxis(FlexibleAxisMode mode, float start, float end, float parentSize,
            out float anchorMin, out float anchorMax, out float pivot, out float size)
        {
            float length = parentSize > 0f ? parentSize : 1f;
            switch (mode)
            {
                case FlexibleAxisMode.Stretch:
                    anchorMin = start / length;
                    anchorMax = end / length;
                    pivot = 0.5f;
                    size = 0f;
                    break;
                case FlexibleAxisMode.Start:
                    anchorMin = anchorMax = start / length;
                    pivot = 0f;
                    size = end - start;
                    break;
                case FlexibleAxisMode.End:
                    anchorMin = anchorMax = end / length;
                    pivot = 1f;
                    size = end - start;
                    break;
                default:
                    anchorMin = anchorMax = (start + end) * 0.5f / length;
                    pivot = 0.5f;
                    size = end - start;
                    break;
            }
        }
    }
}
