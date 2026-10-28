using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // Scene 뷰 위의 화면 전환 버튼. 화면 전환을 쓸 수 있는 프리팹을 열었을 때만 보인다
    [Overlay(typeof(SceneView), "flexible-ui-screens", "Flexible UI", true)]
    public class FlexibleScreenOverlay : IMGUIOverlay
    {
        public override void OnCreated()
        {
            base.OnCreated();
            PrefabStage.prefabStageOpened += OnStage;
            PrefabStage.prefabStageClosing += OnStage;
            FlexibleScreenSession.Changed += Refresh;
            Refresh();
        }

        public override void OnWillBeDestroyed()
        {
            PrefabStage.prefabStageOpened -= OnStage;
            PrefabStage.prefabStageClosing -= OnStage;
            FlexibleScreenSession.Changed -= Refresh;
            base.OnWillBeDestroyed();
        }

        private void OnStage(PrefabStage stage)
        {
            EditorApplication.delayCall += Refresh;
        }

        private void Refresh()
        {
            displayed = FlexibleScreenSession.IsReady(PrefabStageUtility.GetCurrentPrefabStage(), out _);
        }

        public override void OnGUI()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!FlexibleScreenSession.IsReady(stage, out FlexibleLayout layout))
            {
                GUILayout.Label(T("Open a UI prefab"), EditorStyles.miniLabel);
                // 만들어질 때 스테이지를 아직 몰랐으면 여기서 숨긴다
                EditorApplication.delayCall += Refresh;
                return;
            }

            using (new GUILayout.VerticalScope(GUILayout.Width(250f)))
            {
                FlexibleEditPanel.DrawScreenButtons(stage, layout, 34f);
                using (new GUILayout.HorizontalScope())
                {
                    if (stage.scene.isDirty)
                        GUILayout.Label(T("● Unsaved"), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(T("Save"), EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        FlexibleScreenSession.Save(stage);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(T("Window"), EditorStyles.miniButton, GUILayout.Width(60f)))
                        FlexibleWindow.Open();
                }
            }
        }
    }
}
