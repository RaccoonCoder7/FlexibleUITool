using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 툴의 단일 창: 편집 / 미리보기 / 검수 탭 + 창 안 튜토리얼
    public class FlexibleWindow : EditorWindow
    {
        public enum Tab
        {
            Edit,
            Preview,
            Check,
        }

        private static readonly string[] LanguageNames = { "Auto", "한국어", "日本語", "English" };

        [SerializeField] private Tab tab;
        [SerializeField] private FlexibleEditPanel edit = new FlexibleEditPanel();
        [SerializeField] private FlexiblePreviewPanel preview = new FlexiblePreviewPanel();
        [SerializeField] private FlexibleReviewPanel review = new FlexibleReviewPanel();
        private Vector2 scroll;
        private double nextTutorialCheck;
        private double nextAiRepaint;
        private string scrolledFor;
        private string lastHighlight;

        public Tab CurrentTab => tab;

        public FlexibleEditPanel EditPanel => edit;

        // 미리보기 탭에서 서로 다른 화면을 두 개 이상 본 적이 있는지 (튜토리얼 확인용)
        public bool PreviewSawTwoScreens => preview.SeenProfiles.Count >= 2;

        // 검수를 돌린 횟수 (튜토리얼 확인용)
        public int CheckRunCount => review.RunCount;

        [MenuItem("Tools/FlexibleUITool/Flexible UI Window", false, 0)]
        public static FlexibleWindow Open()
        {
            var window = GetWindow<FlexibleWindow>("Flexible UI");
            window.minSize = new Vector2(300f, 260f);
            return window;
        }

        [MenuItem("Tools/FlexibleUITool/Tutorial", false, 1)]
        public static void OpenTutorial()
        {
            FlexibleWindow window = Open();
            window.ShowTab(Tab.Edit);
            FlexibleTutorial.Start(window);
        }

        // CLI review: 검수 탭을 열고 바로 검수한다
        public static FlexibleReviewPanel OpenAndReview(string folderPath, string prefabPath)
        {
            FlexibleWindow window = Open();
            window.tab = Tab.Check;
            window.review.RunFor(folderPath, prefabPath);
            window.Show();
            window.Focus();
            window.Repaint();
            return window.review;
        }

        public void ShowTab(Tab value)
        {
            tab = value;
            Repaint();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Flexible UI", EditorGUIUtility.IconContent("RectTransformBlueprint").image);
            edit ??= new FlexibleEditPanel();
            preview ??= new FlexiblePreviewPanel();
            review ??= new FlexibleReviewPanel();
            edit.OnEnable();
            preview.OnEnable();

            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
            FlexibleScreenSession.Changed += OnSessionChanged;
            Undo.undoRedoPerformed += OnAnyChange;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.projectChanged += OnAnyChange;
            EditorApplication.update += OnUpdate;
            FlexibleText.Changed += Repaint;
            FlexibleClaudeCode.Changed += Repaint;
            FlexibleAiRun.Changed += OnAnyChange;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            FlexibleScreenSession.Changed -= OnSessionChanged;
            Undo.undoRedoPerformed -= OnAnyChange;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            EditorApplication.projectChanged -= OnAnyChange;
            EditorApplication.update -= OnUpdate;
            FlexibleText.Changed -= Repaint;
            FlexibleClaudeCode.Changed -= Repaint;
            FlexibleAiRun.Changed -= OnAnyChange;
            preview.OnDisable();
            review.OnDisable();
            FlexibleGui.Highlight = null;
        }

        private void OnSelectionChanged()
        {
            edit.OnSelectionChanged();
            OnAnyChange();
            SceneView.RepaintAll();
        }

        private void OnStageChanged(PrefabStage stage)
        {
            OnAnyChange();
            SceneView.RepaintAll();
        }

        private void OnSessionChanged()
        {
            OnAnyChange();
        }

        private void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            OnAnyChange();
        }

        private void OnAnyChange()
        {
            preview.MarkDirty();
            Repaint();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (tab == Tab.Edit)
                edit.OnSceneGUI(sceneView);
        }

        // 튜토리얼 진행 확인과 강조 테두리 깜빡임
        private void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            // AI 실행 중에는 경과 시간을 갱신한다
            if (FlexibleAiRun.Current == FlexibleAiRun.State.Running && now >= nextAiRepaint)
            {
                nextAiRepaint = now + 0.5;
                Repaint();
            }

            if (!FlexibleTutorial.Active)
                return;

            if (now < nextTutorialCheck)
                return;
            nextTutorialCheck = now + 0.1;

            if (tab != Tab.Preview)
                preview.SeenProfiles.Clear();

            FlexibleTutorial.Update(this);
            Repaint();
            // Scene 뷰 위 화면 버튼도 깜빡이게
            if (FlexibleGui.Highlight != null && FlexibleGui.Highlight.StartsWith("screen:"))
                SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
                FlexibleGui.ForgetMarked();
            DrawToolbar();
            FlexibleAiPanel.DrawStatus();
            FlexibleTutorial.Draw(this);

            switch (tab)
            {
                case Tab.Edit:
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    edit.Draw();
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.EndScrollView();
                    ScrollToHighlight();
                    break;
                case Tab.Preview:
                    preview.Draw();
                    break;
                case Tab.Check:
                    review.Draw();
                    break;
            }
        }

        // 튜토리얼이 가리키는 컨트롤이 보이는 영역 밖이면 그곳까지 한 번 스크롤한다 (그 뒤엔 사용자가 자유롭게 스크롤)
        private void ScrollToHighlight()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            string id = FlexibleGui.Highlight;
            if (id != lastHighlight)
            {
                lastHighlight = id;
                scrolledFor = null;
            }
            if (string.IsNullOrEmpty(id) || id.StartsWith("tab:") || id == scrolledFor || FlexibleGui.MarkedId != id)
                return;

            scrolledFor = id;
            float viewHeight = GUILayoutUtility.GetLastRect().height;
            Rect target = FlexibleGui.MarkedRect;
            if (target.yMin >= scroll.y && target.yMax <= scroll.y + viewHeight)
                return;
            scroll.y = Mathf.Max(0f, target.yMax - viewHeight + 24f);
            if (target.yMin < scroll.y)
                scroll.y = Mathf.Max(0f, target.yMin - 24f);
            Repaint();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string[] names = { T("Edit"), T("Preview"), T("Check") };
                for (int i = 0; i < names.Length; i++)
                {
                    if (GUILayout.Toggle(tab == (Tab)i, names[i], EditorStyles.toolbarButton, GUILayout.MinWidth(60f)) && tab != (Tab)i)
                    {
                        tab = (Tab)i;
                        GUI.FocusControl(null);
                    }
                    FlexibleGui.Mark("tab:" + i);
                }

                GUILayout.FlexibleSpace();
                if (!FlexibleTutorial.Active && GUILayout.Button(T("Tutorial"), EditorStyles.toolbarButton))
                    FlexibleTutorial.Start(this);

                EditorGUI.BeginChangeCheck();
                int index = EditorGUILayout.Popup((int)FlexibleText.Setting, LanguageNames, EditorStyles.toolbarPopup, GUILayout.Width(72f));
                if (EditorGUI.EndChangeCheck())
                    FlexibleText.Setting = (FlexibleLanguage)index;
            }
        }
    }
}
