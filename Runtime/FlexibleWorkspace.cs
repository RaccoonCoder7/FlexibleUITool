using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlexibleUITool
{
    // 편집 중인 화면 표식 (에디터 툴 전용). 프리팹 모드에 열린 프리팹에는 저장되지 않는 세션 표식으로 붙고(inPlace),
    // CLI가 쓰는 임시 작업 프리팹에는 저장되는 표식으로 붙는다. 저장된 기준 블록 경로·그리드 배치 기록을 보관한다
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class FlexibleWorkspace : MonoBehaviour
    {
        public string sourceGuid;
        public string profileId;
        public string sourceHash;
        public bool inPlace;
        public List<FlexibleWorkspaceLink> links = new List<FlexibleWorkspaceLink>();
        public List<FlexibleGridPlacement> placements = new List<FlexibleGridPlacement>();
        // 기준 화면에서 그리드로 놓은 기록 (기준 화면은 프리팹 자체라 파일에 남지 않고 이 세션 동안만 기억한다)
        public List<FlexibleGridPlacement> basePlacements = new List<FlexibleGridPlacement>();

        // 편집하는 동안만 켜거나 끈 오브젝트 (저장할 때는 원래 상태로 돌려서 저장한다)
        public List<FlexibleViewOverride> viewOverrides = new List<FlexibleViewOverride>();

        public FlexibleGridPlacement FindPlacement(RectTransform target)
        {
            return placements.Find(p => p.target == target);
        }

        public FlexibleViewOverride FindView(GameObject target)
        {
            return viewOverrides.Find(v => v.target == target);
        }
    }

    // 보기 전용으로 바꾼 보임 상태. 오브젝트 켜짐과 Canvas 켜짐(코드가 Canvas를 꺼서 숨기는 패널)을 함께 다룬다.
    // saved* = 저장될 원래 값, shown = 편집 중 보이게 할지
    [Serializable]
    public class FlexibleViewOverride
    {
        public GameObject target;
        public bool savedActive;
        public bool savedCanvas = true;
        public bool shown;

        public Canvas Canvas => target != null ? target.GetComponent<Canvas>() : null;

        // 보이면 오브젝트와 Canvas를 모두 켜고, 숨기면 오브젝트만 끈다
        public bool ExpectedActive => shown;
        public bool ExpectedCanvas => shown || savedCanvas;

        public static bool IsVisible(GameObject go)
        {
            var canvas = go.GetComponent<Canvas>();
            return go.activeSelf && (canvas == null || canvas.enabled);
        }
    }

    [Serializable]
    public class FlexibleWorkspaceLink
    {
        public string sourcePath;
        public RectTransform target;
    }

    // 그리드로 배치한 기록. 현재 값이 result와 같으면 그리드 블록, 다르면 이후 수동 조정된 블록
    [Serializable]
    public class FlexibleGridPlacement
    {
        public RectTransform target;
        public FlexibleCell cell;
        public FlexibleAxisMode modeX;
        public FlexibleAxisMode modeY;
        public bool insideSafeArea;
        public FlexibleRectState result;
        // 칸에 맞춰 줄이기 (FlexibleProfileState와 같은 뜻)
        public bool fit;
        public Vector2 fitAnchorMin;
        public Vector2 fitAnchorMax;
        public float fitMaxScale = 1f;
        // 칸과 보이는 크기가 달라도 이대로 두기로 했다 (FlexibleProfileState.gridSizeKept)
        public bool sizeKept;
    }
}
