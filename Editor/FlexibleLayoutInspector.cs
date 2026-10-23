using System.Linq;
using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // FlexibleLayout 인스펙터: 프로필별 기록 수 요약과 창 열기
    [CustomEditor(typeof(FlexibleLayout))]
    public class FlexibleLayoutInspector : UnityEditor.Editor
    {
        private bool showEntries;

        public override void OnInspectorGUI()
        {
            var layout = (FlexibleLayout)target;
            DrawFitCheck(layout);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("settings"));

            if (layout.Settings != null)
            {
                foreach (FlexibleProfile profile in layout.Settings.Profiles)
                {
                    string text = profile.id == layout.Settings.BaseProfileId
                        ? T("Base (the prefab itself)")
                        : F("{0} records", layout.Entries.Count(e => e.Find(profile.id) != null));
                    EditorGUILayout.LabelField(Label(profile), text);
                }
            }

            if (Application.isPlaying)
                EditorGUILayout.LabelField(T("Current profile"), Label(layout.CurrentProfile));

            showEntries = EditorGUILayout.Foldout(showEntries, F("Record data ({0})", layout.Entries.Count), true);
            if (showEntries)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("entries"), true);
            }

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button(T("Open Flexible Layout window")))
                FlexibleLayoutWindow.Open();
        }

        // 프리팹을 씬에 놓으면 Scene 뷰 위치가 오프셋·Z로 들어가 화면에서 밀린다 (루트 위치는 Revert 대상이 아님)
        private static void DrawFitCheck(FlexibleLayout layout)
        {
            var rt = (RectTransform)layout.transform;
            if (rt.parent == null || !IsFullStretch(rt) || IsFitted(rt))
                return;

            EditorGUILayout.HelpBox(F("Not fitted to the parent (offsets {0} / {1}, Z {2:0}). This usually happens when the prefab is dropped into a scene at the Scene view position.", rt.offsetMin, rt.offsetMax, rt.localPosition.z), MessageType.Warning);
            if (GUILayout.Button(T("Fit to parent (offsets 0, Z 0)")))
                FitToParent(rt);
        }

        public static void FitToParent(RectTransform rt)
        {
            Undo.RecordObject(rt, "Flexible Fit To Parent");
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
            if (PrefabUtility.IsPartOfPrefabInstance(rt))
                PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
        }

        private static bool IsFullStretch(RectTransform rt)
        {
            return rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
        }

        private static bool IsFitted(RectTransform rt)
        {
            return rt.offsetMin.sqrMagnitude < 0.0001f && rt.offsetMax.sqrMagnitude < 0.0001f && Mathf.Abs(rt.localPosition.z) < 0.001f;
        }
    }
}
