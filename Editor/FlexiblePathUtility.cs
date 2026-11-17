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
                // 저장되지 않는 오브젝트(TMP가 폴백 글꼴용으로 만드는 보조 오브젝트 등)는 프리팹의 일부가 아니다
                if ((child.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0)
                    continue;

                result.Add(rt);
                if (child.GetComponent<FlexibleLayout>() == null)
                    Collect(child, result);
                else
                {
                    // 안쪽 Flexible 프리팹의 내용은 그 프리팹이 맡는다. 바깥 프리팹이 그 안에 추가한 오브젝트만 바깥에서 맡는다
                    foreach (Transform added in AddedByOuter(child))
                    {
                        if (added is RectTransform addedRect && (added.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        {
                            result.Add(addedRect);
                            if (added.GetComponent<FlexibleLayout>() == null)
                                Collect(added, result);
                        }
                    }
                }
            }
        }

        // 미리보기·검수용 사본(Object.Instantiate)은 프리팹 연결이 없어서, 원본을 기억해 두고 같은 자리(형제 순번)의 원본에게 묻는다
        private static readonly Dictionary<Transform, Transform> copySources = new Dictionary<Transform, Transform>();

        public static void RegisterCopy(Transform copy, Transform source)
        {
            var gone = new List<Transform>();
            foreach (Transform key in copySources.Keys)
            {
                if (key == null)
                    gone.Add(key);
            }
            foreach (Transform key in gone)
                copySources.Remove(key);
            if (copy != null && source != null)
                copySources[copy] = source;
        }

        // 사본 안의 오브젝트에 해당하는 원본 쪽 오브젝트 (사본이 아니거나 찾지 못하면 null)
        private static Transform FindSource(Transform t)
        {
            var indices = new List<int>();
            for (Transform c = t; c != null; c = c.parent)
            {
                if (copySources.TryGetValue(c, out Transform source) && source != null)
                {
                    for (int i = indices.Count - 1; i >= 0 && source != null; i--)
                        source = indices[i] < source.childCount ? source.GetChild(indices[i]) : null;
                    return source;
                }
                indices.Add(c.GetSiblingIndex());
            }
            return null;
        }

        // from 아래 target 의 자리를 to 아래 같은 자리로 옮겨 찾는다
        private static Transform SamePlace(Transform from, Transform target, Transform to)
        {
            var indices = new List<int>();
            for (Transform c = target; c != from; c = c.parent)
            {
                if (c == null)
                    return null;
                indices.Add(c.GetSiblingIndex());
            }
            for (int i = indices.Count - 1; i >= 0 && to != null; i--)
                to = indices[i] < to.childCount ? to.GetChild(indices[i]) : null;
            return to;
        }

        // 바깥 프리팹에서 안쪽 프리팹(nestedRoot) 안에 추가한 오브젝트들 (각 추가 묶음의 맨 위만, 계층 순서)
        public static List<Transform> AddedByOuter(Transform nestedRoot)
        {
            var added = new List<Transform>();
            if (!UnityEditor.PrefabUtility.IsAnyPrefabInstanceRoot(nestedRoot.gameObject))
            {
                Transform source = FindSource(nestedRoot);
                if (source == null || !UnityEditor.PrefabUtility.IsAnyPrefabInstanceRoot(source.gameObject))
                    return added;
                foreach (Transform t in AddedByOuter(source))
                {
                    Transform mine = SamePlace(source, t, nestedRoot);
                    if (mine != null)
                        added.Add(mine);
                }
                return added;
            }
            var set = new HashSet<Transform>();
            foreach (var item in UnityEditor.PrefabUtility.GetAddedGameObjects(nestedRoot.gameObject))
            {
                if (item.instanceGameObject != null && (item.instanceGameObject.hideFlags & HideFlags.DontSaveInEditor) == 0)
                    set.Add(item.instanceGameObject.transform);
            }
            if (set.Count == 0)
                return added;
            foreach (Transform t in nestedRoot.GetComponentsInChildren<Transform>(true))
            {
                if (set.Contains(t))
                    added.Add(t);
            }
            return added;
        }

        // 안쪽 Flexible 프리팹이 맡는 오브젝트인지 (그 안에 있고, 바깥이 추가한 것이 아님)
        public static bool IsOwnedByNested(Transform target, Transform root)
        {
            for (Transform c = target.parent; c != null && c != root; c = c.parent)
            {
                if (c.GetComponent<FlexibleLayout>() == null)
                    continue;
                for (Transform a = target; a != c; a = a.parent)
                {
                    if (UnityEditor.PrefabUtility.IsAddedGameObjectOverride(a.gameObject))
                        return false;
                }
                return true;
            }
            return false;
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
