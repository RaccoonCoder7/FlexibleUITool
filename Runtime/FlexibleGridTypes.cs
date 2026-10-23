using System;
using UnityEngine;

namespace FlexibleUITool
{
    public enum FlexibleAxisMode
    {
        Auto,
        Stretch,
        Start,
        End,
        Center,
    }

    // 그리드 칸 영역 (열·행은 좌상단 기준 0부터)
    [Serializable]
    public struct FlexibleCell
    {
        public int column;
        public int row;
        public int width;
        public int height;

        public FlexibleCell(int column, int row, int width, int height)
        {
            this.column = column;
            this.row = row;
            this.width = width;
            this.height = height;
        }

        public FlexibleCell Clamped(FlexibleGrid grid)
        {
            int c = Mathf.Clamp(column, 0, Mathf.Max(0, grid.columns - 1));
            int r = Mathf.Clamp(row, 0, Mathf.Max(0, grid.rows - 1));
            return new FlexibleCell(c, r, Mathf.Clamp(width, 1, Mathf.Max(1, grid.columns - c)), Mathf.Clamp(height, 1, Mathf.Max(1, grid.rows - r)));
        }

        public static FlexibleCell FromCorners(Vector2Int a, Vector2Int b)
        {
            return new FlexibleCell(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);
        }

        public override string ToString()
        {
            return $"({column},{row}) {width}x{height}";
        }
    }

    // 블록 역할 태그 (템플릿 매칭·검수 대상 지정용)
    [Serializable]
    public class FlexibleRoleTag
    {
        public RectTransform target;
        public string role;
    }
}
