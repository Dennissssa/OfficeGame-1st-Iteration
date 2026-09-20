using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

sealed class CRTPreviewResources : IDisposable
{
    Texture2D m_Pattern;
    RenderTexture m_Output;
    Material m_Material;
    CRTPreviewPattern m_PatternKind = CRTPreviewPattern.Broadcast;
    int m_Width = 640;
    int m_Height = 360;

    public CRTPreviewPattern PatternKind
    {
        get => m_PatternKind;
        set
        {
            if (m_PatternKind == value)
                return;
            m_PatternKind = value;
            RebuildPattern();
        }
    }

    public void DrawLivePreview(CRTVolume volume, float height, bool drawTvFrame = true)
    {
        Rect r = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint)
            return;
        DrawLivePreview(r, volume, drawTvFrame);
    }

    public void DrawLivePreview(Rect rect, CRTVolume volume, bool drawTvFrame)
    {
        try
        {
            Ensure(Mathf.Max(8, Mathf.RoundToInt(rect.width)), Mathf.Max(8, Mathf.RoundToInt(rect.height)));
            if (m_Output == null || m_Pattern == null || volume == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f));
                GUI.Label(rect, "CRT Shader 未就绪", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            bool blitted = BlitPreview(volume);
            if (drawTvFrame)
                CRTEditorVisuals.DrawTvBezel(rect);

            Rect screen = drawTvFrame ? CRTEditorVisuals.ScreenFromBezel(rect) : rect;
            GUI.DrawTexture(screen, blitted ? m_Output : (Texture)m_Pattern, ScaleMode.StretchToFill, false);
        }
        catch (Exception exception)
        {
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f));
            GUI.Label(rect, "预览失败：滑动条仍可使用\n" + exception.Message, EditorStyles.wordWrappedMiniLabel);
        }
    }

    public Texture RenderToTexture(CRTVolume volume, int width, int height)
    {
        Ensure(width, height);
        if (m_Material == null || volume == null)
            return Texture2D.blackTexture;
        BlitPreview(volume);
        return m_Output != null ? m_Output : Texture2D.blackTexture;
    }

    bool BlitPreview(CRTVolume volume)
    {
        if (m_Material == null || m_Output == null || m_Pattern == null || volume == null)
            return false;

        CRTRendererFeature.ApplyVolumeToMaterial(
            m_Material,
            volume,
            m_Output.width,
            m_Output.height,
            (float)EditorApplication.timeSinceStartup);

        m_Material.SetTexture("_BlitTexture", m_Pattern);
        m_Material.SetTexture("_MainTex", m_Pattern);
        m_Material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
        m_Material.SetVector("_BlitScaleBiasRt", new Vector4(1f, 1f, 0f, 0f));

        var cmd = new CommandBuffer { name = "CRT Preview Blit" };
        cmd.SetRenderTarget(m_Output);
        cmd.ClearRenderTarget(false, true, Color.black);
        cmd.SetViewport(new Rect(0f, 0f, m_Output.width, m_Output.height));
        cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3, 1);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
        return true;
    }

    void Ensure(int width, int height)
    {
        width = Mathf.Clamp(width, 64, 1280);
        height = Mathf.Clamp(height, 36, 720);
        if (m_Width != width || m_Height != height || m_Output == null)
        {
            m_Width = width;
            m_Height = height;
            if (m_Output != null)
                m_Output.Release();
            m_Output = new RenderTexture(m_Width, m_Height, 0, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "CRT Preview"
            };
            m_Output.Create();
            RebuildPattern();
        }

        if (m_Pattern == null)
            RebuildPattern();

        if (m_Material == null)
        {
            Shader shader = Shader.Find("Hidden/Yeah/CRTTelevision");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Effects/CRT/Shaders/CRTTelevision.shader");
            if (shader != null)
            {
                m_Material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "CRT Preview Material"
                };
            }
        }
    }

    void RebuildPattern()
    {
        if (m_Pattern != null)
            UnityEngine.Object.DestroyImmediate(m_Pattern);
        m_Pattern = CRTEditorVisuals.CreateTestPattern(m_Width, m_Height, m_PatternKind);
    }

    public void Dispose()
    {
        if (m_Pattern != null)
            UnityEngine.Object.DestroyImmediate(m_Pattern);
        if (m_Output != null)
        {
            m_Output.Release();
            UnityEngine.Object.DestroyImmediate(m_Output);
        }
        if (m_Material != null)
            UnityEngine.Object.DestroyImmediate(m_Material);
        m_Pattern = null;
        m_Output = null;
        m_Material = null;
    }
}

enum CRTPreviewPattern
{
    Broadcast = 0,
    Grid = 1,
    Circle = 2,
    Checker = 3
}

static class CRTEditorVisuals
{
    static GUIStyle s_PresetButton;
    static GUIStyle s_TvBrand;

    public static void DrawInstallBanner()
    {
        bool ready = CRTInstaller.IsRendererFeatureInstalled();
        Rect r = EditorGUILayout.GetControlRect(false, 22);
        Color bg = ready ? new Color(0.16f, 0.32f, 0.18f) : new Color(0.38f, 0.18f, 0.12f);
        EditorGUI.DrawRect(r, bg);
        string label = ready
            ? "URP Renderer Feature 已安装 · 效果作用于全部场景"
            : "尚未安装到 URP Renderer · 点击安装后才会出现在 Game 视图";
        EditorGUI.LabelField(r, label, EditorStyles.miniLabel);
        if (!ready)
        {
            Rect btn = new Rect(r.xMax - 72, r.y + 2, 68, r.height - 4);
            if (GUI.Button(btn, "安装"))
                CRTInstaller.Install(true);
        }
    }

    public static void DrawPresetRow(Action<CRTPreset> onPreset)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("预设", EditorStyles.miniLabel);
        Rect row = EditorGUILayout.GetControlRect(false, 24);
        string[] names = { "关闭", "轻度", "经典", "街机", "监控", "损坏" };
        float w = row.width / names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            Rect b = new Rect(row.x + w * i + 1, row.y, w - 2, row.height);
            if (GUI.Button(b, names[i], PresetButtonStyle))
                onPreset((CRTPreset)i);
        }
    }

    public static void DrawWarpGraph(CRTVolume volume, float height)
    {
        Rect rect = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint || volume == null)
            return;

        try
        {
            EditorGUI.DrawRect(rect, new Color(0.07f, 0.08f, 0.09f));
            Handles.BeginGUI();
            int nx = 14;
            int ny = 9;
            float aspect = 16f / 9f;
            Handles.color = new Color(0.35f, 0.95f, 0.45f, 0.85f);

            for (int y = 0; y <= ny; y++)
            {
                var pts = new Vector3[nx + 1];
                for (int x = 0; x <= nx; x++)
                {
                    Vector2 uv = new Vector2(x / (float)nx, y / (float)ny);
                    Vector2 w = CRTVolume.WarpUv(uv, volume.edgeCurl.value, volume.curlX.value, volume.curlY.value, volume.cornerPinch.value, volume.overscan.value, aspect, volume.aspectCorrection.value);
                    pts[x] = new Vector3(rect.x + w.x * rect.width, rect.y + (1f - w.y) * rect.height, 0f);
                }
                Handles.DrawAAPolyLine(1.6f, pts);
            }

            for (int x = 0; x <= nx; x++)
            {
                var pts = new Vector3[ny + 1];
                for (int y = 0; y <= ny; y++)
                {
                    Vector2 uv = new Vector2(x / (float)nx, y / (float)ny);
                    Vector2 w = CRTVolume.WarpUv(uv, volume.edgeCurl.value, volume.curlX.value, volume.curlY.value, volume.cornerPinch.value, volume.overscan.value, aspect, volume.aspectCorrection.value);
                    pts[y] = new Vector3(rect.x + w.x * rect.width, rect.y + (1f - w.y) * rect.height, 0f);
                }
                Handles.DrawAAPolyLine(1.6f, pts);
            }

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            var circle = new Vector3[65];
            for (int i = 0; i < circle.Length; i++)
            {
                float t = i / (circle.Length - 1f) * Mathf.PI * 2f;
                Vector2 uv = new Vector2(0.5f + Mathf.Cos(t) * 0.32f, 0.5f + Mathf.Sin(t) * 0.32f);
                Vector2 w = CRTVolume.WarpUv(uv, volume.edgeCurl.value, volume.curlX.value, volume.curlY.value, volume.cornerPinch.value, volume.overscan.value, aspect, volume.aspectCorrection.value);
                circle[i] = new Vector3(rect.x + w.x * rect.width, rect.y + (1f - w.y) * rect.height, 0f);
            }
            Handles.DrawAAPolyLine(2.2f, circle);
            Handles.EndGUI();

            GUI.Label(new Rect(rect.x + 6, rect.y + 4, 180, 16), "边缘卷曲预览", EditorStyles.miniLabel);
        }
        catch
        {
            EditorGUI.DrawRect(rect, new Color(0.07f, 0.08f, 0.09f));
        }
    }

    public static void DrawTvBezel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.12f));
        Rect inner = Rect.MinMaxRect(rect.x + 2, rect.y + 2, rect.xMax - 2, rect.yMax - 2);
        EditorGUI.DrawRect(inner, new Color(0.06f, 0.06f, 0.07f));
        Rect led = new Rect(rect.xMax - 16, rect.yMax - 11, 6, 6);
        EditorGUI.DrawRect(led, new Color(0.85f, 0.12f, 0.1f, 0.95f));
        if (TvBrandStyle != null)
            GUI.Label(new Rect(rect.x + 8, rect.yMax - 16, 80, 14), "CRT", TvBrandStyle);
    }

    public static Rect ScreenFromBezel(Rect rect)
    {
        return Rect.MinMaxRect(rect.x + 10, rect.y + 8, rect.xMax - 10, rect.yMax - 18);
    }

    public static Texture2D CreateTestPattern(int width, int height, CRTPreviewPattern kind)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "CRT Test Pattern"
        };

        var pixels = new Color[width * height];
        Color[] bars =
        {
            new Color(0.75f, 0.75f, 0.75f),
            new Color(0.75f, 0.75f, 0.15f),
            new Color(0.15f, 0.75f, 0.75f),
            new Color(0.15f, 0.75f, 0.15f),
            new Color(0.75f, 0.15f, 0.75f),
            new Color(0.75f, 0.15f, 0.15f),
            new Color(0.15f, 0.15f, 0.75f)
        };

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                Color c;
                switch (kind)
                {
                    case CRTPreviewPattern.Grid:
                        c = GridColor(u, v);
                        break;
                    case CRTPreviewPattern.Circle:
                        c = CircleColor(u, v);
                        break;
                    case CRTPreviewPattern.Checker:
                        c = ((x / 16) + (y / 16)) % 2 == 0 ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.12f, 0.12f, 0.12f);
                        break;
                    default:
                        c = BroadcastColor(u, v, bars);
                        break;
                }
                pixels[y * width + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    static Color BroadcastColor(float u, float v, Color[] bars)
    {
        if (v < 0.7f)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(u * bars.Length), 0, bars.Length - 1);
            Color c = bars[i];
            float grid = GridLine(u, v, 16, 9);
            return Color.Lerp(c, Color.white, grid * 0.35f);
        }

        if (v < 0.82f)
        {
            float ramp = Mathf.Repeat(u * 4f, 1f);
            return new Color(ramp, ramp, ramp);
        }

        float g = u;
        return new Color(g, g, g);
    }

    static Color GridColor(float u, float v)
    {
        Color bg = new Color(0.05f, 0.07f, 0.08f);
        float grid = GridLine(u, v, 16, 9);
        float cross = Mathf.Max(Pulse(u, 0.5f, 0.004f), Pulse(v, 0.5f, 0.006f));
        float circle = CircleLine(u, v, 0.32f);
        Color c = Color.Lerp(bg, new Color(0.25f, 0.9f, 0.4f), grid);
        c = Color.Lerp(c, new Color(1f, 0.85f, 0.2f), cross);
        c = Color.Lerp(c, Color.white, circle);
        return c;
    }

    static Color CircleColor(float u, float v)
    {
        Color bg = new Color(0.08f, 0.08f, 0.1f);
        float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
        Color c = Color.Lerp(new Color(0.2f, 0.45f, 0.85f), bg, Mathf.SmoothStep(0.2f, 0.55f, r));
        c = Color.Lerp(c, Color.white, CircleLine(u, v, 0.2f) + CircleLine(u, v, 0.35f));
        c = Color.Lerp(c, new Color(1f, 0.4f, 0.2f), GridLine(u, v, 8, 5) * 0.5f);
        return c;
    }

    static float GridLine(float u, float v, int nx, int ny)
    {
        float gx = Mathf.Abs(Mathf.Repeat(u * nx + 0.5f, 1f) - 0.5f);
        float gy = Mathf.Abs(Mathf.Repeat(v * ny + 0.5f, 1f) - 0.5f);
        float lx = 1f - Mathf.SmoothStep(0f, 0.03f, gx);
        float ly = 1f - Mathf.SmoothStep(0f, 0.04f, gy);
        return Mathf.Max(lx, ly);
    }

    static float CircleLine(float u, float v, float radius)
    {
        float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
        return 1f - Mathf.SmoothStep(0f, 0.012f, Mathf.Abs(r - radius));
    }

    static float Pulse(float t, float center, float width)
    {
        return 1f - Mathf.SmoothStep(0f, width, Mathf.Abs(t - center));
    }

    static GUIStyle PresetButtonStyle
    {
        get
        {
            if (s_PresetButton == null)
            {
                s_PresetButton = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 10
                };
            }
            return s_PresetButton;
        }
    }

    static GUIStyle TvBrandStyle
    {
        get
        {
            if (s_TvBrand == null)
            {
                s_TvBrand = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.55f, 0.55f, 0.58f) }
                };
            }
            return s_TvBrand;
        }
    }
}
