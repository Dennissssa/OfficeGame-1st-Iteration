using UnityEngine;
using UnityEngine.Events;

namespace JiU
{
    /// <summary>
    /// 使用 AudioManager.EffectsList 下标取 Clip，在<strong>本物体上的 AudioSource</strong> 播放（默认自动添加），
    /// 避免与全局 EffectsSource / Boss 等互相顶掉。可选关闭损坏与 Bait 相关自动播放。
    /// </summary>
    public class PlaySoundOnEventAudioManager : MonoBehaviour
    {
        [Header("本物体播放声道")]
        [Tooltip("不指定则自动 Add 专用 AudioSource（同一物体上多个本组件时请勿共用 GetComponent，否则会抢一个声道、OnFixed 互相 Stop 掐断 Win/Lose）")]
        public AudioSource localSfxSource;

        [Header("可选：不播损坏 / Bait 相关")]
        [Tooltip("勾选后：OnBroken / OnBaiting / OnBaitingEnded 不播放、不占用本声道；击打 Win/Lose 仍生效")]
        public bool skipBrokenAndBaitSounds = false;

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

        [Tooltip("Bait 倒计时自然结束瞬间播放；-1 不播")]
        public int baitEndedEffectIndex = -1;

        [Header("AudioManager.EffectsList — 击打反馈（Win / Lose）")]
        [Tooltip("真正损坏态（IsBroken）下击打修好时；-1 不播")]
        public int repairCorrectEffectIndex = -1;

        [Tooltip("Bait 乱按、空闲乱按等非成功修好时；-1 不播")]
        public int repairWrongEffectIndex = -1;

        [Header("指定 WorkItem")]
        [Tooltip("绑定后：损坏/修好、Bait/结束、击打 Win/Lose 会自动订阅")]
        public WorkItem workItem;

        [Header("随机音调")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0f;

        [Header("其它事件（可选）")]
        public UnityEvent onPlayed;

        /// <summary>Bait 自然结束与 OnFixed 同帧时，若刚播了 baitEnded 音则本帧不再 Stop。</summary>
        bool _suppressNextFixedStop;

        /// <summary>修好瞬间已在本声道播 Win，同帧 OnFixed 不再 Stop，避免把刚播的 Win 掐掉。</summary>
        bool _suppressNextFixedStopForRepairWin;

        /// <summary>摘机禁播时本次未起损坏音，OnFixed 不再 Stop，避免掐维修反馈。</summary>
        bool _brokenPlaybackSkippedDueToPhonePickup;

        /// <summary>摘机禁播时本次未起 Bait 音，OnFixed 同理。</summary>
        bool _baitingPlaybackSkippedDueToPhonePickup;

        /// <summary>调试用：OnBroken 因摘机未起损坏音时为 true，直至 OnFixed 消费。</summary>
        public bool DebugPeekBrokenPlaybackSkippedDueToPhonePickup => _brokenPlaybackSkippedDueToPhonePickup;

        /// <summary>调试用：OnBaiting 因摘机未起 Bait 音时为 true，直至 OnFixed 消费。</summary>
        public bool DebugPeekBaitingPlaybackSkippedDueToPhonePickup => _baitingPlaybackSkippedDueToPhonePickup;

        void Awake()
        {
            EnsureLocalAudioSource();
        }

        AudioSource EnsureLocalAudioSource()
        {
            if (localSfxSource != null)
                return localSfxSource;

            // 必须每个组件独占一个 AudioSource：若用 GetComponent 与同类组件共享，
            // 则 A 播 Win 后 B 的 OnFixed 会对同一声道 Stop，表现为 Win/Lose 随机不响。
            localSfxSource = gameObject.AddComponent<AudioSource>();
            localSfxSource.playOnAwake = false;
            return localSfxSource;
        }

        void Start()
        {
            if (workItem == null) return;

            workItem.OnBroken.AddListener(OnBroken);
            workItem.OnFixed.AddListener(OnFixed);
            workItem.OnBaiting.AddListener(OnBaiting);
            workItem.OnBaitingEnded.AddListener(OnBaitingEnded);
            workItem.OnRepairCorrect.AddListener(OnRepairCorrect);
            workItem.OnRepairIncorrect.AddListener(OnRepairIncorrect);
        }

        void OnDestroy()
        {
            if (workItem == null) return;

            workItem.OnBroken.RemoveListener(OnBroken);
            workItem.OnFixed.RemoveListener(OnFixed);
            workItem.OnBaiting.RemoveListener(OnBaiting);
            workItem.OnBaitingEnded.RemoveListener(OnBaitingEnded);
            workItem.OnRepairCorrect.RemoveListener(OnRepairCorrect);
            workItem.OnRepairIncorrect.RemoveListener(OnRepairIncorrect);
        }

        bool PickupSuppressBrokenBaitNow()
        {
            return GameManager.PickupAudioSuppressAppliesToBoundWorkItem(workItem);
        }

        void OnRepairCorrect()
        {
            if (repairCorrectEffectIndex < 0) return;
            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = false;
            if (PlayClipFromEffectsList(repairCorrectEffectIndex))
                _suppressNextFixedStopForRepairWin = true;
        }

        void OnRepairIncorrect()
        {
            if (repairWrongEffectIndex < 0) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || repairWrongEffectIndex >= am.EffectsList.Count) return;
            var wrongClip = am.EffectsList[repairWrongEffectIndex];
            if (wrongClip == null) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            // 电话挂机且仍 Broke、损坏音在循环：用 OneShot 播错修音，避免替换 clip 掐断循环
            bool phoneOnHookKeepBrokenLoop = workItem != null
                && workItem.IsBroken
                && workItem.PhoneIsOnCradleForSfx
                && loopBrokenSound;

            if (phoneOnHookKeepBrokenLoop)
            {
                src.PlayOneShot(wrongClip);
                onPlayed?.Invoke();
                return;
            }

            src.loop = false;
            PlayClipFromEffectsList(repairWrongEffectIndex);
        }

        void OnBroken()
        {
            if (skipBrokenAndBaitSounds) return;
            _brokenPlaybackSkippedDueToPhonePickup = false;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow())
            {
                _brokenPlaybackSkippedDueToPhonePickup = true;
                return;
            }

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        void OnFixed()
        {
            if (_suppressNextFixedStop)
            {
                _suppressNextFixedStop = false;
                return;
            }

            if (_suppressNextFixedStopForRepairWin)
            {
                _suppressNextFixedStopForRepairWin = false;
                return;
            }

            // 勾选「不播损坏/Bait」时，本声道从未起过损坏循环；若仍 Stop，会在修好同一帧掐掉刚播的 Win/Lose
            //（Lamp / Printer 等同型物体上尤其明显，因依赖击打反馈而非循环轨）
            if (skipBrokenAndBaitSounds)
                return;

            if (_brokenPlaybackSkippedDueToPhonePickup)
            {
                _brokenPlaybackSkippedDueToPhonePickup = false;
                return;
            }

            if (_baitingPlaybackSkippedDueToPhonePickup)
            {
                _baitingPlaybackSkippedDueToPhonePickup = false;
                return;
            }

            StopInternal(resetLoop: true);
        }

        void OnBaiting()
        {
            if (skipBrokenAndBaitSounds) return;
            _baitingPlaybackSkippedDueToPhonePickup = false;
            if (baitEffectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow())
            {
                _baitingPlaybackSkippedDueToPhonePickup = true;
                return;
            }

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || baitEffectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBaitSound;
            PlayClipFromEffectsList(baitEffectIndex);
        }

        void OnBaitingEnded()
        {
            if (skipBrokenAndBaitSounds) return;

            var am = AudioManager.Instance;
            if (am?.EffectsList == null) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = false;
            src.Stop();

            bool suppressBaitEndedClip = PickupSuppressBrokenBaitNow();
            if (baitEndedEffectIndex >= 0 && baitEndedEffectIndex < am.EffectsList.Count && !suppressBaitEndedClip)
            {
                PlayClipFromEffectsList(baitEndedEffectIndex);
                _suppressNextFixedStop = true;
            }
        }

        void StopInternal(bool resetLoop)
        {
            if (localSfxSource == null) return;
            var src = localSfxSource;
            if (resetLoop)
                src.loop = false;
            src.Stop();
        }

        /// <summary> 停止本物体上的物品音效声道 </summary>
        public void Stop()
        {
            StopInternal(resetLoop: true);
        }

        /// <summary> 播放损坏用下标；是否循环与 loopBrokenSound 一致 </summary>
        public void Play()
        {
            if (skipBrokenAndBaitSounds) return;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        /// <summary> 手动播 Bait 用下标（例如 UnityEvent） </summary>
        public void PlayBaitSound()
        {
            if (skipBrokenAndBaitSounds) return;
            if (baitEffectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || baitEffectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            if (loopBaitSound)
                src.loop = true;
            PlayClipFromEffectsList(baitEffectIndex);
        }

        /// <summary> 使用 EffectsList 下标在本物体声道上播放（不受 skipBrokenAndBaitSounds 影响，供 UnityEvent 手动调用） </summary>
        public void PlayAtIndex(int index)
        {
            PlayClipFromEffectsList(index);
        }

        /// <returns>是否成功开始播放</returns>
        bool PlayClipFromEffectsList(int index)
        {
            var src = EnsureLocalAudioSource();
            if (src == null) return false;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || index < 0 || index >= am.EffectsList.Count) return false;

            var clip = am.EffectsList[index];
            if (clip == null) return false;

            if (pitchRandomRange > 0f)
                src.pitch = Random.Range(1f - pitchRandomRange, 1f + pitchRandomRange);
            else
                src.pitch = 1f;

            src.clip = clip;
            src.Play();
            onPlayed?.Invoke();
            return true;
        }

        /// <summary>
        /// 摘机后：对落在 <see cref="GameManager.PickupAudioSuppressAppliesToBoundWorkItem"/> 内的组件，
        /// 立刻停止本地声道上正在播的损坏/Bait 循环，并清理相关内部标记。
        /// 须在 <see cref="GameManager.SuppressNonBaitBrokeItemSfxFromPhonePickup"/> 已设为 true 之后调用。
        /// </summary>
        public static void StopBrokenBaitPlaybackForPickupScope()
        {
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                PlaySoundOnEventAudioManager m = arr[i];
                if (m.workItem == null) continue;
                if (!GameManager.PickupAudioSuppressAppliesToBoundWorkItem(m.workItem)) continue;
                m.StopBrokenBaitPlaybackDueToPhonePickupInternal();
            }
        }

        /// <summary>
        /// 电话挂机且仍处于 Broke、且曾在 Broke 时摘机过（损坏音曾被摘机停掉）时，重新起损坏循环。
        /// 由 <see cref="WorkItem.NotifyPhonePutDownForBaitFlow"/> 调用。
        /// </summary>
        public static void ResumeBrokenLoopAfterPhonePutdownForWorkItem(WorkItem wi)
        {
            if (wi == null || !wi.IsBroken) return;
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].workItem != wi) continue;
                arr[i].ResumeBrokenLoopAfterPhonePutdownInternal();
            }
        }

        void ResumeBrokenLoopAfterPhonePutdownInternal()
        {
            if (skipBrokenAndBaitSounds) return;
            if (workItem == null || !workItem.IsBroken) return;
            if (effectIndex < 0) return;
            if (PickupSuppressBrokenBaitNow()) return;

            var am = AudioManager.Instance;
            if (am == null || am.EffectsList == null || effectIndex >= am.EffectsList.Count) return;

            var src = EnsureLocalAudioSource();
            if (src == null) return;

            src.loop = loopBrokenSound;
            PlayClipFromEffectsList(effectIndex);
        }

        void StopBrokenBaitPlaybackDueToPhonePickupInternal()
        {
            if (localSfxSource != null)
            {
                localSfxSource.loop = false;
                localSfxSource.Stop();
            }

            _brokenPlaybackSkippedDueToPhonePickup = false;
            _baitingPlaybackSkippedDueToPhonePickup = false;
            _suppressNextFixedStop = false;
            _suppressNextFixedStopForRepairWin = false;
        }

        /// <summary>
        /// 电话摘机/挂机瞬间由 <see cref="GameManager"/> 调用：打印每个组件上两个「摘机跳过」标记的当前值。
        /// </summary>
        public static void DebugLogAllPhonePickupSkipFlags(string momentLabel, bool suppressNonBaitBrokeSfxActive)
        {
            var arr = Object.FindObjectsOfType<PlaySoundOnEventAudioManager>(true);
            if (arr == null || arr.Length == 0)
            {
                Debug.Log(
                    $"[PlaySoundOnEventAudioManager] {momentLabel} | 场景中无组件 | " +
                    $"SuppressNonBaitBrokeItemSfxFromPhonePickup={suppressNonBaitBrokeSfxActive}");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(
                $"[PlaySoundOnEventAudioManager] {momentLabel} | " +
                $"SuppressNonBaitBrokeItemSfxFromPhonePickup={suppressNonBaitBrokeSfxActive} | 组件数={arr.Length}");
            for (int i = 0; i < arr.Length; i++)
            {
                PlaySoundOnEventAudioManager m = arr[i];
                string wi = m.workItem != null ? m.workItem.name : "null";
                bool inScope = GameManager.PickupAudioSuppressAppliesToBoundWorkItem(m.workItem);
                sb.AppendLine(
                    $"  [{i}] \"{m.gameObject.name}\" workItem={wi} inPickupSuppressScope={inScope} " +
                    $"_brokenPlaybackSkippedDueToPhonePickup={m._brokenPlaybackSkippedDueToPhonePickup} " +
                    $"_baitingPlaybackSkippedDueToPhonePickup={m._baitingPlaybackSkippedDueToPhonePickup}");
            }

            Debug.Log(sb.ToString().TrimEnd());
        }
    }
}
