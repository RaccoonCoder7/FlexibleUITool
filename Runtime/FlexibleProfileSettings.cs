using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlexibleUITool
{
    // 프로젝트 공용 프로필(비율 구간·작업 해상도·그리드) 정의
    [CreateAssetMenu(fileName = "FlexibleProfileSettings", menuName = "FlexibleUITool/Profile Settings")]
    public class FlexibleProfileSettings : ScriptableObject
    {
        [SerializeField] private string baseProfileId = "portrait";
        [SerializeField] private float hysteresis = 0.01f;
        [SerializeField] private List<FlexibleProfile> profiles = new List<FlexibleProfile>
        {
            new FlexibleProfile("portrait", "Portrait", 0f, new Vector2(1080, 1920), new FlexibleGrid(4, 8, 24, 16)),
            new FlexibleProfile("square", "Square", 0.8f, new Vector2(1080, 1080), new FlexibleGrid(6, 6, 24, 16)),
            new FlexibleProfile("landscape", "Landscape", 1.25f, new Vector2(1920, 1080), new FlexibleGrid(8, 5, 24, 16)),
        };
        [SerializeField] private List<string> roles = new List<string> { "Header", "Footer", "Main", "Side", "Sub", "Overlay" };
        [SerializeField] private List<FlexibleTemplate> templates = new List<FlexibleTemplate>();
        [SerializeField] private List<FlexiblePreviewPreset> previewPresets = new List<FlexiblePreviewPreset>
        {
            new FlexiblePreviewPreset("Phone portrait (19.5:9)", new Vector2(1179, 2556), 0f, 0f, 0.069f, 0.04f),
            new FlexiblePreviewPreset("Phone landscape (19.5:9)", new Vector2(2556, 1179), 0.069f, 0.069f, 0f, 0.053f),
            new FlexiblePreviewPreset("Phone portrait (16:9)", new Vector2(1080, 1920), 0f, 0f, 0f, 0f),
            new FlexiblePreviewPreset("Foldable (open)", new Vector2(2176, 1812), 0f, 0f, 0f, 0f),
            new FlexiblePreviewPreset("Tablet portrait (3:4)", new Vector2(1640, 2360), 0f, 0f, 0.01f, 0.017f),
            new FlexiblePreviewPreset("Tablet landscape (4:3)", new Vector2(2360, 1640), 0f, 0f, 0.015f, 0.024f),
            new FlexiblePreviewPreset("Tablet split (half)", new Vector2(1180, 1640), 0f, 0f, 0.015f, 0.024f),
        };
        [SerializeField] private List<FlexibleReservedArea> reservedAreas = new List<FlexibleReservedArea>();
        // 안에 Canvas가 없는 프리팹이 어떤 규칙의 캔버스에 놓이는지 (프리팹 GUID별, 미리보기·검수용)
        [SerializeField] private List<FlexibleCanvasRuleEntry> canvasRules = new List<FlexibleCanvasRuleEntry>();

        public string BaseProfileId => baseProfileId;
        public List<FlexibleReservedArea> ReservedAreas => reservedAreas;
        public float Hysteresis => hysteresis;
        public IReadOnlyList<FlexibleProfile> Profiles => profiles;
        public IReadOnlyList<string> Roles => roles;
        public List<FlexibleTemplate> Templates => templates;
        public IReadOnlyList<FlexiblePreviewPreset> PreviewPresets => previewPresets;

        // 기준 프로필 작업 해상도의 짧은 변 (런타임 FlexibleCanvasScaler 기준과 맞춘다)
        public float ReferenceShortSide
        {
            get
            {
                FlexibleProfile baseProfile = Find(baseProfileId);
                return baseProfile != null ? Mathf.Min(baseProfile.workResolution.x, baseProfile.workResolution.y) : 1080f;
            }
        }

        public bool TryGetCanvasRule(string prefabGuid, out FlexibleCanvasRule rule)
        {
            FlexibleCanvasRuleEntry entry = canvasRules.Find(e => e.prefabGuid == prefabGuid);
            rule = entry != null ? entry.rule : default;
            return entry != null;
        }

        public void SetCanvasRule(string prefabGuid, FlexibleCanvasRule rule)
        {
            FlexibleCanvasRuleEntry entry = canvasRules.Find(e => e.prefabGuid == prefabGuid);
            if (entry == null)
            {
                entry = new FlexibleCanvasRuleEntry { prefabGuid = prefabGuid };
                canvasRules.Add(entry);
            }
            entry.rule = rule;
        }

        public int IndexOf(string profileId)
        {
            return profiles.FindIndex(p => p.id == profileId);
        }

        public FlexibleProfile Find(string profileId)
        {
            int index = IndexOf(profileId);
            return index >= 0 ? profiles[index] : null;
        }

        // 이 프로필로 선택되는 미리보기 프리셋들의 SafeArea 여백 최댓값 (정규화 좌,우,위,아래)
        public Vector4 GetSafeInsets(int profileIndex)
        {
            var insets = Vector4.zero;
            foreach (FlexiblePreviewPreset preset in previewPresets)
            {
                if (SelectProfile(preset.Aspect, -1) != profileIndex)
                    continue;

                insets = Vector4.Max(insets, new Vector4(preset.safeLeft, preset.safeRight, preset.safeTop, preset.safeBottom));
            }
            return insets;
        }

        public float MaxAspectOf(int index)
        {
            return index + 1 < profiles.Count ? profiles[index + 1].minAspect : float.PositiveInfinity;
        }

        // 가장 넓은 화면으로 보는 비율 (21:9 가로 ≈ 2.33)
        public const float WidestAspect = 2.4f;

        // 이 프로필이 화면에 나올 수 있는 가장 좁은·넓은 비율 (히스테리시스로 경계 너머까지 유지되는 만큼 포함). 기기 프리셋이 없는 구간 검사용
        public List<float> GetEdgeAspects(int index)
        {
            var edges = new List<float>();
            float max = MaxAspectOf(index);
            if (index > 0)
                edges.Add(profiles[index].minAspect - hysteresis);
            edges.Add(float.IsInfinity(max) ? Mathf.Max(WidestAspect, profiles[index].minAspect + 0.1f) : max + hysteresis);
            return edges;
        }

        // 이 프로필로 선택되는 기기들의 SafeArea를 합친 영역 (정규화)
        public Rect GetSafeArea(int index)
        {
            Vector4 insets = GetSafeInsets(index);
            return Rect.MinMaxRect(insets.x, insets.w, 1f - insets.y, 1f - insets.z);
        }

        // 이미 넘어온 경계는 낮추고 아직 안 넘은 경계는 올려서 경계 근처 깜빡임을 막는다
        public int SelectProfile(float aspect, int currentIndex)
        {
            int selected = 0;
            for (int i = 1; i < profiles.Count; i++)
            {
                float threshold = profiles[i].minAspect;
                if (currentIndex >= 0)
                    threshold += i <= currentIndex ? -hysteresis : hysteresis;

                if (aspect >= threshold)
                    selected = i;
            }
            return selected;
        }

        private void OnValidate()
        {
            profiles.Sort((a, b) => a.minAspect.CompareTo(b.minAspect));
            hysteresis = Mathf.Max(0f, hysteresis);
        }
    }

    [Serializable]
    public class FlexibleCanvasRuleEntry
    {
        public string prefabGuid;
        public FlexibleCanvasRule rule;
    }

    // 비율 구간 하나
    [Serializable]
    public class FlexibleProfile
    {
        public string id;
        public string displayName;
        public float minAspect;
        public Vector2 workResolution;
        public FlexibleGrid grid;

        public FlexibleProfile()
        {
        }

        public FlexibleProfile(string id, string displayName, float minAspect, Vector2 workResolution, FlexibleGrid grid)
        {
            this.id = id;
            this.displayName = displayName;
            this.minAspect = minAspect;
            this.workResolution = workResolution;
            this.grid = grid;
        }

        public string Label => string.IsNullOrEmpty(displayName) ? id : $"{displayName} ({id})";
    }

    // 역할별 칸 배치 묶음 (프로필 하나 전용)
    [Serializable]
    public class FlexibleTemplate
    {
        public string name;
        public string profileId;
        public List<FlexibleTemplateSlot> slots = new List<FlexibleTemplateSlot>();
    }

    [Serializable]
    public class FlexibleTemplateSlot
    {
        public string role;
        public FlexibleCell cell;
        public FlexibleAxisMode modeX;
        public FlexibleAxisMode modeY;
        public bool insideSafeArea;

        // 그리드 배치 결과에 더하는 보정 (캔버스 논리 단위)
        public Vector2 positionOffset;
        public Vector2 sizeOffset;
        public bool overrideScale;
        public Vector3 scale = Vector3.one;

        public bool HasAdjustment => positionOffset != Vector2.zero || sizeOffset != Vector2.zero || overrideScale;
    }

    // 미리보기·검수용 기기 프리셋 (SafeArea 여백은 화면 대비 비율)
    [Serializable]
    public class FlexiblePreviewPreset
    {
        public string name;
        public Vector2 resolution;
        public float safeLeft;
        public float safeRight;
        public float safeTop;
        public float safeBottom;

        public FlexiblePreviewPreset()
        {
        }

        public FlexiblePreviewPreset(string name, Vector2 resolution, float safeLeft, float safeRight, float safeTop, float safeBottom)
        {
            this.name = name;
            this.resolution = resolution;
            this.safeLeft = safeLeft;
            this.safeRight = safeRight;
            this.safeTop = safeTop;
            this.safeBottom = safeBottom;
        }

        public float Aspect => resolution.y > 0f ? resolution.x / resolution.y : 1f;

        // 아래 기준 정규화 좌표
        public Rect NormalizedSafeArea => Rect.MinMaxRect(safeLeft, safeBottom, 1f - safeRight, 1f - safeTop);

        // 반시계 90도 회전 (위 여백 → 왼쪽, 아래 → 오른쪽)
        public FlexiblePreviewPreset Rotated()
        {
            return new FlexiblePreviewPreset(name + " (rotated)", new Vector2(resolution.y, resolution.x), safeTop, safeBottom, safeRight, safeLeft);
        }
    }

    // 다른 프리팹이 화면 위에 차지하는 자리 (예: 메뉴 바). 다른 프리팹을 편집·검수할 때 피해야 할 곳으로 쓴다
    [Serializable]
    public class FlexibleReservedArea
    {
        public string name;
        public string prefabGuid;
        public string path;
        // 이 자리를 그리는 캔버스 순서. 이보다 낮거나 같은 순서로 그려지는 버튼은 그 밑에 깔린다
        public int sortingOrder;
        public List<FlexibleReservedRect> rects = new List<FlexibleReservedRect>();
        // 이 프리팹들이 열려 있을 때는 게임이 이 자리를 숨긴다 (타이틀·대화 화면 등). 이 프리팹들은 피하지 않아도 된다
        public List<string> hiddenFor = new List<string>();

        // 화면별 자리 (아래 기준 정규화 좌표). 그 화면에서 보이지 않으면 null
        public Rect? Find(string profileId)
        {
            FlexibleReservedRect found = rects.Find(r => r.profileId == profileId);
            return found != null ? found.rect : (Rect?)null;
        }
    }

    [Serializable]
    public class FlexibleReservedRect
    {
        public string profileId;
        public Rect rect;
    }

    // 배치 보조용 칸 정의 (단위 = 캔버스 논리 단위)
    [Serializable]
    public struct FlexibleGrid
    {
        public int columns;
        public int rows;
        public float margin;
        public float gutter;

        public FlexibleGrid(int columns, int rows, float margin, float gutter)
        {
            this.columns = columns;
            this.rows = rows;
            this.margin = margin;
            this.gutter = gutter;
        }

        public bool IsValid => columns > 0 && rows > 0;
    }
}
