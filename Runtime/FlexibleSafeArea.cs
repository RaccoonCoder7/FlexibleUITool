using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FlexibleUITool
{
    // 화면 SafeArea에 맞춰 앵커를 조절하는 컨테이너 (화면 전체를 덮는 부모 바로 아래에 둔다, 회전 시 자동 갱신)
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Layout/Flexible Safe Area")]
    public class FlexibleSafeArea : UIBehaviour
    {
        // 앱이 SafeArea를 고치는 곳: Screen.safeArea(픽셀, 아래 기준)를 받아 쓸 값을 돌려준다. null이면 Screen.safeArea 그대로.
        // 컨테이너마다 매 프레임 불리므로 가볍게 (상태바 높이 같은 값은 미리 구해 두고 계산만)
        public static Func<Rect, Rect> AdjustSafeArea;

        // 보정까지 반영한 지금의 SafeArea (픽셀, 아래 기준)
        public static Rect CurrentSafeArea => AdjustSafeArea != null ? AdjustSafeArea(Screen.safeArea) : Screen.safeArea;

        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh(true);
        }

        private void Update()
        {
            Refresh(false);
        }

        // 아래 기준 정규화 좌표 (0~1)
        public void ApplyNormalized(Rect normalized)
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = normalized.min;
            rt.anchorMax = normalized.max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 보정한 값이 바뀌어도 (상태바를 켜고 끄는 등) 다음 프레임에 다시 맞춘다
        private void Refresh(bool force)
        {
            Rect safeArea = CurrentSafeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
                return;
            if (screenSize.x <= 0 || screenSize.y <= 0)
                return;

            lastSafeArea = safeArea;
            lastScreenSize = screenSize;
            ApplyNormalized(Rect.MinMaxRect(
                safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y,
                safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y));
        }
    }
}
