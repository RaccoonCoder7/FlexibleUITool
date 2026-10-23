using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

using static FlexibleUITool.EditorTools.FlexibleText;
namespace FlexibleUITool.EditorTools
{
    // Game 뷰 해상도 선택 (유니티 내부 API를 리플렉션으로 호출, 버전이 달라 실패하면 아무것도 하지 않는다)
    public static class FlexibleGameViewUtility
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Assembly EditorAssembly = typeof(UnityEditor.Editor).Assembly;
        private static readonly Type GameViewType = EditorAssembly.GetType("UnityEditor.GameView");
        private static readonly Type SizesType = EditorAssembly.GetType("UnityEditor.GameViewSizes");
        private static readonly Type SizeType = EditorAssembly.GetType("UnityEditor.GameViewSize");
        private static readonly Type SizeKindType = EditorAssembly.GetType("UnityEditor.GameViewSizeType");

        public static bool TryGetSelectedIndex(out int index)
        {
            index = -1;
            try
            {
                EditorWindow gameView = FindGameView();
                if (gameView == null)
                    return false;

                index = (int)GameViewType.GetProperty("selectedSizeIndex", Flags).GetValue(gameView);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("[FlexibleUITool] Could not read the Game view resolution: {0}", e.Message));
                return false;
            }
        }

        public static bool TrySelectIndex(int index)
        {
            try
            {
                EditorWindow gameView = FindGameView();
                if (gameView == null || index < 0)
                    return false;

                GameViewType.GetMethod("SizeSelectionCallback", Flags).Invoke(gameView, new object[] { index, null });
                gameView.Repaint();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("[FlexibleUITool] Could not change the Game view resolution: {0}", e.Message));
                return false;
            }
        }

        // 같은 크기의 고정 해상도가 목록에 없으면 추가한 뒤 선택한다
        public static bool TrySelectResolution(int width, int height, string label)
        {
            try
            {
                object group = GetCurrentGroup(out object sizes);
                int count = (int)group.GetType().GetMethod("GetTotalCount", Flags).Invoke(group, null);
                MethodInfo getSize = group.GetType().GetMethod("GetGameViewSize", Flags);
                for (int i = 0; i < count; i++)
                {
                    object size = getSize.Invoke(group, new object[] { i });
                    if (IsFixed(size) && GetInt(size, "width") == width && GetInt(size, "height") == height)
                        return TrySelectIndex(i);
                }

                object fixedKind = Enum.Parse(SizeKindType, "FixedResolution");
                object newSize = Activator.CreateInstance(SizeType, fixedKind, width, height, label);
                group.GetType().GetMethod("AddCustomSize", Flags).Invoke(group, new[] { newSize });
                SizesType.GetMethod("SaveToHDD", Flags).Invoke(sizes, null);
                return TrySelectIndex(count);
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("[FlexibleUITool] Could not change the Game view resolution: {0}", e.Message));
                return false;
            }
        }

        // Game 뷰가 마지막으로 그린 화면을 PNG로 저장한다 (Unity가 뒤에 있어 플레이가 멈춰 있어도 다시 그린 화면을 읽는다)
        public static bool TryCapture(string file, out Vector2Int size)
        {
            size = default;
            try
            {
                EditorWindow gameView = FindGameView();
                if (gameView == null)
                    return false;

                var texture = EditorAssembly.GetType("UnityEditor.PlayModeView").GetField("m_TargetTexture", Flags).GetValue(gameView) as RenderTexture;
                if (texture == null || !texture.IsCreated())
                    return false;

                RenderTexture previous = RenderTexture.active;
                var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = texture;
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    image.Apply();
                    // D3D·Metal은 렌더 텍스처가 위아래 뒤집혀 있다
                    if (SystemInfo.graphicsUVStartsAtTop)
                        FlipVertically(image);

                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    System.IO.File.WriteAllBytes(file, image.EncodeToPNG());
                    size = new Vector2Int(texture.width, texture.height);
                    return true;
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(F("[FlexibleUITool] Could not capture the Game view: {0}", e.Message));
                return false;
            }
        }

        private static void FlipVertically(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            int width = image.width;
            int height = image.height;
            var row = new Color32[width];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * width;
                int bottom = (height - 1 - y) * width;
                Array.Copy(pixels, top, row, 0, width);
                Array.Copy(pixels, bottom, pixels, top, width);
                Array.Copy(row, 0, pixels, bottom, width);
            }
            image.SetPixels32(pixels);
            image.Apply();
        }

        private static EditorWindow FindGameView()
        {
            UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(GameViewType);
            return views.Length > 0 ? (EditorWindow)views[0] : null;
        }

        private static object GetCurrentGroup(out object sizes)
        {
            Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(SizesType);
            sizes = singleton.GetProperty("instance", Flags).GetValue(null);
            return SizesType.GetProperty("currentGroup", Flags).GetValue(sizes);
        }

        private static bool IsFixed(object size)
        {
            return SizeType.GetProperty("sizeType", Flags).GetValue(size).ToString() == "FixedResolution";
        }

        private static int GetInt(object size, string property)
        {
            return (int)SizeType.GetProperty(property, Flags).GetValue(size);
        }
    }
}
