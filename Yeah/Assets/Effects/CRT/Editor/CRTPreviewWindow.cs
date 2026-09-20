using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CRTPreviewWindow : EditorWindow
{
    CRTPreviewResources m_Preview;
    Vector2 m_Scroll;
    double m_LastRepaint;
    CRTVolume m_CachedVolume;
    bool m_ShowOverlayGrid = true;
    int m_PatternIndex;

    static readonly string[] PatternNames = { "广播测试图", "网格", "圆", "棋盘" };

    [MenuItem("Window/CRT Television")]
    [MenuItem("Yeah/CRT Television 可视化")]
    public static void Open()
    {
        var window = GetWindow<CRTPreviewWindow>();
        window.titleContent = new GUIContent("CRT Television");
        window.minSize = new Vector2(860, 560);
        window.Show();
    }

    void OnEnable()
    {
        m_Preview = new CRTPreviewResources();
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Undo.undoRedoPerformed += Repaint;
        RefreshVolumeTarget();
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Undo.undoRedoPerformed -= Repaint;
        m_Preview?.Dispose();
        m_Preview = null;
        m_CachedVolume = null;
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;
        m_CachedVolume = null;
        RefreshVolumeTarget();
        Repaint();
    }

    void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup - m_LastRepaint < 0.05)
            return;
        m_LastRepaint = EditorApplication.timeSinceStartup;
        Repaint();
    }

    void RefreshVolumeTarget()
    {
        if (m_CachedVolume != null)
            return;

        m_CachedVolume = CRTInstaller.GetOrCreateVolume();
        if (m_CachedVolume == null)
            m_CachedVolume = ScriptableObject.CreateInstance<CRTVolume>();
    }

    void OnGUI()
    {
        RefreshVolumeTarget();
        CRTVolume volume = m_CachedVolume;
        if (volume == null)
        {
            EditorGUILayout.HelpBox("CRT Volume 仍未就绪。请先退出 Play 模式，再点下面按钮。", MessageType.Error);
            if (GUILayout.Button("重新安装", GUILayout.Height(28)))
            {
                CRTInstaller.Install(true);
                m_CachedVolume = null;
                RefreshVolumeTarget();
            }
            return;
        }

        EditorGUILayout.Space(6);
        CRTEditorVisuals.DrawInstallBanner();

        EditorGUILayout.BeginHorizontal();
        DrawPreviewPane(volume);
        DrawControlPane(volume);
        EditorGUILayout.EndHorizontal();
    }

    void DrawPreviewPane(CRTVolume volume)
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        m_PatternIndex = GUILayout.Toolbar(m_PatternIndex, PatternNames);
        if (m_Preview != null)
            m_Preview.PatternKind = (CRTPreviewPattern)m_PatternIndex;

        float previewHeight = Mathf.Clamp((position.width - 340f) * 9f / 16f, 200f, 340f);
        Rect preview = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(previewHeight));
        if (Event.current.type == EventType.Repaint && m_Preview != null)
        {
            m_Preview.DrawLivePreview(preview, volume, true);
            if (m_ShowOverlayGrid)
                DrawOverlayGrid(CRTEditorVisuals.ScreenFromBezel(preview), volume);
        }

        CRTEditorVisuals.DrawWarpGraph(volume, 110f);
        m_ShowOverlayGrid = EditorGUILayout.ToggleLeft("在预览上叠加卷曲线框", m_ShowOverlayGrid);
            EditorGUILayout.HelpBox("画面卷曲会直接改写 Game 视图，不再和原图混合。右侧滑动条写入 URP 默认 Volume。", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    void DrawControlPane(CRTVolume volume)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(320), GUILayout.MinWidth(320), GUILayout.MaxWidth(320));
        EditorGUILayout.LabelField("CRT 电视效果", EditorStyles.boldLabel);
        CRTEditorVisuals.DrawPresetRow(preset =>
        {
            Undo.RecordObject(volume, "CRT Preset");
            volume.ApplyPreset(preset);
            MarkVolumeDirty(volume);
        });

        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

        DrawToggle("启用效果", volume.enableEffect);
        DrawSlider("整体强度", volume.intensity, 0f, 1f);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("几何 · 边缘卷曲", EditorStyles.boldLabel);
        DrawSlider("边缘卷曲", volume.edgeCurl, 0f, 1f);
        DrawSlider("水平卷曲", volume.curlX, 0f, 1.5f);
        DrawSlider("垂直卷曲", volume.curlY, 0f, 1.5f);
        DrawSlider("边角挤压", volume.cornerPinch, 0f, 1f);
        DrawSlider("过扫描", volume.overscan, 0f, 0.2f);
        DrawSlider("圆角", volume.bezelRoundness, 0f, 0.4f);
        DrawSlider("边框厚度", volume.bezelThickness, 0f, 0.2f);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("扫描线 · 掩模 · 色散", EditorStyles.boldLabel);
        DrawToggle("扫描线", volume.enableScanlines);
        DrawSlider("扫描线强度", volume.scanlineIntensity, 0f, 1f);
        DrawSlider("扫描线数量", volume.scanlineCount, 60f, 720f);
        DrawToggle("磷光掩模", volume.enableMask);
        DrawSlider("掩模强度", volume.maskIntensity, 0f, 1f);
        DrawToggle("边缘色散", volume.enableChromatic);
        DrawSlider("色散强度", volume.chromaticAberration, 0f, 0.02f);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("画面 · 模拟信号", EditorStyles.boldLabel);
        DrawToggle("暗角", volume.enableVignette);
        DrawSlider("暗角强度", volume.vignetteIntensity, 0f, 1f);
        DrawSlider("磷光辉光", volume.glow, 0f, 1f);
        DrawSlider("亮度", volume.brightness, -0.5f, 0.5f);
        DrawSlider("对比度", volume.contrast, 0.5f, 2f);
        DrawToggle("闪烁", volume.enableFlicker);
        DrawSlider("闪烁强度", volume.flicker, 0f, 0.2f);
        DrawToggle("噪声", volume.enableNoise);
        DrawSlider("噪声强度", volume.noise, 0f, 0.3f);
        DrawSlider("行抖动", volume.wobble, 0f, 0.02f);
        DrawSlider("滚动亮带", volume.rollBar, 0f, 0.5f);

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("在 Volume 配置里显示全部参数", GUILayout.Height(26)))
        {
            VolumeProfile profile = CRTInstaller.GetDefaultProfile();
            if (profile != null)
                Selection.activeObject = profile;
        }

        EditorGUILayout.EndVertical();
    }

    void DrawSlider(string label, ClampedFloatParameter parameter, float min, float max)
    {
        EditorGUI.BeginChangeCheck();
        float value = EditorGUILayout.Slider(label, parameter.value, min, max);
        if (!EditorGUI.EndChangeCheck())
            return;
        Undo.RecordObject(m_CachedVolume, label);
        parameter.Override(value);
        MarkVolumeDirty(m_CachedVolume);
    }

    void DrawToggle(string label, BoolParameter parameter)
    {
        EditorGUI.BeginChangeCheck();
        bool value = EditorGUILayout.Toggle(label, parameter.value);
        if (!EditorGUI.EndChangeCheck())
            return;
        Undo.RecordObject(m_CachedVolume, label);
        parameter.Override(value);
        MarkVolumeDirty(m_CachedVolume);
    }

    static void MarkVolumeDirty(CRTVolume volume)
    {
        if (volume == null)
            return;
        EditorUtility.SetDirty(volume);
        VolumeProfile profile = CRTInstaller.GetDefaultProfile();
        if (profile != null)
            EditorUtility.SetDirty(profile);
    }

    static void DrawOverlayGrid(Rect rect, CRTVolume volume)
    {
        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, 0.18f);
        const int nx = 10;
        const int ny = 6;
        float aspect = rect.width / Mathf.Max(rect.height, 1f);
        for (int y = 0; y <= ny; y++)
        {
            var pts = new Vector3[nx + 1];
            for (int x = 0; x <= nx; x++)
            {
                Vector2 uv = new Vector2(x / (float)nx, y / (float)ny);
                Vector2 w = CRTVolume.WarpUv(uv, volume.edgeCurl.value, volume.curlX.value, volume.curlY.value, volume.cornerPinch.value, volume.overscan.value, aspect, volume.aspectCorrection.value);
                pts[x] = new Vector3(rect.x + w.x * rect.width, rect.y + (1f - w.y) * rect.height, 0f);
            }
            Handles.DrawAAPolyLine(1.2f, pts);
        }
        Handles.EndGUI();
    }
}
