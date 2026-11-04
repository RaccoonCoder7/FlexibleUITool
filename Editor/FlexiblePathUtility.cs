using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

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

        // 부모의 레이아웃 그룹(가로·세로 줄, 격자)이 위치를 정하는 블록이면 그 부모. 아니면 null
        public static RectTransform GetLayoutGroupParent(Transform target)
        {
            if (target == null || target.parent == null)
                return null;
            var group = target.parent.GetComponent<LayoutGroup>();
            if (group == null || !group.enabled)
                return null;
            var ignorer = target.GetComponent<ILayoutIgnorer>();
            if (ignorer != null && ignorer.ignoreLayout)
                return null;
            return target.parent as RectTransform;
        }

        // 레이아웃 그룹 안이면, 대신 옮겨야 하는 블록 (그룹이 겹쳐 있으면 가장 바깥 그룹). 아니면 null
        public static RectTransform GetPlaceInstead(Transform target)
        {
            RectTransform result = null;
            for (RectTransform group = GetLayoutGroupParent(target); group != null; group = GetLayoutGroupParent(group))
                result = group;
            return result;
        }

        // GetPath로 만든 경로("A/B[1]/C")로 찾는다. 없으면 null
        public static Transform FindByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                Transform next = null;
                foreach (Transform child in current)
                {
                    if (GetSegment(child) == segment)
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null)
                    return null;
                current = next;
            }
            return current;
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
