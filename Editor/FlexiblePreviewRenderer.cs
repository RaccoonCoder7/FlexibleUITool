using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 검수 문제 하나 (code는 언어와 무관한 식별자, blocks는 루트 기준 경로)
    public class FlexibleIssue
    {
        public string code;
        public string[] blocks;

        public string Message
        {
            get
            {
                switch (code)
                {
                    case "OUT_OF_SCREEN": return F("Out of screen: {0}", Name(0));
                    case "OVERLAP": return F("Overlap: {0} ↔ {1}", Name(0), Name(1));
                    case "OUTSIDE_SAFE_AREA": return F("Control outside SafeArea: {0}", Name(0));
                    default: return code;
                }
            }
        }

        private string Name(int index)
        {
            string path = blocks[index];
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }
    }

    // 프리뷰 씬에서 UI 프리팹 사본을 임의 비율로 렌더링하고 배치 문제를 찾는다 (미리보기·일괄 검수 공용)
    public sealed class FlexiblePreviewRenderer : IDisposable
    {
        private const float OverlapTolerance = 2f;

        private Scene scene;
        private readonly Camera camera;
        private readonly RectTransform canvasRect;
        private GameObject template;
        private GameObject instance;
        private FlexibleLayout layout;
        private RenderTexture texture;
        private HashSet<string> baseNested;
        private bool baseNestedReady;

        public bool Dirty { get; set; } = true;
        public RenderTexture Texture => texture;
        public GameObject Instance => instance;
        public Vector2 LogicalSize { get; private set; }

        public FlexiblePreviewRenderer()
        {
            scene = EditorSceneManager.NewPreviewScene();

            var cameraObject = new GameObject("FlexiblePreviewCamera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 4000f;
            cameraObject.transform.position = new Vector3(0f, 0f, -1000f);

            var canvasObject = new GameObject("FlexiblePreviewCanvas", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.position = Vector3.zero;
        }

        // 짧은 변을 기준 해상도 짧은 변에 맞춘 논리 크기 (런타임 FlexibleCanvasScaler와 같은 규칙)
        public static Vector2 GetLogicalSize(float aspect, float shortSide)
        {
            return aspect >= 1f ? new Vector2(shortSide * aspect, shortSide) : new Vector2(shortSide, shortSide / aspect);
        }

        // profileId가 null이면 사본을 그대로(작업 프리팹 등) 그린다
        public void Render(GameObject source, string profileId, Vector2 logicalSize, Vector2Int pixelSize, Rect normalizedSafeArea)
        {
            if (!Prepare(source, profileId, logicalSize, normalizedSafeArea))
                return;

            EnsureTexture(pixelSize);
            camera.targetTexture = texture;
            camera.aspect = logicalSize.x / logicalSize.y;
            camera.orthographicSize = logicalSize.y * 0.5f;
            using (new FlexibleRendererFeatureGuard())
                camera.Render();
        }

        // 그리지 않고 배치만 맞춘다 (블록 위치 조회용)
        public bool Prepare(GameObject source, string profileId, Vector2 logicalSize, Rect normalizedSafeArea)
        {
            if (source == null)
                return false;

            if (Dirty || instance == null || source != template)
                Rebuild(source);

            LogicalSize = logicalSize;
            canvasRect.sizeDelta = logicalSize;

            if (layout != null && profileId != null)
                layout.ApplyProfile(profileId);

            foreach (FlexibleSafeArea safeArea in instance.GetComponentsInChildren<FlexibleSafeArea>(true))
                safeArea.ApplyNormalized(normalizedSafeArea);

            Canvas.ForceUpdateCanvases();
            return true;
        }

        public void SavePng(string path)
        {
            if (texture == null)
                return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            string folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        // 화면 밖으로 나간 블록, 블록끼리 겹침, SafeArea 밖 조작 요소
        public List<string> CollectIssues(Rect normalizedSafeArea)
        {
            return CollectIssueList(normalizedSafeArea).ConvertAll(i => i.Message);
        }

        public List<FlexibleIssue> CollectIssueList(Rect normalizedSafeArea)
        {
            var issues = new List<FlexibleIssue>();
            if (instance == null)
                return issues;

            var root = (RectTransform)instance.transform;
            Rect rootRect = root.rect;
            List<RectTransform> blocks = CollectBlocks(root);

            foreach (RectTransform block in blocks)
            {
                Rect r = GetRectInRoot(root, block);
                if (r.xMin < rootRect.xMin - OverlapTolerance || r.xMax > rootRect.xMax + OverlapTolerance
                    || r.yMin < rootRect.yMin - OverlapTolerance || r.yMax > rootRect.yMax + OverlapTolerance)
                    issues.Add(Issue("OUT_OF_SCREEN", root, block));
            }

            for (int i = 0; i < blocks.Count; i++)
            for (int j = i + 1; j < blocks.Count; j++)
            {
                if (blocks[i].IsChildOf(blocks[j]) || blocks[j].IsChildOf(blocks[i]))
                    continue;

                Rect a = GetRectInRoot(root, blocks[i]);
                Rect b = GetRectInRoot(root, blocks[j]);

                // 바 위에 얹은 타이틀처럼 한쪽이 다른 쪽 안에 완전히 들어가도, 세로 화면에서도 그랬을 때만 의도된 겹침으로 본다
                if (Contains(a, b) || Contains(b, a))
                {
                    HashSet<string> nested = GetBaseNested();
                    if (nested == null || nested.Contains(PairKey(FlexiblePathUtility.GetPath(root, blocks[i]), FlexiblePathUtility.GetPath(root, blocks[j]))))
                        continue;
                }

                float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (width > OverlapTolerance && height > OverlapTolerance)
                    issues.Add(Issue("OVERLAP", root, blocks[i], blocks[j]));
            }

            Rect safeRect = Rect.MinMaxRect(
                rootRect.xMin + rootRect.width * normalizedSafeArea.xMin, rootRect.yMin + rootRect.height * normalizedSafeArea.yMin,
                rootRect.xMin + rootRect.width * normalizedSafeArea.xMax, rootRect.yMin + rootRect.height * normalizedSafeArea.yMax);
            foreach (Selectable selectable in instance.GetComponentsInChildren<Selectable>(false))
            {
                Rect r = GetRectInRoot(root, (RectTransform)selectable.transform);
                if (r.xMin < safeRect.xMin - OverlapTolerance || r.xMax > safeRect.xMax + OverlapTolerance
                    || r.yMin < safeRect.yMin - OverlapTolerance || r.yMax > safeRect.yMax + OverlapTolerance)
                    issues.Add(Issue("OUTSIDE_SAFE_AREA", root, (RectTransform)selectable.transform));
            }

            return issues;
        }

        public void Dispose()
        {
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }

        private void Rebuild(GameObject source)
        {
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);

            template = source;
            baseNestedReady = false;
            instance = UnityEngine.Object.Instantiate(source, canvasRect, false);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var rt = (RectTransform)instance.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            layout = instance.GetComponent<FlexibleLayout>();
            if (layout != null && !layout.enabled)
                layout = null;
            Dirty = false;
        }

        private void EnsureTexture(Vector2Int size)
        {
            size = new Vector2Int(Mathf.Clamp(size.x, 16, 4096), Mathf.Clamp(size.y, 16, 4096));
            if (texture != null && texture.width == size.x && texture.height == size.y)
                return;

            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }

            texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
        }

        // 검수 대상 블록: 역할이 붙은 블록 + 화면 전체를 덮지 않는 루트 직속 블록
        private List<RectTransform> CollectBlocks(RectTransform root)
        {
            var blocks = new List<RectTransform>();
            var instanceLayout = root.GetComponent<FlexibleLayout>();
            if (instanceLayout != null)
            {
                foreach (FlexibleRoleTag tag in instanceLayout.Roles)
                {
                    if (tag.target != null && tag.target.gameObject.activeInHierarchy && !blocks.Contains(tag.target))
                        blocks.Add(tag.target);
                }
            }

            foreach (Transform child in root)
            {
                if (child is RectTransform rt && child.gameObject.activeSelf && !IsFullStretch(rt) && !blocks.Contains(rt))
                    blocks.Add(rt);
            }

            return blocks;
        }

        private static FlexibleIssue Issue(string code, RectTransform root, params RectTransform[] blocks)
        {
            return new FlexibleIssue { code = code, blocks = System.Array.ConvertAll(blocks, b => FlexiblePathUtility.GetPath(root, b)) };
        }

        // 세로(기준) 화면에서 한쪽이 다른 쪽 안에 들어 있던 블록 쌍. 기준 배치를 모르면 null (모든 포함을 허용)
        private HashSet<string> GetBaseNested()
        {
            if (baseNestedReady)
                return baseNested;
            baseNestedReady = true;
            baseNested = null;

            // 프리팹 모드에서 다른 화면을 편집 중이면 열린 내용은 기준 배치가 아니므로 저장된 프리팹(= 기준)을 쓴다
            GameObject baseSource = template;
            PrefabStage stage = template != null ? PrefabStageUtility.GetPrefabStage(template) : null;
            if (stage != null && !FlexibleScreenSession.IsBaseView(stage))
                baseSource = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            if (baseSource == null || !(baseSource.transform is RectTransform))
                return null;

            FlexibleLayout sourceLayout = baseSource.GetComponent<FlexibleLayout>();
            FlexibleProfileSettings settings = sourceLayout != null ? sourceLayout.Settings : null;
            FlexibleProfile baseProfile = settings != null ? settings.Find(settings.BaseProfileId) : null;
            Vector2 size = baseProfile != null && baseProfile.workResolution.x > 0f && baseProfile.workResolution.y > 0f
                ? GetLogicalSize(baseProfile.workResolution.x / baseProfile.workResolution.y, settings.ReferenceShortSide)
                : LogicalSize;

            var probe = new GameObject("FlexibleBaseProbe", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var probeRect = (RectTransform)probe.transform;
                probeRect.SetParent(canvasRect, false);
                probeRect.sizeDelta = size;
                GameObject copy = UnityEngine.Object.Instantiate(baseSource, probeRect, false);
                var root = (RectTransform)copy.transform;
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.sizeDelta = Vector2.zero;
                root.anchoredPosition = Vector2.zero;
                Canvas.ForceUpdateCanvases();

                baseNested = new HashSet<string>();
                List<RectTransform> blocks = CollectBlocks(root);
                for (int i = 0; i < blocks.Count; i++)
                for (int j = i + 1; j < blocks.Count; j++)
                {
                    Rect a = GetRectInRoot(root, blocks[i]);
                    Rect b = GetRectInRoot(root, blocks[j]);
                    if (Contains(a, b) || Contains(b, a))
                        baseNested.Add(PairKey(FlexiblePathUtility.GetPath(root, blocks[i]), FlexiblePathUtility.GetPath(root, blocks[j])));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
            return baseNested;
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) < 0 ? a + "\n" + b : b + "\n" + a;
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - OverlapTolerance && inner.xMax <= outer.xMax + OverlapTolerance
                && inner.yMin >= outer.yMin - OverlapTolerance && inner.yMax <= outer.yMax + OverlapTolerance;
        }

        public static bool IsFullStretch(RectTransform rt)
        {
            return rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
        }

        public static Rect GetRectInRoot(RectTransform root, RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = root.InverseTransformPoint(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 p = root.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
