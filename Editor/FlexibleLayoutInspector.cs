using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
            DrawProfileSource(serializedObject.FindProperty("profileSource"));

            if (layout.Settings != null)
            {
                foreach (FlexibleProfile profile in layout.Settings.Profiles)
                {
                    string text = profile.id == layout.Settings.BaseProfileId
                        ? T("Base (the prefab itself)")
                        : F("{0} changed", layout.Entries.Count(e => e.Find(profile.id) != null));
                    EditorGUILayout.LabelField(Label(profile), text);
                }
            }

            if (Application.isPlaying)
                EditorGUILayout.LabelField(T("Current screen"), Label(layout.CurrentProfile));

            showEntries = EditorGUILayout.Foldout(showEntries, F("Record data ({0})", layout.Entries.Count), true);
            if (showEntries)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("entries"), true);
            }

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button(T("Open the Flexible UI window")))
                FlexibleWindow.Open();
        }

        // 블록이 화면(root) 밖으로 나가면 들어올 때까지 같은 비율로 줄이고, 그래도 걸치면 안쪽으로 민다. 바꿨으면 true
        public static bool FitToScreen(RectTransform root, RectTransform target, float margin = 16f)
        {
            Rect screen = root.rect;
            screen = Rect.MinMaxRect(screen.xMin + margin, screen.yMin + margin, screen.xMax - margin, screen.yMax - margin);
            Rect r = FlexiblePreviewRenderer.GetRectInRoot(root, target);
            if (r.xMin >= screen.xMin && r.xMax <= screen.xMax && r.yMin >= screen.yMin && r.yMax <= screen.yMax)
                return false;

            Undo.RecordObject(target, "Flexible Fit To Screen");
            float shrink = Mathf.Min(1f, screen.width / Mathf.Max(1f, r.width), screen.height / Mathf.Max(1f, r.height));
            if (shrink < 1f)
            {
                Vector3 scale = target.localScale;
                target.localScale = new Vector3(scale.x * shrink, scale.y * shrink, scale.z);
                r = FlexiblePreviewRenderer.GetRectInRoot(root, target);
            }

            var delta = Vector2.zero;
            if (r.xMin < screen.xMin) delta.x = screen.xMin - r.xMin;
            else if (r.xMax > screen.xMax) delta.x = screen.xMax - r.xMax;
            if (r.yMin < screen.yMin) delta.y = screen.yMin - r.yMin;
            else if (r.yMax > screen.yMax) delta.y = screen.yMax - r.yMax;
            if (delta != Vector2.zero && target.parent != null)
            {
                Vector3 world = root.TransformVector(delta);
                target.anchoredPosition += (Vector2)target.parent.InverseTransformVector(world);
            }
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            return true;
        }

        // 최상위 캔버스의 CanvasScaler를 FlexibleCanvasScaler로 바꾼다 (기준 해상도는 그대로). 바꿨으면 true
        public static bool UseFlexibleScaler(Canvas canvas, bool undo = true)
        {
            CanvasScaler old = canvas.GetComponent<CanvasScaler>();
            if (old is FlexibleCanvasScaler)
                return false;

            Vector2 reference = old != null && old.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize ? old.referenceResolution : new Vector2(1080f, 1920f);
            float pixelsPerUnit = old != null ? old.referencePixelsPerUnit : 100f;
            // 기준(세로) 화면이 지금과 똑같이 보이도록 원래 규칙을 넘겨준다 (없애기 전에 읽는다)
            System.Action<FlexibleCanvasScaler> keepRule = old != null ? FlexibleCanvasScaler.CaptureBaseRule(old, FlexibleSettingsUtility.GetOrCreateDefault()) : null;
            if (old != null)
            {
                if (undo)
                    Undo.DestroyObjectImmediate(old);
                else
                    DestroyImmediate(old);
            }
            var scaler = undo ? Undo.AddComponent<FlexibleCanvasScaler>(canvas.gameObject) : canvas.gameObject.AddComponent<FlexibleCanvasScaler>();
            scaler.referenceResolution = reference;
            scaler.referencePixelsPerUnit = pixelsPerUnit;
            keepRule?.Invoke(scaler);
            EditorUtility.SetDirty(scaler);
            return true;
        }

        // 열린 씬에서 아직 일반 CanvasScaler를 쓰는 최상위 캔버스 (꺼진 것 포함, 월드 공간 캔버스는 화면 크기와 무관해 뺀다)
        public static List<Canvas> FindCanvasesToConvert()
        {
            var result = new List<Canvas>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                    {
                        if (canvas.renderMode == RenderMode.WorldSpace || canvas.GetComponentsInParent<Canvas>(true).Length > 1)
                            continue;
                        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                        if (scaler != null && !(scaler is FlexibleCanvasScaler))
                            result.Add(canvas);
                    }
                }
            }
            return result;
        }

        // 바꾼다. 프리팹에 들어 있는 스케일러는 그 프리팹 파일을 열어 바꾸고 저장한다 (씬의 인스턴스는 따라 바뀐다).
        // 바꾸기 전에 저장돼 있던 씬만 저장하고, 다른 미저장 작업 때문에 저장하지 않은 씬이 있으면 true
        public static bool ConvertCanvases(List<Canvas> canvases)
        {
            var inScene = new List<Canvas>();
            var prefabs = new Dictionary<string, CanvasScaler>();
            foreach (Canvas canvas in canvases)
            {
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (PrefabUtility.IsPartOfPrefabInstance(scaler))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(scaler);
                    string path = AssetDatabase.GetAssetPath(source);
                    if (!string.IsNullOrEmpty(path) && !prefabs.ContainsKey(path))
                        prefabs[path] = source;
                }
                else
                    inScene.Add(canvas);
            }

            var scenes = new HashSet<Scene>();
            foreach (Canvas canvas in inScene)
            {
                bool wasDirty = canvas.gameObject.scene.isDirty;
                if (UseFlexibleScaler(canvas) && !wasDirty)
                    scenes.Add(canvas.gameObject.scene);
                else
                    EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            }
            foreach (KeyValuePair<string, CanvasScaler> prefab in prefabs)
                ConvertInPrefab(prefab.Key, prefab.Value);

            // 바꾸기 전에 저장돼 있던 씬만 저장한다 (다른 미저장 작업을 같이 저장하지 않도록)
            bool unsaved = inScene.Any(c => c.gameObject.scene.isDirty && !scenes.Contains(c.gameObject.scene));
            foreach (Scene scene in scenes)
                EditorSceneManager.SaveScene(scene);
            return unsaved;
        }

        // 확인창용 이름: 씬 오브젝트는 이름, 프리팹에 들어 있으면 프리팹 파일 이름
        public static string CanvasLabel(Canvas canvas)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null && PrefabUtility.IsPartOfPrefabInstance(scaler))
                return System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(scaler))) + " (prefab)";
            return canvas.name;
        }

        private static void ConvertInPrefab(string path, CanvasScaler sourceScaler)
        {
            Transform sourceRoot = sourceScaler.transform.root;
            string relative = AnimationUtility.CalculateTransformPath(sourceScaler.transform, sourceRoot);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform target = string.IsNullOrEmpty(relative) ? contents.transform : contents.transform.Find(relative);
                Canvas canvas = target != null ? target.GetComponent<Canvas>() : null;
                if (canvas != null && UseFlexibleScaler(canvas, false))
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // 화면(세로·정사각·가로)을 무엇의 비율로 고를지: 놓인 화면(기본) / 자기 크기(패널용)
        public static void DrawProfileSource(SerializedProperty property)
        {
            string[] options = { T("The screen it is shown on"), T("Its own size (for panels)") };
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup(new GUIContent(T("Choose the screen by"),
                T("The screen: portrait / square / landscape follow the whole screen, even for a small panel inside another prefab. Its own size: they follow this object's own width and height.")),
                property.enumValueIndex, options);
            if (EditorGUI.EndChangeCheck())
                property.enumValueIndex = index;
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
