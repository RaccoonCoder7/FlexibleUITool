using UnityEngine;

namespace FlexibleUITool
{
    // 실행 중에 코드가 채우는 목록(일기 칸·상품 목록 등)에 붙인다. 미리보기·검수 사본에서만 견본 아이템을 채워 그린다 (실행 중에는 아무것도 하지 않는다)
    [DisallowMultipleComponent]
    [AddComponentMenu("Layout/Flexible Preview Items")]
    public class FlexiblePreviewItems : MonoBehaviour
    {
        public GameObject itemPrefab;
        [Min(1)] public int count = 6;
    }
}
