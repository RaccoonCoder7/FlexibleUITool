using System;
using UnityEngine;
using UnityEngine.UI;

namespace FlexibleUITool
{
    // 화면 짧은 변을 기준 해상도 짧은 변에 맞추는 CanvasScaler (회전해도 UI 실제 크기 유지). 기존 CanvasScaler에서 바꾼 캔버스는 기준(세로) 화면에서 원래 규칙을 쓴다
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [AddComponentMenu("Layout/Flexible Canvas Scaler")]
    public class FlexibleCanvasScaler : CanvasScaler
    {
        // 바꾸기 전 CanvasScaler의 규칙을 기준 화면에서 쓸지. 계산에 쓰는 값은 CanvasScaler의 필드에 그대로 들어 있다
        [SerializeField] private bool keepsBaseRule;
        [SerializeField] private ScaleMode baseScaleMode = ScaleMode.ScaleWithScreenSize;
        [SerializeField] private FlexibleProfileSettings baseRuleSettings;

        private Canvas targetCanvas;
        private int profileIndex = -1;

        public bool KeepsBaseRule => keepsBaseRule;

        // 바꾸기 전 CanvasScaler의 규칙을 읽어 둔다. 돌려준 동작을 새 FlexibleCanvasScaler에 실행하면 기준 화면에서 그 규칙을 쓴다
        public static Action<FlexibleCanvasScaler> CaptureBaseRule(CanvasScaler original, FlexibleProfileSettings settings)
        {
            ScaleMode mode = original.uiScaleMode;
            float factor = original.scaleFactor;
            ScreenMatchMode matchMode = original.screenMatchMode;
            float match = original.matchWidthOrHeight;
            Unit unit = original.physicalUnit;
            float fallbackDpi = original.fallbackScreenDPI;
            float spriteDpi = original.defaultSpriteDPI;
            return scaler =>
            {
                scaler.keepsBaseRule = true;
                scaler.baseRuleSettings = settings;
                scaler.baseScaleMode = mode;
                scaler.scaleFactor = factor;
                scaler.screenMatchMode = matchMode;
                scaler.matchWidthOrHeight = match;
                scaler.physicalUnit = unit;
                scaler.fallbackScreenDPI = fallbackDpi;
                scaler.defaultSpriteDPI = spriteDpi;
            };
        }

        // 이 캔버스의 배율 규칙 (미리보기·검수가 실제 게임과 같은 단위로 계산할 때 쓴다)
        public FlexibleCanvasRule Rule => new FlexibleCanvasRule
        {
            valid = true,
            reference = m_ReferenceResolution,
            keepsBaseRule = keepsBaseRule,
            baseMode = baseScaleMode,
            scaleFactor = m_ScaleFactor,
            matchMode = m_ScreenMatchMode,
            match = m_MatchWidthOrHeight,
            unit = m_PhysicalUnit,
            fallbackDpi = m_FallbackScreenDPI,
        };

        // 다른 캔버스의 규칙을 그대로 쓴다 (프리팹 모드의 환경 캔버스가 실제 게임과 같은 단위가 되게)
        public void UseRule(FlexibleCanvasRule rule, FlexibleProfileSettings settings)
        {
            referenceResolution = rule.reference;
            keepsBaseRule = rule.keepsBaseRule;
            baseRuleSettings = settings;
            baseScaleMode = rule.baseMode;
            scaleFactor = rule.scaleFactor;
            screenMatchMode = rule.matchMode;
            matchWidthOrHeight = rule.match;
            physicalUnit = rule.unit;
            fallbackScreenDPI = rule.fallbackDpi;
            profileIndex = -1;
        }

        // 이 화면 크기에서의 배율 (실행 중 Canvas가 쓰는 값과 같다)
        public float GetScaleFactor(Vector2 screenSize)
        {
            return Rule.GetScale(screenSize, UsesBaseRule(screenSize, profileIndex, out _));
        }

        // 이 화면이 기준 화면 구간이고 원래 규칙을 쓰는지 (레이아웃과 같은 기준·여유값으로 고른다)
        private bool UsesBaseRule(Vector2 screenSize, int current, out int selected)
        {
            selected = current;
            if (!keepsBaseRule || baseRuleSettings == null || baseRuleSettings.Profiles.Count == 0 || screenSize.x <= 0f || screenSize.y <= 0f)
                return false;
            selected = baseRuleSettings.SelectProfile(screenSize.x / screenSize.y, current);
            return baseRuleSettings.Profiles[selected].id == baseRuleSettings.BaseProfileId;
        }

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

            bool useBaseRule = UsesBaseRule(screenSize, profileIndex, out profileIndex);
            if (useBaseRule)
            {
                // 기준 화면은 Unity의 원래 계산을 그대로 쓴다
                if (baseScaleMode == ScaleMode.ConstantPixelSize)
                    HandleConstantPixelSize();
                else if (baseScaleMode == ScaleMode.ConstantPhysicalSize)
                    HandleConstantPhysicalSize();
                else
                    base.HandleScaleWithScreenSize();
                return;
            }

            float referenceShort = Mathf.Min(m_ReferenceResolution.x, m_ReferenceResolution.y);
            if (referenceShort <= 0f)
                return;

            SetScaleFactor(Mathf.Min(screenSize.x, screenSize.y) / referenceShort);
            SetReferencePixelsPerUnit(m_ReferencePixelsPerUnit);
        }
    }

    // 화면 크기를 UI 단위로 바꾸는 캔버스 규칙 (FlexibleCanvasScaler와 같은 계산)
    [Serializable]
    public struct FlexibleCanvasRule
    {
        public bool valid;
        public Vector2 reference;
        public bool keepsBaseRule;
        public CanvasScaler.ScaleMode baseMode;
        public float scaleFactor;
        public CanvasScaler.ScreenMatchMode matchMode;
        public float match;
        public CanvasScaler.Unit unit;
        public float fallbackDpi;

        // baseScreen: 이 화면이 기준(세로) 화면인지
        public float GetScale(Vector2 screen, bool baseScreen)
        {
            if (!keepsBaseRule || !baseScreen)
            {
                float referenceShort = Mathf.Min(reference.x, reference.y);
                return referenceShort > 0f ? Mathf.Min(screen.x, screen.y) / referenceShort : 1f;
            }
            switch (baseMode)
            {
                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    return scaleFactor;
                case CanvasScaler.ScaleMode.ConstantPhysicalSize:
                    return (Screen.dpi > 0f ? Screen.dpi : fallbackDpi) / PointsPerInch();
            }
            float width = screen.x / reference.x;
            float height = screen.y / reference.y;
            switch (matchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    return Mathf.Min(width, height);
                case CanvasScaler.ScreenMatchMode.Shrink:
                    return Mathf.Max(width, height);
            }
            return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(width, 2f), Mathf.Log(height, 2f), match));
        }

        public bool SameAs(in FlexibleCanvasRule other)
        {
            if (reference != other.reference || keepsBaseRule != other.keepsBaseRule)
                return false;
            return !keepsBaseRule || (baseMode == other.baseMode && matchMode == other.matchMode && unit == other.unit
                && Mathf.Approximately(scaleFactor, other.scaleFactor) && Mathf.Approximately(match, other.match) && Mathf.Approximately(fallbackDpi, other.fallbackDpi));
        }

        public Vector2 GetLogicalSize(Vector2 screen, bool baseScreen)
        {
            float scale = GetScale(screen, baseScreen);
            return scale > 0f ? screen / scale : screen;
        }

        private float PointsPerInch()
        {
            switch (unit)
            {
                case CanvasScaler.Unit.Centimeters: return 2.54f;
                case CanvasScaler.Unit.Millimeters: return 25.4f;
                case CanvasScaler.Unit.Inches: return 1f;
                case CanvasScaler.Unit.Picas: return 6f;
            }
            return 72f;
        }
    }
}
