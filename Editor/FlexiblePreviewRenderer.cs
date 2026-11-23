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
                    case "BLOCKED_INPUT": return F("Cannot be pressed: {0} covers {1}", Name(0), Name(1));
                    case "OUTSIDE_SAFE_AREA": return F("Control outside SafeArea: {0}", Name(0));
                    case "TEXT_OUTSIDE_SAFE_AREA": return F("Text outside SafeArea (check it can be read): {0}", Name(0));
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
                case "BLOCKED_INPUT": return T("Button covered");
                case "OUTSIDE_SAFE_AREA": return T("Control outside SafeArea");
                case "TEXT_OUTSIDE_SAFE_AREA": return T("Text outside SafeArea");
                case "HIDDEN_OUT_OF_SCREEN": return T("Out of screen when shown");
                case "COLLAPSED": return T("Squashed");
                case "CLIPPED_IN_LIST": return T("Cut off in a list");
                case "UNDER_RESERVED": return T("Under another prefab");
                case "COVERS_RESERVED": return T("Covers another prefab");
                default: return code;
            }
        }

        // 약한 경고: 꼭 고칠 문제는 아니고 확인만 하면 되는 것 (노치 밑 글자는 장식일 수도 있다)
        public static bool IsWeak(string code)
        {
            return code == "TEXT_OUTSIDE_SAFE_AREA";
        }

        // 세로 화면에도 같은 문제가 있어도 그 화면에서 숨기지 않는 것 (누를 수 없는 버튼은 게임을 막는다)
        public static bool AlwaysShown(string code)
        {
            return code == "BLOCKED_INPUT";
        }

        // 화면에 보이는 블록만 보는 기존 검사가 놓치던 것 (숨긴 패널·뒤집힌 크기·목록 잘림·다른 프리팹 자리)
        public static bool IsExtra(string code)
        {
            return code != "OUT_OF_SCREEN" && code != "OVERLAP" && code != "BLOCKED_INPUT" && code != "OUTSIDE_SAFE_AREA" && code != "TEXT_OUTSIDE_SAFE_AREA";
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
        // 안쪽 Flexible 프리팹이 소유한 오브젝트 경로 → 그 프리팹 루트 경로 (그 프리팹 검수에서 따로 보므로 여기서는 뺀다)
        private Dictionary<string, string> nestedOwner;
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

        // 실제 게임에서 이 프리팹을 그리는 캔버스 단위의 논리 크기 (규칙을 모르면 프로젝트 기준)
        public static Vector2 GetLogicalSize(GameObject source, FlexibleProfileSettings settings, string profileId, float aspect)
        {
            FlexibleCanvasRule rule = FlexibleSettingsUtility.FindCanvasRule(source, settings);
            if (!rule.valid)
                return GetLogicalSize(aspect, settings.ReferenceShortSide);

            // 고정 픽셀 규칙은 기기 해상도에 따라 달라지므로 같은 비율의 미리보기 기기가 있으면 그 해상도로 본다
            Vector2 screen = GetLogicalSize(aspect, settings.ReferenceShortSide);
            foreach (FlexiblePreviewPreset preset in settings.PreviewPresets)
            {
                if (Mathf.Abs(preset.Aspect - aspect) < 0.001f)
                {
                    screen = preset.resolution;
                    break;
                }
            }
            return rule.GetLogicalSize(screen, profileId == settings.BaseProfileId);
        }

        // profileId가 null이면 사본을 그대로(편집 중인 화면 등) 그린다. 안쪽 프리팹은 screenProfileId(없으면 profileId) 화면으로 맞춘다
        public void Render(GameObject source, string profileId, Vector2 logicalSize, Vector2Int pixelSize, Rect normalizedSafeArea, string screenProfileId = null, IDictionary<string, bool> view = null)
        {
            using (new FlexibleFontDirtyGuard())
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
        }

        // 그리지 않고 배치만 맞춘다 (블록 위치 조회용)
        // view: 편집용 보기(경로 → 켬/끔, 루트는 "")를 사본에도 똑같이 적용한다
        public bool Prepare(GameObject source, string profileId, Vector2 logicalSize, Rect normalizedSafeArea, string screenProfileId = null, IDictionary<string, bool> view = null)
        {
            if (source == null)
                return false;
            using (new FlexibleFontDirtyGuard())
                return PrepareCopy(source, profileId, logicalSize, normalizedSafeArea, screenProfileId, view);
        }

        private bool PrepareCopy(GameObject source, string profileId, Vector2 logicalSize, Rect normalizedSafeArea, string screenProfileId, IDictionary<string, bool> view)
        {
            if (Dirty || instance == null || source != template || (profileId == null && profileApplied))
                Rebuild(source);

            LogicalSize = logicalSize;
            canvasRect.sizeDelta = logicalSize;

            if (layout != null && profileId != null)
            {
                layout.ApplyProfile(profileId);
                profileApplied = true;
            }
            else if (profileId == null)
                RefitEditing(source);

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
            viewHidden.Clear();
            if (view != null && view.Count > 0)
                ApplyView(view);

            // 미리보기 기기의 SafeArea로 놓고 컴포넌트는 끈다 (플레이 모드에서 실제 화면 값·앱 보정으로 다시 맞추지 않게)
            foreach (FlexibleSafeArea safeArea in instance.GetComponentsInChildren<FlexibleSafeArea>(true))
            {
                safeArea.ApplyNormalized(normalizedSafeArea);
                safeArea.enabled = false;
            }

            // 글자 크기는 첫 그리기에서 정해지므로, 한 번 그린 뒤 레이아웃을 다시 맞추고 한 번 더 그린다 (첫 장만 글자 크기를 모르는 채 찍히지 않게)
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform);
            Canvas.ForceUpdateCanvases();
            return true;
        }

        // 편집 중인 화면은 기록 대신 지금의 칸 배치를 쓴다. 칸에 맞춰 줄이는 블록은 이 화면 크기에서 다시 줄인다 (실행 중과 같게)
        private void RefitEditing(GameObject source)
        {
            var workspace = source.GetComponent<FlexibleWorkspace>();
            if (workspace == null || instance == null)
                return;
            Canvas.ForceUpdateCanvases();
            foreach (FlexibleGridPlacement placement in workspace.placements)
            {
                if (placement.target == null || !placement.fit)
                    continue;
                var target = FlexiblePathUtility.FindByPath(instance.transform, FlexiblePathUtility.GetPath(source.transform, placement.target)) as RectTransform;
                if (target == null)
                    continue;
                LayoutRebuilder.ForceRebuildLayoutImmediate(target);
                new FlexibleProfileState
                {
                    fit = true,
                    fitAnchorMin = placement.fitAnchorMin,
                    fitAnchorMax = placement.fitAnchorMax,
                    fitMaxScale = placement.fitMaxScale,
                }.ApplyFit(target);
            }
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

        // extraOnly: 보이는 블록의 화면 밖·겹침·SafeArea 검사는 건너뛰고, 숨긴 패널·뒤집힌 크기·목록 잘림·다른 프리팹 자리만 본다 (편집 탭의 숨은 문제용)
        public List<FlexibleIssue> CollectIssueList(Rect normalizedSafeArea, bool extraOnly = false)
        {
            var issues = new List<FlexibleIssue>();
            if (instance == null)
                return issues;

            var root = (RectTransform)instance.transform;
            Rect rootRect = root.rect;
            List<RectTransform> blocks = CollectBlocks(root);
            if (extraOnly)
            {
                CollectReserved(root, blocks, issues);
                CollectHiddenAndCollapsed(root, issues);
                return FilterIssues(root, issues);
            }

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

            // 노치 밑 글자 (버튼 안 글자는 위에서 버튼으로 본다). 실제 글자 범위로 보고, 화면 밖으로 나간 것은 화면 밖 검사가 본다
            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.enabled || !IsText(graphic) || graphic.color.a <= 0.01f || IsUnderHiddenCanvas(graphic.transform, root))
                    continue;
                Selectable owner = graphic.GetComponentInParent<Selectable>();
                if (owner != null && !Contains(GetRectInRoot(root, (RectTransform)owner.transform), safeRect))
                    continue;
                RectTransform rt = graphic.rectTransform;
                Rect r;
                if (!TryGetTextRect(root, rt, out r))
                {
                    if (!(graphic is Text legacy) || string.IsNullOrEmpty(legacy.text))
                        continue;
                    r = GetRectInRoot(root, rt);
                }
                if (!IsOutside(r, rootRect) && IsOutside(r, safeRect))
                    issues.Add(Issue("TEXT_OUTSIDE_SAFE_AREA", root, rt));
            }

            CollectBlockedInput(root, rootRect, issues);
            CollectReserved(root, blocks, issues);
            CollectHiddenAndCollapsed(root, issues);
            return FilterIssues(root, issues);
        }

        // 버튼 가운데를 그 위의 다른 그림이 덮어 눌리지 않는 경우.
        // 화면 전체로 늘어난 층(막·팝업·안내 오버레이)이 버튼 위에 있으면 일부러 막은 것이라 보지 않는다
        private void CollectBlockedInput(RectTransform root, Rect screen, List<FlexibleIssue> issues)
        {
            Dictionary<Transform, long> drawKeys = DrawKeys(root);
            var hits = new List<(Graphic graphic, Rect rect, long key)>();
            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(false))
            {
                if (graphic.enabled && graphic.raycastTarget)
                    hits.Add((graphic, GetRectInRoot(root, graphic.rectTransform), drawKeys[graphic.transform]));
            }

            foreach (Selectable selectable in instance.GetComponentsInChildren<Selectable>(false))
            {
                if (!selectable.enabled || !selectable.interactable)
                    continue;
                Transform button = selectable.transform;
                Vector2 point = GetRectInRoot(root, (RectTransform)button).center;
                if (!screen.Contains(point))
                    continue;

                Graphic top = null;
                long topKey = long.MinValue, ownKey = long.MinValue;
                var atPoint = new List<(Graphic graphic, long key)>();
                foreach ((Graphic graphic, Rect rect, long key) hit in hits)
                {
                    if (!hit.rect.Contains(point) || !ReceivesRaycast(hit.graphic, root, point))
                        continue;
                    atPoint.Add((hit.graphic, hit.key));
                    if (hit.key > topKey)
                    {
                        top = hit.graphic;
                        topKey = hit.key;
                    }
                    if (hit.graphic.transform.IsChildOf(button) && hit.key > ownKey)
                        ownKey = hit.key;
                }
                // 자기 그림이 그 자리에 없으면(마스크로 잘림 등) 원래 누를 수 없는 자리다
                if (ownKey == long.MinValue || top.transform.IsChildOf(button) || button.IsChildOf(top.transform))
                    continue;
                if (atPoint.Exists(h => h.key > ownKey && IsFullScreenLayer(h.graphic.transform, button, root, screen)))
                    continue;
                issues.Add(Issue("BLOCKED_INPUT", root, top.rectTransform, (RectTransform)button));
            }
        }

        // 그림에서 버튼과의 공통 부모 아래까지 올라가며 화면 전체로 늘어난 층이 있는지
        private static bool IsFullScreenLayer(Transform covering, Transform button, RectTransform root, Rect screen)
        {
            for (Transform t = covering; t != null && !button.IsChildOf(t); t = t.parent)
            {
                if (t is RectTransform rt && IsFullStretch(rt) && Contains(GetRectInRoot(root, rt), screen))
                    return true;
            }
            return false;
        }

        // CanvasGroup·마스크·꺼진 Canvas까지 따져 이 자리의 입력을 받는지
        private static bool ReceivesRaycast(Graphic graphic, RectTransform root, Vector2 point)
        {
            bool groups = true;
            for (Transform t = graphic.transform; t != null; t = t == root ? null : t.parent)
            {
                if (groups)
                {
                    foreach (CanvasGroup group in t.GetComponents<CanvasGroup>())
                    {
                        if (!group.enabled)
                            continue;
                        if (!group.blocksRaycasts)
                            return false;
                        if (group.ignoreParentGroups)
                            groups = false;
                    }
                }
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled)
                    return false;
                if (t != graphic.transform && t is RectTransform rt)
                {
                    var mask = t.GetComponent<Mask>();
                    var rectMask = t.GetComponent<RectMask2D>();
                    if (((mask != null && mask.enabled) || (rectMask != null && rectMask.enabled)) && !GetRectInRoot(root, rt).Contains(point))
                        return false;
                }
            }
            return true;
        }

        // ───────── 잘못 잡히는 문제 거르기 ─────────

        // 안쪽 Flexible 프리팹 것(그 프리팹 검수에서 따로 본다), 보이지 않는 것, 화면을 다 덮는 배경, 실제 글자는 화면 안인 글자 칸,
        // 스크롤바 기본 구조, 장식 그림과의 겹침, 화면을 덮는 막(Dim) 위아래로 나뉜 겹침을 빼고, 같은 문제는 한 번만 남긴다
        private List<FlexibleIssue> FilterIssues(RectTransform root, List<FlexibleIssue> issues)
        {
            var byPath = new Dictionary<string, RectTransform>();
            bool Find(string path, out RectTransform found)
            {
                if (!byPath.TryGetValue(path, out found))
                    byPath[path] = found = FlexiblePathUtility.FindByPath(root, path) as RectTransform;
                return found != null;
            }
            Rect screen = root.rect;
            List<(RectTransform rt, long key)> backdrops = null;
            Dictionary<Transform, long> drawKeys = null;
            var seen = new HashSet<string>();
            var result = new List<FlexibleIssue>();
            foreach (FlexibleIssue issue in issues)
            {
                if (!seen.Add(issue.code + "|" + string.Join("|", issue.blocks)))
                    continue;
                if (issue.blocks.Length == 1)
                {
                    if (nestedOwner != null && nestedOwner.ContainsKey(issue.blocks[0]))
                        continue;
                    if (!Find(issue.blocks[0], out RectTransform target))
                    {
                        result.Add(issue);
                        continue;
                    }
                    if (IsFalseSingle(issue.code, root, screen, target))
                        continue;
                }
                else if (issue.code == "OVERLAP")
                {
                    string ownerA = null, ownerB = null;
                    bool nestedA = nestedOwner != null && nestedOwner.TryGetValue(issue.blocks[0], out ownerA);
                    bool nestedB = nestedOwner != null && nestedOwner.TryGetValue(issue.blocks[1], out ownerB);
                    if (nestedA && nestedB && ownerA == ownerB)
                        continue;
                    if (Find(issue.blocks[0], out RectTransform a) && Find(issue.blocks[1], out RectTransform b))
                    {
                        if (IsDecorative(a) || IsDecorative(b) || !TextRectsOverlap(root, a, b))
                            continue;
                        drawKeys = drawKeys ?? DrawKeys(root);
                        backdrops = backdrops ?? FindBackdrops(root, drawKeys);
                        if (SeparatedByBackdrop(a, b, backdrops, drawKeys))
                            continue;
                    }
                }
                result.Add(issue);
            }
            return result;
        }

        private static bool IsFalseSingle(string code, RectTransform root, Rect screen, RectTransform target)
        {
            bool outside = code == "OUT_OF_SCREEN" || code == "HIDDEN_OUT_OF_SCREEN";
            if (outside)
            {
                // 보이는 것이 없거나(충돌판 등) 화면을 통째로 덮는 배경·막은 화면 밖으로 넘쳐도 문제가 아니다
                if (!HasVisual(target) || Contains(GetRectInRoot(root, target), screen))
                    return true;
                // 글자 칸이 넓어도 실제 글자가 화면 안이면 괜찮다
                if (TryGetTextRect(root, target, out Rect text) && !IsOutside(text, screen))
                    return true;
            }
            // 스크롤바 안의 기본 구조(음수 여백)와 숨긴 패널 속 스크롤바 위치는 보이는 문제가 아니다
            if ((code == "COLLAPSED" || code == "HIDDEN_OUT_OF_SCREEN") && target.GetComponentInParent<Scrollbar>(true) != null)
                return true;
            return false;
        }

        // 보이는 그림이나 누를 수 있는 것이 하나라도 있는지
        private static bool HasVisual(RectTransform target)
        {
            if (target.GetComponentInChildren<Selectable>(true) != null)
                return true;
            foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.enabled && graphic.color.a > 0.01f)
                    return true;
            }
            return false;
        }

        // 장식 그림: 누를 수 있는 것·글자가 없고, 그림이 모두 입력을 받지 않는다
        private static bool IsDecorative(RectTransform target)
        {
            if (target.GetComponentInChildren<Selectable>(true) != null)
                return false;
            bool any = false;
            foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.enabled)
                    continue;
                any = true;
                if (IsText(graphic) || graphic.raycastTarget)
                    return false;
            }
            return any;
        }

        private static bool IsText(Graphic graphic)
        {
            return graphic is Text || IsTmp(graphic);
        }

        // TMP 글자 (TextMeshProUGUI와 그것을 상속한 프로젝트 글자 컴포넌트까지, TMP 직접 참조 없이)
        private static bool IsTmp(Graphic graphic)
        {
            for (System.Type type = graphic.GetType(); type != null && type != typeof(MaskableGraphic); type = type.BaseType)
            {
                if (type.Name == "TMP_Text")
                    return true;
            }
            return false;
        }

        // 글자 블록은 칸 대신 실제 글자 범위로 겹침을 본다
        private static bool TextRectsOverlap(RectTransform root, RectTransform a, RectTransform b)
        {
            bool textA = TryGetTextRect(root, a, out Rect ra);
            bool textB = TryGetTextRect(root, b, out Rect rb);
            if (!textA && !textB)
                return true;
            if (!textA)
                ra = GetRectInRoot(root, a);
            if (!textB)
                rb = GetRectInRoot(root, b);
            return Overlaps(ra, rb);
        }

        // TMP 글자의 실제 범위 (TMP를 직접 참조하지 않고 textBounds를 읽는다). 글자가 없거나 TMP가 아니면 false
        private static bool TryGetTextRect(RectTransform root, RectTransform target, out Rect rect)
        {
            rect = default;
            Graphic graphic = target.GetComponent<Graphic>();
            if (graphic == null || !IsTmp(graphic))
                return false;
            if (!(graphic.GetType().GetProperty("textBounds")?.GetValue(graphic) is Bounds bounds) || bounds.size.x <= 0f || bounds.size.y <= 0f)
                return false;
            Vector2 min = root.InverseTransformPoint(target.TransformPoint(bounds.min));
            Vector2 max = root.InverseTransformPoint(target.TransformPoint(bounds.max));
            rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return true;
        }

        // 화면을 거의 다 덮고 입력을 막거나 보이는 막(Dim·팝업 배경). 그려지는 순서 값과 함께
        private static List<(RectTransform rt, long key)> FindBackdrops(RectTransform root, Dictionary<Transform, long> drawKeys)
        {
            var result = new List<(RectTransform, long)>();
            Rect screen = root.rect;
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.enabled || !(graphic.raycastTarget || graphic.color.a > 0.05f) || IsUnderHiddenCanvas(graphic.transform, root))
                    continue;
                Rect r = GetRectInRoot(root, graphic.rectTransform);
                if (r.width >= screen.width * 0.98f && r.height >= screen.height * 0.98f)
                    result.Add((graphic.rectTransform, drawKeys[graphic.transform]));
            }
            return result;
        }

        // 겹친 두 블록 사이에 화면을 덮는 막이 그려지면, 위쪽은 모달 패널이라 아래쪽과 동시에 눌리거나 읽히지 않는다
        private static bool SeparatedByBackdrop(RectTransform a, RectTransform b, List<(RectTransform rt, long key)> backdrops, Dictionary<Transform, long> drawKeys)
        {
            if (backdrops.Count == 0)
                return false;
            long keyA = drawKeys[a];
            long keyB = drawKeys[b];
            RectTransform low = keyA <= keyB ? a : b;
            long lo = Math.Min(keyA, keyB), hi = Math.Max(keyA, keyB);
            foreach ((RectTransform rt, long key) in backdrops)
            {
                if (key > lo && key < hi && !rt.IsChildOf(low) && !low.IsChildOf(rt))
                    return true;
            }
            return false;
        }

        // 그려지는 순서: 캔버스 정렬 순서가 먼저, 같으면 계층 순서
        private static Dictionary<Transform, long> DrawKeys(RectTransform root)
        {
            var keys = new Dictionary<Transform, long>();
            var orders = new Dictionary<Canvas, int>();
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                keys[all[i]] = ((long)FlexibleReservedAreas.EffectiveOrder(all[i], root, orders) << 32) + i;
            return keys;
        }

        // 안쪽 Flexible 프리팹이 소유한 오브젝트 (경로 → 그 프리팹 루트 경로). 바깥 프리팹에서 추가한 오브젝트는 빼서 바깥 소유로 둔다
        private static Dictionary<string, string> FindNestedOwners(GameObject source)
        {
            var owners = new Dictionary<string, string>();
            Transform root = source.transform;
            foreach (FlexibleLayout nested in source.GetComponentsInChildren<FlexibleLayout>(true))
            {
                if (nested.gameObject == source)
                    continue;
                string nestedPath = FlexiblePathUtility.GetPath(root, nested.transform);
                var added = new HashSet<Transform>(FlexiblePathUtility.AddedByOuter(nested.transform));
                foreach (Transform t in nested.GetComponentsInChildren<Transform>(true))
                {
                    if (t == nested.transform || IsUnder(t, added, nested.transform))
                        continue;
                    // 더 안쪽 Flexible 프리팹이 있으면 그쪽이 소유한다 (나중에 덮어쓴다)
                    owners[FlexiblePathUtility.GetPath(root, t)] = nestedPath;
                }
            }
            return owners;
        }

        public static bool IsUnder(Transform t, HashSet<Transform> roots, Transform stop)
        {
            if (roots.Count == 0)
                return false;
            for (Transform c = t; c != null && c != stop; c = c.parent)
            {
                if (roots.Contains(c))
                    return true;
            }
            return false;
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
            FindHidden(root, root, false, viewHidden, hiddenRoots, shown);
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
        private static void FindHidden(RectTransform root, Transform parent, bool masked, HashSet<GameObject> skip, List<RectTransform> hiddenRoots, List<GameObject> shown)
        {
            bool childMasked = masked || parent.GetComponent<Mask>() != null || parent.GetComponent<RectMask2D>() != null;
            bool grouped = parent.GetComponent<LayoutGroup>() != null;
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rt) || skip.Contains(child.gameObject))
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
                FindHidden(root, rt, childMasked, skip, hiddenRoots, shown);
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
            nestedOwner = FindNestedOwners(source);
            baseNestedReady = false;
            profileApplied = false;
            instance = UnityEngine.Object.Instantiate(source, canvasRect, false);
            FlexiblePathUtility.RegisterCopy(instance.transform, source.transform);
            instance.hideFlags = HideFlags.HideAndDontSave;
            FitCopy((RectTransform)instance.transform);
            // 맨 위가 꺼진 채(오브젝트 또는 Canvas) 저장된 프리팹(실행 중에 코드가 켬)도 그려 보이게 켠다
            instance.SetActive(true);
            RemoveRootCanvas(instance);
            layout = instance.GetComponent<FlexibleLayout>();
            if (layout != null && !layout.enabled)
                layout = null;
            FillPreviewItems(instance);
            Dirty = false;
        }

        // 코드가 채우는 목록에 견본 아이템을 넣는다 (이미 아이템이 있으면 그대로 둔다)
        private static void FillPreviewItems(GameObject copy)
        {
            foreach (FlexiblePreviewItems items in copy.GetComponentsInChildren<FlexiblePreviewItems>(true))
            {
                if (items.itemPrefab == null)
                    continue;
                bool hasItem = false;
                foreach (Transform child in items.transform)
                    hasItem |= child.gameObject.activeSelf;
                if (hasItem)
                    continue;
                for (int i = 0; i < Mathf.Max(1, items.count); i++)
                {
                    GameObject item = UnityEngine.Object.Instantiate(items.itemPrefab, items.transform, false);
                    item.hideFlags = HideFlags.HideAndDontSave;
                    item.SetActive(true);
                }
            }
        }

        // 보기에서 일부러 끈 오브젝트 (「나중에 열리는 패널」 검사가 다시 켜서 보고하지 않게)
        private readonly HashSet<GameObject> viewHidden = new HashSet<GameObject>();

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
                    if (!pair.Value)
                    {
                        viewHidden.Add(target.gameObject);
                        continue;
                    }
                    // 켜는 것은 위쪽의 꺼진 오브젝트·Canvas도 켠다 (꺼진 페이지 안의 하위 페이지만 골라 켜도 보이게, 창의 보기와 같게).
                    // 코드가 Canvas를 꺼서 숨기는 패널도 보이게
                    for (Transform t = target; t != null && t != instance.transform; t = t.parent)
                    {
                        if (!t.gameObject.activeSelf && (t == target || !view.TryGetValue(FlexiblePathUtility.GetPath(instance.transform, t), out bool keep) || keep))
                            t.gameObject.SetActive(true);
                        var canvas = t.GetComponent<Canvas>();
                        if (canvas != null && !canvas.enabled)
                            canvas.enabled = true;
                    }
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

        // 맨 위가 캔버스인 프리팹의 사본은 다른 캔버스 안에서 그려지지 않을 수 있어(URP), 사본에서만 캔버스와 그것을 요구하는 컴포넌트를 뗀다
        public static void RemoveRootCanvas(GameObject copy)
        {
            var canvas = copy.GetComponent<Canvas>();
            if (canvas == null)
                return;

            var removing = new List<Component> { canvas };
            for (bool found = true; found;)
            {
                found = false;
                foreach (Component component in copy.GetComponents<Component>())
                {
                    if (component == null || removing.Contains(component) || !RequiresAny(component.GetType(), removing))
                        continue;
                    removing.Add(component);
                    found = true;
                }
            }
            for (int i = removing.Count - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(removing[i]);
        }

        private static bool RequiresAny(Type type, List<Component> components)
        {
            foreach (RequireComponent require in type.GetCustomAttributes(typeof(RequireComponent), true))
            {
                foreach (Type needed in new[] { require.m_Type0, require.m_Type1, require.m_Type2 })
                {
                    if (needed != null && components.Exists(needed.IsInstanceOfType))
                        return true;
                }
            }
            return false;
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
                ? GetLogicalSize(baseSource, settings, baseProfile.id, baseProfile.workResolution.x / baseProfile.workResolution.y)
                : LogicalSize;

            var probe = new GameObject("FlexibleBaseProbe", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var probeRect = (RectTransform)probe.transform;
                probeRect.SetParent(canvasRect, false);
                probeRect.sizeDelta = size;
                GameObject copy = UnityEngine.Object.Instantiate(baseSource, probeRect, false);
                FlexiblePathUtility.RegisterCopy(copy.transform, baseSource.transform);
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
                    if (IsVisiblyOutside(root, block, screen))
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
                if (IsVisiblyOutside(root, block, screen))
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
                if (IsVisiblyOutside(root, rt, screen))
                    result.Add(rt);
                else
                    CollectOutsideChildren(root, screen, rt, depth + 1, result);
            }
        }

        // 그림이 없는 묶음은 틀만 나가고 안의 그림이 모두 화면 안이면 나간 것이 아니다
        private static bool IsVisiblyOutside(RectTransform root, RectTransform target, Rect screen)
        {
            if (!IsOutside(GetRectInRoot(root, target), screen))
                return false;
            Graphic own = target.GetComponent<Graphic>();
            if (own != null && own.enabled)
                return true;

            bool drawsSomething = false;
            foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.enabled || !IsOnUnder(graphic.transform, target))
                    continue;
                drawsSomething = true;
                if (IsOutside(GetRectInRoot(root, graphic.rectTransform), screen))
                    return true;
            }
            return !drawsSomething;
        }

        private static bool IsOnUnder(Transform t, Transform top)
        {
            for (; t != top; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                    return false;
            }
            return true;
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
