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
        // 에디터 배치용: 이 프리팹만 프로젝트 그리드 대신 쓰는 그리드 (실행 중에는 쓰지 않는다)
        [SerializeField] private bool useOwnGrid;
        [SerializeField] private List<FlexibleProfileGrid> ownGrids = new List<FlexibleProfileGrid>();

        // 화면(세로·정사각·가로)을 무엇의 가로세로 비율로 고를지. 기본은 화면, 패널처럼 자기 모양을 따르려면 OwnSize
        [SerializeField] private FlexibleProfileSource profileSource = FlexibleProfileSource.Screen;

        private FlexibleProfileState[] baseStates;
        private int currentIndex = -1;
        private bool pending;
        private Canvas rootCanvas;
        private Vector2 lastSelectionSize;

        public FlexibleProfileSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        public List<FlexibleLayoutEntry> Entries => entries;
        public List<FlexibleRoleTag> Roles => roles;

        public FlexibleProfileSource ProfileSource
        {
            get => profileSource;
            set => profileSource = value;
        }

        public bool UseOwnGrid
        {
            get => useOwnGrid;
            set => useOwnGrid = value;
        }
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

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            rootCanvas = null;
            Refresh();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            rootCanvas = null;
        }

        // 화면을 따르면 자기 크기가 그대로여도(화면을 채우지 않는 패널) 화면이 바뀔 때 다시 고른다
        private void LateUpdate()
        {
            if (pending || (profileSource == FlexibleProfileSource.Screen && TryGetSelectionSize(out Vector2 size) && size != lastSelectionSize))
                Refresh();
        }

        // 화면을 고를 때 보는 크기: 기본은 이 UI가 놓인 화면(최상위 캔버스), OwnSize면 자기 크기
        public bool TryGetSelectionSize(out Vector2 size)
        {
            var target = (RectTransform)transform;
            if (profileSource == FlexibleProfileSource.Screen)
            {
                // 에디터에서는 부모 바뀜 알림이 오지 않으므로 매번 찾는다
                if (rootCanvas == null || !Application.isPlaying)
                {
                    Canvas canvas = GetComponentInParent<Canvas>(true);
                    rootCanvas = canvas != null ? canvas.rootCanvas : null;
                }
                if (rootCanvas != null)
                    target = (RectTransform)rootCanvas.transform;
            }
            size = target.rect.size;
            return size.x > 0f && size.y > 0f;
        }

        // 에디터용: 지금 값을 기준 배치로 다시 기억한다 (기준 화면 편집 뒤 다른 화면을 보여 주기 전에 부른다)
        public void RecaptureBase()
        {
            CaptureBase();
        }

        // 에디터 화면 전환·미리보기용: 화면 screenProfileId를 보여 줄 때 이 레이아웃이 쓸 배치를 적용한다
        public void ApplyForScreen(string screenProfileId)
        {
            if (settings == null || settings.Profiles.Count == 0)
                return;
            string id = screenProfileId;
            if (profileSource == FlexibleProfileSource.OwnSize)
            {
                Rect rect = ((RectTransform)transform).rect;
                if (rect.width > 0f && rect.height > 0f)
                    id = settings.Profiles[settings.SelectProfile(rect.width / rect.height, -1)].id;
            }
            ApplyProfile(id);
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
            if (!TryGetSelectionSize(out Vector2 size))
                return;
            bool resized = size != lastSelectionSize;
            lastSelectionSize = size;

            int next = settings.SelectProfile(size.x / size.y, currentIndex);
            if (next == currentIndex)
            {
                // 같은 화면 안에서 크기만 바뀌면 칸에 맞춰 줄이는 블록만 다시 맞춘다
                if (resized)
                    Refit();
                return;
            }

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

        private void Refit()
        {
            FlexibleProfile profile = CurrentProfile;
            if (profile == null)
                return;
            foreach (FlexibleLayoutEntry entry in entries)
            {
                FlexibleProfileState state = entry.target != null ? entry.Find(profile.id) : null;
                if (state != null && state.fit)
                    state.ApplyFit(entry.target);
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

        // 이 프리팹이 그 화면에서 쓰는 그리드: 전용 그리드를 켰으면 그 값, 아니면 프로젝트 그리드
        public FlexibleGrid GetGrid(string profileId)
        {
            if (useOwnGrid)
            {
                FlexibleProfileGrid own = ownGrids.Find(g => g.profileId == profileId);
                if (own != null)
                    return own.grid;
            }
            FlexibleProfile profile = settings != null ? settings.Find(profileId) : null;
            return profile != null ? profile.grid : default;
        }

        // 전용 그리드를 켤 때, 아직 값이 없는 화면은 지금 프로젝트 그리드로 채운다
        public void FillOwnGrids()
        {
            ownGrids.RemoveAll(g => g == null || settings == null || settings.Find(g.profileId) == null);
            if (settings == null)
                return;
            foreach (FlexibleProfile profile in settings.Profiles)
            {
                if (ownGrids.Find(g => g.profileId == profile.id) == null)
                    ownGrids.Add(new FlexibleProfileGrid { profileId = profile.id, grid = profile.grid });
            }
        }

        public void SetOwnGrid(string profileId, FlexibleGrid grid)
        {
            FlexibleProfileGrid own = ownGrids.Find(g => g.profileId == profileId);
            if (own == null)
                ownGrids.Add(own = new FlexibleProfileGrid { profileId = profileId });
            own.grid = grid;
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

    public enum FlexibleProfileSource
    {
        Screen,
        OwnSize,
    }

    // 프리팹 전용 그리드 (화면 하나분)
    [Serializable]
    public class FlexibleProfileGrid
    {
        public string profileId;
        public FlexibleGrid grid;
    }
}
