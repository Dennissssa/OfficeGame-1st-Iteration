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
    /// 播放 EffectsList[index]。优先使用 EffectsSource（与 PlaySoundOnEventAudioManager、JiUGameManagerBossAudio 一致）；
    /// 若未分配 EffectsSource，则回退到 EffectsSourceList 中与 index 对应的条目。
    /// 下标无效时静默忽略，避免 InputSystem 等回调里抛异常。
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
