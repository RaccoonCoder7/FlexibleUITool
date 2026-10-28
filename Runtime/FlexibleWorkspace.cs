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

        public FlexibleGridPlacement FindPlacement(RectTransform target)
        {
            return placements.Find(p => p.target == target);
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
    }
}
