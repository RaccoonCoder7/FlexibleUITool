using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FlexibleUITool.EditorTools
{
    // 미리보기·검수가 글자를 그리며 동적 폰트(TMP)에 글자를 채워 넣어도, 그 때문에 폰트 파일이 저장 대상이 되지 않게 한다 (TMP 직접 참조 없이 리플렉션)
    public sealed class FlexibleFontDirtyGuard : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        private static Type fontType;
        private static bool fontTypeSearched;
        private readonly List<UnityEngine.Object> clean = new List<UnityEngine.Object>();

        public FlexibleFontDirtyGuard()
        {
            Type type = FontType();
            if (type == null)
                return;
            foreach (UnityEngine.Object font in Resources.FindObjectsOfTypeAll(type))
            {
                if (!EditorUtility.IsPersistent(font))
                    continue;
                foreach (UnityEngine.Object part in Parts(font))
                {
                    if (!EditorUtility.IsDirty(part))
                        clean.Add(part);
                }
            }
        }

        public void Dispose()
        {
            foreach (UnityEngine.Object part in clean)
            {
                if (part != null && EditorUtility.IsDirty(part))
                    EditorUtility.ClearDirty(part);
            }
        }

        // 폰트 애셋과 함께 바뀌는 아틀라스 텍스처·머티리얼
        private static IEnumerable<UnityEngine.Object> Parts(UnityEngine.Object font)
        {
            yield return font;
            if (font.GetType().GetProperty("atlasTextures", Flags)?.GetValue(font) is Texture2D[] textures)
            {
                foreach (Texture2D texture in textures)
                {
                    if (texture != null && EditorUtility.IsPersistent(texture))
                        yield return texture;
                }
            }
            if (font.GetType().GetProperty("material", Flags)?.GetValue(font) is Material material && EditorUtility.IsPersistent(material))
                yield return material;
        }

        private static Type FontType()
        {
            if (fontTypeSearched)
                return fontType;
            fontTypeSearched = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                fontType = assembly.GetType("TMPro.TMP_FontAsset");
                if (fontType != null)
                    break;
            }
            return fontType;
        }
    }
}
