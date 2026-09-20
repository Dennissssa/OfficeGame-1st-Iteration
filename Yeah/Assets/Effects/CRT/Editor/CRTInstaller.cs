using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class CRTInstaller
{
    const string MenuPath = "Yeah/安装 CRT Television 到 URP";
    const string SampleProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    const string DefaultProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
    const string ShaderPath = "Assets/Effects/CRT/Shaders/CRTTelevision.shader";

    static CRTInstaller()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                Install(false);
        };
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += () => Install(false);
    }

    [MenuItem(MenuPath)]
    static void InstallFromMenu()
    {
        Install(true);
    }

    public static bool IsRendererFeatureInstalled()
    {
        foreach (UniversalRendererData renderer in FindRenderers())
        {
            if (HasCrtFeature(renderer))
                return true;
        }
        return false;
    }

    public static VolumeProfile GetDefaultProfile()
    {
        var sample = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SampleProfilePath);
        if (sample != null)
            return sample;

        UniversalRenderPipelineAsset urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null)
            urp = UniversalRenderPipeline.asset;
        if (urp != null && urp.volumeProfile != null)
            return urp.volumeProfile;

        return AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultProfilePath);
    }

    public static CRTVolume GetOrCreateVolume()
    {
        try
        {
            VolumeProfile profile = GetDefaultProfile();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (profile != null && TryGetValid(profile, out CRTVolume playingVolume))
                    return playingVolume;
                return ScriptableObject.CreateInstance<CRTVolume>();
            }

            Install(false);
            profile = GetDefaultProfile();
            if (profile == null)
                return ScriptableObject.CreateInstance<CRTVolume>();

            return EnsureCrtComponent(profile);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("CRT Television: 重建 Volume 失败，将使用临时组件。\n" + exception);
            return ScriptableObject.CreateInstance<CRTVolume>();
        }
    }

    public static void Install(bool log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (log)
                Debug.LogWarning("CRT Television: 请先退出 Play 模式再安装。");
            return;
        }

        Shader shader = Shader.Find("Hidden/Yeah/CRTTelevision");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            if (log)
                Debug.LogWarning("CRT Television: 还找不到 Shader Hidden/Yeah/CRTTelevision。");
            return;
        }

        foreach (UniversalRendererData renderer in FindRenderers())
            AddFeatureIfMissing(renderer, shader);

        VolumeProfile profile = GetDefaultProfile();
        if (profile != null)
            EnsureCrtComponent(profile);

        AssetDatabase.SaveAssets();

        if (log)
            Debug.Log("CRT Television 已安装到 URP Renderer 和 SampleSceneProfile。可在 Window/CRT Television 中可视化调节。");
    }

    static CRTVolume EnsureCrtComponent(VolumeProfile profile)
    {
        if (profile == null)
            return null;

        RemoveBrokenComponents(profile);

        if (TryGetValid(profile, out CRTVolume existing))
            return existing;

        CRTVolume volume = profile.Add<CRTVolume>(true);
        volume.name = "CRTVolume";
        volume.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;

        string assetPath = AssetDatabase.GetAssetPath(profile);
        if (!string.IsNullOrEmpty(assetPath))
        {
            AssetDatabase.AddObjectToAsset(volume, profile);
            EditorUtility.SetDirty(volume);
            EditorUtility.SetDirty(profile);
        }

        return volume;
    }

    static bool TryGetValid(VolumeProfile profile, out CRTVolume volume)
    {
        volume = null;
        if (profile.components == null)
            return false;

        foreach (VolumeComponent component in profile.components)
        {
            if (component is CRTVolume crt && crt != null)
            {
                volume = crt;
                return true;
            }
        }

        return false;
    }

    static void RemoveBrokenComponents(VolumeProfile profile)
    {
        if (profile.components == null || profile.components.Count == 0)
            return;

        bool changed = false;
        for (int i = profile.components.Count - 1; i >= 0; i--)
        {
            VolumeComponent component = profile.components[i];
            if (component == null)
            {
                profile.components.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
            EditorUtility.SetDirty(profile);
    }

    static IEnumerable<UniversalRendererData> FindRenderers()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        foreach (string guid in guids)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            if (renderer != null)
                yield return renderer;
        }
    }

    static bool HasCrtFeature(UniversalRendererData renderer)
    {
        foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
        {
            if (feature is CRTRendererFeature)
                return true;
        }
        return false;
    }

    static bool AddFeatureIfMissing(UniversalRendererData renderer, Shader shader)
    {
        if (HasCrtFeature(renderer))
        {
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature is CRTRendererFeature crt && crt.ShaderAsset == null)
                {
                    crt.ShaderAsset = shader;
                    EditorUtility.SetDirty(crt);
                    EditorUtility.SetDirty(renderer);
                    return true;
                }
            }
            return false;
        }

        var instance = ScriptableObject.CreateInstance<CRTRendererFeature>();
        instance.name = "CRT Television";
        instance.hideFlags = HideFlags.HideInHierarchy;
        instance.ShaderAsset = shader;

        AssetDatabase.AddObjectToAsset(instance, renderer);

        SerializedObject so = new SerializedObject(renderer);
        so.Update();
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = instance;
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(instance, out _, out long localId))
        {
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(instance);
        EditorUtility.SetDirty(renderer);
        return true;
    }
}
