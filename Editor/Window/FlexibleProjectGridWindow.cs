using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // 프로젝트 그리드: 화면마다 하나, 전용 그리드를 켜지 않은 모든 프리팹이 쓴다
    public class FlexibleProjectGridWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/FlexibleUITool/Project Grids", false, 1)]
        public static void Open()
        {
            var window = GetWindow<FlexibleProjectGridWindow>();
            window.titleContent = new GUIContent(T("Project grids"));
            window.minSize = new Vector2(340f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            FlexibleText.Changed += Repaint;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            FlexibleText.Changed -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnGUI()
        {
            titleContent.text = T("Project grids");
            FlexibleProfileSettings settings = FlexibleSettingsUtility.GetOrCreateDefault();
            if (settings == null)
                return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Label(T("Each screen has its own grid. Every prefab that does not use its own grid uses these, so a change here applies to all of them. Blocks already placed do not move until you re-place them."), FlexibleGui.WrapLabel);
            EditorGUILayout.Space(4f);

            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                FlexibleProfile profile = settings.Profiles[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(FlexibleEditPanel.Name(profile), EditorStyles.boldLabel);
                    FlexibleGrid grid = profile.grid;
                    if (FlexibleGui.GridFields(ref grid))
                    {
                        Undo.RecordObject(settings, "Flexible Project Grid");
                        profile.grid = grid;
                        EditorUtility.SetDirty(settings);
                        // 프로젝트 공용 설정 파일이므로 바뀌는 즉시 디스크에 쓴다
                        AssetDatabase.SaveAssetIfDirty(settings);
                        SceneView.RepaintAll();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
