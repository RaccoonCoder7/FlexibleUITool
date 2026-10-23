using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // FlexibleCanvasScaler 인스펙터: 동작에 영향을 주는 기준 해상도만 보여 준다
    [CustomEditor(typeof(FlexibleCanvasScaler))]
    public class FlexibleCanvasScalerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty reference = serializedObject.FindProperty("m_ReferenceResolution");
            EditorGUILayout.PropertyField(reference, new GUIContent("Reference Resolution"));
            serializedObject.ApplyModifiedProperties();

            Vector2 value = reference.vector2Value;
            float shortSide = Mathf.Min(value.x, value.y);
            EditorGUILayout.HelpBox(F("Scales so that the screen's short side is {0:0} units. UI has the same real size in portrait and landscape.", shortSide), MessageType.None);

            var scaler = (FlexibleCanvasScaler)target;
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
                EditorGUILayout.HelpBox(T("World Space canvases are not scaled."), MessageType.Info);
        }
    }
}
