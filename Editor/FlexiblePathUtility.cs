using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 원본·작업 프리팹 사이에서 오브젝트를 맞추기 위한 계층 경로 계산
    public static class FlexiblePathUtility
    {
        // 루트 자신과, 하위 FlexibleLayout이 따로 관리하는 서브트리 안쪽은 제외한다
        public static List<RectTransform> CollectTargets(RectTransform root)
        {
            var result = new List<RectTransform>();
            Collect(root, result);
            return result;
        }

        public static Dictionary<string, RectTransform> BuildMap(RectTransform root)
        {
            var map = new Dictionary<string, RectTransform>();
            foreach (RectTransform rt in CollectTargets(root))
                map[GetPath(root, rt)] = rt;
            return map;
        }

        // 같은 이름 형제가 있으면 "이름[순번]"으로 구분한다
        public static string GetPath(Transform root, Transform target)
        {
            var segments = new List<string>();
            for (Transform t = target; t != null && t != root; t = t.parent)
                segments.Add(GetSegment(t));

            var builder = new StringBuilder();
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                builder.Append(segments[i]);
                if (i > 0)
                    builder.Append('/');
            }
            return builder.ToString();
        }

        private static void Collect(Transform parent, List<RectTransform> result)
        {
            foreach (Transform child in parent)
            {
                if (child is not RectTransform rt)
                    continue;

                result.Add(rt);
                if (child.GetComponent<FlexibleLayout>() == null)
                    Collect(child, result);
            }
        }

        private static string GetSegment(Transform t)
        {
            Transform parent = t.parent;
            if (parent == null)
                return t.name;

            int sameNameCount = 0;
            int order = 0;
            foreach (Transform sibling in parent)
            {
                if (sibling.name != t.name)
                    continue;

                if (sibling == t)
                    order = sameNameCount;
                sameNameCount++;
            }

            return sameNameCount > 1 ? $"{t.name}[{order}]" : t.name;
        }
    }
}
