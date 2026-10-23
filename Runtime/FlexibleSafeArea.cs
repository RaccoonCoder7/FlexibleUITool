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

        private void Refresh(bool force)
        {
            Rect safeArea = Screen.safeArea;
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
