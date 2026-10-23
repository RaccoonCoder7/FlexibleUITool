using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlexibleUITool
{
    // 작업 프리팹 표식 (에디터 툴 전용, 원본 경로·그리드 배치 기록을 보관)
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class FlexibleWorkspace : MonoBehaviour
    {
        public string sourceGuid;
        public string profileId;
        public string sourceHash;
        public List<FlexibleWorkspaceLink> links = new List<FlexibleWorkspaceLink>();
        public List<FlexibleGridPlacement> placements = new List<FlexibleGridPlacement>();

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
