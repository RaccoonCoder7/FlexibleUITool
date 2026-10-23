using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlexibleUITool
{
    // 자기 rect 비율로 프로필을 골라 등록된 하위 RectTransform 배치를 바꿔 끼운다 (프리팹 기본값 = 기준 프로필)
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Layout/Flexible Layout")]
    public class FlexibleLayout : UIBehaviour
    {
        [SerializeField] private FlexibleProfileSettings settings;
        [SerializeField] private List<FlexibleLayoutEntry> entries = new List<FlexibleLayoutEntry>();
        [SerializeField] private List<FlexibleRoleTag> roles = new List<FlexibleRoleTag>();

        private FlexibleProfileState[] baseStates;
        private int currentIndex = -1;
        private bool pending;

        public FlexibleProfileSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        public List<FlexibleLayoutEntry> Entries => entries;
        public List<FlexibleRoleTag> Roles => roles;
        public int CurrentProfileIndex => currentIndex;

        public FlexibleProfile CurrentProfile =>
            settings != null && currentIndex >= 0 && currentIndex < settings.Profiles.Count ? settings.Profiles[currentIndex] : null;

        public event Action<FlexibleLayout> ProfileChanged;

        protected override void Awake()
        {
            base.Awake();
            CaptureBase();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Refresh();
        }

        private void LateUpdate()
        {
            if (pending)
                Refresh();
        }

        public void Refresh()
        {
            if (!IsActive() || settings == null || settings.Profiles.Count == 0)
                return;

            // 레이아웃 재구축 루프 안에서 RectTransform을 건드리지 않도록 다음 LateUpdate로 미룬다
            if (CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                pending = true;
                return;
            }

            pending = false;
            Rect rect = ((RectTransform)transform).rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            int next = settings.SelectProfile(rect.width / rect.height, currentIndex);
            if (next == currentIndex)
                return;

            currentIndex = next;
            ApplyProfile(settings.Profiles[next].id);
            ProfileChanged?.Invoke(this);
        }

        // 해당 프로필 상태가 없는 대상은 기동 시점(프리팹 기본) 상태로 되돌린다
        public void ApplyProfile(string profileId)
        {
            if (baseStates == null || baseStates.Length != entries.Count)
                CaptureBase();

            for (int i = 0; i < entries.Count; i++)
            {
                FlexibleLayoutEntry entry = entries[i];
                if (entry.target == null)
                    continue;

                FlexibleProfileState baseState = baseStates[i];
                FlexibleProfileState state = entry.Find(profileId) ?? baseState;
                state.ApplyTo(entry, baseState);
            }
        }

        public string GetRole(RectTransform target)
        {
            FlexibleRoleTag tag = roles.Find(r => r.target == target);
            return tag?.role;
        }

        public void SetRole(RectTransform target, string role)
        {
            roles.RemoveAll(r => r.target == target || r.target == null);
            if (!string.IsNullOrEmpty(role))
                roles.Add(new FlexibleRoleTag { target = target, role = role });
        }

        private void CaptureBase()
        {
            baseStates = new FlexibleProfileState[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                FlexibleLayoutEntry entry = entries[i];
                if (entry.target == null)
                    continue;

                baseStates[i] = FlexibleProfileState.Capture(entry, null, entry.ControlsActive, entry.ControlsLayoutGroup);
            }
        }
    }
}
