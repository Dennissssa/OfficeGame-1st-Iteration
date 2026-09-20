using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[DisallowMultipleRendererFeature("CRT Television")]
public sealed class CRTRendererFeature : ScriptableRendererFeature
{
    public static bool SuppressForCapture;

    [SerializeField] Shader m_Shader;
    [SerializeField] RenderPassEvent m_InjectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    [SerializeField] bool m_ApplyInSceneView;
    [SerializeField] bool m_IgnoreOverlayCameras = true;
    [SerializeField] string[] m_IgnoredCameraNames = { "Camera 2", "Camera2" };

    Material m_Material;
    CRTPass m_Pass;

    public Shader ShaderAsset
    {
        get => m_Shader;
        set => m_Shader = value;
    }

    public override void Create()
    {
        if (m_Shader == null)
            m_Shader = Shader.Find("Hidden/Yeah/CRTTelevision");

        if (m_Shader != null)
        {
            if (m_Material != null)
                CoreUtils.Destroy(m_Material);
            m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
        }

        m_Pass = new CRTPass(m_Material);
        m_Pass.renderPassEvent = m_InjectionPoint;
        m_Pass.requiresIntermediateTexture = true;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (SuppressForCapture || m_Material == null || m_Pass == null)
            return;

        Camera camera = renderingData.cameraData.camera;
        if (ShouldIgnoreCamera(camera))
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;
        if (!m_ApplyInSceneView && cameraType != CameraType.Game)
            return;

        var stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        if (stack == null)
            return;

        CRTVolume volume = stack.GetComponent<CRTVolume>();
        if (volume == null || !volume.IsActive())
            return;

        m_Pass.Setup(volume);
        renderer.EnqueuePass(m_Pass);
    }

    bool ShouldIgnoreCamera(Camera camera)
    {
        if (camera == null)
            return true;

        if (camera.GetComponent<CRTIgnoreCamera>() != null)
            return true;

        string cameraName = camera.gameObject.name;
        if (ContainsIgnoreToken(cameraName, "Camera 2") || ContainsIgnoreToken(cameraName, "Camera2"))
            return true;

        if (m_IgnoredCameraNames != null)
        {
            for (int i = 0; i < m_IgnoredCameraNames.Length; i++)
            {
                if (ContainsIgnoreToken(cameraName, m_IgnoredCameraNames[i]))
                    return true;
            }
        }

        if (m_IgnoreOverlayCameras)
        {
            UniversalAdditionalCameraData extra = camera.GetUniversalAdditionalCameraData();
            if (extra != null && extra.renderType == CameraRenderType.Overlay)
                return true;
        }

        return false;
    }

    static bool ContainsIgnoreToken(string cameraName, string token)
    {
        return !string.IsNullOrEmpty(token) &&
               cameraName.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass = null;
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }

    public static void ApplyVolumeToMaterial(Material material, CRTVolume volume, int width, int height, float time)
    {
        if (material == null || volume == null)
            return;

        float aspect = height > 0 ? (float)width / height : 1f;
        material.SetVector("_CRTPacked0", new Vector4(volume.intensity.value, volume.edgeCurl.value, volume.curlX.value, volume.curlY.value));
        material.SetVector("_CRTPacked1", new Vector4(volume.cornerPinch.value, volume.overscan.value, volume.bezelRoundness.value, volume.bezelThickness.value));
        material.SetVector("_CRTPacked2", new Vector4(volume.bezelSoftness.value, volume.scanlineIntensity.value, volume.scanlineCount.value, volume.scanlineSharpness.value));
        material.SetVector("_CRTPacked3", new Vector4(volume.maskIntensity.value, volume.maskScale.value, (float)volume.maskType.value, volume.chromaticAberration.value));
        material.SetVector("_CRTPacked4", new Vector4(volume.chromaticEdgeBoost.value, volume.vignetteIntensity.value, volume.vignetteSmoothness.value, volume.brightness.value));
        material.SetVector("_CRTPacked5", new Vector4(volume.contrast.value, volume.saturation.value, volume.glow.value, volume.flicker.value));
        material.SetVector("_CRTPacked6", new Vector4(volume.noise.value, volume.wobble.value, volume.rollBar.value, time));
        material.SetVector("_CRTPacked7", new Vector4(aspect, 0f, 0f, 0f));
        material.SetVector("_CRTFlags0", new Vector4(
            volume.enableScanlines.value ? 1f : 0f,
            volume.enableMask.value ? 1f : 0f,
            volume.enableChromatic.value ? 1f : 0f,
            volume.enableVignette.value ? 1f : 0f));
        material.SetVector("_CRTFlags1", new Vector4(
            volume.enableFlicker.value ? 1f : 0f,
            volume.enableNoise.value ? 1f : 0f,
            volume.aspectCorrection.value ? 1f : 0f,
            0f));
        material.SetColor("_CRTBezelColor", volume.bezelColor.value);
        material.SetColor("_CRTPhosphorTint", volume.phosphorTint.value);
        material.SetVector("_CRTScreenParams", new Vector4(width, height, width > 0 ? 1f / width : 0f, height > 0 ? 1f / height : 0f));
    }

    sealed class CRTPass : ScriptableRenderPass
    {
        readonly Material m_Material;
        CRTVolume m_Volume;

        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("CRT Television");

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public CRTPass(Material material)
        {
            m_Material = material;
            profilingSampler = s_Sampler;
            requiresIntermediateTexture = true;
        }

        public void Setup(CRTVolume volume)
        {
            m_Volume = volume;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null || m_Volume == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_CRTTelevision";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            ApplyVolumeToMaterial(m_Material, m_Volume, width, height, Time.unscaledTime);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CRT Television", out var passData, s_Sampler))
            {
                passData.source = source;
                passData.material = m_Material;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc<PassData>(static (data, context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
