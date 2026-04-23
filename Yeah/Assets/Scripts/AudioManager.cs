using UnityEngine;
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    //Audio player components
    public AudioSource EffectsSource;
    public AudioSource MusicSource;
    
    public List<AudioSource> EffectsSourceList = new List<AudioSource>();
    public List<AudioClip> EffectsList = new List<AudioClip>();
    public List<AudioClip> MusicList = new List<AudioClip>();

    public float soundBuffer = 0.01f;

    //Random pitch adjustments
    public float LowPitchRand = 0.9f;
    public float HighPitchRand = 1.1f;

    private static AudioManager _instance;

    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                Debug.LogError("Audio manager is NULL. FUCK!");
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Plays EffectsList[index]. Prefers EffectsSource (same as PlaySoundOnEventAudioManager, JiUGameManagerBossAudio).
    /// If EffectsSource is unset, falls back to the EffectsSourceList entry for that index.
    /// Invalid indices are ignored silently to avoid exceptions inside Input System callbacks.
    /// </summary>
    public void PlaySound(int index)
    {
        if (EffectsList == null || index < 0 || index >= EffectsList.Count)
            return;

        AudioClip clip = EffectsList[index];
        if (clip == null)
            return;

        if (EffectsSource != null)
        {
            EffectsSource.clip = clip;
            EffectsSource.Play();
            return;
        }

        if (EffectsSourceList != null && index < EffectsSourceList.Count && EffectsSourceList[index] != null)
        {
            EffectsSourceList[index].clip = clip;
            EffectsSourceList[index].Play();
        }
    }

    public void PlayMusic(int index)
    {
        if (MusicSource == null || MusicList == null || index < 0 || index >= MusicList.Count)
            return;

        AudioClip clip = MusicList[index];
        if (clip == null)
            return;

        if (MusicSource.isPlaying)
            MusicSource.Stop();

        MusicSource.clip = clip;
        MusicSource.Play();
    }

    //public void PlayRandom(AudioClip clip)
    //{
    //    float randomPitch = Random.Range(LowPitchRand, HighPitchRand);

    //    EffectsSource.pitch = randomPitch;
    //    EffectsSource.clip = clip;
    //    EffectsSource.Play();
    //}
}
