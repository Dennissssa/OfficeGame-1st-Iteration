using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(CRTVolume))]
sealed class CRTVolumeEditor : VolumeComponentEditor
{
    SerializedDataParameter m_EnableEffect;
    SerializedDataParameter m_Intensity;
    SerializedDataParameter m_EdgeCurl;
    SerializedDataParameter m_CurlX;
    SerializedDataParameter m_CurlY;
    SerializedDataParameter m_CornerPinch;
    SerializedDataParameter m_Overscan;
    SerializedDataParameter m_AspectCorrection;
    SerializedDataParameter m_BezelRoundness;
    SerializedDataParameter m_BezelThickness;
    SerializedDataParameter m_BezelSoftness;
    SerializedDataParameter m_BezelColor;
    SerializedDataParameter m_EnableScanlines;
    SerializedDataParameter m_ScanlineIntensity;
    SerializedDataParameter m_ScanlineCount;
    SerializedDataParameter m_ScanlineSharpness;
    SerializedDataParameter m_EnableMask;
    SerializedDataParameter m_MaskType;
    SerializedDataParameter m_MaskIntensity;
    SerializedDataParameter m_MaskScale;
    SerializedDataParameter m_EnableChromatic;
    SerializedDataParameter m_ChromaticAberration;
    SerializedDataParameter m_ChromaticEdgeBoost;
    SerializedDataParameter m_EnableVignette;
    SerializedDataParameter m_VignetteIntensity;
    SerializedDataParameter m_VignetteSmoothness;
    SerializedDataParameter m_Brightness;
    SerializedDataParameter m_Contrast;
    SerializedDataParameter m_Saturation;
    SerializedDataParameter m_PhosphorTint;
    SerializedDataParameter m_Glow;
    SerializedDataParameter m_EnableFlicker;
    SerializedDataParameter m_Flicker;
    SerializedDataParameter m_EnableNoise;
    SerializedDataParameter m_Noise;
    SerializedDataParameter m_Wobble;
    SerializedDataParameter m_RollBar;

    bool m_FoldGeo = true;
    bool m_FoldScan = true;
    bool m_FoldPicture = false;
    bool m_FoldAnalog = false;

    CRTPreviewResources m_Preview;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<CRTVolume>(serializedObject);
        m_EnableEffect = Unpack(o.Find(x => x.enableEffect));
        m_Intensity = Unpack(o.Find(x => x.intensity));
        m_EdgeCurl = Unpack(o.Find(x => x.edgeCurl));
        m_CurlX = Unpack(o.Find(x => x.curlX));
        m_CurlY = Unpack(o.Find(x => x.curlY));
        m_CornerPinch = Unpack(o.Find(x => x.cornerPinch));
        m_Overscan = Unpack(o.Find(x => x.overscan));
        m_AspectCorrection = Unpack(o.Find(x => x.aspectCorrection));
        m_BezelRoundness = Unpack(o.Find(x => x.bezelRoundness));
        m_BezelThickness = Unpack(o.Find(x => x.bezelThickness));
        m_BezelSoftness = Unpack(o.Find(x => x.bezelSoftness));
        m_BezelColor = Unpack(o.Find(x => x.bezelColor));
        m_EnableScanlines = Unpack(o.Find(x => x.enableScanlines));
        m_ScanlineIntensity = Unpack(o.Find(x => x.scanlineIntensity));
        m_ScanlineCount = Unpack(o.Find(x => x.scanlineCount));
        m_ScanlineSharpness = Unpack(o.Find(x => x.scanlineSharpness));
        m_EnableMask = Unpack(o.Find(x => x.enableMask));
        m_MaskType = Unpack(o.Find(x => x.maskType));
        m_MaskIntensity = Unpack(o.Find(x => x.maskIntensity));
        m_MaskScale = Unpack(o.Find(x => x.maskScale));
        m_EnableChromatic = Unpack(o.Find(x => x.enableChromatic));
        m_ChromaticAberration = Unpack(o.Find(x => x.chromaticAberration));
        m_ChromaticEdgeBoost = Unpack(o.Find(x => x.chromaticEdgeBoost));
        m_EnableVignette = Unpack(o.Find(x => x.enableVignette));
        m_VignetteIntensity = Unpack(o.Find(x => x.vignetteIntensity));
        m_VignetteSmoothness = Unpack(o.Find(x => x.vignetteSmoothness));
        m_Brightness = Unpack(o.Find(x => x.brightness));
        m_Contrast = Unpack(o.Find(x => x.contrast));
        m_Saturation = Unpack(o.Find(x => x.saturation));
        m_PhosphorTint = Unpack(o.Find(x => x.phosphorTint));
        m_Glow = Unpack(o.Find(x => x.glow));
        m_EnableFlicker = Unpack(o.Find(x => x.enableFlicker));
        m_Flicker = Unpack(o.Find(x => x.flicker));
        m_EnableNoise = Unpack(o.Find(x => x.enableNoise));
        m_Noise = Unpack(o.Find(x => x.noise));
        m_Wobble = Unpack(o.Find(x => x.wobble));
        m_RollBar = Unpack(o.Find(x => x.rollBar));
        m_Preview = new CRTPreviewResources();
    }

    public override void OnDisable()
    {
        m_Preview?.Dispose();
        m_Preview = null;
        base.OnDisable();
    }

    public override void OnInspectorGUI()
    {
        CRTVolume volume = target as CRTVolume;
        if (volume == null)
            return;

        CRTEditorVisuals.DrawInstallBanner();
        m_Preview.DrawLivePreview(volume, 168f);
        CRTEditorVisuals.DrawWarpGraph(volume, 88f);
        CRTEditorVisuals.DrawPresetRow(preset =>
        {
            volume.ApplyPreset(preset);
            serializedObject.Update();
        });

        if (GUILayout.Button("打开 CRT 可视化窗口", GUILayout.Height(26)))
            CRTPreviewWindow.Open();

        EditorGUILayout.Space(4);
        PropertyField(m_EnableEffect);
        PropertyField(m_Intensity);

        m_FoldGeo = EditorGUILayout.BeginFoldoutHeaderGroup(m_FoldGeo, "几何 · 边缘卷曲");
        if (m_FoldGeo)
        {
            PropertyField(m_EdgeCurl);
            PropertyField(m_CurlX);
            PropertyField(m_CurlY);
            PropertyField(m_CornerPinch);
            PropertyField(m_Overscan);
            PropertyField(m_AspectCorrection);
            PropertyField(m_BezelRoundness);
            PropertyField(m_BezelThickness);
            PropertyField(m_BezelSoftness);
            PropertyField(m_BezelColor);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        m_FoldScan = EditorGUILayout.BeginFoldoutHeaderGroup(m_FoldScan, "扫描线 · 磷光掩模 · 色散");
        if (m_FoldScan)
        {
            PropertyField(m_EnableScanlines);
            PropertyField(m_ScanlineIntensity);
            PropertyField(m_ScanlineCount);
            PropertyField(m_ScanlineSharpness);
            PropertyField(m_EnableMask);
            PropertyField(m_MaskType);
            PropertyField(m_MaskIntensity);
            PropertyField(m_MaskScale);
            PropertyField(m_EnableChromatic);
            PropertyField(m_ChromaticAberration);
            PropertyField(m_ChromaticEdgeBoost);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        m_FoldPicture = EditorGUILayout.BeginFoldoutHeaderGroup(m_FoldPicture, "画面 · 暗角 · 辉光");
        if (m_FoldPicture)
        {
            PropertyField(m_EnableVignette);
            PropertyField(m_VignetteIntensity);
            PropertyField(m_VignetteSmoothness);
            PropertyField(m_Brightness);
            PropertyField(m_Contrast);
            PropertyField(m_Saturation);
            PropertyField(m_PhosphorTint);
            PropertyField(m_Glow);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        m_FoldAnalog = EditorGUILayout.BeginFoldoutHeaderGroup(m_FoldAnalog, "模拟信号不稳");
        if (m_FoldAnalog)
        {
            PropertyField(m_EnableFlicker);
            PropertyField(m_Flicker);
            PropertyField(m_EnableNoise);
            PropertyField(m_Noise);
            PropertyField(m_Wobble);
            PropertyField(m_RollBar);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (volume.enableEffect.value && volume.intensity.value > 0.001f && EditorWindow.focusedWindow != null)
            EditorWindow.focusedWindow.Repaint();
    }
}
