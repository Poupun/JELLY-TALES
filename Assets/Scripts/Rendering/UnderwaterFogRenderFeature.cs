using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Render Feature for underwater fog post-processing effect.
/// Applies screen-space depth fog when underwater for enhanced visual depth.
/// Professional implementation for Minecraft-style water rendering.
/// Updated to use modern RTHandle API instead of deprecated RenderTargetHandle.
/// </summary>
public class UnderwaterFogRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Shader")]
        public Shader underwaterFogShader;
    }

    public Settings settings = new Settings();
    private UnderwaterFogRenderPass renderPass;
    private Material underwaterFogMaterial;

    public override void Create()
    {
        // Create material from shader
        if (settings.underwaterFogShader != null)
        {
            underwaterFogMaterial = new Material(settings.underwaterFogShader);
        }
        else
        {
            // Try to find shader by name
            var shader = Shader.Find("Hidden/UnderwaterFog");
            if (shader != null)
            {
                underwaterFogMaterial = new Material(shader);
            }
        }

        // Create render pass
        renderPass = new UnderwaterFogRenderPass(underwaterFogMaterial)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderPass == null || underwaterFogMaterial == null) return;

        // Only add pass if underwater fog is enabled
        float underwaterEnabled = Shader.GetGlobalFloat("_UnderwaterFogEnabled");
        if (underwaterEnabled > 0.5f)
        {
            renderPass.Setup(renderer.cameraColorTargetHandle);
            renderer.EnqueuePass(renderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            renderPass?.Dispose();

            if (underwaterFogMaterial != null)
            {
                CoreUtils.Destroy(underwaterFogMaterial);
            }
        }
    }

    /// <summary>
    /// Render pass that applies underwater fog effect using modern RTHandle API
    /// </summary>
    class UnderwaterFogRenderPass : ScriptableRenderPass
    {
        private Material material;
        private RTHandle source;
        private RTHandle tempTextureHandle;

        private static readonly int TempTextureID = Shader.PropertyToID("_TempUnderwaterFogTexture");

        public UnderwaterFogRenderPass(Material material)
        {
            this.material = material;
        }

        public void Setup(RTHandle source)
        {
            this.source = source;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Configure render texture using modern RTHandle API
            var descriptor = cameraTextureDescriptor;
            descriptor.depthBufferBits = 0; // No depth buffer needed for this pass

            RenderingUtils.ReAllocateIfNeeded(ref tempTextureHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempUnderwaterFogTexture");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || source == null || tempTextureHandle == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("UnderwaterFogPass");

            // Blit with underwater fog material using modern Blitter API
            Blitter.BlitCameraTexture(cmd, source, tempTextureHandle, material, 0);
            Blitter.BlitCameraTexture(cmd, tempTextureHandle, source);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            // RTHandles are managed automatically in modern URP
        }

        public void Dispose()
        {
            tempTextureHandle?.Release();
        }
    }
}