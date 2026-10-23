using UnityEngine;
using UnityEngine.UI;

namespace FlexibleUITool
{
    // 화면 짧은 변을 기준 해상도 짧은 변에 맞추는 CanvasScaler (항상 Scale With Screen Size, 회전해도 UI 실제 크기 유지)
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [AddComponentMenu("Layout/Flexible Canvas Scaler")]
    public class FlexibleCanvasScaler : CanvasScaler
    {
        private Canvas targetCanvas;

        protected override void OnEnable()
        {
            targetCanvas = GetComponent<Canvas>();
            uiScaleMode = ScaleMode.ScaleWithScreenSize;
            base.OnEnable();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            uiScaleMode = ScaleMode.ScaleWithScreenSize;
            referenceResolution = new Vector2(1080, 1920);
        }

        protected override void OnValidate()
        {
            uiScaleMode = ScaleMode.ScaleWithScreenSize;
            base.OnValidate();
        }
#endif

        protected override void HandleScaleWithScreenSize()
        {
            Vector2 screenSize = targetCanvas.renderingDisplaySize;
            int displayIndex = targetCanvas.targetDisplay;
            if (displayIndex > 0 && displayIndex < Display.displays.Length)
            {
                Display display = Display.displays[displayIndex];
                screenSize = new Vector2(display.renderingWidth, display.renderingHeight);
            }

            float referenceShort = Mathf.Min(m_ReferenceResolution.x, m_ReferenceResolution.y);
            if (referenceShort <= 0f)
                return;

            SetScaleFactor(Mathf.Min(screenSize.x, screenSize.y) / referenceShort);
            SetReferencePixelsPerUnit(m_ReferencePixelsPerUnit);
        }
    }
}
