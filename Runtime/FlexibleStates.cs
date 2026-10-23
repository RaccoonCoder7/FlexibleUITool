using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FlexibleUITool
{
    // RectTransform 배치값 스냅샷
    [Serializable]
    public struct FlexibleRectState
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector3 localScale;

        public static FlexibleRectState Capture(RectTransform rt)
        {
            return new FlexibleRectState
            {
                anchorMin = rt.anchorMin,
                anchorMax = rt.anchorMax,
                pivot = rt.pivot,
                anchoredPosition = rt.anchoredPosition,
                sizeDelta = rt.sizeDelta,
                localScale = rt.localScale,
            };
        }

        public void ApplyTo(RectTransform rt)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPosition;
            rt.localScale = localScale;
        }

        public bool Approximately(in FlexibleRectState other, float epsilon)
        {
            return Near(anchorMin, other.anchorMin, epsilon * 0.01f)
                && Near(anchorMax, other.anchorMax, epsilon * 0.01f)
                && Near(pivot, other.pivot, epsilon * 0.01f)
                && Near(anchoredPosition, other.anchoredPosition, epsilon)
                && Near(sizeDelta, other.sizeDelta, epsilon)
                && (localScale - other.localScale).sqrMagnitude <= epsilon * epsilon * 0.0001f;
        }

        private static bool Near(Vector2 a, Vector2 b, float epsilon)
        {
            return Mathf.Abs(a.x - b.x) <= epsilon && Mathf.Abs(a.y - b.y) <= epsilon;
        }
    }

    // LayoutGroup 파라미터 스냅샷 (Horizontal/Vertical/Grid 공용)
    [Serializable]
    public struct FlexibleLayoutGroupState
    {
        public int paddingLeft;
        public int paddingRight;
        public int paddingTop;
        public int paddingBottom;
        public TextAnchor childAlignment;
        public float spacing;
        public Vector2 cellSize;
        public Vector2 gridSpacing;
        public GridLayoutGroup.Corner startCorner;
        public GridLayoutGroup.Axis startAxis;
        public GridLayoutGroup.Constraint constraint;
        public int constraintCount;

        public static FlexibleLayoutGroupState Capture(LayoutGroup group)
        {
            var state = new FlexibleLayoutGroupState
            {
                paddingLeft = group.padding.left,
                paddingRight = group.padding.right,
                paddingTop = group.padding.top,
                paddingBottom = group.padding.bottom,
                childAlignment = group.childAlignment,
            };

            if (group is HorizontalOrVerticalLayoutGroup line)
                state.spacing = line.spacing;

            if (group is GridLayoutGroup grid)
            {
                state.cellSize = grid.cellSize;
                state.gridSpacing = grid.spacing;
                state.startCorner = grid.startCorner;
                state.startAxis = grid.startAxis;
                state.constraint = grid.constraint;
                state.constraintCount = grid.constraintCount;
            }

            return state;
        }

        public void ApplyTo(LayoutGroup group)
        {
            group.padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
            group.childAlignment = childAlignment;

            if (group is HorizontalOrVerticalLayoutGroup line)
                line.spacing = spacing;

            if (group is GridLayoutGroup grid)
            {
                grid.cellSize = cellSize;
                grid.spacing = gridSpacing;
                grid.startCorner = startCorner;
                grid.startAxis = startAxis;
                grid.constraint = constraint;
                grid.constraintCount = constraintCount;
            }
        }

        public bool Approximately(in FlexibleLayoutGroupState other, float epsilon)
        {
            return paddingLeft == other.paddingLeft
                && paddingRight == other.paddingRight
                && paddingTop == other.paddingTop
                && paddingBottom == other.paddingBottom
                && childAlignment == other.childAlignment
                && Mathf.Abs(spacing - other.spacing) <= epsilon
                && (cellSize - other.cellSize).sqrMagnitude <= epsilon * epsilon
                && (gridSpacing - other.gridSpacing).sqrMagnitude <= epsilon * epsilon
                && startCorner == other.startCorner
                && startAxis == other.startAxis
                && constraint == other.constraint
                && constraintCount == other.constraintCount;
        }
    }

    // 한 프로필에서 대상 하나가 가질 상태
    [Serializable]
    public class FlexibleProfileState
    {
        public string profileId;
        public FlexibleRectState rect;
        public bool controlActive;
        public bool active;
        public bool controlLayoutGroup;
        public FlexibleLayoutGroupState layoutGroup;
        public bool gridPlaced;
        public FlexibleCell gridCell;
        public FlexibleAxisMode gridModeX;
        public FlexibleAxisMode gridModeY;
        public bool gridInsideSafeArea;
        // 그리드로 배치한 적이 있으면 그때의 결과 (손으로 고친 뒤에도 남겨 칸+보정을 다시 계산한다)
        public bool gridOrigin;
        public FlexibleRectState gridResult;

        public static FlexibleProfileState Capture(FlexibleLayoutEntry entry, string profileId, bool controlActive, bool controlLayoutGroup)
        {
            var state = new FlexibleProfileState
            {
                profileId = profileId,
                rect = FlexibleRectState.Capture(entry.target),
                controlActive = controlActive,
                active = entry.target.gameObject.activeSelf,
                controlLayoutGroup = controlLayoutGroup && entry.layoutGroup != null,
            };

            if (state.controlLayoutGroup)
                state.layoutGroup = FlexibleLayoutGroupState.Capture(entry.layoutGroup);

            return state;
        }

        public void ApplyTo(FlexibleLayoutEntry entry)
        {
            rect.ApplyTo(entry.target);

            if (controlLayoutGroup && entry.layoutGroup != null)
                layoutGroup.ApplyTo(entry.layoutGroup);

            if (controlActive && entry.target.gameObject.activeSelf != active)
                entry.target.gameObject.SetActive(active);
        }
    }

    // 프로필별 상태를 가진 배치 대상
    [Serializable]
    public class FlexibleLayoutEntry
    {
        public RectTransform target;
        public LayoutGroup layoutGroup;
        public List<FlexibleProfileState> states = new List<FlexibleProfileState>();

        public bool ControlsActive => states.Exists(s => s.controlActive);
        public bool ControlsLayoutGroup => layoutGroup != null && states.Exists(s => s.controlLayoutGroup);

        public FlexibleProfileState Find(string profileId)
        {
            return states.Find(s => s.profileId == profileId);
        }
    }
}
