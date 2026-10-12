using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace shaderscripts
{
    /// <summary>
    /// URP 轮廓高亮渲染特性 (Unity 6 兼容：优化属性查找与严格代码规范)
    /// </summary>
    public class OutlineRendererFeature : ScriptableRendererFeature
    {
        [Header("描边渲染配置")]
        [Tooltip("目标层级：需要进行高亮描边的物体 LayerMask")]
        public LayerMask targetLayer;

        [Tooltip("描边材质：使用 Custom/SelectionOutline Shader 生成的材质")]
        public Material outlineMaterial;

        [Tooltip("【极其重要】遮罩基础 Shader：为了防止打包丢失，请在这里填入 Universal Render Pipeline/Unlit")]
        public Shader maskShader; // 硬引用防止打包剔除

        private CustomRenderPass scriptablePass;
        private Material maskMaterial;

        // 解决警告：基于字符串的属性查找低效，预先获取 Property ID
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public override void Create()
        {
            scriptablePass = new CustomRenderPass();
            scriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            EnsureMaskMaterial();
        }

        private void EnsureMaskMaterial()
        {
            if (maskMaterial == null)
            {
                Shader unlitShader = maskShader;
                
                // 解决警告：移除未解析的隐藏着色器
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

                if (unlitShader != null)
                {
                    maskMaterial = new Material(unlitShader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    if (maskMaterial.HasProperty(BaseColorId))
                        maskMaterial.SetColor(BaseColorId, Color.white);
                    if (maskMaterial.HasProperty(ColorId))
                        maskMaterial.SetColor(ColorId, Color.white);
                    if (maskMaterial.HasProperty(BaseMapId))
                        maskMaterial.SetTexture(BaseMapId, Texture2D.whiteTexture);
                }
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (outlineMaterial == null) return;
            if (targetLayer.value == 0) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview || renderingData.cameraData.cameraType == CameraType.Reflection) return;

            EnsureMaskMaterial();
            if (maskMaterial == null) return;

            if (maskMaterial.HasProperty(BaseColorId))
                maskMaterial.SetColor(BaseColorId, Color.white);
            if (maskMaterial.HasProperty(ColorId))
                maskMaterial.SetColor(ColorId, Color.white);

            scriptablePass.Setup(targetLayer, outlineMaterial, maskMaterial);
            renderer.EnqueuePass(scriptablePass);
        }

        protected override void Dispose(bool disposing)
        {
            scriptablePass?.Dispose();
            if (maskMaterial != null)
            {
                CoreUtils.Destroy(maskMaterial);
                maskMaterial = null;
            }
        }

        // 解决警告：Class 可被设为 private
        private class CustomRenderPass : ScriptableRenderPass
        {
            // 解决警告：Instance fields 命名规范匹配
            private LayerMask targetLayer;
            private Material outlineMaterial;
            private Material maskMaterial;
            private RTHandle maskRT;

            // 解决警告：使用对象初始值设定项
            private readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("LightweightForward"),
                new ShaderTagId("SRPDefaultUnlit")
            };

            private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

            public CustomRenderPass()
            {
                profilingSampler = new ProfilingSampler("OutlineRendererFeaturePass");
            }

            public void Setup(LayerMask targetLayerParam, Material outlineMatParam, Material maskMatParam)
            {
                targetLayer = targetLayerParam;
                outlineMaterial = outlineMatParam;
                maskMaterial = maskMatParam;
            }

            // 解决警告：Method 可被设为 private
            private void Configure(RenderTextureDescriptor cameraTextureDescriptor)
            {
                RenderTextureDescriptor maskDesc = cameraTextureDescriptor;
                maskDesc.colorFormat = RenderTextureFormat.ARGB32;
                maskDesc.depthBufferBits = 0;
                maskDesc.msaaSamples = 1;

                RenderingUtils.ReAllocateHandleIfNeeded(
                    ref maskRT,
                    maskDesc,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "_OutlineMaskRT"
                );
            }

            public void Dispose()
            {
                maskRT?.Release();
                maskRT = null;
            }

            // 解决警告：字段命名规则为大写驼峰
            private class MaskPassData
            {
                public RendererListHandle RendererList;
            }

            private class CopyPassData
            {
                public TextureHandle SourceColor;
            }

            private class CompositePassData
            {
                public Material Material;
                public TextureHandle SourceColor;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (outlineMaterial == null || maskMaterial == null) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                if (!resourceData.cameraColor.IsValid()) return;

                Configure(cameraData.cameraTargetDescriptor);
                TextureHandle maskTexture = renderGraph.ImportTexture(maskRT);

                FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, targetLayer);
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    shaderTagIdList,
                    renderingData,
                    cameraData,
                    lightData,
                    cameraData.defaultOpaqueSortFlags
                );
                drawingSettings.overrideMaterial = maskMaterial;
                drawingSettings.overrideMaterialPassIndex = 0;

                RendererListParams rlParams = new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );
                RendererListHandle maskRendererList = renderGraph.CreateRendererList(rlParams);

                // Pass 1: 渲染遮罩 Mask
                using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Render Mask", out var passData, profilingSampler))
                {
                    passData.RendererList = maskRendererList;
                    
                    // 解决警告：形参具有相同的默认值，去掉了多余的 AccessFlags.Write
                    builder.SetRenderAttachment(maskTexture, 0);

                    if (resourceData.activeDepthTexture.IsValid())
                    {
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                    }

                    builder.UseRendererList(maskRendererList);
                    builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, Color.clear);
                        context.cmd.DrawRendererList(data.RendererList);
                    });
                }

                var copyColorDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
                copyColorDesc.name = "_OutlineSourceColorTex";
                copyColorDesc.clearBuffer = false;
                TextureHandle copiedColorTexture = renderGraph.CreateTexture(copyColorDesc);

                // Pass 2: 解决找不到 AddBlitPass 的致命错误！手动构建 Copy Pass
                using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Outline Copy Camera Color", out var passData, profilingSampler))
                {
                    passData.SourceColor = resourceData.activeColorTexture;
                    builder.UseTexture(passData.SourceColor, AccessFlags.Read);
                    builder.SetRenderAttachment(copiedColorTexture, 0);

                    builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                    {
                        // 使用底层 Blitter 接口完美平替 AddBlitPass
                        Blitter.BlitTexture(context.cmd, data.SourceColor, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }

                // Pass 3: 屏幕后处理混合
                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Outline Composite", out var passData, profilingSampler))
                {
                    passData.Material = outlineMaterial;
                    passData.SourceColor = copiedColorTexture;

                    builder.UseTexture(copiedColorTexture, AccessFlags.Read);
                    builder.UseTexture(maskTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                    RTHandle maskRTHandle = maskRT;

                    builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
                    {
                        data.Material.SetTexture(MaskTexId, maskRTHandle);
                        Blitter.BlitTexture(context.cmd, data.SourceColor, new Vector4(1, 1, 0, 0), data.Material, 0);
                    });
                }
            }
        }
    }
}