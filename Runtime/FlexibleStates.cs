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
        // 칸에 맞춰 줄이는 블록: 칸 영역(부모 기준 앵커)과 최대 배율. 같은 화면 안에서 크기가 바뀌어도 다시 맞춘다
        public bool fit;
        public Vector2 fitAnchorMin;
        public Vector2 fitAnchorMax;
        public float fitMaxScale = 1f;
        // 이 화면에서 바꾼 Content Size Fitter 설정 (칸 크기를 쓰려고 끈 경우 등)
        public bool controlSizeFitter;
        public ContentSizeFitter.FitMode fitterHorizontal;
        public ContentSizeFitter.FitMode fitterVertical;
        // 칸과 보이는 크기가 달라도 이대로 두기로 한 그리드 블록 (다시 묻지 않는다)
        public bool gridSizeKept;

        // 블록이 칸 영역에 다 들어가는 가장 큰 배율(최대 fitMaxScale)로 맞춘다
        public void ApplyFit(RectTransform target)
        {
            if (!fit || !(target.parent is RectTransform parent))
                return;
            Vector2 room = Vector2.Scale(parent.rect.size, fitAnchorMax - fitAnchorMin);
            Vector2 size = FitSize(target);
            if (room.x <= 0f || room.y <= 0f || size.x <= 0f || size.y <= 0f)
                return;
            float scale = Mathf.Min(fitMaxScale, room.x / size.x, room.y / size.y);
            target.localScale = new Vector3(scale, scale, target.localScale.z);
        }

        // 맞출 크기 (배율 1 기준): 블록 자신과, 그 밖으로 나가 보이는 자식(펼친 화살표·아래 버튼 등)까지 덮는 크기.
        // 배율은 피벗을 중심으로 바뀌므로 피벗에서 더 먼 쪽 거리의 두 배로 잡는다. 마스크 안쪽은 마스크 칸까지만 본다
        public static Vector2 FitSize(RectTransform target)
        {
            Rect own = target.rect;
            Vector2 min = own.min;
            Vector2 max = own.max;
            bool outside = false;
            foreach (Transform child in target)
                Grow(target, child as RectTransform, ref min, ref max, ref outside);
            if (!outside)
                return own.size;
            return new Vector2(2f * Mathf.Max(-min.x, max.x), 2f * Mathf.Max(-min.y, max.y));
        }

        private static void Grow(RectTransform target, RectTransform rt, ref Vector2 min, ref Vector2 max, ref bool outside)
        {
            if (rt == null || !rt.gameObject.activeSelf)
                return;
            var graphic = rt.GetComponent<Graphic>();
            // TMP가 글자용으로 만드는 보조 오브젝트는 칸이 실제 글자와 무관하다
            if (graphic != null && graphic.GetType().Name.StartsWith("TMP_SubMesh"))
                return;
            bool clips = rt.GetComponent<Mask>() != null || rt.GetComponent<RectMask2D>() != null;
            if (clips || (graphic != null && graphic.enabled && graphic.color.a > 0.01f))
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 p = target.InverseTransformPoint(corner);
                    if (p.x < min.x - 0.5f || p.y < min.y - 0.5f || p.x > max.x + 0.5f || p.y > max.y + 0.5f)
                        outside = true;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
            if (clips)
                return;
            foreach (Transform child in rt)
                Grow(target, child as RectTransform, ref min, ref max, ref outside);
        }

        public static FlexibleProfileState Capture(FlexibleLayoutEntry entry, string profileId, bool controlActive, bool controlLayoutGroup)
        {
            return Capture(entry, profileId, controlActive, controlLayoutGroup, false);
        }

        public static FlexibleProfileState Capture(FlexibleLayoutEntry entry, string profileId, bool controlActive, bool controlLayoutGroup, bool controlSizeFitter)
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

            if (controlSizeFitter && entry.sizeFitter != null)
            {
                state.controlSizeFitter = true;
                state.fitterHorizontal = entry.sizeFitter.horizontalFit;
                state.fitterVertical = entry.sizeFitter.verticalFit;
            }

            return state;
        }

        public void ApplyTo(FlexibleLayoutEntry entry)
        {
            ApplyTo(entry, null);
        }

        // 이 상태가 활성 여부·LayoutGroup을 기록하지 않았으면 fallback(기동 시점 기본 상태)의 값을 쓴다.
        // 그래야 다른 프로필이 바꿔 둔 값(예: 가로에서 숨김)이 이 프로필로 돌아왔을 때 남지 않는다
        public void ApplyTo(FlexibleLayoutEntry entry, FlexibleProfileState fallback)
        {
            // 피터를 먼저 바꿔야 그 뒤 크기 계산(칸에 맞춰 줄이기)이 바뀐 설정을 따른다
            FlexibleProfileState fitter = controlSizeFitter ? this : fallback;
            if (fitter != null && fitter.controlSizeFitter && entry.sizeFitter != null)
            {
                entry.sizeFitter.horizontalFit = fitter.fitterHorizontal;
                entry.sizeFitter.verticalFit = fitter.fitterVertical;
            }

            rect.ApplyTo(entry.target);
            ApplyFit(entry.target);

            FlexibleProfileState group = controlLayoutGroup ? this : fallback;
            if (group != null && group.controlLayoutGroup && entry.layoutGroup != null)
                group.layoutGroup.ApplyTo(entry.layoutGroup);

            FlexibleProfileState visibility = controlActive ? this : fallback;
            if (visibility != null && visibility.controlActive && entry.target.gameObject.activeSelf != visibility.active)
                entry.target.gameObject.SetActive(visibility.active);
        }
    }

    // 프로필별 상태를 가진 배치 대상
    [Serializable]
    public class FlexibleLayoutEntry
    {
        public RectTransform target;
        public LayoutGroup layoutGroup;
        public ContentSizeFitter sizeFitter;
        public List<FlexibleProfileState> states = new List<FlexibleProfileState>();

        public bool ControlsActive => states.Exists(s => s.controlActive);
        public bool ControlsLayoutGroup => layoutGroup != null && states.Exists(s => s.controlLayoutGroup);
        public bool ControlsSizeFitter => sizeFitter != null && states.Exists(s => s.controlSizeFitter);

        public FlexibleProfileState Find(string profileId)
        {
            return states.Find(s => s.profileId == profileId);
        }
    }
}
