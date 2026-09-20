using UnityEngine.Rendering;

public static class CRTTelevision
{
    public static CRTVolume Current
    {
        get
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            return stack != null ? stack.GetComponent<CRTVolume>() : null;
        }
    }

    public static bool Enabled
    {
        get
        {
            CRTVolume volume = Current;
            return volume != null && volume.IsActive();
        }
        set
        {
            CRTVolume volume = Current;
            if (volume == null)
                return;
            volume.enableEffect.Override(value);
            if (value && volume.intensity.value <= 0.001f)
                volume.intensity.Override(1f);
        }
    }

    public static float Intensity
    {
        get
        {
            CRTVolume volume = Current;
            return volume != null ? volume.intensity.value : 0f;
        }
        set
        {
            CRTVolume volume = Current;
            if (volume != null)
                volume.intensity.Override(value);
        }
    }

    public static float EdgeCurl
    {
        get
        {
            CRTVolume volume = Current;
            return volume != null ? volume.edgeCurl.value : 0f;
        }
        set
        {
            CRTVolume volume = Current;
            if (volume != null)
                volume.edgeCurl.Override(value);
        }
    }

    public static void ApplyPreset(CRTPreset preset)
    {
        CRTVolume volume = Current;
        if (volume != null)
            volume.ApplyPreset(preset);
    }
}
