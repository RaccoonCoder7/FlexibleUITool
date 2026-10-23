using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;

namespace FlexibleUITool.EditorTools
{
    // 미리보기를 그리는 동안만 SRP 렌더러 기능(CRT·비네트 등 게임 화면 효과)을 끄고 바로 되돌린다 (URP 직접 참조 없이 리플렉션)
    public sealed class FlexibleRendererFeatureGuard : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<object> disabled = new List<object>();

        public FlexibleRendererFeatureGuard()
        {
            try
            {
                foreach (object feature in EnumerateFeatures())
                {
                    if (feature.GetType().GetProperty("isActive", Flags)?.GetValue(feature) is bool active && active)
                    {
                        SetActive(feature, false);
                        disabled.Add(feature);
                    }
                }
            }
            catch (Exception)
            {
                // 파이프라인 구조가 달라 찾지 못하면 효과가 섞인 채로 그린다
                Dispose();
            }
        }

        public void Dispose()
        {
            foreach (object feature in disabled)
                SetActive(feature, true);
            disabled.Clear();
        }

        private static IEnumerable<object> EnumerateFeatures()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                yield break;

            if (!(pipeline.GetType().GetField("m_RendererDataList", Flags)?.GetValue(pipeline) is IEnumerable rendererDataList))
                yield break;

            foreach (object rendererData in rendererDataList)
            {
                if (rendererData == null)
                    continue;
                if (!(rendererData.GetType().GetProperty("rendererFeatures", Flags)?.GetValue(rendererData) is IEnumerable features))
                    continue;

                foreach (object feature in features)
                {
                    if (feature != null)
                        yield return feature;
                }
            }
        }

        private static void SetActive(object feature, bool active)
        {
            feature.GetType().GetMethod("SetActive", Flags, null, new[] { typeof(bool) }, null)?.Invoke(feature, new object[] { active });
        }
    }
}
