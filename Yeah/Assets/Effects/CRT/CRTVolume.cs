using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
[VolumeComponentMenu("Post-processing/CRT Television")]
[VolumeRequiresRendererFeatures(typeof(CRTRendererFeature))]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
[DisplayInfo(name = "CRT Television")]
public sealed class CRTVolume : VolumeComponent
{
    [Header("Master")]
    [InspectorName("启用效果")]
    [Tooltip("关闭后完全跳过 CRT 通道，无额外开销。")]
    public BoolParameter enableEffect = new BoolParameter(true, true);

    [InspectorName("整体强度")]
    [Tooltip("扫描线、掩模、色散等附加效果的强度。画面卷曲始终直接作用在画面上，不会和原图混合。")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f, true);

    [Header("Geometry")]
    [InspectorName("边缘卷曲")]
    [Tooltip("CRT 玻壳桶形畸变，边缘向内卷曲。")]
    public ClampedFloatParameter edgeCurl = new ClampedFloatParameter(0.22f, 0f, 1f, true);

    [InspectorName("水平卷曲")]
    public ClampedFloatParameter curlX = new ClampedFloatParameter(1f, 0f, 1.5f, true);

    [InspectorName("垂直卷曲")]
    public ClampedFloatParameter curlY = new ClampedFloatParameter(1.15f, 0f, 1.5f, true);

    [InspectorName("边角挤压")]
    [Tooltip("四角额外向内收，模拟显像管边角。")]
    public ClampedFloatParameter cornerPinch = new ClampedFloatParameter(0.12f, 0f, 1f, true);

    [InspectorName("过扫描")]
    [Tooltip("略微放大画面，裁掉卷曲后的黑边。")]
    public ClampedFloatParameter overscan = new ClampedFloatParameter(0.02f, 0f, 0.2f, true);

    [InspectorName("按画面比例校正")]
    public BoolParameter aspectCorrection = new BoolParameter(true, true);

    [InspectorName("圆角边框")]
    public ClampedFloatParameter bezelRoundness = new ClampedFloatParameter(0.08f, 0f, 0.4f, true);

    [InspectorName("边框厚度")]
    public ClampedFloatParameter bezelThickness = new ClampedFloatParameter(0.03f, 0f, 0.2f, true);

    [InspectorName("边框软边")]
    public ClampedFloatParameter bezelSoftness = new ClampedFloatParameter(0.04f, 0.001f, 0.2f, true);

    [InspectorName("边框颜色")]
    public ColorParameter bezelColor = new ColorParameter(new Color(0.02f, 0.02f, 0.025f, 1f), false, false, true, true);

    [Header("Scanlines")]
    [InspectorName("扫描线")]
    public BoolParameter enableScanlines = new BoolParameter(true, true);

    [InspectorName("扫描线强度")]
    public ClampedFloatParameter scanlineIntensity = new ClampedFloatParameter(0.42f, 0f, 1f, true);

    [InspectorName("扫描线数量")]
    public ClampedFloatParameter scanlineCount = new ClampedFloatParameter(280f, 60f, 720f, true);

    [InspectorName("扫描线锐度")]
    public ClampedFloatParameter scanlineSharpness = new ClampedFloatParameter(1.4f, 0.2f, 4f, true);

    [Header("Phosphor Mask")]
    [InspectorName("磷光掩模")]
    public BoolParameter enableMask = new BoolParameter(true, true);

    [InspectorName("掩模类型")]
    public MaskTypeParameter maskType = new MaskTypeParameter(CRTMaskType.ApertureGrille, true);

    [InspectorName("掩模强度")]
    public ClampedFloatParameter maskIntensity = new ClampedFloatParameter(0.35f, 0f, 1f, true);

    [InspectorName("掩模尺度(像素)")]
    public ClampedFloatParameter maskScale = new ClampedFloatParameter(1f, 0.6f, 4f, true);

    [Header("Chromatic")]
    [InspectorName("边缘色散")]
    public BoolParameter enableChromatic = new BoolParameter(true, true);

    [InspectorName("色散强度")]
    public ClampedFloatParameter chromaticAberration = new ClampedFloatParameter(0.0035f, 0f, 0.02f, true);

    [InspectorName("边缘色散加重")]
    public ClampedFloatParameter chromaticEdgeBoost = new ClampedFloatParameter(2.2f, 0f, 6f, true);

    [Header("Picture")]
    [InspectorName("暗角")]
    public BoolParameter enableVignette = new BoolParameter(true, true);

    [InspectorName("暗角强度")]
    public ClampedFloatParameter vignetteIntensity = new ClampedFloatParameter(0.28f, 0f, 1f, true);

    [InspectorName("暗角范围")]
    public ClampedFloatParameter vignetteSmoothness = new ClampedFloatParameter(0.55f, 0.05f, 1f, true);

    [InspectorName("亮度")]
    public ClampedFloatParameter brightness = new ClampedFloatParameter(0.06f, -0.5f, 0.5f, true);

    [InspectorName("对比度")]
    public ClampedFloatParameter contrast = new ClampedFloatParameter(1.08f, 0.5f, 2f, true);

    [InspectorName("饱和度")]
    public ClampedFloatParameter saturation = new ClampedFloatParameter(1.05f, 0f, 2f, true);

    [InspectorName("磷光染色")]
    public ColorParameter phosphorTint = new ColorParameter(new Color(0.96f, 1.02f, 0.94f, 1f), false, false, true, true);

    [InspectorName("磷光辉光")]
    public ClampedFloatParameter glow = new ClampedFloatParameter(0.18f, 0f, 1f, true);

    [Header("Analog")]
    [InspectorName("闪烁")]
    public BoolParameter enableFlicker = new BoolParameter(true, true);

    [InspectorName("闪烁强度")]
    public ClampedFloatParameter flicker = new ClampedFloatParameter(0.035f, 0f, 0.2f, true);

    [InspectorName("噪声")]
    public BoolParameter enableNoise = new BoolParameter(true, true);

    [InspectorName("噪声强度")]
    public ClampedFloatParameter noise = new ClampedFloatParameter(0.04f, 0f, 0.3f, true);

    [InspectorName("行抖动")]
    public ClampedFloatParameter wobble = new ClampedFloatParameter(0.0012f, 0f, 0.02f, true);

    [InspectorName("滚动亮带")]
    public ClampedFloatParameter rollBar = new ClampedFloatParameter(0.08f, 0f, 0.5f, true);

    public bool IsActive()
    {
        return active && enableEffect.value;
    }

    public static Vector2 WarpUv(Vector2 uv, float edgeCurl, float curlX, float curlY, float cornerPinch, float overscan, float aspect, bool aspectCorrect)
    {
        Vector2 p = uv * 2f - Vector2.one;
        if (aspectCorrect && aspect > 0.0001f)
            p.x *= aspect;

        float cx = Mathf.Max(0f, edgeCurl * curlX) * 0.42f;
        float cy = Mathf.Max(0f, edgeCurl * curlY) * 0.42f;
        p.x *= 1f + p.y * p.y * cx;
        p.y *= 1f + p.x * p.x * cy;

        float corner = Mathf.Abs(p.x * p.y) * Mathf.Max(0f, cornerPinch) * 0.55f;
        p *= 1f + corner;

        if (aspectCorrect && aspect > 0.0001f)
            p.x /= aspect;

        float zoom = 1f - Mathf.Clamp(overscan, 0f, 0.2f);
        p *= zoom;
        return p * 0.5f + Vector2.one * 0.5f;
    }

    public void ApplyPreset(CRTPreset preset)
    {
        enableEffect.Override(preset != CRTPreset.Off);

        switch (preset)
        {
            case CRTPreset.Off:
                intensity.Override(0f);
                break;
            case CRTPreset.Subtle:
                ApplyLook(0.7f, 0.1f, 0.9f, 1f, 0.04f, 0.01f, 0.04f, 0.02f, true, 0.22f, 220f, true, CRTMaskType.ApertureGrille, 0.18f, true, 0.0015f, 0.18f, 0.02f, 0.01f, 0.04f, 0.0004f, 0.03f);
                break;
            case CRTPreset.Classic:
                ApplyLook(1f, 0.22f, 1f, 1.15f, 0.12f, 0.02f, 0.08f, 0.03f, true, 0.42f, 280f, true, CRTMaskType.ApertureGrille, 0.35f, true, 0.0035f, 0.28f, 0.06f, 0.035f, 0.04f, 0.0012f, 0.08f);
                break;
            case CRTPreset.Arcade:
                ApplyLook(1f, 0.32f, 1.05f, 1.25f, 0.2f, 0.0f, 0.12f, 0.04f, true, 0.62f, 240f, true, CRTMaskType.SlotMask, 0.48f, true, 0.006f, 0.38f, 0.08f, 0.05f, 0.06f, 0.002f, 0.12f);
                break;
            case CRTPreset.SecurityMonitor:
                ApplyLook(0.92f, 0.08f, 0.7f, 0.85f, 0.03f, 0.015f, 0.02f, 0.015f, true, 0.55f, 360f, true, CRTMaskType.ShadowMask, 0.22f, true, 0.001f, 0.22f, -0.02f, 0.06f, 0.08f, 0.0018f, 0.16f);
                saturation.Override(0.55f);
                phosphorTint.Override(new Color(0.85f, 1.05f, 0.9f, 1f));
                contrast.Override(1.2f);
                break;
            case CRTPreset.Broken:
                ApplyLook(1f, 0.38f, 1.2f, 1.4f, 0.28f, 0f, 0.1f, 0.05f, true, 0.7f, 190f, true, CRTMaskType.ApertureGrille, 0.55f, true, 0.012f, 0.45f, 0.1f, 0.12f, 0.16f, 0.008f, 0.28f);
                break;
        }
    }

    void ApplyLook(
        float intensityValue,
        float curl,
        float x,
        float y,
        float pinch,
        float scan,
        float roundness,
        float thickness,
        bool scanlines,
        float scanInt,
        float scanCount,
        bool mask,
        CRTMaskType maskKind,
        float maskInt,
        bool chroma,
        float chromaInt,
        float vig,
        float bright,
        float flick,
        float noi,
        float wob,
        float roll)
    {
        intensity.Override(intensityValue);
        edgeCurl.Override(curl);
        curlX.Override(x);
        curlY.Override(y);
        cornerPinch.Override(pinch);
        overscan.Override(scan);
        bezelRoundness.Override(roundness);
        bezelThickness.Override(thickness);
        enableScanlines.Override(scanlines);
        scanlineIntensity.Override(scanInt);
        scanlineCount.Override(scanCount);
        enableMask.Override(mask);
        maskType.Override(maskKind);
        maskIntensity.Override(maskInt);
        enableChromatic.Override(chroma);
        chromaticAberration.Override(chromaInt);
        enableVignette.Override(true);
        vignetteIntensity.Override(vig);
        brightness.Override(bright);
        contrast.Override(1.08f);
        saturation.Override(1.05f);
        phosphorTint.Override(new Color(0.96f, 1.02f, 0.94f, 1f));
        glow.Override(0.18f);
        enableFlicker.Override(flick > 0.001f);
        flicker.Override(flick);
        enableNoise.Override(noi > 0.001f);
        noise.Override(noi);
        wobble.Override(wob);
        rollBar.Override(roll);
        aspectCorrection.Override(true);
        bezelSoftness.Override(0.04f);
        bezelColor.Override(new Color(0.02f, 0.02f, 0.025f, 1f));
        chromaticEdgeBoost.Override(2.2f);
        vignetteSmoothness.Override(0.55f);
        scanlineSharpness.Override(1.4f);
        maskScale.Override(1f);
    }
}

public enum CRTMaskType
{
    ApertureGrille = 0,
    ShadowMask = 1,
    SlotMask = 2
}

public enum CRTPreset
{
    Off = 0,
    Subtle = 1,
    Classic = 2,
    Arcade = 3,
    SecurityMonitor = 4,
    Broken = 5
}

[Serializable]
public sealed class MaskTypeParameter : VolumeParameter<CRTMaskType>
{
    public MaskTypeParameter(CRTMaskType value, bool overrideState = false) : base(value, overrideState)
    {
    }
}
