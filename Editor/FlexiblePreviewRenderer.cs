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
        // 덧붙임 정보 (다른 프리팹이 차지한 자리의 이름 등)
        public string detail;

        public string Message
        {
            get
            {
                switch (code)
                {
                    case "OUT_OF_SCREEN": return F("Out of screen: {0}", Name(0));
                    case "OVERLAP": return F("Overlap: {0} ↔ {1}", Name(0), Name(1));
                    case "OUTSIDE_SAFE_AREA": return F("Control outside SafeArea: {0}", Name(0));
                    case "HIDDEN_OUT_OF_SCREEN": return F("Out of screen when shown: {0}", Name(0));
                    case "COLLAPSED": return F("Squashed to a negative size: {0}", Name(0));
                    case "CLIPPED_IN_LIST": return F("Items cut off across a list: {0}", Name(0));
                    case "UNDER_RESERVED": return F("Under {1}: {0}", Name(0), detail);
                    case "COVERS_RESERVED": return F("Covers part of {1}: {0}", Name(0), detail);
                    default: return code;
                }
            }
        }

        // 문제 종류 이름 (요약·묶음 표시용)
        public static string Title(string code)
        {
            switch (code)
            {
                case "OUT_OF_SCREEN": return T("Out of screen");
                case "OVERLAP": return T("Overlap");
                case "OUTSIDE_SAFE_AREA": return T("Control outside SafeArea");
                case "HIDDEN_OUT_OF_SCREEN": return T("Out of screen when shown");
                case "COLLAPSED": return T("Squashed");
                case "CLIPPED_IN_LIST": return T("Cut off in a list");
                case "UNDER_RESERVED": return T("Under another prefab");
                case "COVERS_RESERVED": return T("Covers another prefab");
                default: return code;
            }
        }

        // 화면에 보이는 블록만 보는 기존 검사가 놓치던 것 (숨긴 패널·뒤집힌 크기·목록 잘림·다른 프리팹 자리)
        public static bool IsExtra(string code)
        {
            return code != "OUT_OF_SCREEN" && code != "OVERLAP" && code != "OUTSIDE_SAFE_AREA";
        }

        // 관련 블록 이름 (겹침이면 「A ↔ B」)
        public string BlockNames => blocks.Length > 1 ? Name(0) + " ↔ " + Name(1) : Name(0);

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
        // 사본에 다른 화면 배치를 입혔는지. 입힌 뒤 「그대로 그리기」를 하려면 사본을 새로 만들어야 한다
        private bool profileApplied;
        // 마지막으로 맞춘 화면 (null이면 비율로 고른다)
        private string screenProfile;

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

        // profileId가 null이면 사본을 그대로(편집 중인 화면 등) 그린다. 안쪽 프리팹은 screenProfileId(없으면 profileId) 화면으로 맞춘다
        public void Render(GameObject source, string profileId, Vector2 logicalSize, Vector2Int pixelSize, Rect normalizedSafeArea, string screenProfileId = null, IDictionary<string, bool> view = null)
        {
            if (!Prepare(source, profileId, logicalSize, normalizedSafeArea, screenProfileId, view))
                return;

            EnsureTexture(pixelSize);
            camera.targetTexture = texture;
            camera.aspect = logicalSize.x / logicalSize.y;
            camera.orthographicSize = logicalSize.y * 0.5f;
            using (new FlexibleRendererFeatureGuard())
                camera.Render();
        }

        // 그리지 않고 배치만 맞춘다 (블록 위치 조회용)
        // view: 편집용 보기(경로 → 켬/끔, 루트는 "")를 사본에도 똑같이 적용한다
        public bool Prepare(GameObject source, string profileId, Vector2 logicalSize, Rect normalizedSafeArea, string screenProfileId = null, IDictionary<string, bool> view = null)
        {
            if (source == null)
                return false;

            if (Dirty || instance == null || source != template || (profileId == null && profileApplied))
                Rebuild(source);

            LogicalSize = logicalSize;
            canvasRect.sizeDelta = logicalSize;

            if (layout != null && profileId != null)
            {
                layout.ApplyProfile(profileId);
                profileApplied = true;
            }

            // 안쪽 프리팹(자기 FlexibleLayout을 가진 중첩 프리팹)도 같은 화면으로 (바깥이 먼저 놓은 뒤라 위에서 아래 순서)
            string screen = screenProfileId ?? profileId;
            screenProfile = screen;
            if (screen != null)
            {
                foreach (FlexibleLayout nested in instance.GetComponentsInChildren<FlexibleLayout>(true))
                {
                    if (nested.gameObject != instance)
                        nested.ApplyForScreen(screen);
                }
            }
            if (view != null && view.Count > 0)
                ApplyView(view);

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

            // 블록 목록만 넘기면 화면 전체를 덮는 컨테이너(맨 위 Canvas 등) 안쪽을 보지 않으므로, 편집 탭과 같이 전체를 본다
            foreach (RectTransform outside in FindOutside(root))
                issues.Add(Issue("OUT_OF_SCREEN", root, outside));

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
                // 화면 전체를 덮는 뒤 배경 버튼(바깥을 누르면 닫기)은 노치 밑까지 덮는 것이 정상
                if (Contains(r, safeRect))
                    continue;
                if (r.xMin < safeRect.xMin - OverlapTolerance || r.xMax > safeRect.xMax + OverlapTolerance
                    || r.yMin < safeRect.yMin - OverlapTolerance || r.yMax > safeRect.yMax + OverlapTolerance)
                    issues.Add(Issue("OUTSIDE_SAFE_AREA", root, (RectTransform)selectable.transform));
            }

            CollectReserved(root, blocks, issues);
            CollectHiddenAndCollapsed(root, issues);
            return issues;
        }

        // ───────── 다른 프리팹이 차지한 자리 ─────────

        // 그 자리보다 낮은 순서로 그려지는 버튼은 밑에 깔려 누를 수 없고, 높은 순서의 블록이 일부만 걸치면 그 자리를 가린다
        private void CollectReserved(RectTransform root, List<RectTransform> blocks, List<FlexibleIssue> issues)
        {
            FlexibleProfileSettings settings = FlexibleReservedAreas.SettingsOf(instance);
            if (settings.ReservedAreas.Count == 0)
                return;
            string profileId = screenProfile;
            if (profileId == null || settings.Find(profileId) == null)
                profileId = settings.Profiles[settings.SelectProfile(LogicalSize.x / Mathf.Max(1f, LogicalSize.y), -1)].id;
            string assetPath = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(assetPath))
                assetPath = PrefabStageUtility.GetPrefabStage(template)?.assetPath;
            string guid = string.IsNullOrEmpty(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);

            var orders = new Dictionary<Canvas, int>();
            foreach ((FlexibleReservedArea area, Rect rect) in FlexibleReservedAreas.ForScreen(settings, guid, profileId, root.rect))
            {
                string label = FlexibleReservedAreas.Label(area);
                foreach (Selectable selectable in instance.GetComponentsInChildren<Selectable>(false))
                {
                    var rt = (RectTransform)selectable.transform;
                    if (IsUnderHiddenCanvas(rt, root) || FlexibleReservedAreas.EffectiveOrder(rt, root, orders) > area.sortingOrder)
                        continue;
                    // 화면 전체를 덮는 뒤 배경 버튼(바깥을 누르면 닫기 등)은 그 자리를 다 덮어도 문제가 아니다
                    Rect r = GetRectInRoot(root, rt);
                    if (Overlaps(r, rect) && !Contains(r, rect))
                        issues.Add(new FlexibleIssue { code = "UNDER_RESERVED", blocks = new[] { FlexiblePathUtility.GetPath(root, rt) }, detail = label });
                }
                foreach (RectTransform block in blocks)
                {
                    if (!block.gameObject.activeInHierarchy || IsUnderHiddenCanvas(block, root) || FlexibleReservedAreas.EffectiveOrder(block, root, orders) <= area.sortingOrder)
                        continue;
                    Rect r = GetRectInRoot(root, block);
                    if (Overlaps(r, rect) && !Contains(r, rect))
                        issues.Add(new FlexibleIssue { code = "COVERS_RESERVED", blocks = new[] { FlexiblePathUtility.GetPath(root, block) }, detail = label });
                }
            }
        }

        private static bool Overlaps(Rect a, Rect b)
        {
            return Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > OverlapTolerance
                && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > OverlapTolerance;
        }

        // 맨 위를 뺀 위쪽 어딘가의 Canvas가 꺼져 있어 지금은 안 보이는지
        private static bool IsUnderHiddenCanvas(Transform t, Transform root)
        {
            for (Transform c = t; c != null && c != root; c = c.parent)
            {
                var canvas = c.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled)
                    return true;
            }
            return false;
        }

        // ───────── 숨긴 패널·뒤집힌 크기·목록 잘림 ─────────

        // 게임이 나중에 켜는 패널(꺼 둔 오브젝트, Canvas를 끈 패널)을 사본에서 잠깐 켜서, 화면 밖·뒤집힌 크기·목록 잘림을 찾는다
        private void CollectHiddenAndCollapsed(RectTransform root, List<FlexibleIssue> issues)
        {
            var hiddenRoots = new List<RectTransform>();
            var shown = new List<GameObject>();
            FindHidden(root, root, false, hiddenRoots, shown);
            foreach (GameObject go in shown)
                go.SetActive(true);
            try
            {
                if (shown.Count > 0)
                    Canvas.ForceUpdateCanvases();

                List<RectTransform> collapsed = FindCollapsed(root);
                foreach (RectTransform t in collapsed)
                    issues.Add(Issue("COLLAPSED", root, t));

                var outside = new List<RectTransform>();
                foreach (RectTransform hidden in hiddenRoots)
                    CollectHiddenOutside(root, root.rect, hidden, 0, collapsed, outside);
                foreach (RectTransform t in outside)
                    issues.Add(Issue("HIDDEN_OUT_OF_SCREEN", root, t));

                foreach (RectTransform t in FindClippedLists(root))
                    issues.Add(Issue("CLIPPED_IN_LIST", root, t));
            }
            finally
            {
                for (int i = shown.Count - 1; i >= 0; i--)
                    shown[i].SetActive(false);
                if (shown.Count > 0)
                    Canvas.ForceUpdateCanvases();
            }
        }

        // 숨긴 패널: Canvas가 꺼진 것, 꺼져 있고 화면에서 큰 자리를 차지하는 것(목록 칸·버튼 안 표시 같은 작은 것은 빼고).
        // 마스크 안(목록 견본)과 레이아웃 그룹 안(켜면 옆 칸이 밀림)의 꺼진 것은 켜지 않는다
        private static void FindHidden(RectTransform root, Transform parent, bool masked, List<RectTransform> hiddenRoots, List<GameObject> shown)
        {
            bool childMasked = masked || parent.GetComponent<Mask>() != null || parent.GetComponent<RectMask2D>() != null;
            bool grouped = parent.GetComponent<LayoutGroup>() != null;
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rt))
                    continue;
                if (!child.gameObject.activeSelf)
                {
                    if (childMasked || grouped || !IsPanelLike(root, rt))
                        continue;
                    shown.Add(child.gameObject);
                    hiddenRoots.Add(rt);
                }
                else if (IsHiddenByCanvas(rt))
                {
                    hiddenRoots.Add(rt);
                }
                FindHidden(root, rt, childMasked, hiddenRoots, shown);
            }
        }

        private static bool IsPanelLike(RectTransform root, RectTransform rt)
        {
            Rect r = GetRectInRoot(root, rt);
            Rect screen = root.rect;
            float share = r.width * r.height / Mathf.Max(1f, screen.width * screen.height);
            bool stretched = rt.anchorMin.x != rt.anchorMax.x && rt.anchorMin.y != rt.anchorMax.y;
            return share >= 0.15f || (stretched && share >= 0.02f);
        }

        // 크기가 음수가 된 것: 늘어나는 블록의 고정 여백이 이 화면에서 남은 자리보다 클 때 생긴다 (가장 바깥 것만)
        private static List<RectTransform> FindCollapsed(RectTransform root)
        {
            var result = new List<RectTransform>();
            CollectCollapsed(root, result);
            return result;
        }

        private static void CollectCollapsed(Transform parent, List<RectTransform> result)
        {
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf)
                    continue;
                Rect r = rt.rect;
                if (r.width < -1f || r.height < -1f)
                    result.Add(rt);
                else
                    CollectCollapsed(rt, result);
            }
        }

        private static void CollectHiddenOutside(RectTransform root, Rect screen, RectTransform target, int depth, List<RectTransform> collapsed, List<RectTransform> result)
        {
            // 이미 나간 것·뒤집힌 것의 안쪽은 따로 세지 않는다 (숨긴 패널 안의 숨긴 패널도 위에서부터 본다)
            if (result.Exists(r => target.IsChildOf(r)) || collapsed.Exists(c => target.IsChildOf(c)))
                return;
            if (IsOutside(GetRectInRoot(root, target), screen))
            {
                result.Add(target);
                return;
            }
            if (depth >= ChildCheckDepth || target.GetComponent<Mask>() != null || target.GetComponent<RectMask2D>() != null)
                return;
            foreach (Transform child in target)
            {
                if (child is RectTransform rt && child.gameObject.activeSelf)
                    CollectHiddenOutside(root, screen, rt, depth + 1, collapsed, result);
            }
        }

        // 한 방향으로만 스크롤하는 목록에서, 스크롤하지 않는 방향으로 칸이 잘리는 것 (칸을 코드가 만들면 격자 설정으로 계산)
        private static List<RectTransform> FindClippedLists(RectTransform root)
        {
            var result = new List<RectTransform>();
            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(false))
            {
                if (scroll.horizontal == scroll.vertical || scroll.content == null)
                    continue;
                bool vertical = scroll.vertical;
                RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
                Rect view = GetRectInRoot(root, viewport);
                bool clipped = false;
                bool anyItem = false;
                foreach (Transform child in scroll.content)
                {
                    if (!(child is RectTransform item) || !child.gameObject.activeSelf)
                        continue;
                    anyItem = true;
                    clipped |= SticksOut(GetRectInRoot(root, item), view, vertical);
                }

                var grid = scroll.content.GetComponent<GridLayoutGroup>();
                if (!anyItem && grid != null)
                {
                    bool fixedCount = grid.constraint == (vertical ? GridLayoutGroup.Constraint.FixedColumnCount : GridLayoutGroup.Constraint.FixedRowCount);
                    int count = fixedCount ? Mathf.Max(1, grid.constraintCount) : 1;
                    float need = vertical
                        ? grid.padding.horizontal + count * grid.cellSize.x + (count - 1) * grid.spacing.x
                        : grid.padding.vertical + count * grid.cellSize.y + (count - 1) * grid.spacing.y;
                    Vector3 contentScale = root.InverseTransformVector(scroll.content.TransformVector(Vector3.one));
                    need *= vertical ? Mathf.Abs(contentScale.x) : Mathf.Abs(contentScale.y);
                    clipped |= need > (vertical ? view.width : view.height) + OverlapTolerance;
                }
                if (clipped)
                    result.Add((RectTransform)scroll.transform);
            }
            return result;
        }

        private static bool SticksOut(Rect item, Rect view, bool vertical)
        {
            return vertical
                ? item.xMin < view.xMin - OverlapTolerance || item.xMax > view.xMax + OverlapTolerance
                : item.yMin < view.yMin - OverlapTolerance || item.yMax > view.yMax + OverlapTolerance;
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
            profileApplied = false;
            instance = UnityEngine.Object.Instantiate(source, canvasRect, false);
            instance.hideFlags = HideFlags.HideAndDontSave;
            FitCopy((RectTransform)instance.transform);
            // 맨 위가 꺼진 채(오브젝트 또는 Canvas) 저장된 프리팹(실행 중에 코드가 켬)도 그려 보이게 켠다
            instance.SetActive(true);
            var rootCanvas = instance.GetComponent<Canvas>();
            if (rootCanvas != null)
                rootCanvas.enabled = true;
            layout = instance.GetComponent<FlexibleLayout>();
            if (layout != null && !layout.enabled)
                layout = null;
            Dirty = false;
        }

        private void ApplyView(IDictionary<string, bool> view)
        {
            var map = new Dictionary<string, Transform>();
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                map[t == instance.transform ? "" : FlexiblePathUtility.GetPath(instance.transform, t)] = t;
            foreach (KeyValuePair<string, bool> pair in view)
            {
                if (pair.Key.Length == 0)
                {
                    // 맨 위는 사본을 늘 켜 두므로, Canvas가 꺼진 채 저장된 경우만 켠다
                    var rootCanvas = instance.GetComponent<Canvas>();
                    if (pair.Value && rootCanvas != null)
                        rootCanvas.enabled = true;
                    continue;
                }
                if (map.TryGetValue(pair.Key, out Transform target))
                {
                    target.gameObject.SetActive(pair.Value);
                    // 코드가 Canvas를 꺼서 숨기는 패널도 보이게
                    var canvas = target.GetComponent<Canvas>();
                    if (pair.Value && canvas != null)
                        canvas.enabled = true;
                }
            }
        }

        // 사본 루트를 부모(미리보기 캔버스)에 꽉 채운다.
        // 맨 위가 Screen Space 캔버스인 프리팹은 배율이 0으로 저장돼 있는 경우가 많아(실행 중엔 캔버스가 정한다) 1로 맞춘다
        public static void FitCopy(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
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
        public static List<RectTransform> CollectBlocks(RectTransform root)
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

            AddBlocks(root, root, 0, blocks);
            return blocks;
        }

        // 화면을 덮는 컨테이너(Canvas·MainUI 같은 묶음)는 그 자체로는 옮길 게 없으므로 안쪽 블록을 본다 (CLI describe와 같은 규칙).
        // 꺼진 것·Canvas가 꺼진 패널·마스크 안·안쪽 Flexible 프리팹 안은 들어가지 않는다
        private static void AddBlocks(RectTransform root, Transform parent, int depth, List<RectTransform> blocks)
        {
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf || IsHiddenByCanvas(rt))
                    continue;
                if (IsFullStretch(rt) || IsScreenSized(root, rt))
                {
                    bool inner = rt.GetComponent<FlexibleLayout>() != null || rt.GetComponent<Mask>() != null || rt.GetComponent<RectMask2D>() != null;
                    if (depth < ChildCheckDepth && !inner)
                        AddBlocks(root, rt, depth + 1, blocks);
                    continue;
                }
                if (!blocks.Contains(rt))
                    blocks.Add(rt);
            }
        }

        private static bool IsScreenSized(RectTransform root, RectTransform rt)
        {
            Rect r = GetRectInRoot(root, rt);
            return r.width >= root.rect.width * 0.98f && r.height >= root.rect.height * 0.98f;
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
                FitCopy(root);
                copy.SetActive(true);
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

        private const int ChildCheckDepth = 3;

        private static bool IsOutside(Rect r, Rect screen)
        {
            return r.xMin < screen.xMin - OverlapTolerance || r.xMax > screen.xMax + OverlapTolerance
                || r.yMin < screen.yMin - OverlapTolerance || r.yMax > screen.yMax + OverlapTolerance;
        }

        // 화면 밖으로 나가는 것: 검수 대상 블록과, 보이는 블록·화면을 덮는 패널 안쪽 자식(3단계까지).
        // 나간 것의 아래는 따로 세지 않고, 마스크로 잘리는 내용(스크롤 목록 등)과 Canvas가 꺼져 안 보이는 패널은 보지 않는다.
        // blocks를 주면 그 블록들만 본다
        public static List<RectTransform> FindOutside(RectTransform root, List<RectTransform> blocks = null)
        {
            var result = new List<RectTransform>();
            Rect screen = root.rect;
            if (blocks != null)
            {
                foreach (RectTransform block in blocks)
                {
                    if (IsOutside(GetRectInRoot(root, block), screen))
                        result.Add(block);
                    else if (block.gameObject.activeInHierarchy)
                        CollectOutsideChildren(root, screen, block, 1, result);
                }
                return result;
            }

            // 화면을 덮는 컨테이너(검수 블록에서는 빠짐)도 안쪽으로 내려가 본다
            List<RectTransform> checkedBlocks = CollectBlocks(root);
            foreach (Transform child in root)
            {
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf || IsHiddenByCanvas(rt) || checkedBlocks.Contains(rt))
                    continue;
                CollectOutsideChildren(root, screen, rt, 1, result);
            }
            foreach (RectTransform block in checkedBlocks)
            {
                if (IsHiddenByCanvas(block))
                    continue;
                if (IsOutside(GetRectInRoot(root, block), screen))
                    result.Add(block);
                else if (block.gameObject.activeInHierarchy)
                    CollectOutsideChildren(root, screen, block, 1, result);
            }
            return result;
        }

        private static bool IsHiddenByCanvas(RectTransform rt)
        {
            var canvas = rt.GetComponent<Canvas>();
            return canvas != null && !canvas.enabled;
        }

        private static void CollectOutsideChildren(RectTransform root, Rect screen, RectTransform parent, int depth, List<RectTransform> result)
        {
            if (depth > ChildCheckDepth || parent.GetComponent<Mask>() != null || parent.GetComponent<RectMask2D>() != null || IsHiddenByCanvas(parent))
                return;
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rt) || !child.gameObject.activeSelf)
                    continue;
                if (IsOutside(GetRectInRoot(root, rt), screen))
                    result.Add(rt);
                else
                    CollectOutsideChildren(root, screen, rt, depth + 1, result);
            }
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
