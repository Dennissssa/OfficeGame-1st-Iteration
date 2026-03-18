using UnityEngine;
using UnityEngine.Events;

namespace JiU
{
    /// <summary>
    /// 通过 AudioManager 的 EffectsList 下标播放音效。
    /// 支持 WorkItem：损坏/修复、Bait 开始/Bait 自然结束（与手动修好）。
    /// </summary>
    public class PlaySoundOnEventAudioManager : MonoBehaviour
    {
        [Header("AudioManager.EffectsList — 损坏")]
        [Tooltip("OnBroken 时播放；-1 不播")]
        public int effectIndex = 0;

        [Tooltip("损坏音是否循环，直到 OnFixed 修好")]
        public bool loopBrokenSound = false;

        [Header("AudioManager.EffectsList — Bait")]
        [Tooltip("进入 Bait 时播放；-1 不播")]
        public int baitEffectIndex = -1;

        [Tooltip("Bait 提示音是否循环，直到玩家修好或 Bait 时间到")]
        public bool loopBaitSound = false;

        [Tooltip("Bait 倒计时自然结束瞬间播放（如“噗”一声）；-1 不播。会与同一帧的 OnFixed 错开，避免被 Stop 切掉")]
        public int baitEndedEffectIndex = -1;

        [Header("指定 WorkItem")]
        [Tooltip("绑定后：损坏/修好、Bait/结束 会自动订阅")]
        public WorkItem workItem;

        [Header("随机音调")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0f;

        [Header("其它事件（可选）")]
        public UnityEvent onPlayed;

        /// <summary>Bait 自然结束与 OnFixed 同帧时，若刚播了 baitEnded 音则本帧不再 Stop。</summary>
        bool _suppressNextFixedStop;

        void Start()
        {
            if (workItem == null) return;

            workItem.OnBroken.AddListener(OnBroken);
            workItem.OnFixed.AddListener(OnFixed);
            workItem.OnBaiting.AddListener(OnBaiting);
            workItem.OnBaitingEnded.AddListener(OnBaitingEnded);
        }

        void OnDestroy()
        {
            if (workItem == null) return;

            workItem.OnBroken.RemoveListener(OnBroken);
            workItem.OnFixed.RemoveListener(OnFixed);
            workItem.OnBaiting.RemoveListener(OnBaiting);
            workItem.OnBaitingEnded.RemoveListener(OnBaitingEnded);
        }

        void OnBroken()
        {
            if (effectIndex < 0) return;
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (effectIndex >= am.EffectsList.Count) return;

            am.EffectsSource.loop = loopBrokenSound;
            PlayAtIndex(effectIndex);
        }

        void OnFixed()
        {
            if (_suppressNextFixedStop)
            {
                _suppressNextFixedStop = false;
                return;
            }

            StopInternal(resetLoop: true);
        }

        void OnBaiting()
        {
            if (baitEffectIndex < 0) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (baitEffectIndex >= am.EffectsList.Count) return;

            if (loopBaitSound)
                am.EffectsSource.loop = true;
            PlayAtIndex(baitEffectIndex);
        }

        void OnBaitingEnded()
        {
            var am = AudioManager.Instance;
            if (am?.EffectsSource == null) return;

            am.EffectsSource.loop = false;
            am.EffectsSource.Stop();

            if (baitEndedEffectIndex >= 0 && baitEndedEffectIndex < am.EffectsList.Count)
            {
                PlayAtIndex(baitEndedEffectIndex);
                _suppressNextFixedStop = true;
            }
        }

        void StopInternal(bool resetLoop)
        {
            var am = AudioManager.Instance;
            if (am?.EffectsSource == null) return;
            if (resetLoop)
                am.EffectsSource.loop = false;
            am.EffectsSource.Stop();
        }

        /// <summary> 停止特效声道 </summary>
        public void Stop()
        {
            StopInternal(resetLoop: true);
        }

        /// <summary> 播放损坏用下标；是否循环与 loopBrokenSound 一致 </summary>
        public void Play()
        {
            if (effectIndex < 0) return;
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            am.EffectsSource.loop = loopBrokenSound;
            PlayAtIndex(effectIndex);
        }

        /// <summary> 手动播 Bait 用下标（例如 UnityEvent） </summary>
        public void PlayBaitSound()
        {
            if (baitEffectIndex < 0) return;
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (loopBaitSound)
                am.EffectsSource.loop = true;
            PlayAtIndex(baitEffectIndex);
        }

        public void PlayAtIndex(int index)
        {
            var am = AudioManager.Instance;
            if (am == null || am.EffectsSource == null) return;
            if (index < 0 || index >= am.EffectsList.Count) return;

            if (pitchRandomRange > 0f)
                am.EffectsSource.pitch = Random.Range(1f - pitchRandomRange, 1f + pitchRandomRange);
            else
                am.EffectsSource.pitch = 1f;

            am.PlaySound(index);
            onPlayed?.Invoke();
        }
    }
}
