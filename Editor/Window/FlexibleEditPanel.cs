using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 편집 탭: 화면(세로·정사각·가로) 전환, 선택한 블록 상태, 그리드 배치, 템플릿
    [System.Serializable]
    public class FlexibleEditPanel
    {
        // 선택지는 FlexibleAxisMode.Stretch부터 (Auto는 고를 수 없다)
        private static string[] ModeLabelsX => new[] { T("Stretch"), T("Stick left"), T("Stick right"), T("Center") };
        private static string[] ModeLabelsY => new[] { T("Stretch"), T("Stick bottom"), T("Stick top"), T("Center") };
        private static readonly int DragHint = "FlexibleGridDrag".GetHashCode();
        private const string InsideSafeAreaKey = "FlexibleUITool.InsideSafeArea";

        [SerializeField] private bool showGrid;
        [SerializeField] private bool showTemplates;
        [SerializeField] private bool showMore;
        [SerializeField] private bool dragPlacement = true;
        [SerializeField] private int templateIndex;
        [SerializeField] private string templateName = "";
        // 칸에 놓은 적 없는 블록에 쓰는 SafeArea 기본값 (사용자별)
        private bool insideSafeArea = true;
        private bool dragging;
        private Vector2Int dragStart;
        private FlexibleCell dragCell;

        // 블록 하나의 그리드 입력값: 칸에 놓였으면 그 기록, 아니면 지금 덮고 있는 칸 + Stretch
        private struct BlockInput
        {
            public RectTransform target;
            public FlexibleGridPlacement placement;
            public FlexibleCell cell;
            public FlexibleAxisMode modeX;
            public FlexibleAxisMode modeY;
            public bool insideSafeArea;
            // 칸에 맞춰 줄이기 (배치 기록에 남는다)
            public bool fit;
            // 이번 배치에서 크기를 지키는 축을 칸 크기 대신 세로 화면 크기로 (한 번만)
            public bool ownSize;
        }

        public bool GridOpen => showGrid;

        public bool TemplatesOpen => showTemplates;

        // 템플릿 「적용」을 누른 횟수 (튜토리얼 확인용)
        public int TemplateApplyCount { get; private set; }

        public void SetFoldouts(bool grid, bool templates)
        {
            showGrid = grid;
            showTemplates = templates;
            SceneView.RepaintAll();
        }

        public void SuggestTemplateName(string name)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                templateName = name;
        }

        public void OnEnable()
        {
            insideSafeArea = EditorPrefs.GetBool(InsideSafeAreaKey, true);
        }

        private BlockInput GetInput(RectTransform target, FlexibleWorkspace session, FlexibleGridContext context)
        {
            var input = new BlockInput { target = target, placement = session.FindPlacement(target) };
            if (input.placement != null)
            {
                input.cell = input.placement.cell.Clamped(context.grid);
                // 예전 버전의 Auto는 지금 블록이 실제로 쓰는 모드로 보여 준다
                input.modeX = FlexibleGridMath.ResolveX(input.placement.modeX, input.cell, context.grid);
                input.modeY = FlexibleGridMath.ResolveY(input.placement.modeY, input.cell, context.grid);
                input.insideSafeArea = input.placement.insideSafeArea;
                input.fit = input.placement.fit || FlexibleGridPlacementService.IsContentSized(target);
                return input;
            }
            input.fit = FlexibleGridPlacementService.IsContentSized(target);

            input.modeX = FlexibleAxisMode.Stretch;
            input.modeY = FlexibleAxisMode.Stretch;
            input.insideSafeArea = insideSafeArea;
            input.cell = new FlexibleCell(0, 0, 1, 1);
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = context.root.InverseTransformPoint(corners[0]);
            Vector2 max = context.root.InverseTransformPoint(corners[2]);
            Rect area = context.GetArea(input.insideSafeArea);
            // 칸 경계에 딱 붙은 가장자리가 이웃 칸으로 잡히지 않게 조금 안쪽을 본다
            if (FlexibleGridMath.TryGetCell(area, context.grid, min + Vector2.one, out Vector2Int a)
                && FlexibleGridMath.TryGetCell(area, context.grid, max - Vector2.one, out Vector2Int b))
                input.cell = FlexibleCell.FromCorners(a, b);
            return input;
        }

        // 입력값대로 다시 놓는다. 손으로 옮긴 보정은 새 칸·모드 위에 그대로 얹는다
        private static void Place(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input, FlexibleSpecService.BaseSizes sizes = null)
        {
            FlexibleGridAdjustment adjustment = default;
            if (input.placement != null)
                FlexibleGridPlacementService.TryGetAdjustment(input.placement, out adjustment);
            else
            {
                // 새로 놓을 때(드래그)는 손으로 바꾼 스케일도 원래 값으로 돌린다. 그래야 놓은 칸과 같은 크기로 보인다
                adjustment.overrideScale = true;
                adjustment.scale = FlexibleGridPlacementService.GetBaseScale(session, input.target);
            }
            FlexibleCell cell = input.cell.Clamped(context.grid);
            // 칸에 맞춰 줄이는 블록은 세로 화면 크기·모양을 지킨 채 줄인다 (칸 크기를 쓰면 내용물보다 좁아질 수 있다)
            if ((input.ownSize || input.fit) && sizes != null)
            {
                // 칸에 맞춰 줄이는 블록은 늘어나는 축도 크기를 지킨다 (가운데로)
                FlexibleAxisMode sizeX = input.fit && FlexibleGridMath.ResolveX(input.modeX, cell, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : input.modeX;
                FlexibleAxisMode sizeY = input.fit && FlexibleGridMath.ResolveY(input.modeY, cell, context.grid) == FlexibleAxisMode.Stretch ? FlexibleAxisMode.Center : input.modeY;
                adjustment.size = FlexibleSpecService.KeepSizeOffset(context, input.target, cell, sizeX, sizeY, input.insideSafeArea,
                    sizes.Get(FlexiblePathUtility.GetPath(context.root, input.target)));
            }
            FlexibleGridPlacementService.Apply(context, session, input.target, cell, input.modeX, input.modeY, input.insideSafeArea, adjustment, input.fit);

            // 화면을 다 덮는 배경이 붙은 블록을 줄이면 배경도 같이 줄어든다는 것을 알린다
            Vector2? baseSize = input.fit && sizes != null ? sizes.Get(FlexiblePathUtility.GetPath(context.root, input.target)) : null;
            if (baseSize.HasValue && FlexibleGridPlacementService.IsScreenBackdrop(input.target, baseSize.Value, sizes.Screen))
            {
                fillNoteTarget = input.target;
                fillNote = F("\"{0}\" has its own background covering the whole portrait screen (a dim or page background). Fitting shrinks that background too, so the screen edges are left uncovered. Keep this block as it is and fit the parts inside it instead.", input.target.name);
            }
        }

        // 고른 블록 전부에 한 번에 적용하고 Undo 한 번으로 묶는다
        private static void PlaceAll(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs, System.Func<BlockInput, BlockInput> change)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            FlexibleSpecService.BaseSizes sizes = null;
            try
            {
                foreach (BlockInput input in inputs)
                {
                    BlockInput changed = change(input);
                    if ((changed.ownSize || changed.fit) && sizes == null)
                        sizes = OpenBaseSizes(session, context.root);
                    Place(context, session, changed, sizes);
                }
            }
            finally
            {
                sizes?.Dispose();
            }
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        // ───────── 칸과 보이는 크기가 다를 때 ─────────

        // 「칸 채우기」를 누른 뒤 안쪽에서 삐져나온 것 (그 블록을 고르고 있는 동안 보인다)
        private static RectTransform fillNoteTarget;
        private static string fillNote;

        // 내용에 맞춰 크기가 정해지는 블록은 칸 모양과 다르면 칸보다 작게 보인다. 이때만 알린다
        private static bool TryGetSizeMismatch(FlexibleGridContext context, FlexibleGridPlacement placement, out Rect cells, out Rect shown)
        {
            cells = shown = default;
            if (placement == null || placement.target == null || placement.sizeKept || !FlexibleGridPlacementService.IsContentSized(placement.target))
                return false;
            cells = FlexibleGridMath.GetSlotRect(context.GetArea(placement.insideSafeArea), context.grid, placement.cell.Clamped(context.grid));
            shown = ShownRect(context.root, placement.target);
            return Mathf.Abs(shown.width - cells.width) > 2f || Mathf.Abs(shown.height - cells.height) > 2f;
        }

        // 화면(root) 기준으로 실제로 보이는 범위 (스케일 포함)
        private static Rect ShownRect(RectTransform root, RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = root.InverseTransformPoint(corners[0]);
            Vector2 max = root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void DrawSizeMismatch(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input)
        {
            if (fillNoteTarget != null && fillNoteTarget == input.target && !string.IsNullOrEmpty(fillNote))
                EditorGUILayout.HelpBox(fillNote, MessageType.Info);
            if (!TryGetSizeMismatch(context, input.placement, out Rect cells, out Rect shown))
                return;

            bool onBase = FlexibleScreenSession.IsBaseView(PrefabStageUtility.GetCurrentPrefabStage());
            EditorGUILayout.HelpBox(F("\"{0}\" looks {1:0} x {2:0}, but the cells you placed it on are {3:0} x {4:0}. Its size follows its contents (Content Size Fitter), so it keeps its shape and is scaled down to fit. Choose what to do:",
                input.target.name, shown.width, shown.height, cells.width, cells.height), MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                string fillTip = onBase
                    ? T("Turns off its Content Size Fitter so it takes the size of the cells. This is the base screen, so it changes every screen that has no layout of its own. Contents that do not shrink may stick out.")
                    : T("Turns off its Content Size Fitter on this screen only, so it takes the size of the cells. Contents that do not shrink may stick out.");
                if (GUILayout.Button(new GUIContent(T("Fill the cells"), fillTip), GUILayout.Height(24f)))
                {
                    FillCells(context, session, input);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent(T("Keep it as it is"), T("Keeps its shape and size. You will not be asked again for this object on this screen.")), GUILayout.Height(24f)))
                {
                    Undo.RecordObject(session, "Flexible Keep Size");
                    input.placement.sizeKept = true;
                    EditorUtility.SetDirty(session);
                }
            }
        }

        // 이 화면에서 Content Size Fitter를 끄고 칸 크기 그대로 다시 놓는다
        private static void FillCells(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input)
        {
            RectTransform block = input.target;
            var fitter = block.GetComponent<ContentSizeFitter>();
            // 스크롤 영역이 있으면 피터는 그대로 두고 안쪽을 맞춘다 (제목·버튼은 제 크기를 지키고 스크롤 영역만 늘고 준다)
            ScrollRect scroll = fitter.horizontalFit == ContentSizeFitter.FitMode.Unconstrained
                ? block.GetComponentsInChildren<ScrollRect>(false).FirstOrDefault(s => !IsClipped(s.transform, block)) : null;
            if (scroll != null)
            {
                FillByScroll(context, session, input, (RectTransform)scroll.transform);
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(fitter, "Flexible Fill Cells");
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            BlockInput fill = input;
            fill.placement = null;
            fill.fit = false;
            fill.ownSize = false;
            fill.modeX = FlexibleAxisMode.Stretch;
            fill.modeY = FlexibleAxisMode.Stretch;
            PlaceAll(context, session, new List<BlockInput> { fill }, i => i);
            Undo.CollapseUndoOperations(group);

            List<string> outside = FindStickingOut(input.target);
            fillNoteTarget = input.target;
            fillNote = outside.Count == 0
                ? F("\"{0}\" now fills the cells.", input.target.name)
                : F("\"{0}\" now fills the cells, but these parts inside do not shrink and stick out: {1}. To make them follow, change their layout in the prefab (for example give a scroll area a Layout Element with Flexible Height).",
                    input.target.name, string.Join(", ", outside.Take(3)) + (outside.Count > 3 ? ", …" : ""));
        }

        // 칸 모양으로 놓고 안쪽을 맞춘다. 바꾸는 것은 RectTransform뿐이라 이 화면의 기록으로만 남는다.
        // · 폭: 넘치는 안쪽은 세로 화면에서의 폭 비율로 좁히거나(그림·글자·스크롤) 줄인다(그 밖의 묶음). 디자인의 좌우 여백이 남는다
        // · 높이: 스크롤 영역이 받는다. 다른 부분의 최소 높이 때문에 더 못 줄이면 블록 전체를 조금 줄이고 그만큼 폭을 넓혀 칸을 채운다
        private static void FillByScroll(FlexibleGridContext context, FlexibleWorkspace session, BlockInput input, RectTransform scroll)
        {
            RectTransform block = input.target;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(block.GetComponentsInChildren<RectTransform>(true).Cast<Object>().ToArray(), "Flexible Fill Cells");

            // 다시 눌러도 같은 결과가 나오게, 안쪽은 원래 디자인(저장된 기준 배치)에서 새로 계산한다
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            foreach (RectTransform inside in block.GetComponentsInChildren<RectTransform>(true))
            {
                RectTransform saved = inside != block && stage != null ? FlexibleScreenSession.FindSaved(stage, inside) : null;
                if (saved != null)
                    FlexibleRectState.Capture(saved).ApplyTo(inside);
            }

            BlockInput fill = input;
            fill.placement = null;
            fill.fit = false;
            fill.ownSize = false;
            fill.modeX = FlexibleAxisMode.Center;
            fill.modeY = FlexibleAxisMode.Center;
            PlaceAll(context, session, new List<BlockInput> { fill }, i => i);

            FlexibleGridPlacement placement = session.FindPlacement(block);
            Rect cells = FlexibleGridMath.GetSlotRect(context.GetArea(placement.insideSafeArea), context.grid, placement.cell.Clamped(context.grid));
            // 높이를 직접 정하는 곳: 스크롤 영역이 부모에 늘어나 붙어 있거나 레이아웃이 높이를 정하면 그 위로 올라간다
            RectTransform elastic = scroll;
            while (elastic != block && elastic.parent is RectTransform up
                && (!Mathf.Approximately(elastic.anchorMin.y, elastic.anchorMax.y)
                    || (up.GetComponent<HorizontalOrVerticalLayoutGroup>() is HorizontalOrVerticalLayoutGroup upGroup && upGroup.childControlHeight)))
                elastic = up;

            Vector3 baseScale = block.localScale;
            float factor = 1f;
            var touched = new HashSet<RectTransform>();
            FlexibleSpecService.BaseSizes sizes = OpenBaseSizes(session, context.root);
            try
            {
                for (int round = 0; round < 8; round++)
                {
                    block.localScale = new Vector3(baseScale.x * factor, baseScale.y * factor, baseScale.z);
                    block.sizeDelta = new Vector2(cells.width / Mathf.Abs(baseScale.x * factor), block.sizeDelta.y);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(block);
                    NarrowInside(context.root, block, block, block.rect.width, touched, sizes);
                    if (elastic != block)
                        FitHeight(block, elastic, cells.height / Mathf.Abs(baseScale.y * factor));
                    LayoutRebuilder.ForceRebuildLayoutImmediate(block);

                    float shown = block.rect.height * Mathf.Abs(baseScale.y * factor);
                    float next = Mathf.Min(1f, factor * cells.height / Mathf.Max(1f, shown));
                    if (Mathf.Abs(next - factor) < 0.0005f)
                        break;
                    factor = next;
                }
            }
            finally
            {
                sizes?.Dispose();
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(block);
            Undo.CollapseUndoOperations(group);

            List<string> outside = FindStickingOut(block);
            bool tooTall = block.rect.height * Mathf.Abs(block.localScale.y) > cells.height + 2f;
            fillNoteTarget = block;
            if (tooTall)
                fillNote = F("\"{0}\" is still taller than the cells: even with \"{1}\" at its smallest, the other parts need more room.", block.name, scroll.name);
            else if (outside.Count > 0)
                fillNote = F("\"{0}\" now fills the cells (\"{1}\" became shorter or longer), but these parts still stick out: {2}.", block.name, scroll.name, string.Join(", ", outside.Take(3)) + (outside.Count > 3 ? ", …" : ""));
            else
                fillNote = F("\"{0}\" now fills the cells: \"{1}\" became shorter or longer, and wide parts inside were narrowed. Only this screen changes.", block.name, scroll.name);
            if (!tooTall && factor < 0.995f)
                fillNote += " " + F("The other parts need more height than the cells, so it is also shown at {0:0%} size.", factor);
        }

        // 높이를 정하는 곳(elastic)을 블록이 목표 높이를 넘지 않는 가장 큰 값으로 맞춘다.
        // 다른 부분의 최소 높이 때문에 목표까지 못 줄이면, 블록 높이가 더 늘지 않는 가장 큰 값 (남는 빈 공간을 스크롤 영역이 쓴다)
        private static void FitHeight(RectTransform block, RectTransform elastic, float target)
        {
            const float minHeight = 80f;
            float Measure(float value)
            {
                elastic.sizeDelta = new Vector2(elastic.sizeDelta.x, value);
                LayoutRebuilder.ForceRebuildLayoutImmediate(block);
                return block.rect.height;
            }

            float low = minHeight;
            float goal = Mathf.Max(target, Measure(low)) + 0.5f;
            float high = Mathf.Max(low, target);
            if (Measure(high) <= goal)
                return;
            for (int i = 0; i < 24; i++)
            {
                float mid = (low + high) * 0.5f;
                if (Measure(mid) <= goal)
                    low = mid;
                else
                    high = mid;
            }
            Measure(low);
        }

        // 폭이 들어갈 자리보다 넓은 안쪽을 세로 화면에서의 폭 비율로 맞춘다 (한 번 맞춘 것은 자리가 바뀌면 다시 맞춘다).
        // 레이아웃이 폭을 정하거나 부모에 늘어나 붙은 것은 부모를 따라가므로 그 안만 본다
        private static void NarrowInside(RectTransform root, RectTransform block, RectTransform node, float room, HashSet<RectTransform> touched, FlexibleSpecService.BaseSizes sizes)
        {
            if (node != block && (node.GetComponent<Mask>() != null || node.GetComponent<RectMask2D>() != null))
                return;
            var group = node.GetComponent<HorizontalOrVerticalLayoutGroup>();
            float padding = group != null ? group.padding.horizontal : 0;
            float inner = Mathf.Min(node.rect.width, room) - padding;
            Vector2? baseNode = sizes?.Get(FlexiblePathUtility.GetPath(root, node));
            foreach (Transform t in node)
            {
                if (!(t is RectTransform child) || !child.gameObject.activeSelf)
                    continue;
                var graphic = child.GetComponent<Graphic>();
                if (graphic != null && graphic.GetType().Name.StartsWith("TMP_SubMesh"))
                    continue;
                var element = child.GetComponent<LayoutElement>();
                bool driven = group != null && group.childControlWidth && (element == null || !element.ignoreLayout);
                bool stretched = !Mathf.Approximately(child.anchorMin.x, child.anchorMax.x);
                if (driven || stretched)
                {
                    NarrowInside(root, block, child, inner, touched, sizes);
                    continue;
                }

                float scale = Mathf.Max(0.0001f, Mathf.Abs(child.localScale.x));
                float width = child.rect.width * scale;
                if (inner > 1f && (width > inner + 1f || touched.Contains(child)))
                {
                    // 세로 화면에서 부모와의 좌우 여백을 그대로 둔다 (테두리 그림은 크기가 변하지 않으므로 여백도 같은 값이어야 한다).
                    // 여백을 지키면 너무 좁아질 때만 세로 화면의 폭 비율로 (모르면 꽉 채운다)
                    float want = inner;
                    Vector2? baseChild = sizes?.Get(FlexiblePathUtility.GetPath(root, child));
                    if (baseChild.HasValue && baseNode.HasValue && baseNode.Value.x - padding > 1f)
                    {
                        float gap = Mathf.Max(0f, baseNode.Value.x - padding - baseChild.Value.x);
                        float ratio = Mathf.Clamp(baseChild.Value.x / (baseNode.Value.x - padding), 0.1f, 1f);
                        want = inner - gap >= inner * ratio * 0.6f ? inner - gap : inner * ratio;
                    }
                    if (Mathf.Abs(want - width) > 0.5f)
                    {
                        if (graphic != null || child.GetComponent<ScrollRect>() != null)
                        {
                            // 글자는 좁아지면 줄이 늘어난다. 레이아웃이 높이를 정하지 않으면 늘어난 만큼 영역도 키워
                            // 아래 것과의 간격을 원래 디자인과 같게 둔다 (영역 밖으로 넘치는 정도가 그대로)
                            bool heightFree = !(group != null && group.childControlHeight) && Mathf.Approximately(child.anchorMin.y, child.anchorMax.y);
                            float baseWidth = baseChild.HasValue ? baseChild.Value.x : child.sizeDelta.x;
                            float baseHeight = baseChild.HasValue ? baseChild.Value.y : child.sizeDelta.y;
                            float lines = heightFree ? PreferredHeightAt(child, want / scale) - PreferredHeightAt(child, baseWidth) : 0f;
                            child.sizeDelta = new Vector2(want / scale, Mathf.Abs(lines) > 0.5f ? baseHeight + lines : child.sizeDelta.y);
                        }
                        else
                        {
                            float s = scale * want / width;
                            child.localScale = new Vector3(s, s, child.localScale.z);
                        }
                    }
                    touched.Add(child);
                }
                NarrowInside(root, block, child, child.rect.width, touched, sizes);
            }
        }

        // 이 폭일 때 내용(글자 등)이 원하는 높이. 폭을 잠시 바꿔 재고 되돌린다
        private static float PreferredHeightAt(RectTransform target, float width)
        {
            Vector2 size = target.sizeDelta;
            target.sizeDelta = new Vector2(width, size.y);
            foreach (ILayoutElement element in target.GetComponents<ILayoutElement>())
            {
                element.CalculateLayoutInputHorizontal();
                element.CalculateLayoutInputVertical();
            }
            float height = LayoutUtility.GetPreferredHeight(target);
            target.sizeDelta = size;
            return height;
        }

        // 블록 안에서 마스크에 잘리는 자리인지
        private static bool IsClipped(Transform t, Transform block)
        {
            for (Transform p = t.parent; p != null && p != block; p = p.parent)
            {
                if (p.GetComponent<Mask>() != null || p.GetComponent<RectMask2D>() != null)
                    return true;
            }
            return false;
        }

        // 블록 밖으로 나간 안쪽 그림·버튼 (마스크로 잘리는 것, 이미 적은 것의 안쪽은 뺀다)
        private static List<string> FindStickingOut(RectTransform block)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(block);
            Rect bounds = block.rect;
            float margin = Mathf.Max(2f, Mathf.Min(bounds.width, bounds.height) * 0.02f);
            var listed = new List<Transform>();
            var corners = new Vector3[4];
            foreach (RectTransform child in block.GetComponentsInChildren<RectTransform>(false))
            {
                if (child == block || child.GetComponent<Graphic>() == null || child.GetComponent<Graphic>().GetType().Name.StartsWith("TMP_SubMesh"))
                    continue;
                bool skip = false;
                for (Transform t = child.parent; t != null && t != block; t = t.parent)
                {
                    if (listed.Contains(t) || t.GetComponent<Mask>() != null || t.GetComponent<RectMask2D>() != null)
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip)
                    continue;
                child.GetWorldCorners(corners);
                Vector2 min = block.InverseTransformPoint(corners[0]);
                Vector2 max = block.InverseTransformPoint(corners[2]);
                if (min.x < bounds.xMin - margin || min.y < bounds.yMin - margin || max.x > bounds.xMax + margin || max.y > bounds.yMax + margin)
                    listed.Add(child);
            }
            return listed.Select(t => t.name).ToList();
        }

        // 저장된 프리팹(= 세로 화면)에서 블록 크기·위치를 읽는다
        private static FlexibleSpecService.BaseSizes OpenBaseSizes(FlexibleWorkspace session, RectTransform root)
        {
            string path = AssetDatabase.GUIDToAssetPath(session.sourceGuid);
            return new FlexibleSpecService.BaseSizes(path, FlexibleReservedAreas.SettingsOf(root.gameObject));
        }

        // 안전 영역의 모서리·가장자리·가운데에 세로 화면과 같은 거리·크기로 붙인다
        private static void PinAll(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs, string pin)
        {
            FlexibleSpecService.TryParsePin(pin, out FlexibleAxisMode modeX, out FlexibleAxisMode modeY);
            var whole = new FlexibleCell(0, 0, context.grid.columns, context.grid.rows);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            using (FlexibleSpecService.BaseSizes sizes = OpenBaseSizes(session, context.root))
            {
                foreach (BlockInput input in inputs)
                {
                    Rect? rect = sizes.GetRect(FlexiblePathUtility.GetPath(context.root, input.target));
                    if (!rect.HasValue)
                        continue;
                    var adjustment = new FlexibleGridAdjustment
                    {
                        size = FlexibleSpecService.KeepSizeOffset(context, input.target, whole, modeX, modeY, input.insideSafeArea, rect.Value.size),
                        position = FlexibleSpecService.PinOffset(modeX, modeY, rect.Value, sizes.Screen, context.grid.margin),
                    };
                    FlexibleGridPlacementService.Apply(context, session, input.target, whole, modeX, modeY, input.insideSafeArea, adjustment, input.fit);
                }
            }
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        // ───────── 여러 개를 한 덩어리로 (가상 그룹) ─────────
        // 흩어진 팝업 조각(딤·틀·제목·목록·버튼)을 부모 없이 함께 옮긴다. 세로 화면에서의 서로 위치·크기 비율을 지킨 채
        // 묶음 전체를 목표 자리에 맞춰 놓고, 결과는 각 오브젝트의 이 화면 배치로 기록된다 (계층은 바꾸지 않는다)
        private static bool moveTogether = true;

        private static bool IsGroup(List<RectTransform> targets) => moveTogether && targets.Count > 1;

        // 묶음을 마지막으로 놓은 칸 (묶음은 칸 안에서 줄어들어 실제 영역만으로는 입력한 칸을 알 수 없다)
        private static string groupCellKey;
        private static FlexibleCell groupCell;

        private static string GroupKey(FlexibleWorkspace session, List<RectTransform> targets)
        {
            return session.profileId + "|" + string.Join(",", targets.Select(t => t.GetInstanceID()).OrderBy(id => id));
        }

        private static void RememberGroupCell(FlexibleWorkspace session, List<RectTransform> targets, FlexibleCell cell)
        {
            groupCellKey = GroupKey(session, targets);
            groupCell = cell;
        }

        private FlexibleCell GetGroupCell(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets)
        {
            if (groupCellKey == GroupKey(session, targets))
                return groupCell.Clamped(context.grid);
            Rect union = UnionInRoot(context.root, targets);
            Rect area = context.GetArea(insideSafeArea);
            if (FlexibleGridMath.TryGetCell(area, context.grid, union.min + Vector2.one, out Vector2Int a)
                && FlexibleGridMath.TryGetCell(area, context.grid, union.max - Vector2.one, out Vector2Int b))
                return FlexibleCell.FromCorners(a, b);
            return new FlexibleCell(0, 0, context.grid.columns, context.grid.rows);
        }

        // 묶음도 칸 번호로 놓을 수 있다
        private void DrawGroupCells(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets)
        {
            FlexibleCell cell = GetGroupCell(context, session, targets);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Column"), T("Row"), T("Width (cells)"), T("Height (cells)"));
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                cell.column = EditorGUILayout.DelayedIntField(T("Column"), cell.column);
                cell.row = EditorGUILayout.DelayedIntField(T("Row"), cell.row);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                cell.width = EditorGUILayout.DelayedIntField(T("Width (cells)"), cell.width);
                cell.height = EditorGUILayout.DelayedIntField(T("Height (cells)"), cell.height);
            }
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUIUtility.labelWidth = labelWidth;
            if (!changed)
                return;
            cell = cell.Clamped(context.grid);
            RememberGroupCell(session, targets, cell);
            PlaceGroupInCells(context, session, targets, FlexibleGridMath.GetSlotRect(context.GetArea(insideSafeArea), context.grid, cell));
        }

        // 칸 영역(root 좌표) 안에 묶음을 모양 그대로 가장 크게 (세로 화면 크기보다 크게는 하지 않는다)
        private static void PlaceGroupInCells(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets, Rect cells)
        {
            PlaceGroup(context, session, targets, (union, screen) =>
            {
                float s = Mathf.Min(1f, cells.width / Mathf.Max(1f, union.width), cells.height / Mathf.Max(1f, union.height));
                Vector2 size = union.size * s;
                return new Rect(cells.center - size * 0.5f, size);
            });
        }

        // 안전 영역의 그 모서리·가장자리에서 세로 화면과 같은 거리로 묶음을 붙인다 (안 들어가면 줄인다)
        private static void PinGroup(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets, string pin)
        {
            FlexibleSpecService.TryParsePin(pin, out FlexibleAxisMode modeX, out FlexibleAxisMode modeY);
            groupCellKey = null;
            Rect area = context.GetArea(true);
            PlaceGroup(context, session, targets, (union, screen) =>
            {
                float s = Mathf.Min(1f, area.width / Mathf.Max(1f, union.width), area.height / Mathf.Max(1f, union.height));
                Vector2 size = union.size * s;
                float x = modeX == FlexibleAxisMode.Start ? area.xMin + union.xMin
                    : modeX == FlexibleAxisMode.End ? area.xMax - (screen.x - union.xMax) - size.x
                    : area.center.x + (union.center.x - screen.x * 0.5f) - size.x * 0.5f;
                float y = modeY == FlexibleAxisMode.Start ? area.yMin + union.yMin
                    : modeY == FlexibleAxisMode.End ? area.yMax - (screen.y - union.yMax) - size.y
                    : area.center.y + (union.center.y - screen.y * 0.5f) - size.y * 0.5f;
                x = Mathf.Clamp(x, area.xMin, Mathf.Max(area.xMin, area.xMax - size.x));
                y = Mathf.Clamp(y, area.yMin, Mathf.Max(area.yMin, area.yMax - size.y));
                return new Rect(x, y, size.x, size.y);
            });
        }

        // goal(세로 화면에서의 묶음 영역, 세로 화면 크기) → 이 화면에서 묶음이 놓일 영역(root 좌표)
        private static void PlaceGroup(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets, System.Func<Rect, Vector2, Rect> goal)
        {
            RectTransform root = context.root;
            using (FlexibleSpecService.BaseSizes sizes = OpenBaseSizes(session, root))
            {
                var members = new List<(RectTransform target, string path, Rect rect)>();
                foreach (RectTransform target in targets)
                {
                    string path = FlexiblePathUtility.GetPath(root, target);
                    Rect? rect = sizes.GetRect(path);
                    if (rect.HasValue && rect.Value.width > 0f && rect.Value.height > 0f)
                        members.Add((target, path, rect.Value));
                }
                if (members.Count == 0)
                    return;
                // 버튼 아래 글자처럼 밖으로 나가 보이는 자식도 묶음 크기에 넣는다 (칸 밖으로 삐져나오지 않게)
                Rect union = sizes.GetVisibleRect(members[0].path) ?? members[0].rect;
                foreach (var m in members)
                {
                    Rect visible = sizes.GetVisibleRect(m.path) ?? m.rect;
                    union = Rect.MinMaxRect(Mathf.Min(union.xMin, visible.xMin), Mathf.Min(union.yMin, visible.yMin), Mathf.Max(union.xMax, visible.xMax), Mathf.Max(union.yMax, visible.yMax));
                }
                Rect where = goal(union, sizes.Screen);
                // 화면 크기가 아직 정해지지 않았으면(칸이 0 이하) 놓지 않는다 (배율이 뒤집힌다)
                if (where.width <= 0f || where.height <= 0f)
                    return;
                float s = where.width / Mathf.Max(1f, union.width);

                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.RecordObject(session, "Place together");
                foreach (var m in members)
                {
                    Undo.RecordObject(m.target, "Place together");
                    // 칸 배치 기록은 지운다 (묶음 안 자리는 칸이 아니라 서로의 위치로 정해진다)
                    session.placements.RemoveAll(p => p.target == m.target);
                    Vector3 baseScale = FlexibleGridPlacementService.GetBaseScale(session, m.target);
                    m.target.localScale = new Vector3(baseScale.x * s, baseScale.y * s, baseScale.z);
                    // 늘어나는 앵커라 이 화면에서 크기가 달라졌으면 세로 화면 크기로 (모양을 지키려고)
                    Vector2? baseSize = sizes.Get(m.path);
                    if (baseSize.HasValue)
                    {
                        m.target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, baseSize.Value.x);
                        m.target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, baseSize.Value.y);
                    }
                    Vector2 want = where.min + (m.rect.center - union.min) * s;
                    Rect now = FlexiblePreviewRenderer.GetRectInRoot(root, m.target);
                    m.target.position += root.TransformVector(want - now.center);
                }
                Undo.CollapseUndoOperations(group);
            }
            SceneView.RepaintAll();
        }

        // 고른 것들이 지금 차지하는 영역 (root 좌표)
        private static Rect UnionInRoot(RectTransform root, List<RectTransform> targets)
        {
            Rect union = FlexiblePreviewRenderer.GetRectInRoot(root, targets[0]);
            foreach (RectTransform t in targets)
            {
                Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, t);
                union = Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin), Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax));
            }
            return union;
        }

        private static readonly string[] Pins = { "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right" };
        private static readonly string[] PinLabels = { "\u2196", "\u2191", "\u2197", "\u2190", "\u00b7", "\u2192", "\u2199", "\u2193", "\u2198" };

        // 고른 블록들의 값이 모두 같으면 그 값, 다르면 「—」로 보여 주는 숫자 칸. 바꾼 값을 돌려준다
        private static bool MixedIntField(string label, List<BlockInput> inputs, System.Func<BlockInput, int> get, out int value)
        {
            int first = get(inputs[0]);
            EditorGUI.showMixedValue = inputs.Exists(i => get(i) != first);
            EditorGUI.BeginChangeCheck();
            value = EditorGUILayout.DelayedIntField(label, first);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            return changed;
        }

        private static bool MixedModeField(string label, string[] labels, List<BlockInput> inputs, System.Func<BlockInput, FlexibleAxisMode> get, out FlexibleAxisMode value)
        {
            FlexibleAxisMode first = get(inputs[0]);
            EditorGUI.showMixedValue = inputs.Exists(i => get(i) != first);
            EditorGUI.BeginChangeCheck();
            // 선택지는 Stretch부터 (Auto는 고를 수 없다)
            value = (FlexibleAxisMode)(EditorGUILayout.Popup(label, (int)first - 1, labels) + 1);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            return changed;
        }

        public void Draw()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                DrawNoPrefab();
                return;
            }

            DrawBackToOuter(stage);

            if (FlexibleScreenSession.IsLegacyWorkspace(stage))
            {
                DrawLegacyWorkspace(stage);
                return;
            }

            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
            {
                DrawChecklist(stage);
                return;
            }

            DrawScreens(stage, layout);
            DrawNotice(stage);
            DrawOutside(stage);
            DrawProblem(stage, layout);
            DrawPendingLegacy(stage);
            // 탭 순서: 보기 → 선택한 오브젝트 → 그리드 → AI → 템플릿 → 기타
            DrawView(stage);
            DrawSelected(stage, layout);
            DrawGrid(stage, layout);
            FlexibleAiPanel.DrawLauncher(stage, layout);
            DrawTemplates(stage, layout);
            DrawMore(stage, layout);
        }

        // ───────── 검수 「문제 보기」로 온 문제 ─────────

        // 검수에서 「문제 보기」를 누른 문제 (그 프리팹·화면을 열고 있는 동안만 보인다). 고치는 건 평소 도구로 한다
        private static string problemPath;
        private static string problemProfile;
        private static string problemScreen;
        private static List<FlexibleIssue> problemIssues = new List<FlexibleIssue>();

        public static void SetProblem(string path, string profileId, string screenName, List<FlexibleIssue> issues)
        {
            problemPath = path;
            problemProfile = profileId;
            problemScreen = screenName;
            problemIssues = new List<FlexibleIssue>(issues);
        }

        // 묻지 않고 알리기만 하는 결과 (확인창은 Windows에서 늘 ⚠ 아이콘이라 실패처럼 보인다). 그 프리팹을 연 동안만 보인다
        private static string notice;
        private static string noticePath;
        private static MessageType noticeType;

        private static void ShowNotice(PrefabStage stage, string message, MessageType type = MessageType.Info)
        {
            notice = message;
            noticePath = stage != null ? stage.assetPath : null;
            noticeType = type;
        }

        private static void DrawNotice(PrefabStage stage)
        {
            if (string.IsNullOrEmpty(notice) || noticePath != stage.assetPath)
                return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(notice, noticeType);
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    notice = null;
            }
        }

        // 다시 검수하거나 그 프리팹을 닫으면 지난 문제는 지운다
        public static void ClearProblem()
        {
            problemPath = null;
            problemIssues = new List<FlexibleIssue>();
        }

        [InitializeOnLoadMethod]
        private static void ForgetProblemOnClose()
        {
            PrefabStage.prefabStageClosing += stage =>
            {
                if (stage != null && stage.assetPath == problemPath)
                    ClearProblem();
                if (stage != null && stage.assetPath == noticePath)
                    notice = null;
            };
        }

        private static void DrawProblem(PrefabStage stage, FlexibleLayout layout)
        {
            if (problemPath != stage.assetPath || problemIssues.Count == 0 || FlexibleScreenSession.GetActiveProfileId(stage) != problemProfile)
                return;

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(F("Problem on {0}", problemScreen), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(T("Select them"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        Transform root = stage.prefabContentsRoot.transform;
                        Object[] found = problemIssues.Where(i => i.blocks != null).SelectMany(i => i.blocks).Distinct()
                            .Select(b => FlexiblePathUtility.FindByPath(root, b)).Where(t => t != null).Select(t => (Object)t.gameObject).ToArray();
                        if (found.Length > 0)
                            Selection.objects = found;
                    }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                        ClearProblem();
                }
                const int MaxLines = 5;
                List<string> messages = problemIssues.Select(i => i.Message).Distinct().ToList();
                foreach (string message in messages.Take(MaxLines))
                    GUILayout.Label("· " + message, FlexibleGui.WrapLabel);
                if (messages.Count > MaxLines)
                    GUILayout.Label(F("... and {0} more", messages.Count - MaxLines), EditorStyles.miniLabel);
                if (problemProfile == layout.Settings.BaseProfileId)
                    GUILayout.Label(T("This is the base (portrait) screen: a fix here changes every portrait device."), EditorStyles.wordWrappedMiniLabel);
            }
        }

        // ───────── 보기 (편집하는 동안만) ─────────

        [SerializeField] private bool showView;
        // 사용자가 직접 펼치거나 접었는지 (그 전에는 숨긴 블록이 있을 때만 펼친다)
        [SerializeField] private bool viewToggled;
        // 펼친 줄 ("프리팹 경로|블록 경로")
        [SerializeField] private List<string> viewExpanded = new List<string>();
        private Vector2 viewScroll;
        private int viewRevealed;
        // Shift+클릭 범위의 시작 줄과 지금 그린 줄 목록 (하이라키와 같게)
        private Transform viewAnchor;
        private List<ViewRow> viewRows = new List<ViewRow>();
        private static GUIStyle viewRowLabel;
        private static GUIStyle viewRowChanged;

        private const float ViewRowHeight = 18f;
        private const float ViewIndent = 14f;
        private const int ViewMaxRows = 16;

        // 하이라키처럼 보이는 줄 하나. 안쪽 프리팹은 펼치지 않고 그 프리팹을 여는 버튼을 단다
        private struct ViewRow
        {
            public Transform target;
            public int depth;
            public bool hasChildren;
            public string nestedPath;
        }

        // 하이라키와 같은 구조로 편집하는 동안만 켜고 끈다 (저장되지 않음, 미리보기·검수에도 같이 적용)
        private void DrawView(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            var changedViews = session != null
                ? new HashSet<Transform>(session.viewOverrides.Where(v => v.target != null && v.target != root).Select(v => v.target.transform))
                : new HashSet<Transform>();
            int overridden = changedViews.Count;
            EditorGUILayout.Space(4f);
            bool anyHidden = root.transform.Cast<Transform>().Any(t => !FlexibleViewOverride.IsVisible(t.gameObject));
            bool open = viewToggled ? showView : anyHidden || overridden > 0;
            string title = overridden > 0 ? F("View while editing ({0} changed, not saved)", overridden) : T("View while editing");
            EditorGUI.BeginChangeCheck();
            open = EditorGUILayout.Foldout(open, new GUIContent(title, open ? null : T("Show or hide objects only while you edit, for example panels that cover the screen. Nothing here is saved, on any screen. The Preview and Check tabs use the same view.")), true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck())
            {
                showView = open;
                viewToggled = true;
            }
            FlexibleGui.Mark("view");
            if (!open)
                return;

            RevealSelection(stage, root.transform);
            var rows = new List<ViewRow>();
            CollectViewRows(stage, root.transform, root.transform, 0, rows);
            viewRows = rows;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool scroll = rows.Count > ViewMaxRows;
                if (scroll)
                    viewScroll = EditorGUILayout.BeginScrollView(viewScroll, GUILayout.Height(ViewMaxRows * ViewRowHeight + 4f));
                Rect area = GUILayoutUtility.GetRect(10f, rows.Count * ViewRowHeight, GUILayout.ExpandWidth(true));
                for (int i = 0; i < rows.Count; i++)
                {
                    var rect = new Rect(area.x, area.y + i * ViewRowHeight, area.width, ViewRowHeight);
                    if (DrawViewRow(stage, root.transform, rows[i], rect, changedViews))
                        GUIUtility.ExitGUI();
                }
                if (scroll)
                    EditorGUILayout.EndScrollView();

                using (new EditorGUI.DisabledScope(overridden == 0))
                {
                    if (GUILayout.Button(T("Back to the saved view")))
                        FlexibleScreenSession.ClearView(stage, true);
                }
            }
        }

        private string ViewKey(PrefabStage stage, Transform root, Transform t)
        {
            return stage.assetPath + "|" + FlexiblePathUtility.GetPath(root, t);
        }

        private void CollectViewRows(PrefabStage stage, Transform root, Transform parent, int depth, List<ViewRow> rows)
        {
            foreach (Transform child in parent)
            {
                bool nested = PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject);
                var row = new ViewRow
                {
                    target = child,
                    depth = depth,
                    hasChildren = !nested && child.childCount > 0,
                    nestedPath = nested ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) : null
                };
                rows.Add(row);
                if (row.hasChildren && viewExpanded.Contains(ViewKey(stage, root, child)))
                    CollectViewRows(stage, root, child, depth + 1, rows);
            }
        }

        // 줄 하나: 펼침 화살표 · 보임 체크 · 아이콘과 이름(누르면 선택) · 안쪽 프리팹이면 「열기」. 프리팹을 열면 true
        private bool DrawViewRow(PrefabStage stage, Transform root, ViewRow row, Rect rect, HashSet<Transform> changedViews)
        {
            GameObject go = row.target.gameObject;
            if (viewRowLabel == null)
            {
                viewRowLabel = new GUIStyle(EditorStyles.label) { padding = new RectOffset(2, 2, 0, 0) };
                viewRowChanged = new GUIStyle(viewRowLabel) { fontStyle = FontStyle.Bold };
            }
            if (Selection.gameObjects.Contains(go))
                EditorGUI.DrawRect(rect, new Color(FlexibleGui.Accent.r, FlexibleGui.Accent.g, FlexibleGui.Accent.b, 0.25f));

            float x = rect.x + row.depth * ViewIndent;
            string key = ViewKey(stage, root, row.target);
            if (row.hasChildren)
            {
                bool expanded = viewExpanded.Contains(key);
                bool now = EditorGUI.Foldout(new Rect(x, rect.y, ViewIndent, rect.height), expanded, GUIContent.none, true);
                if (now != expanded)
                {
                    if (now)
                        viewExpanded.Add(key);
                    else
                        viewExpanded.RemoveAll(k => k == key || k.StartsWith(key + "/"));
                }
            }
            x += ViewIndent;

            bool visible = FlexibleViewOverride.IsVisible(go);
            EditorGUI.BeginChangeCheck();
            bool on = EditorGUI.Toggle(new Rect(x, rect.y + 1f, 16f, rect.height), visible);
            if (EditorGUI.EndChangeCheck())
                ShowInView(stage, root, row.target, on);
            x += 18f;

            float buttonWidth = row.nestedPath != null ? 52f + 18f : 0f;
            var labelRect = new Rect(x, rect.y, Mathf.Max(10f, rect.xMax - x - buttonWidth - 2f), rect.height);
            bool shown = true;
            for (Transform t = row.target; t != null && t != root; t = t.parent)
                shown &= FlexibleViewOverride.IsVisible(t.gameObject);
            string name = go.name;
            if (row.hasChildren && !viewExpanded.Contains(key))
            {
                int inside = changedViews.Count(t => t != row.target && t.IsChildOf(row.target));
                if (inside > 0)
                    name += "  " + F("({0} changed inside)", inside);
            }
            var content = new GUIContent(name, AssetPreview.GetMiniThumbnail(go));
            Color old = GUI.contentColor;
            if (!shown)
                GUI.contentColor = new Color(old.r, old.g, old.b, 0.5f);
            GUI.Label(labelRect, content, changedViews.Contains(row.target) ? viewRowChanged : viewRowLabel);
            GUI.contentColor = old;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && labelRect.Contains(e.mousePosition))
            {
                if (e.shift && TryGetViewRange(row.target, out List<Object> range))
                {
                    // 하이라키처럼: Shift는 시작 줄부터 여기까지, Ctrl+Shift는 지금 선택에 더한다
                    Selection.objects = (e.control || e.command ? Selection.objects.Concat(range).Distinct() : range).ToArray();
                }
                else
                {
                    if (e.control || e.command)
                        Selection.objects = Selection.objects.Contains(go) ? Selection.objects.Where(o => o != go).ToArray() : Selection.objects.Append(go).ToArray();
                    else
                        Selection.activeGameObject = go;
                    viewAnchor = row.target;
                }
                viewRevealed = go.GetInstanceID();
                if (e.clickCount == 2 && row.nestedPath != null)
                {
                    e.Use();
                    OpenNestedPrefab(row.nestedPath, stage);
                    return true;
                }
                e.Use();
            }

            if (row.nestedPath != null)
            {
                var nestedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(row.nestedPath);
                if (nestedAsset != null && nestedAsset.GetComponent<FlexibleLayout>() != null)
                    GUI.Label(new Rect(rect.xMax - buttonWidth, rect.y, 16f, rect.height), new GUIContent($"<color=#{ColorUtility.ToHtmlStringRGB(FlexibleGui.Good)}>✔</color>", T("Already Flexible UI")), FlexibleGui.WrapLabel);
                var buttonRect = new Rect(rect.xMax - 52f, rect.y + 1f, 52f, rect.height - 2f);
                if (GUI.Button(buttonRect, new GUIContent(T("Open"), T("Open this prefab to show or hide what is inside it.")), EditorStyles.miniButton))
                {
                    OpenNestedPrefab(row.nestedPath, stage);
                    return true;
                }
            }
            return false;
        }

        // 시작 줄(마지막으로 누른 줄, 없으면 지금 선택한 오브젝트)부터 target 줄까지
        private bool TryGetViewRange(Transform target, out List<Object> range)
        {
            range = null;
            Transform anchor = viewAnchor != null && Selection.Contains(viewAnchor.gameObject) ? viewAnchor : Selection.activeTransform;
            int from = anchor != null ? viewRows.FindIndex(r => r.target == anchor) : -1;
            int to = viewRows.FindIndex(r => r.target == target);
            if (from < 0 || to < 0)
                return false;
            viewAnchor = anchor;
            range = new List<Object>();
            for (int i = Mathf.Min(from, to); i <= Mathf.Max(from, to); i++)
                range.Add(viewRows[i].target.gameObject);
            return true;
        }

        // 하이라키에서 고른 블록이 보이게 위쪽을 펼친다 (안쪽 프리팹 속이면 그 프리팹 줄까지)
        private void RevealSelection(PrefabStage stage, Transform root)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null || selected.GetInstanceID() == viewRevealed)
                return;
            viewRevealed = selected.GetInstanceID();
            Transform target = selected.transform;
            if (target == root || !target.IsChildOf(root))
                return;

            var chain = new List<Transform>();
            for (Transform t = target; t != null && t != root; t = t.parent)
                chain.Insert(0, t);
            // 안쪽 프리팹 속은 보이지 않으므로 바깥에서 가장 먼저 만나는 안쪽 프리팹에서 멈춘다
            int stop = chain.FindIndex(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
            if (stop >= 0)
                chain.RemoveRange(stop + 1, chain.Count - stop - 1);
            for (int i = 0; i < chain.Count - 1; i++)
            {
                string key = ViewKey(stage, root, chain[i]);
                if (!viewExpanded.Contains(key))
                    viewExpanded.Add(key);
            }

            // 펼친 뒤 그 줄이 스크롤 밖이면 보이는 곳으로
            var rows = new List<ViewRow>();
            CollectViewRows(stage, root, root, 0, rows);
            int index = rows.FindIndex(r => r.target == chain[chain.Count - 1]);
            if (index < 0 || rows.Count <= ViewMaxRows)
                return;
            float top = index * ViewRowHeight;
            float visibleHeight = ViewMaxRows * ViewRowHeight;
            if (top < viewScroll.y || top + ViewRowHeight > viewScroll.y + visibleHeight)
                viewScroll.y = Mathf.Max(0f, top - visibleHeight * 0.5f);
        }

        // 켤 때는 꺼진 위쪽 블록도 같이 켜야 보인다 (끌 때는 그것만)
        private static void ShowInView(PrefabStage stage, Transform root, Transform target, bool on)
        {
            if (on)
            {
                for (Transform t = target.parent; t != null && t != root; t = t.parent)
                {
                    if (!FlexibleViewOverride.IsVisible(t.gameObject))
                        FlexibleScreenSession.SetViewVisible(stage, t.gameObject, true);
                }
            }
            FlexibleScreenSession.SetViewVisible(stage, target.gameObject, on);
        }

        // ───────── 안쪽 프리팹으로 들어갔다가 돌아오기 ─────────

        private const string ReturnKeyPrefix = "FlexibleUITool.ReturnTo.";

        // 안쪽 프리팹을 열 때 바깥 프리팹을 기억해 두고, 안쪽에서 「돌아가기」로 바로 돌아오게 한다. 보던 화면(가로 등)으로 연다
        public static void OpenNestedPrefab(string path, PrefabStage from)
        {
            if (from != null && from.assetPath != path)
                SessionState.SetString(ReturnKeyPrefix + path, from.assetPath);
            OpenOnSameScreen(path, from);
        }

        private static void OpenOnSameScreen(string path, PrefabStage from)
        {
            if (!FlexibleScreenSession.PrepareToOpen(path))
                return;
            FlexibleScreenSession.OpenNextOn(path, FlexibleScreenSession.GetActiveProfileId(from));
            PrefabStageUtility.OpenPrefab(path);
            // 저장 확인에서 취소하는 등으로 열리지 않았으면 다음에 열 때 쓰지 않도록 지운다
            FlexibleScreenSession.OpenNextOn(path, null);
        }

        private static void DrawBackToOuter(PrefabStage stage)
        {
            string outer = SessionState.GetString(ReturnKeyPrefix + stage.assetPath, "");
            if (string.IsNullOrEmpty(outer) || AssetDatabase.LoadAssetAtPath<GameObject>(outer) == null)
                return;
            if (GUILayout.Button(F("← Back to \"{0}\"", Path.GetFileNameWithoutExtension(outer)), EditorStyles.miniButton))
            {
                OpenOnSameScreen(outer, stage);
                GUIUtility.ExitGUI();
            }
        }

        // ───────── 안쪽 프리팹 ─────────

        // 바깥 프리팹 안에 들어 있는 프리팹 하나 (같은 프리팹 여러 개는 하나로)
        public class NestedPrefab
        {
            public string path;
            public string name;
            public bool fullScreen;
            public bool flexible;
        }


        // 열린 프리팹 안의 프리팹 중 화면을 꽉 채우는 것(전체 화면 UI)과 이미 Flexible UI인 것. 버튼 같은 부품은 뺀다
        public static List<NestedPrefab> FindNestedPrefabs(PrefabStage stage)
        {
            var result = new List<NestedPrefab>();
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            Rect screen = root.rect;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) || !(t is RectTransform rt))
                    continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !(asset.transform is RectTransform) || result.Exists(n => n.path == path))
                    continue;

                Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, rt);
                bool full = screen.width > 0f && Mathf.Abs(r.width - screen.width) < screen.width * 0.02f && Mathf.Abs(r.height - screen.height) < screen.height * 0.02f;
                bool flexible = asset.GetComponent<FlexibleLayout>() != null;
                if (full || flexible)
                    result.Add(new NestedPrefab { path = path, name = Path.GetFileNameWithoutExtension(path), fullScreen = full, flexible = flexible });
            }
            return result;
        }

        // 프리팹 파일을 직접 열어 Flexible UI로 만든다 (컴포넌트·화면 설정, 크기 맞춤 컴포넌트가 있는 캔버스면 그것도 교체)
        public static bool MakeFlexible(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var layout = contents.GetComponent<FlexibleLayout>();
                if (layout != null && layout.Settings != null)
                    return false;
                if (layout == null)
                    layout = contents.AddComponent<FlexibleLayout>();
                layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                var canvas = contents.GetComponent<Canvas>();
                if (canvas != null && contents.GetComponent<CanvasScaler>() != null)
                    FlexibleLayoutInspector.UseFlexibleScaler(canvas, false);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                FlexibleSettingsUtility.RecordCanvasRule(path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void MakeNestedFlexible(PrefabStage stage, FlexibleLayout layout, List<string> paths)
        {
            // 기준 화면에서 저장된 상태로 바꾼다 (안쪽 프리팹 파일이 바뀌면 열린 프리팹 안의 인스턴스도 다시 불러와진다)
            string baseId = layout.Settings.BaseProfileId;
            if (FlexibleScreenSession.GetActiveProfileId(stage) != baseId)
                FlexibleScreenSession.ShowProfile(stage, baseId);
            else if (stage.scene.isDirty)
                FlexibleScreenSession.Save(stage);

            int made = 0;
            foreach (string path in paths)
            {
                if (MakeFlexible(path))
                    made++;
            }

            // 안쪽이 Flexible UI가 되면 바깥 프리팹이 관리하는 목록이 달라지므로, 다시 불러온 뒤 한 번 저장해 목록을 새로 만든다
            EditorApplication.delayCall += () =>
            {
                PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
                if (current != null && current.assetPath == stage.assetPath)
                    FlexibleScreenSession.Save(current);
            };
            ShowNotice(stage, F("Made {0} prefabs Flexible UI.", made));
        }

        // ───────── 프리팹이 열려 있지 않을 때 ─────────

        private static void DrawNoPrefab()
        {
            if (FlexibleTutorial.Active || FlexibleAiRun.Current == FlexibleAiRun.State.Running)
                return;

            // 실행 중인 씬은 변환 대상이 아니다 (프리팹 연결도 달라 보여 틀린 안내가 된다)
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                DrawSceneCanvasCheck();
            if (!string.IsNullOrEmpty(sceneSetupResult) && sceneSetupScene == EditorSceneManager.GetActiveScene().path)
                EditorGUILayout.HelpBox(sceneSetupResult, MessageType.Info);

            EditorGUILayout.Space(4f);
            GUILayout.Label(T("Open a UI prefab"), FlexibleGui.Title);
            List<string> paths = GetSelectedPrefabPaths();
            GUILayout.Label(EnsureSceneUiRows().Count > 0 || paths.Count > 0
                ? T("Open one from the list of this scene's UI prefabs below, or double-click a UI prefab in the Project window.")
                : T("This scene has no UI prefabs yet. Build the UI under a Canvas in the scene, or double-click a UI prefab in the Project window."), FlexibleGui.WrapLabel);

            for (int i = 0; i < paths.Count; i++)
            {
                EditorGUILayout.Space(4f);
                string label = i == 0 ? F("Open \"{0}\"", Path.GetFileNameWithoutExtension(paths[i])) : F("Open \"{0}\" (the outer prefab)", Path.GetFileNameWithoutExtension(paths[i]));
                if (GUILayout.Button(label, i == 0 ? FlexibleGui.BigButton : GUI.skin.button, GUILayout.Height(i == 0 ? 34f : 26f)))
                {
                    if (FlexibleScreenSession.PrepareToOpen(paths[i]))
                        PrefabStageUtility.OpenPrefab(paths[i]);
                    GUIUtility.ExitGUI();
                }
            }
            DrawSceneUiList();
        }

        // ───────── 씬에 있는 UI 프리팹 목록 (하이라키에서 고르지 않고 바로 연다) ─────────

        private struct SceneUiRow
        {
            public GameObject instance;
            public string path;
            public int depth;
            public bool hasChildren;
            public bool flexible;
        }

        private static List<SceneUiRow> sceneUiRows;
        private static GUIStyle sceneUiMark;
        private static GUIStyle SceneUiMark => sceneUiMark ??= new GUIStyle(EditorStyles.miniLabel) { richText = true, alignment = TextAnchor.MiddleLeft };

        // 씬 UI 목록과 「아직 준비 안 된 UI」 검사는 씬을 통째로 훑어 오래 걸리므로(큰 씬에서 약 0.1초), 매번이 아니라 씬·계층·애셋이 바뀔 때만 다시 한다
        [InitializeOnLoadMethod]
        private static void WatchSceneForLists()
        {
            void Invalidate()
            {
                // 실행 중에는 오브젝트가 계속 생기고 사라지므로 다시 찾지 않는다
                if (EditorApplication.isPlaying)
                    return;
                sceneUiRows = null;
                sceneCanvases = null;
            }
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
            EditorSceneManager.activeSceneChangedInEditMode += (a, b) => Invalidate();
            EditorSceneManager.sceneOpened += (scene, mode) => Invalidate();
            EditorSceneManager.sceneClosed += scene => Invalidate();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Invalidate();
            };
        }
        private static readonly HashSet<int> sceneUiExpanded = new HashSet<int>();
        private static Vector2 sceneUiScroll;
        private static GUIStyle sceneUiLabel;

        // 열린 씬의 UI 프리팹 인스턴스를 하이라키 순서로. 안쪽 프리팹은 그것을 담은 프리팹 아래 한 단계씩 들여 쓴다
        public static List<GameObject> FindSceneUiPrefabs()
        {
            var result = new List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (rt.hideFlags != HideFlags.None || !PrefabUtility.IsAnyPrefabInstanceRoot(rt.gameObject))
                            continue;
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rt.gameObject));
                        if (asset != null && asset.transform is RectTransform)
                            result.Add(rt.gameObject);
                    }
                }
            }
            return result;
        }

        private static int PrefabDepth(Transform t)
        {
            int depth = 0;
            for (Transform up = t.parent; up != null; up = up.parent)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(up.gameObject))
                    depth++;
            }
            return depth;
        }

        private static void DrawSceneUiList()
        {
            EnsureSceneUiRows();
            if (sceneUiRows.Count == 0)
                return;
            DrawSceneUiRows();
        }

        private static List<SceneUiRow> EnsureSceneUiRows()
        {
            if (sceneUiRows == null)
            {
                List<GameObject> found = FindSceneUiPrefabs();
                sceneUiRows = new List<SceneUiRow>();
                for (int i = 0; i < found.Count; i++)
                {
                    GameObject go = found[i];
                    int depth = PrefabDepth(go.transform);
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    sceneUiRows.Add(new SceneUiRow
                    {
                        instance = go,
                        path = path,
                        depth = depth,
                        hasChildren = i + 1 < found.Count && found[i + 1].transform.IsChildOf(go.transform),
                        flexible = asset != null && asset.GetComponent<FlexibleLayout>() != null
                    });
                }
            }
            sceneUiRows.RemoveAll(r => r.instance == null);
            return sceneUiRows;
        }

        private static void DrawSceneUiRows()
        {
            // 접힌 프리팹의 안쪽은 건너뛴다
            var shown = new List<SceneUiRow>();
            Transform folded = null;
            foreach (SceneUiRow row in sceneUiRows)
            {
                if (folded != null && row.instance.transform.IsChildOf(folded))
                    continue;
                folded = null;
                shown.Add(row);
                if (row.hasChildren && !sceneUiExpanded.Contains(row.instance.GetInstanceID()))
                    folded = row.instance.transform;
            }

            EditorGUILayout.Space(8f);
            GUILayout.Label(T("UI prefabs in this scene"), EditorStyles.boldLabel);
            if (sceneUiLabel == null)
                sceneUiLabel = new GUIStyle(EditorStyles.label) { padding = new RectOffset(2, 2, 0, 0) };

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool scroll = shown.Count > ViewMaxRows;
                if (scroll)
                    sceneUiScroll = EditorGUILayout.BeginScrollView(sceneUiScroll, GUILayout.Height(ViewMaxRows * ViewRowHeight + 4f));
                Rect area = GUILayoutUtility.GetRect(10f, shown.Count * ViewRowHeight, GUILayout.ExpandWidth(true));
                for (int i = 0; i < shown.Count; i++)
                {
                    var rect = new Rect(area.x, area.y + i * ViewRowHeight, area.width, ViewRowHeight);
                    if (DrawSceneUiRow(shown[i], rect))
                    {
                        if (scroll)
                            EditorGUILayout.EndScrollView();
                        GUIUtility.ExitGUI();
                    }
                }
                if (scroll)
                    EditorGUILayout.EndScrollView();
            }
        }

        // 줄 하나: 펼침 화살표 · 아이콘과 이름(누르면 선택, 두 번 누르면 열기) · ✔ · 「열기」. 열었으면 true
        private static bool DrawSceneUiRow(SceneUiRow row, Rect rect)
        {
            GameObject go = row.instance;
            if (Selection.gameObjects.Contains(go))
                EditorGUI.DrawRect(rect, new Color(FlexibleGui.Accent.r, FlexibleGui.Accent.g, FlexibleGui.Accent.b, 0.25f));

            float x = rect.x + row.depth * ViewIndent;
            int id = go.GetInstanceID();
            if (row.hasChildren)
            {
                bool expanded = sceneUiExpanded.Contains(id);
                if (EditorGUI.Foldout(new Rect(x, rect.y, ViewIndent, rect.height), expanded, GUIContent.none, true) != expanded)
                {
                    if (expanded)
                        sceneUiExpanded.Remove(id);
                    else
                        sceneUiExpanded.Add(id);
                }
            }
            x += ViewIndent;

            const float buttonWidth = 52f;
            const float markWidth = 34f;
            var labelRect = new Rect(x, rect.y, Mathf.Max(10f, rect.xMax - x - buttonWidth - markWidth - 4f), rect.height);
            Transform parent = go.transform.parent;
            var content = new GUIContent(go.name, AssetPreview.GetMiniThumbnail(go), parent != null ? FlexiblePathUtility.GetPath(parent.root, go.transform) : go.name);
            Color old = GUI.contentColor;
            if (!go.activeInHierarchy)
                GUI.contentColor = new Color(old.r, old.g, old.b, 0.5f);
            GUI.Label(labelRect, content, sceneUiLabel);
            GUI.contentColor = old;
            if (row.flexible)
            {
                // 세로 말고 만든 화면 수 (예: ✔ 1/2). 화면마다 바꾼 오브젝트 수는 툴팁으로
                var layout = go.GetComponent<FlexibleLayout>();
                FlexibleProfileSettings settings = layout != null && layout.Settings != null ? layout.Settings : FlexibleSettingsUtility.GetOrCreateDefault();
                List<FlexibleProfile> others = settings.Profiles.Where(p => p.id != settings.BaseProfileId).ToList();
                List<int> counts = others.Select(p => layout != null ? FlexibleScreenSession.CountRecords(layout, p.id) : 0).ToList();
                int made = counts.Count(c => c > 0);
                Color color = made == others.Count ? FlexibleGui.Good : Color.gray;
                string tip = string.Join("\n", others.Select((p, i) => F("{0}: {1} changed", Name(p), counts[i])));
                GUI.Label(new Rect(labelRect.xMax + 2f, rect.y, markWidth, rect.height),
                    new GUIContent($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>✔ {made}/{others.Count}</color>", tip), SceneUiMark);
            }

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && labelRect.Contains(e.mousePosition))
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                bool open = e.clickCount == 2;
                e.Use();
                if (open)
                {
                    if (FlexibleScreenSession.PrepareToOpen(row.path))
                        PrefabStageUtility.OpenPrefab(row.path);
                    return true;
                }
            }

            var buttonRect = new Rect(rect.xMax - buttonWidth, rect.y + 1f, buttonWidth, rect.height - 2f);
            if (GUI.Button(buttonRect, new GUIContent(T("Open"), row.path), EditorStyles.miniButton))
            {
                if (FlexibleScreenSession.PrepareToOpen(row.path))
                    PrefabStageUtility.OpenPrefab(row.path);
                return true;
            }
            return false;
        }

        // 지금 선택에서 열 수 있는 UI 프리팹: Project 창의 프리팹 파일, 또는 씬에서 고른 프리팹 인스턴스.
        // 씬에서 안쪽 프리팹(다른 프리팹 안의 프리팹)을 골랐으면 안쪽 → 바깥 순서로 둘 다
        public static List<string> GetSelectedPrefabPaths()
        {
            var paths = new List<string>();
            var selected = Selection.activeObject as GameObject;
            if (selected == null)
                return paths;

            string assetPath = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (assetPath.EndsWith(".prefab"))
                    AddUiPrefab(paths, assetPath);
                return paths;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(selected))
            {
                AddUiPrefab(paths, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(PrefabUtility.GetNearestPrefabInstanceRoot(selected)));
                AddUiPrefab(paths, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(PrefabUtility.GetOutermostPrefabInstanceRoot(selected)));
            }
            return paths;
        }

        private static void AddUiPrefab(List<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path) || paths.Contains(path))
                return;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && asset.transform is RectTransform)
                paths.Add(path);
        }

        private static List<Canvas> sceneCanvases;
        private static bool showSceneList;
        private static List<string> scenePrefabs;
        private static List<RectTransform> sceneLoose;
        private static readonly Dictionary<int, bool> looseChoice = new Dictionary<int, bool>();
        // 씬 UI를 프리팹으로 저장할 폴더 (기본은 씬에 이미 있는 UI 프리팹의 폴더, 「바꾸기」로 고른다)
        private static string prefabFolder;
        // 마지막 씬 준비 결과 (확인창 대신 창 안에 보여 준다)
        private static string sceneSetupResult;
        private static string sceneSetupScene;

        // 열린 씬에 아직 Flexible UI가 아닌 UI가 있으면 한 번에 바꾸는 버튼을 보여 준다
        // (최상위 캔버스의 크기 맞춤 교체 + 화면을 꽉 채우는 UI 프리팹을 Flexible UI로)
        private static void DrawSceneCanvasCheck()
        {
            if (sceneCanvases == null)
            {
                sceneCanvases = FlexibleLayoutInspector.FindCanvasesToConvert();
                scenePrefabs = FindScenePrefabsToMake();
                sceneLoose = FindLooseSceneUi();
            }
            sceneCanvases.RemoveAll(c => c == null);
            sceneLoose.RemoveAll(r => r == null);
            if (sceneCanvases.Count == 0 && scenePrefabs.Count == 0 && sceneLoose.Count == 0)
                return;

            EditorGUILayout.Space(4f);
            // 0개인 항목은 빼고 할 일만 적는다
            var parts = new List<string>();
            if (sceneCanvases.Count > 0)
                parts.Add(F("{0} canvases without Flexible Canvas Scaler", sceneCanvases.Count));
            if (scenePrefabs.Count > 0)
                parts.Add(F("{0} full-screen prefabs that are not Flexible UI", scenePrefabs.Count));
            if (sceneLoose.Count > 0)
                parts.Add(F("{0} UI groups that are not prefabs", sceneLoose.Count));
            EditorGUILayout.HelpBox(F("Not ready for portrait / square / landscape yet: {0}.", string.Join(", ", parts)), MessageType.Warning);
            showSceneList = EditorGUILayout.Foldout(showSceneList, T("Show the list"), true);
            if (showSceneList)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (sceneCanvases.Count > 0)
                        GUILayout.Label(F("Canvases to switch to Flexible Canvas Scaler: {0}", string.Join(", ", sceneCanvases.Select(FlexibleLayoutInspector.CanvasLabel))), EditorStyles.wordWrappedMiniLabel);
                    if (scenePrefabs.Count > 0)
                        GUILayout.Label(F("Full-screen prefabs to make Flexible UI: {0}", string.Join(", ", scenePrefabs.Select(Path.GetFileNameWithoutExtension))), EditorStyles.wordWrappedMiniLabel);
                    if (sceneLoose.Count > 0)
                    {
                        GUILayout.Label(T("UI placed straight in the scene (not a prefab). Checked ones are saved as new prefabs and made Flexible UI; they look the same as now:"), EditorStyles.wordWrappedMiniLabel);
                        foreach (IGrouping<Transform, RectTransform> group in sceneLoose.GroupBy(r => IsTopCanvas(r) ? r : r.parent).ToList())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Label(group.Key.name, EditorStyles.miniBoldLabel);
                                GUILayout.FlexibleSpace();
                                bool whole = UsesWholeCanvas(group.Key);
                                int unit = EditorGUILayout.Popup(whole ? 0 : 1, new[] { T("One prefab"), T("One per child") }, GUILayout.Width(120f));
                                if (unit != (whole ? 0 : 1))
                                {
                                    canvasUnit[group.Key.GetInstanceID()] = unit == 0;
                                    sceneCanvases = null;
                                    GUIUtility.ExitGUI();
                                }
                            }
                            foreach (RectTransform loose in group)
                            {
                                int id = loose.GetInstanceID();
                                bool on = !looseChoice.TryGetValue(id, out bool chosen) || chosen;
                                looseChoice[id] = EditorGUILayout.ToggleLeft(IsTopCanvas(loose) ? F("{0} (whole canvas)", loose.name) : loose.parent.name + "/" + loose.name, on);
                            }
                        }
                    }
                }
            }
            // 새 프리팹 폴더는 미리 골라 두고, 원하면 바꾼다 (누를 때 따로 묻지 않는다)
            if (sceneLoose.Count > 0)
            {
                // 기본 폴더는 아직 없어도 된다 (저장할 때 만든다). 고른 폴더가 지워졌으면 기본으로 돌린다
                if (string.IsNullOrEmpty(prefabFolder) || (prefabFolder != DefaultPrefabFolder && !AssetDatabase.IsValidFolder(prefabFolder)))
                    prefabFolder = DefaultPrefabFolder;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(F("New prefabs go to {0}", prefabFolder), T("UI placed straight in the scene is saved as prefabs in this folder.")), EditorStyles.miniLabel);
                    if (GUILayout.Button(T("Change..."), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        string picked = PickPrefabFolder(prefabFolder);
                        if (picked != null)
                            prefabFolder = picked;
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button(T("Make the scene's UI Flexible UI"), FlexibleGui.BigButton, GUILayout.Height(30f)))
            {
                List<RectTransform> chosenLoose = sceneLoose.Where(r => !looseChoice.TryGetValue(r.GetInstanceID(), out bool c) || c).ToList();
                SetUpScene(sceneCanvases, scenePrefabs, chosenLoose, true, prefabFolder);
                sceneCanvases = null;
                GUIUtility.ExitGUI();
            }
        }

        // 화면 공간의 최상위 캔버스(프리팹이 아닌 것) 바로 아래에 프리팹이 아닌 채 놓인 UI 묶음.
        // Flexible UI는 프리팹 단위로 편집하므로 이런 UI는 화면별 배치를 만들 수 없다
        public static List<RectTransform> FindLooseSceneUi()
        {
            var result = new List<RectTransform>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                    {
                        Transform t = canvas.transform;
                        if (canvas.renderMode == RenderMode.WorldSpace || (t.parent != null && t.parent.GetComponentInParent<Canvas>(true) != null)
                            || PrefabUtility.IsPartOfAnyPrefab(canvas.gameObject))
                            continue;
                        List<RectTransform> children = t.Cast<Transform>().OfType<RectTransform>().Where(IsLooseUi).ToList();
                        if (children.Count == 0)
                            continue;
                        // 한 화면의 조각들이면 캔버스 통째로, 화면들을 담은 캔버스면 화면마다 프리팹으로
                        if (UsesWholeCanvas(t))
                            result.Add((RectTransform)t);
                        else
                            result.AddRange(children);
                    }
                }
            }
            return result;
        }

        // 캔버스마다 사용자가 고른 변환 단위 (true = 캔버스 통째로). 고르지 않았으면 구조를 보고 정한다
        private static readonly Dictionary<int, bool> canvasUnit = new Dictionary<int, bool>();

        public static bool UsesWholeCanvas(Transform canvas)
        {
            return canvasUnit.TryGetValue(canvas.GetInstanceID(), out bool whole) ? whole : IsOneScreenCanvas(canvas);
        }

        public static void SetWholeCanvas(Transform canvas, bool whole)
        {
            canvasUnit[canvas.GetInstanceID()] = whole;
        }

        // 직계 자식의 과반이 화면 일부만 차지하면(버튼·로고 등) 캔버스 하나가 한 화면이다.
        // 화면을 채우는 자식과 크기 0인 묶음(자식을 모아 둔 화면)은 그 자체가 한 화면이다
        public static bool IsOneScreenCanvas(Transform canvas)
        {
            int total = 0, partial = 0;
            foreach (Transform child in canvas)
            {
                if (!(child is RectTransform rt) || rt.CompareTag("EditorOnly") || rt.gameObject.hideFlags != HideFlags.None)
                    continue;
                total++;
                bool pointGroup = rt.anchorMin == rt.anchorMax && rt.sizeDelta.sqrMagnitude < 1f && rt.childCount > 0;
                if (!pointGroup && !IsFullScreen(rt))
                    partial++;
            }
            return partial * 2 > total;
        }

        private static bool IsTopCanvas(Transform t)
        {
            return t.GetComponent<Canvas>() != null && (t.parent == null || t.parent.GetComponentInParent<Canvas>(true) == null);
        }

        private static bool IsLooseUi(RectTransform rt)
        {
            if (PrefabUtility.IsPartOfAnyPrefab(rt.gameObject) || rt.CompareTag("EditorOnly") || rt.gameObject.hideFlags != HideFlags.None)
                return false;
            // 안에 든 그림이 모두 프리팹 안이면 프리팹을 담는 묶음일 뿐이다
            return rt.GetComponentsInChildren<Graphic>(true).Any(g => !PrefabUtility.IsPartOfAnyPrefab(g));
        }

        // 씬 UI를 프리팹 파일로 저장하고 씬 오브젝트를 그 프리팹으로 잇는다. 모습은 그대로 둔다
        public static string SaveAsPrefab(RectTransform rt, string folder)
        {
            bool wholeCanvas = IsTopCanvas(rt);
            if (!wholeCanvas)
                MakeScreenContainer(rt);
            FlexibleSettingsUtility.EnsureFolder(folder);
            // 여러 캔버스에 같은 이름(Background 등)이 흔하므로 파일 이름 앞에 캔버스 이름을 붙여 구분한다
            string name = rt.name;
            Canvas canvas = rt.parent != null ? rt.parent.GetComponentInParent<Canvas>(true) : null;
            if (wholeCanvas)
                name = rt.gameObject.scene.name + "_" + name;
            else if (canvas != null && !name.StartsWith(canvas.rootCanvas.name))
                name = canvas.rootCanvas.name + "_" + name;
            name = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (name.Length > 0 ? name : "UI") + ".prefab");
            return PrefabUtility.SaveAsPrefabAssetAndConnect(rt.gameObject, path, InteractionMode.UserAction) != null ? path : null;
        }

        // 크기 0인 점에 자식을 모아 둔 묶음(가운데 정렬 그룹 등)은 화면을 꽉 채우게 바꾼다.
        // 크기 0인 부모 안에서는 모든 기준점이 한 점이므로, 자식 기준점을 그 점의 비율 위치로 옮기면 어느 화면 크기에서도 모습이 같다
        public static void MakeScreenContainer(RectTransform rt)
        {
            bool point = rt.anchorMin == rt.anchorMax && rt.sizeDelta.sqrMagnitude < 1f;
            if (!point || rt.localScale != Vector3.one || rt.localRotation != Quaternion.identity)
                return;

            List<RectTransform> children = rt.Cast<Transform>().OfType<RectTransform>().ToList();
            Undo.RecordObjects(children.Cast<Object>().Append(rt).ToArray(), "Make screen container");
            Vector2 anchor = rt.anchorMin;
            Vector2 shift = rt.anchoredPosition;
            foreach (RectTransform child in children)
            {
                Vector2 min = child.offsetMin + shift;
                Vector2 max = child.offsetMax + shift;
                child.anchorMin = anchor;
                child.anchorMax = anchor;
                child.offsetMin = min;
                child.offsetMax = max;
            }
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 새 프리팹을 둘 기본 폴더 (없으면 저장할 때 만든다)
        private const string DefaultPrefabFolder = FlexibleSettingsUtility.DataFolder + "/Prefabs";

        // 폴더를 고른다 (Assets 안만). 취소하면 null
        private static string PickPrefabFolder(string start)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            // 아직 없는 폴더면 있는 데까지 올라가서 연다
            while (!string.IsNullOrEmpty(start) && start != "Assets" && !AssetDatabase.IsValidFolder(start))
                start = Path.GetDirectoryName(start)?.Replace('\\', '/');
            string picked = EditorUtility.SaveFolderPanel(T("Folder for the new prefabs"), Path.Combine(projectRoot, string.IsNullOrEmpty(start) ? "Assets" : start), "");
            if (string.IsNullOrEmpty(picked))
                return null;
            picked = picked.Replace('\\', '/');
            if (picked != Application.dataPath && !picked.StartsWith(Application.dataPath + "/"))
            {
                EditorUtility.DisplayDialog("Flexible UI", T("Pick a folder inside Assets."), T("OK"));
                return null;
            }
            return "Assets" + picked.Substring(Application.dataPath.Length);
        }

        // 열린 씬의 프리팹 인스턴스(안쪽 프리팹 포함) 중 화면을 꽉 채우는 UI 프리팹으로, 아직 Flexible UI가 아닌 것
        public static List<string> FindScenePrefabsToMake()
        {
            var paths = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (RectTransform rt in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (!PrefabUtility.IsAnyPrefabInstanceRoot(rt.gameObject) || !IsFullScreen(rt))
                            continue;
                        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rt.gameObject);
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (asset == null || !(asset.transform is RectTransform) || paths.Contains(path))
                            continue;
                        var layout = asset.GetComponent<FlexibleLayout>();
                        if (layout == null || layout.Settings == null)
                            paths.Add(path);
                    }
                }
            }
            return paths;
        }

        // 열린 씬에서 이 프리팹 파일들의 인스턴스 루트
        private static List<GameObject> SceneInstancesOf(List<string> paths)
        {
            var result = new List<GameObject>();
            if (paths.Count == 0)
                return result;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && paths.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)))
                            result.Add(t.gameObject);
                    }
                }
            }
            return result;
        }

        // 화면을 꽉 채우는지: 화면 공간의 최상위 캔버스까지 올라가며 모두 늘이기(앵커 0~1, 여백 0)인지 본다 (꺼진 오브젝트도 판단 가능)
        public static bool IsFullScreen(RectTransform target)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                var canvas = current.GetComponent<Canvas>();
                if (canvas != null && (current.parent == null || current.parent.GetComponentInParent<Canvas>(true) == null))
                    return canvas.renderMode != RenderMode.WorldSpace;
                if (!(current is RectTransform rt))
                    return false;
                bool stretch = (rt.anchorMin - Vector2.zero).sqrMagnitude < 0.0001f && (rt.anchorMax - Vector2.one).sqrMagnitude < 0.0001f
                    && rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
                if (!stretch)
                    return false;
            }
            return false;
        }

        // 확인을 받고 씬의 UI를 한 번에 바꾼다. 「프리팹까지 모두」 / 「캔버스만」 / 「취소」
        // loose: 프리팹이 아닌 씬 UI. 프리팹으로 저장한 뒤 Flexible UI로 만든다 (folder가 없으면 물어본다)
        public static void SetUpScene(List<Canvas> canvases, List<string> prefabs, List<RectTransform> loose, bool ask, string folder = null)
        {
            loose = loose ?? new List<RectTransform>();
            // 목록은 창의 「목록 보기」에 있다 (확인창은 길면 잘리므로 개수만)
            string text = F("This gets the UI in the open scene ready for portrait / square / landscape: {0} canvases switch to Flexible Canvas Scaler (phones in portrait look the same), and {1} full-screen prefabs become Flexible UI (their current look becomes the portrait layout; the prefab files are changed).\n\nSee \"Show the list\" in the window for the names.", canvases.Count, prefabs.Count);
            if (loose.Count > 0)
                text += "\n\n" + F("{0} UI groups placed straight in the scene are saved as new prefabs in {1} and become Flexible UI. They look the same as now.", loose.Count, folder ?? DefaultPrefabFolder);

            int choice = 0;
            if (ask)
            {
                if ((prefabs.Count > 0 || loose.Count > 0) && canvases.Count > 0)
                    choice = EditorUtility.DisplayDialogComplex("Flexible UI", text, T("Do all"), T("Cancel"), T("Canvases only"));
                else
                    choice = EditorUtility.DisplayDialog("Flexible UI", text, T("Do all"), T("Cancel")) ? 0 : 1;
            }
            if (choice == 1)
                return;
            if (choice == 0 && loose.Count > 0 && folder == null)
                folder = DefaultPrefabFolder;

            // 바꾸기 전에 저장돼 있던 씬만 끝에 저장한다 (다른 미저장 작업을 같이 저장하지 않도록)
            var cleanScenes = new List<UnityEngine.SceneManagement.Scene>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !scene.isDirty && !string.IsNullOrEmpty(scene.path))
                    cleanScenes.Add(scene);
            }

            int made = 0;
            var newPrefabs = new List<string>();
            var newInstances = new List<GameObject>();
            // 프리팹마다 몇 초씩 걸리므로 진행 막대를 띄운다
            int steps = 1 + (choice == 0 ? loose.Count + prefabs.Count : 0);
            int step = 0;
            bool unsaved;
            try
            {
                EditorUtility.DisplayProgressBar("Flexible UI", T("Canvases"), 0f);
                unsaved = canvases.Count > 0 && FlexibleLayoutInspector.ConvertCanvases(canvases);
                step++;
                if (choice == 0)
                {
                    foreach (RectTransform rt in loose)
                    {
                        if (rt == null)
                            continue;
                        EditorUtility.DisplayProgressBar("Flexible UI", rt.name, (float)step++ / steps);
                        if (!cleanScenes.Contains(rt.gameObject.scene))
                            unsaved = true;
                        string path = SaveAsPrefab(rt, folder);
                        if (path != null && MakeFlexible(path))
                        {
                            newPrefabs.Add(path);
                            newInstances.Add(rt.gameObject);
                        }
                    }
                    foreach (string path in prefabs)
                    {
                        EditorUtility.DisplayProgressBar("Flexible UI", Path.GetFileNameWithoutExtension(path), (float)step++ / steps);
                        if (MakeFlexible(path))
                            made++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            foreach (UnityEngine.SceneManagement.Scene scene in cleanScenes)
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene);
            }
            if (!ask)
                return;

            string done = F("Done: {0} canvases, {1} prefabs.", canvases.Count, made + newPrefabs.Count);
            if (newPrefabs.Count > 0)
                done += "\n" + F("New prefabs in {0}: {1}", folder, string.Join(", ", newPrefabs.Select(Path.GetFileNameWithoutExtension)));
            // 바뀐 것을 Hierarchy에서 펼쳐 골라 둔다 (새 프리팹 → Flexible UI가 된 프리팹 → 캔버스 순)
            List<GameObject> changed = newInstances.Count > 0 ? newInstances : SceneInstancesOf(prefabs);
            if (changed.Count == 0)
                changed = canvases.Where(c => c != null).Select(c => c.gameObject).ToList();
            if (changed.Count > 0)
            {
                Selection.objects = changed.Cast<Object>().ToArray();
                EditorGUIUtility.PingObject(changed[0]);
            }
            if (newInstances.Count > 0)
            {
                // 골라 두었으니 바로 아래 열기 버튼이 나온다
                done += "\n" + T("The new prefabs are selected in the Hierarchy. Press Open below to make their square and landscape layouts.");
            }
            else if (made > 0)
                done += "\n" + T("The prefabs that became Flexible UI are selected in the Hierarchy. Press Open below to make their square and landscape layouts.");
            if (unsaved)
                done += "\n" + T("The scene had other unsaved changes, so it was not saved. Save it with Ctrl+S.");
            // 결과는 확인창 대신 창 안에 남긴다
            sceneSetupResult = done;
            sceneSetupScene = EditorSceneManager.GetActiveScene().path;
        }

        // ───────── 준비 체크리스트 ─────────

        private static void DrawChecklist(PrefabStage stage)
        {
            GameObject root = stage.prefabContentsRoot;
            GUILayout.Label(Path.GetFileNameWithoutExtension(stage.assetPath), FlexibleGui.Title);
            if (!(root.transform is RectTransform rootRect))
            {
                EditorGUILayout.HelpBox(T("This prefab is not UI (its root has no RectTransform), so it cannot be used."), MessageType.Error);
                return;
            }

            GUILayout.Label(T("This prefab is not ready for screen switching yet. Press the button and it is set up automatically. What you see now becomes the portrait (base) layout."), FlexibleGui.WrapLabel);
            var layout = root.GetComponent<FlexibleLayout>();
            // 맨 위가 캔버스면 화면 크기를 그대로 쓰므로 채우기 조건은 맞은 것으로 본다
            var canvas = root.GetComponent<Canvas>();
            bool stretch = canvas != null || IsFullStretch(rootRect);
            // 스케일러가 있는 캔버스(= 화면 최상위로 쓰는 캔버스)만 따진다. 다른 캔버스 안에 들어가는 캔버스는 스케일러가 동작하지 않는다
            CanvasScaler rootScaler = root.GetComponent<CanvasScaler>();
            bool scalerOk = canvas == null || rootScaler == null || rootScaler is FlexibleCanvasScaler;
            // 버튼이 알아서 하는 내부 항목(컴포넌트·설정 연결)은 적지 않고, 이 프리팹에서 실제로 바뀌는 것만 알린다
            if (canvas != null && rootScaler != null && !scalerOk)
                CheckRow(false, T("The canvas uses Flexible Canvas Scaler (the same size rule on every screen)"));
            if (canvas == null && !stretch)
                CheckRow(false, T("Fills the whole screen (anchors stretch, offsets 0)"));

            EditorGUILayout.Space(4f);
            if (GUILayout.Button(T("Set up this prefab"), FlexibleGui.BigButton, GUILayout.Height(34f)))
            {
                // 화면을 채우지 않는 UI는 전체 화면인지 패널인지 묻는다 (패널이면 크기를 그대로 둔다)
                bool fit = !stretch && EditorUtility.DisplayDialog(T("Set up this prefab"),
                    T("This prefab does not fill the screen. Is it a full-screen UI?\n\nFull screen: it is stretched to fill the screen.\nPanel: it keeps its size (for a panel shown inside another prefab)."),
                    T("Full screen"), T("Panel"));
                if (layout == null)
                    layout = Undo.AddComponent<FlexibleLayout>(root);
                Undo.RecordObject(layout, "Flexible Setup");
                if (layout.Settings == null)
                    layout.Settings = FlexibleSettingsUtility.GetOrCreateDefault();
                if (fit)
                    FlexibleLayoutInspector.FitToParent(rootRect);
                if (canvas != null && rootScaler != null)
                    FlexibleLayoutInspector.UseFlexibleScaler(canvas);
                EditorUtility.SetDirty(layout);
                FlexibleScreenSession.Save(stage);
                FlexibleSettingsUtility.RecordCanvasRule(stage.assetPath);
                FlexibleScreenSession.EnsureEnvironmentScaler(stage);

                // 안쪽에 전체 화면 프리팹이 있으면 같이 만들지 한 번에 묻는다
                List<string> inner = FindNestedPrefabs(stage).Where(n => !n.flexible && n.fullScreen).Select(n => n.path).ToList();
                if (inner.Count > 0 && EditorUtility.DisplayDialog(T("Set up this prefab"),
                        F("This prefab has {0} full-screen prefabs inside: {1}\nMake them Flexible UI too? Their current look becomes their portrait layout.", inner.Count, string.Join(", ", inner.Select(Path.GetFileNameWithoutExtension))),
                        T("Make them too"), T("Later")))
                    MakeNestedFlexible(stage, layout, inner);
                GUIUtility.ExitGUI();
            }
        }

        // 맨 위가 캔버스인데 일반 CanvasScaler면, 화면마다 크기 규칙이 달라지므로 바꾸게 한다
        private static void DrawScalerCheck(PrefabStage stage)
        {
            var canvas = stage.prefabContentsRoot.GetComponent<Canvas>();
            CanvasScaler scaler = stage.prefabContentsRoot.GetComponent<CanvasScaler>();
            if (canvas == null || scaler == null || scaler is FlexibleCanvasScaler)
                return;

            EditorGUILayout.HelpBox(T("This canvas does not use Flexible Canvas Scaler, so the UI size changes differently from screen to screen (for example much smaller in landscape). Phones in portrait look the same after the change."), MessageType.Warning);
            if (GUILayout.Button(T("Use Flexible Canvas Scaler"), GUILayout.Height(24f)))
            {
                FlexibleLayoutInspector.UseFlexibleScaler(canvas);
                FlexibleScreenSession.Save(stage);
                GUIUtility.ExitGUI();
            }
        }

        private static void CheckRow(bool ok, string text)
        {
            var style = new GUIStyle(EditorStyles.label) { richText = true };
            string mark = ok ? "<color=#4c4>✔</color>" : "<color=#e66>✖</color>";
            GUILayout.Label($"{mark}  {text}", style);
        }

        private static bool IsFullStretch(RectTransform rt)
        {
            return rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
                && rt.offsetMin.sqrMagnitude < 0.0001f && rt.offsetMax.sqrMagnitude < 0.0001f;
        }

        // ───────── 화면 버튼 ─────────

        private static void DrawScreens(PrefabStage stage, FlexibleLayout layout)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Path.GetFileNameWithoutExtension(stage.assetPath), FlexibleGui.Title);
                GUILayout.FlexibleSpace();
                // 저장할 게 없으면 「저장됨」으로 보여 눌렀는지·저장됐는지 헷갈리지 않게 한다
                bool unsaved = stage.scene.isDirty;
                Color previousColor = GUI.backgroundColor;
                if (unsaved)
                    GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                bool savePressed = GUILayout.Button(new GUIContent(unsaved ? T("Save (Ctrl+S)") : T("Saved ✓"), unsaved ? T("There are unsaved changes. Switching screens also saves them.") : T("Everything is saved.")), GUILayout.Height(22f), GUILayout.Width(110f));
                GUI.backgroundColor = previousColor;
                if (savePressed)
                {
                    FlexibleScreenSession.Save(stage);
                    GUIUtility.ExitGUI();
                }
                FlexibleGui.Mark("save");
            }

            GUILayout.Label(T("Which screen are you editing?"), EditorStyles.miniLabel);
            DrawScreenButtons(stage, layout, 40f);
            DrawShapeCheck(stage);
            DrawScalerCheck(stage);
            DrawScreenChanges(stage);
            // 저장 안 됨은 저장 버튼 색으로만 알린다 (안내 줄이 생기면 아래 입력칸이 밀려 헛클릭이 난다)
        }

        // ───────── 이 화면에서 바뀐 블록·화면 밖 블록 ─────────

        private static bool showChanged;
        private static string infoStage;
        private static List<RectTransform> changedCache = new List<RectTransform>();
        private static List<RectTransform> outsideCache = new List<RectTransform>();

        // 1초마다 다시 센다 (블록 수가 많아도 창이 무거워지지 않게)
        private static void RefreshScreenInfo(PrefabStage stage)
        {
            // 1초마다가 아니라 무언가 바뀌었을 때만 다시 센다 (큰 프리팹에서 한 번에 약 0.1초)
            HookChanges();
            string infoKey = stage.assetPath + "#" + FlexibleScreenSession.GetActiveProfileId(stage) + "#" + changeStamp + "#" + FlexibleScreenSession.SaveCount;
            if (infoStage == infoKey)
                return;
            infoStage = infoKey;
            changedCache = FlexibleScreenSession.ChangedBlocks(stage);
            outsideCache.Clear();
            // 기준 화면에서는 경고하지 않고, 세로에서도 화면 밖이던 것(일부러 밖에 둔 것)은 빼고 알린다
            if (!FlexibleScreenSession.IsBaseView(stage))
            {
                var root = (RectTransform)stage.prefabContentsRoot.transform;
                HashSet<string> onBase = BaseOutside(stage);
                outsideCache = FlexiblePreviewRenderer.FindOutside(root).Where(t => !onBase.Contains(FlexiblePathUtility.GetPath(root, t))).ToList();
            }
        }

        private static string baseOutsideKey;
        private static HashSet<string> baseOutside = new HashSet<string>();

        // 저장된 기준(세로) 배치에서 화면 밖에 있는 것의 경로 (저장할 때마다 다시 구한다)
        private static HashSet<string> BaseOutside(PrefabStage stage)
        {
            string key = stage.assetPath + "#" + FlexibleScreenSession.SaveCount;
            if (key == baseOutsideKey)
                return baseOutside;
            baseOutsideKey = key;
            baseOutside = new HashSet<string>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null || layout.Settings == null)
                return baseOutside;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 size = FlexibleSpecService.GetWorkSize(asset, baseProfile, settings);
            using (var renderer = new FlexiblePreviewRenderer())
            {
                renderer.Prepare(asset, baseProfile.id, size, new Rect(0f, 0f, 1f, 1f), null, FlexibleScreenSession.GetViewByPath(stage));
                var copyRoot = (RectTransform)renderer.Instance.transform;
                foreach (RectTransform t in FlexiblePreviewRenderer.FindOutside(copyRoot))
                    baseOutside.Add(FlexiblePathUtility.GetPath(copyRoot, t));
            }
            return baseOutside;
        }

        public static void RefreshScreenInfoNow()
        {
            infoStage = null;
        }

        // 화면 밖 경고는 보기 목록 아래에 둔다 (목록에서 블록을 켜고 끄면 경고가 생겼다 없어져, 위에 있으면 목록이 밀린다)
        private static void DrawOutside(PrefabStage stage)
        {
            RefreshScreenInfo(stage);
            outsideCache.RemoveAll(t => t == null);

            if (outsideCache.Count > 0)
            {
                EditorGUILayout.HelpBox(F("{0} objects stick out of the screen: {1}", outsideCache.Count, string.Join(", ", outsideCache.Take(5).Select(t => t.name)) + (outsideCache.Count > 5 ? ", …" : "")), MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(T("Fit them all into the screen"), T("Fitting shrinks each object evenly until it fits, then moves it inside. Check the result and adjust by hand if needed.")), GUILayout.Height(24f)))
                    {
                        var root = (RectTransform)stage.prefabContentsRoot.transform;
                        Undo.IncrementCurrentGroup();
                        int group = Undo.GetCurrentGroup();
                        List<RectTransform> fitted = outsideCache.ToList();
                        foreach (RectTransform target in fitted)
                            FlexibleLayoutInspector.FitToScreen(root, target);
                        Undo.CollapseUndoOperations(group);
                        // 경고가 사라진 뒤에도 무엇이 바뀌었는지 보이게 골라 둔다
                        Selection.objects = fitted.Select(t => (Object)t.gameObject).ToArray();
                        RefreshScreenInfoNow();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button(T("Select them"), GUILayout.Width(100f), GUILayout.Height(24f)))
                        Selection.objects = outsideCache.Select(t => (Object)t.gameObject).ToArray();
                }
            }
            DrawReservedOver(stage);
            DrawExtraIssues(stage);
        }

        // 다른 프리팹이 차지하는 자리(메뉴 바 등)가 이 프리팹 위에 보이는지. 게임이 숨기는 화면이면 끈다 (검사·Scene 표시에서 빠진다)
        private static void DrawReservedOver(PrefabStage stage)
        {
            FlexibleProfileSettings settings = FlexibleReservedAreas.SettingsOf(stage.prefabContentsRoot);
            string guid = AssetDatabase.AssetPathToGUID(stage.assetPath);
            List<FlexibleReservedArea> areas = FlexibleReservedAreas.FromOthers(settings, guid);
            if (areas.Count == 0)
                return;
            // 이 프리팹의 버튼과 겹치는 자리(또는 이미 끈 자리)만 보여 준다. 상관없는 프리팹에는 나오지 않는다 (1초마다 다시 센다)
            string key = stage.assetPath + "#" + FlexibleScreenSession.GetActiveProfileId(stage);
            if (key != reservedKey || EditorApplication.timeSinceStartup - reservedAt > 1.0)
            {
                reservedKey = key;
                reservedAt = EditorApplication.timeSinceStartup;
                reservedRelevant = FindRelevantAreas(stage, settings, guid, areas);
            }
            foreach (FlexibleReservedArea area in areas)
            {
                if (!reservedRelevant.Contains(area))
                    continue;
                bool shown = FlexibleReservedAreas.IsShownOver(area, guid);
                EditorGUI.BeginChangeCheck();
                shown = EditorGUILayout.ToggleLeft(new GUIContent(F("{0} is shown over this prefab", FlexibleReservedAreas.Label(area)),
                    T("Turn it off if the game hides it while this prefab is open (a title screen, a full-screen conversation ...). Then its place is not drawn here and the check stops reporting buttons under it.")), shown);
                if (EditorGUI.EndChangeCheck())
                    FlexibleReservedAreas.SetShownOver(settings, area, guid, shown);
            }
        }

        private static string reservedKey;
        private static double reservedAt;
        private static HashSet<FlexibleReservedArea> reservedRelevant = new HashSet<FlexibleReservedArea>();

        private static HashSet<FlexibleReservedArea> FindRelevantAreas(PrefabStage stage, FlexibleProfileSettings settings, string guid, List<FlexibleReservedArea> areas)
        {
            var result = new HashSet<FlexibleReservedArea>(areas.Where(a => !FlexibleReservedAreas.IsShownOver(a, guid)));
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            List<Rect> buttons = root.GetComponentsInChildren<Selectable>(false)
                .Select(b => FlexiblePreviewRenderer.GetRectInRoot(root, (RectTransform)b.transform)).ToList();
            foreach ((FlexibleReservedArea area, Rect rect) in FlexibleReservedAreas.ForScreen(settings, guid, FlexibleScreenSession.GetActiveProfileId(stage), root.rect))
            {
                if (buttons.Any(r => Mathf.Min(r.xMax, rect.xMax) - Mathf.Max(r.xMin, rect.xMin) > 2f && Mathf.Min(r.yMax, rect.yMax) - Mathf.Max(r.yMin, rect.yMin) > 2f
                    && !(r.xMin <= rect.xMin && r.xMax >= rect.xMax && r.yMin <= rect.yMin && r.yMax >= rect.yMax)))
                    result.Add(area);
            }
            return result;
        }

        // ───────── 보이지 않는 곳의 문제 (숨긴 패널·뒤집힌 크기·목록 잘림·다른 프리팹 자리) ─────────

        private const int ExtraShown = 8;
        private static List<FlexibleIssue> extraCache = new List<FlexibleIssue>();
        private static string extraKey;
        private static string extraScope;
        private static double extraAt;
        private static int changeStamp;
        private static double lastChangeAt;
        private static bool changeHooked;
        private static bool repaintScheduled;
        private static string baseExtraKey;
        private static HashSet<string> baseExtra = new HashSet<string>();

        private static string IssueKey(FlexibleIssue issue) => issue.code + "|" + string.Join("|", issue.blocks);

        // 사본을 만들어 검사하므로 무겁다: 내용·화면·보기가 바뀌고 편집이 1초 멈춘 뒤에만 다시 구한다 (끄는 동안 멈칫하지 않게).
        // 목록이 그리는 도중 바뀌지 않도록 Layout 때만 구한다
        // 프리팹·씬에 무언가 바뀔 때마다 번호를 올린다 (검사 결과를 다시 구할지 정하는 데 쓴다)
        private static void HookChanges()
        {
            if (changeHooked)
                return;
            changeHooked = true;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                changeStamp++;
                lastChangeAt = EditorApplication.timeSinceStartup;
            };
        }

        private static void RefreshExtraIssues(PrefabStage stage)
        {
            HookChanges();
            if (Event.current != null && Event.current.type != EventType.Layout)
                return;
            if (FlexibleScreenSession.IsBaseView(stage))
            {
                extraCache.Clear();
                extraKey = null;
                return;
            }

            Dictionary<string, bool> view = FlexibleScreenSession.GetViewByPath(stage);
            string profileId = FlexibleScreenSession.GetActiveProfileId(stage);
            // 다른 프리팹·화면으로 바뀌면 이전 결과는 바로 지운다
            string scope = stage.assetPath + "#" + profileId;
            if (scope != extraScope)
            {
                extraScope = scope;
                extraCache.Clear();
                extraAt = 0;
            }
            string key = stage.assetPath + "#" + profileId + "#" + changeStamp + "#" + FlexibleScreenSession.SaveCount + "#"
                + string.Join(",", view.Select(p => p.Key + "=" + p.Value)) + "#"
                + FlexibleReservedAreas.SettingsOf(stage.prefabContentsRoot).ReservedAreas.Sum(a => 1 + (a.hiddenFor != null ? a.hiddenFor.Count * 7 : 0));
            if (key == extraKey)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastChangeAt < 1.0 || now - extraAt < 2.0)
            {
                RepaintLater(1.1);
                return;
            }
            extraKey = key;
            extraAt = EditorApplication.timeSinceStartup;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            HashSet<string> onBase = BaseExtra(stage);
            // 렌더러는 다시 쓰고(미리보기 씬을 매번 만들지 않는다) 사본만 새로 만든다. 필요한 종류만 검사한다
            if (extraRenderer == null)
            {
                extraRenderer = new FlexiblePreviewRenderer();
                AssemblyReloadEvents.beforeAssemblyReload += () =>
                {
                    extraRenderer?.Dispose();
                    extraRenderer = null;
                };
            }
            extraRenderer.Dirty = true;
            extraRenderer.Prepare(stage.prefabContentsRoot, null, root.rect.size, new Rect(0f, 0f, 1f, 1f), profileId);
            extraCache = extraRenderer.CollectIssueList(new Rect(0f, 0f, 1f, 1f), true)
                .Where(i => FlexibleIssue.IsExtra(i.code) && !onBase.Contains(IssueKey(i))).ToList();
        }

        private static FlexiblePreviewRenderer extraRenderer;

        // 창이 가만히 있어도 잠시 뒤 다시 그려 검사가 돌게 한다
        private static void RepaintLater(double seconds)
        {
            if (repaintScheduled)
                return;
            repaintScheduled = true;
            double at = EditorApplication.timeSinceStartup + seconds;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < at)
                    return;
                EditorApplication.update -= tick;
                repaintScheduled = false;
                foreach (FlexibleWindow window in Resources.FindObjectsOfTypeAll<FlexibleWindow>())
                    window.Repaint();
            };
            EditorApplication.update += tick;
        }

        // 기준(세로) 화면에도 있던 문제는 원래 디자인이라 빼고 알린다 (저장할 때마다 다시 구한다)
        private static HashSet<string> BaseExtra(PrefabStage stage)
        {
            string key = stage.assetPath + "#" + FlexibleScreenSession.SaveCount;
            if (key == baseExtraKey)
                return baseExtra;
            baseExtraKey = key;
            baseExtra = new HashSet<string>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
            var layout = asset != null ? asset.GetComponent<FlexibleLayout>() : null;
            if (layout == null || layout.Settings == null)
                return baseExtra;
            FlexibleProfileSettings settings = layout.Settings;
            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            Vector2 size = FlexibleSpecService.GetWorkSize(asset, baseProfile, settings);
            using (var renderer = new FlexiblePreviewRenderer())
            {
                renderer.Prepare(asset, baseProfile.id, size, new Rect(0f, 0f, 1f, 1f));
                foreach (FlexibleIssue issue in renderer.CollectIssueList(new Rect(0f, 0f, 1f, 1f)))
                {
                    if (FlexibleIssue.IsExtra(issue.code))
                        baseExtra.Add(IssueKey(issue));
                }
            }
            return baseExtra;
        }

        private static void DrawExtraIssues(PrefabStage stage)
        {
            RefreshExtraIssues(stage);
            if (extraCache.Count == 0)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(new GUIContent(F("⚠ {0} problems you cannot see right now", extraCache.Count),
                    T("Found in panels the game opens later, in lists, and in places other prefabs take. Show a hidden panel to edit it on this screen.")), EditorStyles.boldLabel);
                foreach (FlexibleIssue issue in extraCache.Take(ExtraShown))
                {
                    Transform target = FlexiblePathUtility.FindByPath(root, issue.blocks[0]);
                    Transform hidden = target != null ? LowestHidden(target, root) : null;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(issue.Message, FlexibleGui.WrapLabel);
                        if (hidden != null && GUILayout.Button(new GUIContent(T("Show"), F("Shows \"{0}\" while you edit (not saved).", hidden.name)), EditorStyles.miniButton, GUILayout.Width(56f)))
                        {
                            ShowInView(stage, root, hidden, true);
                            Selection.activeTransform = target;
                        }
                        using (new EditorGUI.DisabledScope(target == null))
                        {
                            if (GUILayout.Button(T("Select"), EditorStyles.miniButton, GUILayout.Width(56f)))
                            {
                                Selection.activeTransform = target;
                                EditorGUIUtility.PingObject(target);
                            }
                        }
                    }
                }
                if (extraCache.Count > ExtraShown)
                    GUILayout.Label(F("… and {0} more. The Check tab lists all of them.", extraCache.Count - ExtraShown), EditorStyles.miniLabel);
            }
        }

        // 블록을 가리고 있는 가장 가까운 숨김 (꺼진 오브젝트 또는 Canvas가 꺼진 패널)
        private static Transform LowestHidden(Transform target, Transform root)
        {
            for (Transform t = target; t != null && t != root; t = t.parent)
            {
                if (!FlexibleViewOverride.IsVisible(t.gameObject))
                    return t;
            }
            return null;
        }

        private static void DrawScreenChanges(PrefabStage stage)
        {
            RefreshScreenInfo(stage);
            changedCache.RemoveAll(t => t == null);

            if (FlexibleScreenSession.IsBaseView(stage))
                return;
            showChanged = EditorGUILayout.Foldout(showChanged, F("Objects with their own layout on this screen ({0})", changedCache.Count), true);
            if (!showChanged)
                return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (changedCache.Count == 0)
                    GUILayout.Label(T("None yet. Move, resize or hide an object to give this screen its own layout."), EditorStyles.wordWrappedMiniLabel);
                foreach (RectTransform target in changedCache.ToList())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(target.name, EditorStyles.label))
                        {
                            Selection.activeTransform = target;
                            EditorGUIUtility.PingObject(target.gameObject);
                        }
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(T("Undo this one"), EditorStyles.miniButton, GUILayout.Width(110f)))
                        {
                            FlexibleScreenSession.ResetToBase(stage, target);
                            RefreshScreenInfoNow();
                        }
                    }
                }
            }
        }

        // 화면 버튼 줄 (편집 탭·Scene 뷰 오버레이 공용)
        public static void DrawScreenButtons(PrefabStage stage, FlexibleLayout layout, float height)
        {
            FlexibleProfileSettings settings = layout.Settings;
            string active = FlexibleScreenSession.GetActiveProfileId(stage);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (FlexibleProfile p in settings.Profiles)
                {
                    bool on = p.id == active;
                    // 지금 편집 중인 화면은 저장 전 변경까지 센다
                    string sub = p.id == settings.BaseProfileId ? T("base")
                        : on ? F("{0} changed", LiveChangedCount(stage))
                        : F("{0} changed", FlexibleScreenSession.CountRecords(layout, p.id));
                    // 기준 화면은 모든 화면에, 다른 화면은 그 화면에만 반영된다는 것을 툴팁으로 알린다
                    string tip = p.id == settings.BaseProfileId
                        ? F("Edit the {0} screen ({1:0}x{2:0}). Changes here apply to every screen. Switching screens saves the prefab.", Name(p), p.workResolution.x, p.workResolution.y)
                        : F("Edit the {0} screen ({1:0}x{2:0}). Only this screen changes; untouched objects stay as in {3}. Switching screens saves the prefab.", Name(p), p.workResolution.x, p.workResolution.y, Name(settings.Find(settings.BaseProfileId)));
                    var content = new GUIContent($"{Name(p)}\n<size=10>{sub}</size>", tip);
                    Color previous = GUI.backgroundColor;
                    if (on)
                        GUI.backgroundColor = FlexibleGui.Accent;
                    bool pressed = GUILayout.Button(content, on ? FlexibleGui.BigButtonOn : FlexibleGui.BigButton, GUILayout.Height(height), GUILayout.MinWidth(60f));
                    GUI.backgroundColor = previous;
                    FlexibleGui.Mark("screen:" + p.id);
                    if (pressed && !on)
                    {
                        FlexibleScreenSession.ShowProfile(stage, p.id);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private static string liveStage;
        private static double liveAt;
        private static int liveCount;

        private static int LiveChangedCount(PrefabStage stage)
        {
            if (liveStage != stage.assetPath || EditorApplication.timeSinceStartup - liveAt > 1.0)
            {
                liveStage = stage.assetPath;
                liveAt = EditorApplication.timeSinceStartup;
                liveCount = FlexibleScreenSession.ChangedBlocks(stage).Count;
            }
            return liveCount;
        }

        public static string Name(FlexibleProfile profile)
        {
            if (profile == null)
                return "-";
            return string.IsNullOrEmpty(profile.displayName) ? profile.id : T(profile.displayName);
        }

        // ───────── 선택한 오브젝트 ─────────

        // 안쪽 Flexible 프리팹 안의 오브젝트: 그 프리팹 것이면 그 프리팹에서 배치하도록 안내하고(true), 이 프리팹에서 추가한 것이면 여기서 배치된다고 알린다
        private static bool DrawNestedOwner(PrefabStage stage, RectTransform root, RectTransform target)
        {
            Transform nested = null;
            for (Transform c = target.parent; c != null && c != root; c = c.parent)
            {
                if (c.GetComponent<FlexibleLayout>() != null)
                {
                    nested = c;
                    break;
                }
            }
            if (nested == null)
                return false;
            string nestedPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nested.gameObject);
            string nestedName = string.IsNullOrEmpty(nestedPath) ? nested.name : Path.GetFileNameWithoutExtension(nestedPath);
            if (!FlexiblePathUtility.IsOwnedByNested(target, root))
            {
                GUILayout.Label(F("Added inside \"{0}\" by this prefab, so its layout is kept here.", nestedName), EditorStyles.wordWrappedMiniLabel);
                return false;
            }
            GUILayout.Label(F("Part of \"{0}\". Its layout is kept in that prefab.", nestedName), EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(nestedPath) && GUILayout.Button(F("Open \"{0}\"", nestedName)))
            {
                OpenNestedPrefab(nestedPath, stage);
                GUIUtility.ExitGUI();
            }
            return true;
        }

        private static bool showSelected = true;

        private static void DrawSelected(PrefabStage stage, FlexibleLayout layout)
        {
            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            EditorGUILayout.Space(4f);
            bool open = showSelected || FlexibleTutorial.Active;
            EditorGUI.BeginChangeCheck();
            open = EditorGUILayout.Foldout(open, T("Selected object"), true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck())
                showSelected = open;
            if (!open)
                return;
            List<RectTransform> targets = GetSelectedBlocks(root);
            if (targets.Count == 0)
            {
                GUILayout.Label(T("Select an object to edit."), EditorStyles.wordWrappedMiniLabel);
                return;
            }

            FlexibleProfile baseProfile = settings.Find(settings.BaseProfileId);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(targets.Count == 1 ? targets[0].name : F("{0} objects selected", targets.Count), EditorStyles.boldLabel);
                }
                if (targets.Count > 1)
                    GUILayout.Label(Shorten(string.Join(", ", targets.Select(t => t.name)), 120), EditorStyles.wordWrappedMiniLabel);
                if (targets.Count == 1 && DrawNestedOwner(stage, root, targets[0]))
                    return;

                // 여러 개면 상태가 모두 같을 때만 그 안내를 보이고, 다르면 화면마다 따로 둔 블록 수를 알린다
                List<FlexibleBlockStatus> statuses = targets.Select(t => FlexibleScreenSession.GetStatus(stage, t)).ToList();
                List<RectTransform> own = targets.Where((t, i) => statuses[i] == FlexibleBlockStatus.Own).ToList();
                bool same = statuses.All(s => s == statuses[0]);
                if (same && statuses[0] == FlexibleBlockStatus.BaseScreen)
                {
                    // 기준 화면 안내는 위 화면 설명과 같아서 적지 않는다
                }
                else if (same && statuses[0] == FlexibleBlockStatus.SameAsBase)
                    GUILayout.Label(F("○ Same as {0}. Move it to give this screen its own layout.", Name(baseProfile)), FlexibleGui.WrapLabel);
                else if (same && statuses[0] == FlexibleBlockStatus.NotSavedYet)
                    GUILayout.Label(T("New object (not saved yet). After saving it appears on every screen."), EditorStyles.wordWrappedMiniLabel);
                else if (own.Count > 0)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string text = own.Count == targets.Count ? T("Has its own layout on this screen") : F("{0} of {1} objects have their own layout on this screen", own.Count, targets.Count);
                        GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(FlexibleGui.Good)}>●</color> " + text, FlexibleGui.WrapLabel);
                        if (GUILayout.Button(F("Back to {0}", Name(baseProfile)), GUILayout.Width(130f)))
                        {
                            foreach (RectTransform t in own)
                                FlexibleScreenSession.ResetToBase(stage, t);
                        }
                    }
                }

                DrawRolePopup(layout, targets, settings);
                DrawTransformFields(stage, targets);
                if (targets.Count == 1)
                    DrawReservedToggle(stage, targets[0]);
            }
        }

        // 메뉴 바처럼 다른 화면 위에 늘 떠 있는 블록: 다른 프리팹을 편집·검수할 때 피해야 할 자리로 등록한다
        private static void DrawReservedToggle(PrefabStage stage, RectTransform target)
        {
            bool on = FlexibleReservedAreas.Find(stage, target) != null;
            EditorGUI.BeginChangeCheck();
            on = EditorGUILayout.ToggleLeft(new GUIContent(T("Other prefabs keep clear of this"),
                T("For a bar that stays on top of other screens, such as a menu bar. While you edit other prefabs, its place is drawn in the Scene view, and the check and the AI keep buttons out of it.")), on);
            FlexibleGui.Mark("reserved");
            if (EditorGUI.EndChangeCheck())
                FlexibleReservedAreas.Set(stage, target, on);
        }

        // 지금 화면에서의 위치·크기·배율 (Inspector로 가지 않고 바로 고친다). 여러 개를 고르면 값이 다른 칸은 「—」
        private static void DrawTransformFields(PrefabStage stage, List<RectTransform> targets)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Pos X"), T("Pos Y"), T("Width"), T("Height"), T("Scale"));
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Pos X"), targets, t => t.anchoredPosition.x, (t, v) => t.anchoredPosition = new Vector2(v, t.anchoredPosition.y));
                MixedFloat(T("Pos Y"), targets, t => t.anchoredPosition.y, (t, v) => t.anchoredPosition = new Vector2(t.anchoredPosition.x, v));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Width"), targets, t => t.rect.width, (t, v) => t.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, v)));
                MixedFloat(T("Height"), targets, t => t.rect.height, (t, v) => t.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, v)));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                MixedFloat(T("Scale"), targets, t => t.localScale.x, (t, v) => t.localScale = new Vector3(v, v, t.localScale.z));
                var root = (RectTransform)stage.prefabContentsRoot.transform;
                bool anyOutside = targets.Any(t => FlexiblePreviewRenderer.FindOutside(root, new List<RectTransform> { t }).Count > 0);
                using (new EditorGUI.DisabledScope(!anyOutside))
                {
                    if (GUILayout.Button(new GUIContent(T("Fit into the screen"), T("Shrinks the object evenly until it fits the screen, then moves it inside.")), EditorStyles.miniButton, GUILayout.Width(130f)))
                    {
                        Undo.IncrementCurrentGroup();
                        int group = Undo.GetCurrentGroup();
                        foreach (RectTransform target in targets)
                            FlexibleLayoutInspector.FitToScreen(root, target);
                        Undo.CollapseUndoOperations(group);
                        RefreshScreenInfoNow();
                    }
                }
            }
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private static void MixedFloat(string label, List<RectTransform> targets, System.Func<RectTransform, float> get, System.Action<RectTransform, float> set)
        {
            float first = get(targets[0]);
            EditorGUI.showMixedValue = targets.Exists(t => Mathf.Abs(get(t) - first) > 0.001f);
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.DelayedFloatField(label, Mathf.Round(first * 100f) / 100f);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck())
                return;
            Undo.RecordObjects(targets.Cast<Object>().ToArray(), "Flexible Edit Block");
            foreach (RectTransform target in targets)
            {
                set(target, value);
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
            RefreshScreenInfoNow();
            SceneView.RepaintAll();
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static void DrawRolePopup(FlexibleLayout layout, List<RectTransform> targets, FlexibleProfileSettings settings)
        {
            var options = new List<string> { T("(none)") };
            options.AddRange(settings.Roles);
            List<string> roles = targets.Select(t => layout.GetRole(t)).ToList();
            string current = roles[0];
            int index = string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
            if (index < 0)
            {
                options.Add(current);
                index = options.Count - 1;
            }

            EditorGUI.showMixedValue = roles.Any(r => (r ?? "") != (current ?? ""));
            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.Popup(new GUIContent(T("Role (optional)"), T("Used by templates and the check tab. Header, Main, Footer ...")), index, options.ToArray());
            EditorGUI.showMixedValue = false;
            FlexibleGui.Mark("role");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Role");
                foreach (RectTransform t in targets)
                    layout.SetRole(t, index == 0 ? null : options[index]);
                EditorUtility.SetDirty(layout);
            }
        }

        // ───────── 그리드 ─────────

        private void DrawGrid(PrefabStage stage, FlexibleLayout layout)
        {
            EditorGUILayout.Space(6f);
            EditorGUI.BeginChangeCheck();
            showGrid = EditorGUILayout.Foldout(showGrid, new GUIContent(T("Place on a grid"), showGrid ? null : T("Select an object, then drag over the blue cells in the Scene view, or type the cells below. The object snaps to those cells.")), true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            FlexibleGui.Mark("grid");
            if (!showGrid)
                return;

            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            FlexibleProfile profile = settings.Profiles[profileIndex];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // 자주 쓰는 배치 칸을 위에, 거의 바꾸지 않는 그리드 설정은 접어서 아래에 둔다
                FlexibleGrid grid = layout.GetGrid(profile.id);
                var context = new FlexibleGridContext(root, grid, settings.GetSafeInsets(profileIndex));
                List<BlockInput> inputs = context.grid.IsValid
                    ? GetSelectedBlocks(root).Select(t => GetInput(t, session, context)).ToList()
                    : new List<BlockInput>();
                DrawStalePlacements(context, session);
                DrawPlacement(context, session, inputs);

                EditorGUILayout.Space(4f);
                bool open = showGridSettings || FlexibleTutorial.Active || !grid.IsValid;
                EditorGUI.BeginChangeCheck();
                string gridTitle = layout.UseOwnGrid
                    ? F("Grid settings ({0} x {1}, this prefab only)", grid.columns, grid.rows)
                    : F("Grid settings ({0} x {1}, project)", grid.columns, grid.rows);
                open = EditorGUILayout.Foldout(open, gridTitle, true);
                if (EditorGUI.EndChangeCheck())
                    showGridSettings = open;
                if (open)
                    DrawGridSettings(layout, profile);
            }
        }

        [SerializeField] private bool showGridSettings;

        // 프리팹 모양은 Game 뷰 해상도를 따른다. 지금 화면과 모양이 다르면 알리고 맞추는 버튼을 준다
        private static void DrawShapeCheck(PrefabStage stage)
        {
            if (FlexibleScreenSession.IsShapeMatching(stage, out FlexibleProfile profile))
                return;

            EditorGUILayout.HelpBox(F("The prefab is not drawn at the {0} work size ({1:0}x{2:0}), because Unity's Game view has a different size. Changes made now would be measured at that other size. Press the button to fix it.", Name(profile), profile.workResolution.x, profile.workResolution.y), MessageType.Warning);
            if (GUILayout.Button(T("Fix the Game view size"), GUILayout.Height(26f)))
                FlexibleScreenSession.SelectWorkResolution(profile);
        }

        // 전용 그리드를 켜면 이 프리팹의 화면별 그리드를 고칠 수 있고, 끄면 프로젝트 그리드를 읽기 전용으로 보여 준다
        private void DrawGridSettings(FlexibleLayout layout, FlexibleProfile profile)
        {
            EditorGUI.BeginChangeCheck();
            bool own = EditorGUILayout.ToggleLeft(T("Use a grid only for this prefab"), layout.UseOwnGrid);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(layout, "Flexible Own Grid");
                layout.UseOwnGrid = own;
                if (own)
                    layout.FillOwnGrids();
                EditorUtility.SetDirty(layout);
                SceneView.RepaintAll();
            }
            FlexibleGui.Mark("grid:own");

            FlexibleGrid grid = layout.GetGrid(profile.id);
            using (new EditorGUI.DisabledScope(!layout.UseOwnGrid))
            {
                if (FlexibleGui.GridFields(ref grid))
                {
                    Undo.RecordObject(layout, "Flexible Own Grid");
                    layout.SetOwnGrid(profile.id, grid);
                    EditorUtility.SetDirty(layout);
                    SceneView.RepaintAll();
                }
            }
            FlexibleGui.Mark("grid:size");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!layout.UseOwnGrid)
                    GUILayout.Label(F("Project grid for the {0} screen, used by every prefab without its own grid. Change it in Project grids, or turn on the option above.", Name(profile)), EditorStyles.wordWrappedMiniLabel);
                else
                    GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("Project grids..."), EditorStyles.miniButton, GUILayout.Width(110f)))
                    FlexibleProjectGridWindow.Open();
            }

            EditorGUI.BeginChangeCheck();
            dragPlacement = EditorGUILayout.ToggleLeft(T("Place by dragging in the Scene view"), dragPlacement);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
        }

        private static void DrawStalePlacements(FlexibleGridContext context, FlexibleWorkspace session)
        {
            var stale = FlexibleGridPlacementService.FindStale(context, session);
            if (stale.Count == 0)
                return;

            EditorGUILayout.HelpBox(F("{0} grid objects would move if placed again with the current grid or screen size. (Manually adjusted objects are excluded.)", stale.Count), MessageType.Info);
            if (GUILayout.Button(F("Re-place grid objects ({0})", stale.Count)))
            {
                FlexibleGridPlacementService.ReapplyStale(session, stale);
                Selection.objects = stale.Where(s => s.placement.target != null).Select(s => (Object)s.placement.target.gameObject).ToArray();
            }
        }

        private void DrawPlacement(FlexibleGridContext context, FlexibleWorkspace session, List<BlockInput> inputs)
        {
            if (!context.grid.IsValid)
            {
                EditorGUILayout.HelpBox(T("Grid columns and rows must be 1 or more."), MessageType.Error);
                return;
            }
            if (inputs.Count == 0)
            {
                GUILayout.Label(T("Select the object to place."), EditorStyles.miniLabel);
                return;
            }

            // 여러 개를 고르면 한 덩어리로 옮길지 (끄면 하나씩 같은 값으로)
            if (inputs.Count > 1)
            {
                moveTogether = EditorGUILayout.ToggleLeft(new GUIContent(T("Move them together"),
                    T("The selected objects move and shrink as one group and keep their places to each other, like pieces of one popup. Drag over the cells in the Scene view or use Pin to.")), moveTogether);
                if (moveTogether)
                {
                    List<RectTransform> group = inputs.Select(i => i.target).ToList();
                    DrawGroupCells(context, session, group);
                    DrawGroupPins(context, session, group);
                    return;
                }
            }

            // 블록마다 지금 상태 한 줄 (많으면 앞의 몇 개만)
            const int MaxLines = 5;
            foreach (BlockInput input in inputs.Take(MaxLines))
                EditorGUILayout.LabelField(F("{0}: {1}", input.target.name, input.placement == null ? F("Not placed on the grid (now covers {0})", input.cell) : Describe(input.placement)), EditorStyles.wordWrappedMiniLabel);
            if (inputs.Count > MaxLines)
                GUILayout.Label(F("... and {0} more", inputs.Count - MaxLines), EditorStyles.miniLabel);

            // 값을 바꾸면 고른 블록 전부에 바로 적용한다 (숫자는 입력을 마쳤을 때). 블록마다 값이 다르면 「—」
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = FlexibleGui.FitLabels(T("Column"), T("Row"), T("Width (cells)"), T("Height (cells)"), T("Horizontal"), T("Vertical"));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (MixedIntField(T("Column"), inputs, i => i.cell.column, out int column))
                    PlaceAll(context, session, inputs, i => { i.cell.column = column; return i; });
                if (MixedIntField(T("Row"), inputs, i => i.cell.row, out int row))
                    PlaceAll(context, session, inputs, i => { i.cell.row = row; return i; });
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (MixedIntField(T("Width (cells)"), inputs, i => i.cell.width, out int width))
                    PlaceAll(context, session, inputs, i => { i.cell.width = width; return i; });
                if (MixedIntField(T("Height (cells)"), inputs, i => i.cell.height, out int height))
                    PlaceAll(context, session, inputs, i => { i.cell.height = height; return i; });
            }
            using (new EditorGUILayout.VerticalScope())
            {
                if (MixedModeField(T("Horizontal"), ModeLabelsX, inputs, i => i.modeX, out FlexibleAxisMode modeX))
                    PlaceAll(context, session, inputs, i => { i.modeX = modeX; return i; });
                if (MixedModeField(T("Vertical"), ModeLabelsY, inputs, i => i.modeY, out FlexibleAxisMode modeY))
                    PlaceAll(context, session, inputs, i => { i.modeY = modeY; return i; });
            }
            FlexibleGui.Mark("grid:mode");

            // 크기: 크기를 지키는 축은 칸 크기를 쓰므로, 세로 화면 크기로 되돌리거나 칸에 맞춰 줄인다
            using (new EditorGUILayout.HorizontalScope())
            {
                bool anyFixed = inputs.Exists(i => FlexibleGridMath.ResolveX(i.modeX, i.cell, context.grid) != FlexibleAxisMode.Stretch
                    || FlexibleGridMath.ResolveY(i.modeY, i.cell, context.grid) != FlexibleAxisMode.Stretch);
                using (new EditorGUI.DisabledScope(!anyFixed))
                {
                    if (GUILayout.Button(new GUIContent(T("Use its own size"), T("Sticking to an edge or centering keeps the size of the cells. This puts back the size the object has on the portrait screen.")), EditorStyles.miniButton))
                        PlaceAll(context, session, inputs, i => { i.ownSize = true; return i; });
                }
                bool shownFit = inputs[0].fit;
                // 내용에 맞춰 크기가 정해지는 블록은 칸 크기를 따를 수 없으므로 늘 줄여서 놓는다 (끌 수 없게 하고 이유를 보인다)
                bool contentSized = inputs.Exists(i => FlexibleGridPlacementService.IsContentSized(i.target));
                EditorGUI.showMixedValue = inputs.Exists(i => i.fit != shownFit);
                EditorGUI.BeginChangeCheck();
                bool fit;
                using (new EditorGUI.DisabledScope(contentSized && inputs.Count == 1))
                {
                    fit = EditorGUILayout.ToggleLeft(new GUIContent(T("Shrink to fit the cells"),
                        T("Keeps the portrait size and shape, and scales the object down on every screen size so it fits the cells (big pictures, popups, button rows).")), shownFit);
                }
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                    PlaceAll(context, session, inputs, i => { i.fit = fit || FlexibleGridPlacementService.IsContentSized(i.target); i.ownSize = i.fit; return i; });
            }
            if (inputs.Count == 1)
                DrawSizeMismatch(context, session, inputs[0]);

            // 모서리에 붙이기: 안전 영역의 그 모서리에서 세로 화면과 같은 거리·크기
            using (new EditorGUILayout.HorizontalScope())
            {
                var pinLabel = new GUIContent(T("Pin to"), T("Puts the object at the same distance from that corner or edge of the safe area as on the portrait screen, with its portrait size. Good for bars, corner buttons and centered popups."));
                GUILayout.Label(pinLabel, GUILayout.Width(Mathf.Max(EditorGUIUtility.labelWidth, EditorStyles.label.CalcSize(pinLabel).x + 4f)));
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(84f)))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            for (int col = 0; col < 3; col++)
                            {
                                int index = row * 3 + col;
                                if (GUILayout.Button(new GUIContent(PinLabels[index], Pins[index]), EditorStyles.miniButton, GUILayout.Width(26f)))
                                    PinAll(context, session, inputs, Pins[index]);
                            }
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }

            // SafeArea도 블록마다의 값이다. 바꾸면 고른 블록 전부를 그 값으로 바로 다시 놓는다 (칸에 안 놓인 블록은 지금 덮고 있는 칸에)
            bool shownSafe = inputs[0].insideSafeArea;
            // 이 화면의 대표 기기에 노치·홈 바 자리가 없으면 체크가 아무 효과도 없다. 헷갈리지 않게 끄고 이유를 보인다
            bool noInsets = context.safeInsets.sqrMagnitude < 0.0000001f;
            EditorGUI.showMixedValue = inputs.Exists(i => i.insideSafeArea != shownSafe);
            EditorGUI.BeginChangeCheck();
            bool inside;
            using (new EditorGUI.DisabledScope(noInsets))
            {
                inside = EditorGUILayout.ToggleLeft(new GUIContent(T("Keep the selected objects inside the safe area (red = notch / home bar)"),
                    T("Turn it off for backgrounds and bars that should reach the screen edge.") + "\n"
                    + F("SafeArea insets of the reference device: left {0:P1} · right {1:P1} · top {2:P1} · bottom {3:P1}", context.safeInsets.x, context.safeInsets.y, context.safeInsets.z, context.safeInsets.w)), shownSafe);
            }
            EditorGUI.showMixedValue = false;
            if (noInsets)
                GUILayout.Label(T("This screen's reference device has no notch or home bar, so the whole screen is the safe area."), EditorStyles.wordWrappedMiniLabel);
            if (EditorGUI.EndChangeCheck())
            {
                // 마지막으로 고른 값은 다음에 새로 놓는 블록의 기본값이 된다
                insideSafeArea = inside;
                EditorPrefs.SetBool(InsideSafeAreaKey, inside);
                PlaceAll(context, session, inputs, i => { i.insideSafeArea = inside; return i; });
            }
            EditorGUIUtility.labelWidth = labelWidth;
            // 바꾸면 바로 움직이므로 따로 안내하지 않는다
        }

        private static void DrawGroupPins(FlexibleGridContext context, FlexibleWorkspace session, List<RectTransform> targets)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var pinLabel = new GUIContent(T("Pin to"), T("Puts the group at the same distance from that corner or edge of the safe area as on the portrait screen. If it does not fit, the whole group is scaled down."));
                GUILayout.Label(pinLabel, GUILayout.Width(Mathf.Max(EditorGUIUtility.labelWidth, EditorStyles.label.CalcSize(pinLabel).x + 4f)));
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(84f)))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            for (int col = 0; col < 3; col++)
                            {
                                int index = row * 3 + col;
                                if (GUILayout.Button(new GUIContent(PinLabels[index], Pins[index]), EditorStyles.miniButton, GUILayout.Width(26f)))
                                    PinGroup(context, session, targets, Pins[index]);
                            }
                        }
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        private static string Describe(FlexibleGridPlacement placement)
        {
            if (placement == null)
                return T("Not placed on the grid");
            string where = placement.insideSafeArea ? T(", inside SafeArea") : "";
            if (FlexibleWorkspaceService.IsUntouchedPlacement(placement))
                return F("Grid object {0}{1}", placement.cell, where);
            if (FlexibleGridPlacementService.TryGetAdjustment(placement, out FlexibleGridAdjustment adjustment))
                return F("Grid {0}{1} + adjustment ({2})", placement.cell, where, adjustment);
            return F("Manually adjusted (anchors or pivot changed from grid {0}{1}; excluded from templates)", placement.cell, where);
        }

        // ───────── 템플릿 ─────────

        private void DrawTemplates(PrefabStage stage, FlexibleLayout layout)
        {
            showTemplates = EditorGUILayout.Foldout(showTemplates, new GUIContent(T("Templates"), showTemplates ? null : T("Save where each role (Header, Main, ...) sits on the grid of a screen, and reuse it on other prefabs.")), true, EditorStyles.foldoutHeader);
            FlexibleGui.Mark("templates");
            if (!showTemplates)
                return;

            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            FlexibleProfile profile = settings.Profiles[profileIndex];
            var context = new FlexibleGridContext(root, layout.GetGrid(profile.id), settings.GetSafeInsets(profileIndex));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // 템플릿은 칸 번호로 저장되므로, 전용 그리드를 쓰면 다른 프리팹과 크기가 달라질 수 있다
                if (layout.UseOwnGrid)
                    EditorGUILayout.HelpBox(T("This prefab uses its own grid. Templates keep cell numbers, so objects may come out a different size than in other prefabs."), MessageType.Info);
                List<FlexibleTemplate> templates = settings.Templates.Where(t => t.profileId == profile.id).ToList();
                if (templates.Count > 0)
                {
                    templateIndex = Mathf.Clamp(templateIndex, 0, templates.Count - 1);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        templateIndex = EditorGUILayout.Popup(templateIndex, templates.Select(t => F("{0} ({1} slots)", t.name, t.slots.Count)).ToArray());
                        if (GUILayout.Button(T("Apply"), GUILayout.Width(60)))
                        {
                            TemplateApplyCount++;
                            ApplyTemplate(context, session, layout, templates[templateIndex]);
                        }
                        FlexibleGui.Mark("template:apply");
                        if (GUILayout.Button(T("Delete"), GUILayout.Width(60))
                            && EditorUtility.DisplayDialog(T("Delete template"), F("Delete the template '{0}'? (shared by the project)", templates[templateIndex].name), T("Delete"), T("Cancel")))
                        {
                            FlexibleGridPlacementService.DeleteTemplate(settings, templates[templateIndex]);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(T("No templates for this screen yet."), EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    templateName = EditorGUILayout.TextField(templateName);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(templateName)))
                    {
                        if (GUILayout.Button(T("Save current layout as template"), GUILayout.Width(210)))
                            SaveTemplate(session, layout, profile, settings);
                    }
                    FlexibleGui.Mark("template:save");
                }
            }
        }

        private void SaveTemplate(FlexibleWorkspace session, FlexibleLayout layout, FlexibleProfile profile, FlexibleProfileSettings settings)
        {
            var skipped = new List<string>();
            FlexibleTemplate template = FlexibleGridPlacementService.CreateTemplate(session, layout, profile.id, templateName.Trim(), skipped);
            string skippedText = skipped.Count > 0 ? "\n\n" + T("Excluded objects:") + "\n· " + string.Join("\n· ", skipped) : "";
            if (template.slots.Count == 0)
            {
                ShowNotice(PrefabStageUtility.GetCurrentPrefabStage(), T("Nothing to save. Only objects that have a role and were placed on the grid go into a template.") + skippedText, MessageType.Warning);
                return;
            }

            FlexibleGridPlacementService.SaveTemplate(settings, template);
            // 방금 저장한 템플릿을 목록에서 골라 둔다
            templateIndex = Mathf.Max(0, settings.Templates.Where(t => t.profileId == profile.id).ToList().FindIndex(t => t.name == template.name));
            IEnumerable<string> saved = template.slots.Select(s => s.HasAdjustment ? F("{0} (with adjustment)", s.role) : s.role);
            ShowNotice(PrefabStageUtility.GetCurrentPrefabStage(), F("Saved {1} roles to '{0}'.", template.name, template.slots.Count) + $"\n{string.Join(", ", saved)}{skippedText}");
            templateName = "";
        }

        private static void ApplyTemplate(FlexibleGridContext context, FlexibleWorkspace session, FlexibleLayout layout, FlexibleTemplate template)
        {
            var missing = new List<string>();
            int applied = FlexibleGridPlacementService.ApplyTemplate(context, session, layout, template, missing);
            string message = F("Placed {0} objects.", applied);
            if (missing.Count > 0)
                message += "\n" + F("Roles missing in this prefab: {0}", string.Join(", ", missing));
            ShowNotice(PrefabStageUtility.GetCurrentPrefabStage(), message, missing.Count > 0 ? MessageType.Warning : MessageType.Info);
        }

        // ───────── 그 밖 ─────────

        private void DrawMore(PrefabStage stage, FlexibleLayout layout)
        {
            showMore = EditorGUILayout.Foldout(showMore, T("More"), true, EditorStyles.foldoutHeader);
            if (!showMore)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                FlexibleScreenSession.AutoGameView = EditorGUILayout.ToggleLeft(T("Change the Game view size when switching screens"), FlexibleScreenSession.AutoGameView);

                FlexibleProfileSettings settings = layout.Settings;
                foreach (FlexibleProfile p in settings.Profiles)
                {
                    if (p.id == settings.BaseProfileId)
                        continue;
                    int count = FlexibleScreenSession.CountRecords(layout, p.id);
                    using (new EditorGUI.DisabledScope(count == 0))
                    {
                        if (GUILayout.Button(F("Undo all {0} changes ({1} objects)", Name(p), count))
                            && EditorUtility.DisplayDialog(T("Undo all changes"), F("Make every object of the {0} screen the same as {1} again? This cannot be undone.", Name(p), Name(settings.Find(settings.BaseProfileId))), T("Yes"), T("Cancel")))
                        {
                            FlexibleScreenSession.ClearProfile(stage, p.id);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (GUILayout.Button(T("Select the screen settings asset")))
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }
        }

        // ───────── 옛 작업 프리팹 ─────────

        private static void DrawLegacyWorkspace(PrefabStage stage)
        {
            var workspace = stage.prefabContentsRoot.GetComponent<FlexibleWorkspace>();
            string sourcePath = AssetDatabase.GUIDToAssetPath(workspace.sourceGuid);
            GUILayout.Label(T("Old work copy"), FlexibleGui.Title);
            GUILayout.Label(F("This is a work copy made by an older version of the tool ({0} screen of {1}). Now you edit the original prefab directly, so this copy is no longer needed.",
                workspace.profileId, Path.GetFileNameWithoutExtension(sourcePath)), FlexibleGui.WrapLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Apply it to the original and open the original"), FlexibleGui.BigButton, GUILayout.Height(32f)))
                {
                    string path = stage.assetPath;
                    if (stage.scene.isDirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, path);
                        stage.ClearDirtiness();
                    }
                    StageUtility.GoToMainStage();
                    ApplyLegacy(path, true);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(T("Discard it"), GUILayout.Height(32f), GUILayout.Width(100f))
                    && EditorUtility.DisplayDialog(T("Discard"), T("Delete this old work copy? Changes in it are lost."), T("Delete"), T("Cancel")))
                {
                    FlexibleWorkspaceService.Delete(stage.assetPath);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // 원본을 열었는데 옛 버전의 작업 프리팹이 남아 있으면 알려 준다
        private static void DrawPendingLegacy(PrefabStage stage)
        {
            List<string> pending = FindLegacyWorkspaces(stage.assetPath);
            if (pending.Count == 0)
                return;

            EditorGUILayout.HelpBox(F("Unfinished work from an older version of the tool: {0}", string.Join(", ", pending.Select(LegacyProfileId))), MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Apply it")))
                {
                    if (FlexibleScreenSession.ShowProfile(stage, FlexibleScreenSession.GetActiveProfileId(stage)))
                    {
                        StageUtility.GoToMainStage();
                        foreach (string path in pending)
                            ApplyLegacy(path, false);
                        PrefabStageUtility.OpenPrefab(stage.assetPath);
                    }
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(T("Discard it"))
                    && EditorUtility.DisplayDialog(T("Discard"), T("Delete the old work copies? Changes in them are lost."), T("Delete"), T("Cancel")))
                {
                    foreach (string path in pending)
                        AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private static string LegacyProfileId(string workspacePath)
        {
            FlexibleWorkspace workspace = FlexibleWorkspaceService.LoadWorkspaceComponent(workspacePath);
            return workspace != null ? workspace.profileId : Path.GetFileNameWithoutExtension(workspacePath);
        }

        private static List<string> FindLegacyWorkspaces(string sourcePath)
        {
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(FlexibleWorkspaceService.WorkspaceFolder))
                return new List<string>();
            return AssetDatabase.FindAssets("t:Prefab", new[] { FlexibleWorkspaceService.WorkspaceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !Path.GetFileName(p).StartsWith("_cli_") && p.EndsWith("__" + guid.Substring(0, 8) + ".prefab"))
                .ToList();
        }

        private static void ApplyLegacy(string workspacePath, bool openSource)
        {
            FlexibleCommitReport report = FlexibleWorkspaceService.Commit(workspacePath);
            if (report.success)
                FlexibleWorkspaceService.Delete(workspacePath);

            var message = new StringBuilder();
            message.AppendLine(report.success ? F("Applied the {0} screen to {1}.", report.profileId, report.sourcePath) : T("Could not apply. The old work copy was kept."));
            foreach (string warning in report.warnings.Take(10))
                message.AppendLine("· " + warning);
            EditorUtility.DisplayDialog(T("Old work copy"), message.ToString(), T("OK"));
            if (report.success && openSource && FlexibleScreenSession.PrepareToOpen(report.sourcePath))
                PrefabStageUtility.OpenPrefab(report.sourcePath);
        }

        public static RectTransform GetSelectedBlock(RectTransform root)
        {
            var target = Selection.activeTransform as RectTransform;
            return target != null && target != root && target.IsChildOf(root) ? target : null;
        }

        // 고른 블록 전부 (프리팹 루트와 프리팹 밖 오브젝트는 뺀다), Hierarchy 순서
        public static List<RectTransform> GetSelectedBlocks(RectTransform root)
        {
            var picked = new HashSet<RectTransform>();
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go != null && go.transform is RectTransform rt && rt != root && rt.IsChildOf(root))
                    picked.Add(rt);
            }
            if (picked.Count <= 1)
                return picked.ToList();
            return root.GetComponentsInChildren<RectTransform>(true).Where(picked.Contains).ToList();
        }

        // ───────── Scene 뷰 그리드 ─────────

        public void OnSceneGUI(SceneView sceneView)
        {
            if (!showGrid)
                return;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
                return;
            FlexibleWorkspace session = FlexibleScreenSession.GetSession(stage);
            if (session == null)
                return;

            var root = (RectTransform)stage.prefabContentsRoot.transform;
            FlexibleProfileSettings settings = layout.Settings;
            int profileIndex = settings.IndexOf(FlexibleScreenSession.GetActiveProfileId(stage));
            if (profileIndex < 0 || !layout.GetGrid(settings.Profiles[profileIndex].id).IsValid)
                return;

            var context = new FlexibleGridContext(root, layout.GetGrid(settings.Profiles[profileIndex].id), settings.GetSafeInsets(profileIndex));
            FlexibleGrid grid = context.grid;
            List<RectTransform> targets = GetSelectedBlocks(root);
            // 드래그는 블록 하나, 또는 여러 개를 한 덩어리로 옮길 때 (여러 블록을 같은 칸에 겹쳐 놓는 일은 없으므로)
            BlockInput? single = targets.Count == 1 ? GetInput(targets[0], session, context) : (BlockInput?)null;
            bool group = IsGroup(targets);
            Rect area = context.GetArea(single?.insideSafeArea ?? insideSafeArea);
            DrawUnsafeArea(root, root.rect, context.GetArea(true));
            for (int r = 0; r < grid.rows; r++)
            for (int c = 0; c < grid.columns; c++)
                DrawRect(root, FlexibleGridMath.GetSlotRect(area, grid, new FlexibleCell(c, r, 1, 1)),
                    new Color(0.2f, 0.6f, 1f, 0.05f), new Color(0.2f, 0.6f, 1f, 0.5f));

            // 주황 = 드래그 중인 범위, 아니면 선택 블록들이 이 화면에서 놓인 칸 (칸에 놓은 적 없으면 그리지 않는다)
            var orangeFill = new Color(1f, 0.6f, 0.1f, 0.15f);
            var orangeLine = new Color(1f, 0.6f, 0.1f, 1f);
            if (dragging)
            {
                Rect dragRect = FlexibleGridMath.GetSlotRect(area, grid, dragCell.Clamped(grid));
                DrawRect(root, dragRect, orangeFill, orangeLine);
                DrawDragSize(root, dragRect, dragCell.Clamped(grid));
            }
            else if (group)
                DrawRect(root, UnionInRoot(root, targets), orangeFill, orangeLine);
            else
            {
                foreach (RectTransform target in targets)
                {
                    FlexibleGridPlacement placed = session.FindPlacement(target);
                    if (placed != null)
                        DrawRect(root, FlexibleGridMath.GetSlotRect(context.GetArea(placed.insideSafeArea), grid, placed.cell.Clamped(grid)), orangeFill, orangeLine);
                }
            }

            if (dragPlacement && (single.HasValue || group))
                HandleDrag(context, session, area, single, group ? targets : null);
            else
                dragging = false;
        }

        private void HandleDrag(FlexibleGridContext context, FlexibleWorkspace session, Rect area, BlockInput? single, List<RectTransform> group)
        {
            RectTransform root = context.root;
            FlexibleGrid grid = context.grid;
            int id = GUIUtility.GetControlID(DragHint, FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && TryPickCell(root, area, grid, e.mousePosition, out Vector2Int start))
                    {
                        GUIUtility.hotControl = id;
                        dragging = true;
                        dragStart = start;
                        dragCell = FlexibleCell.FromCorners(start, start);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        if (TryPickCell(root, area, grid, e.mousePosition, out Vector2Int current))
                            dragCell = FlexibleCell.FromCorners(dragStart, current);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (dragging && GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        dragging = false;
                        e.Use();
                        if (group != null)
                        {
                            RememberGroupCell(session, group, dragCell.Clamped(grid));
                            PlaceGroupInCells(context, session, group, FlexibleGridMath.GetSlotRect(area, grid, dragCell.Clamped(grid)));
                            break;
                        }
                        // 드래그로 칸을 새로 정하면 손으로 바꾼 위치·크기·스케일은 버린다 (모드·SafeArea는 유지).
                        // 스케일도 원래 값으로 돌려야 블록이 드래그한 범위와 같은 크기로 보인다
                        BlockInput input = single.Value;
                        BlockInput dragged = input;
                        dragged.cell = dragCell;
                        dragged.placement = null;
                        PlaceAll(context, session, new List<BlockInput> { dragged }, i => i);
                        // 드래그한 범위와 다르게 놓였으면 Scene 뷰에서 바로 알린다 (고르는 버튼은 창에 있다)
                        if (TryGetSizeMismatch(context, session.FindPlacement(input.target), out _, out _))
                            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(T("It is not the size you dragged. Choose what to do in the Flexible UI window.")), 4.0);
                    }
                    break;
            }
        }

        private static GUIStyle dragSizeStyle;

        // 드래그 중인 범위 가운데에 「가로 칸 x 세로 칸」
        private static void DrawDragSize(RectTransform root, Rect rect, FlexibleCell cell)
        {
            dragSizeStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = Texture2D.whiteTexture },
                padding = new RectOffset(8, 8, 3, 3),
            };
            var content = new GUIContent($"{cell.width} x {cell.height}");
            Vector2 size = dragSizeStyle.CalcSize(content);
            Vector2 center = HandleUtility.WorldToGUIPoint(root.TransformPoint(rect.center));
            Handles.BeginGUI();
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.05f, 0.9f);
            GUI.Label(new Rect(center - size * 0.5f, size), content, dragSizeStyle);
            GUI.backgroundColor = previous;
            Handles.EndGUI();
        }

        private static bool TryPickCell(RectTransform root, Rect area, FlexibleGrid grid, Vector2 mousePosition, out Vector2Int picked)
        {
            picked = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var plane = new Plane(root.forward, root.position);
            if (!plane.Raycast(ray, out float distance))
                return false;

            Vector2 local = root.InverseTransformPoint(ray.GetPoint(distance));
            return root.rect.Contains(local) && FlexibleGridMath.TryGetCell(area, grid, local, out picked);
        }

        // 대표 기기 SafeArea 밖(노치·홈 인디케이터 자리)을 붉게 칠한다
        private static void DrawUnsafeArea(RectTransform root, Rect full, Rect safe)
        {
            var fill = new Color(1f, 0.2f, 0.2f, 0.18f);
            var none = new Color(0f, 0f, 0f, 0f);
            if (safe.yMax < full.yMax)
                DrawRect(root, Rect.MinMaxRect(full.xMin, safe.yMax, full.xMax, full.yMax), fill, none);
            if (safe.yMin > full.yMin)
                DrawRect(root, Rect.MinMaxRect(full.xMin, full.yMin, full.xMax, safe.yMin), fill, none);
            if (safe.xMin > full.xMin)
                DrawRect(root, Rect.MinMaxRect(full.xMin, safe.yMin, safe.xMin, safe.yMax), fill, none);
            if (safe.xMax < full.xMax)
                DrawRect(root, Rect.MinMaxRect(safe.xMax, safe.yMin, full.xMax, safe.yMax), fill, none);

            // 경계는 늘 그린다 (노치가 없는 화면에서도 안전 영역이 어디인지 보이게)
            DrawRect(root, safe, none, new Color(0.3f, 0.9f, 0.4f, 0.9f));
            bool whole = safe == full;
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.3f, 0.9f, 0.4f, 1f) } };
            Handles.Label(root.TransformPoint(new Vector3(safe.xMin, safe.yMax)), whole ? T("Safe area = whole screen") : T("Safe area"), style);
        }

        private static void DrawRect(RectTransform root, Rect rect, Color fill, Color outline)
        {
            var corners = new Vector3[]
            {
                root.TransformPoint(new Vector3(rect.xMin, rect.yMin)),
                root.TransformPoint(new Vector3(rect.xMin, rect.yMax)),
                root.TransformPoint(new Vector3(rect.xMax, rect.yMax)),
                root.TransformPoint(new Vector3(rect.xMax, rect.yMin)),
            };
            Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
        }
    }
}
