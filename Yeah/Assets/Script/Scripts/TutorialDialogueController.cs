using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 教程对话框控制器（单例）。
///
/// 持有唯一一个共享的对话框实例（由 TutorialDialogueBox.prefab 摆到场景中），
/// 供所有 <see cref="TutorialStepTrigger"/> 复用。当某个触发器被激活时，控制器负责：
///   1. 换 Animation 子物体 Animator 的 Controller（对应该步教程动画）
///   2. 逐句显示 DialogueText 并 PlayOneShot 对应音频（不再依赖 Play On Awake）
///   3. 在标记句之后运行门控（ForceBreak/ForceBait + 等待修复）
///   4. 处理 phone 教程首句/末句对 GameManager 的通知
///
/// GameManager 与 WorkItem 的教程逻辑完全不变——本控制器只是「表现层」。
/// </summary>
[AddComponentMenu("Tutorial/Tutorial Dialogue Controller")]
public class TutorialDialogueController : MonoBehaviour
{
    public static TutorialDialogueController Instance { get; private set; }

    // ─── 共享对话框引用 ────────────────────────────────────────

    [Header("Shared Dialogue Box — 共享对话框（单 prefab 实例）")]
    [Tooltip("对话框根 GameObject（TutorialDialogueBox 实例）；播放时 SetActive(true)，隐藏时 SetActive(false)")]
    public GameObject boxRoot;

    [Tooltip("Animation 子物体上的 Animator；每步会把它的 runtimeAnimatorController 换成该步的动画")]
    public Animator animationAnimator;

    [Tooltip("DialogueText（Sam 台词）TMP")]
    public TextMeshProUGUI dialogueText;

    [Tooltip("播放语音的 AudioSource（可用对话框根上那个，记得关掉 Play On Awake）")]
    public AudioSource voiceSource;

    // ─── Debug ─────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] bool debugLog = false;

    // ─── 运行时状态 ────────────────────────────────────────────

    TutorialStepTrigger _currentTrigger;
    Coroutine _runRoutine;

    // 记录当前因 extra hold 被临时隐藏的物体，便于统一恢复（含被中途打断的情况）
    readonly List<GameObject> _holdHidden = new List<GameObject>();

    // 当前正在显示的句子；修复触发关闭时用它的 extraHoldSeconds / hideDuringExtraHold 做优雅关闭
    TutorialSentence _activeSentence;

    // ─── 生命周期 ──────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TutorialDialogueController] 场景中存在多个实例，销毁多余的。", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (boxRoot != null)
            boxRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ─── 触发器接口 ────────────────────────────────────────────

    /// <summary>由 TutorialStepTrigger.OnEnable 调用：开始播放该步内容。</summary>
    public void PlayStep(TutorialStepTrigger trigger)
    {
        if (trigger == null) return;

        // 教程期间一般是串行的；若有正在进行的步骤，末位优先（停掉旧的）
        if (_runRoutine != null)
            StopCoroutine(_runRoutine);

        _activeSentence = null;
        _currentTrigger = trigger;
        _runRoutine = StartCoroutine(RunStep(trigger));
    }

    /// <summary>由 TutorialStepTrigger.OnDisable 调用（通常是 WorkItem.Fix 修好工位）：
    /// 不立刻关掉整个框，而是先按 extra hold 只隐藏指定内容、框继续显示，停留结束后再整体关闭。</summary>
    public void StopStep(TutorialStepTrigger trigger)
    {
        if (trigger != _currentTrigger) return;

        if (_runRoutine != null)
        {
            StopCoroutine(_runRoutine);
            _runRoutine = null;
        }

        _currentTrigger = null;
        _runRoutine = StartCoroutine(GracefulCloseRoutine(_activeSentence));
    }

    /// <summary>修复后：框保持显示，仅隐藏该句指定的物体（不再整体关闭）。
    /// 之后框继续显示，等下一步（下一个工位损坏）的 RunStep 恢复内容并刷新为新一步。</summary>
    IEnumerator GracefulCloseRoutine(TutorialSentence lastSentence)
    {
        // 只隐藏指定内容，boxRoot 及其它子物体（如 Zoom Call）保持显示
        if (lastSentence != null)
            SetHoldHidden(lastSentence.hideDuringExtraHold, true);

        _runRoutine = null;
        _activeSentence = null;
        yield break;
    }

    // ─── 核心流程 ──────────────────────────────────────────────

    IEnumerator RunStep(TutorialStepTrigger t)
    {
        Log($"[TutorialDialogueController] ▶ 开始步骤 \"{t.name}\"，共 {t.sentences?.Count ?? 0} 句。");

        // 显示对话框
        if (boxRoot != null && !boxRoot.activeSelf)
            boxRoot.SetActive(true);

        // 恢复上一步优雅关闭可能残留的隐藏内容
        SetHoldHidden(null, false);

        // 起始动画（步骤默认）
        ApplyAnimator(t.animatorController);

        // phone 首句：通知 GameManager 重置末句门闩
        if (t.isPhoneTutorialStartLine && GameManager.Instance != null)
            GameManager.Instance.RegisterPhoneTutorialStartLine();

        // 逐句播放
        if (t.sentences != null)
        {
            for (int i = 0; i < t.sentences.Count; i++)
            {
                TutorialSentence s = t.sentences[i];
                if (s == null) continue;

                _activeSentence = s; // 记录当前句：修复触发关闭时用它的 extraHold / hideDuringExtraHold

                // 本句动画（非空则切换；留空沿用当前）
                ApplyAnimator(s.animatorController);

                // 文字
                if (dialogueText != null)
                {
                    dialogueText.text = s.text;
                    dialogueText.ForceMeshUpdate();
                }

                // 音频 + 停留（内容全程可见；隐藏发生在修复后的优雅关闭里）
                float wait;
                if (s.voiceClip != null && voiceSource != null)
                {
                    voiceSource.Stop();
                    voiceSource.PlayOneShot(s.voiceClip);
                    wait = s.voiceClip.length + Mathf.Max(0f, s.extraHoldSeconds);
                }
                else
                {
                    wait = Mathf.Max(0f, s.noClipHoldSeconds);
                }

                Log($"[TutorialDialogueController]   句 {i}: \"{s.text}\"，等待 {wait:F2}s。");
                if (wait > 0f)
                    yield return new WaitForSeconds(wait);

                // 该句之后开放门控判定
                if (s.openGateAfterThisSentence)
                    yield return RunGate(t);
            }
        }

        // phone 末句：通知 GameManager，可选等待修复后再隐藏
        if (t.isPhoneTutorialLastLine)
        {
            if (GameManager.Instance != null)
            {
                Log($"[TutorialDialogueController] \"{t.name}\" → RegisterPhoneTutorialLastLineCompleted");
                GameManager.Instance.RegisterPhoneTutorialLastLineCompleted();
            }

            WorkItem waitFix = t.keepVisibleUntilRepaired != null ? t.keepVisibleUntilRepaired : t.gateWorkItem;
            if (waitFix != null)
                yield return WaitUntilBrokeOrBaitThenRepaired(waitFix);
        }

        _runRoutine = null;
        Log($"[TutorialDialogueController] ■ 步骤 \"{t.name}\" 完成。");

        // 完成后处理
        if (t.hideBoxOnComplete)
        {
            _currentTrigger = null;
            HideBox();
            t.gameObject.SetActive(false); // 触发器自身关闭（OnDisable 里 StopStep 会因已非 current 而跳过）
        }
        // 否则：保持显示，等待外部（WorkItem.Fix → 触发器 SetActive(false)）来 StopStep 隐藏
    }

    /// <summary>运行门控：等空闲 → 强制 Broke/Bait → 再等修复恢复空闲。</summary>
    IEnumerator RunGate(TutorialStepTrigger t)
    {
        if (t.gateMode == TutorialWorkItemGateMode.Disabled)
            yield break;

        if (t.gateWorkItem == null)
        {
            Debug.LogWarning($"[TutorialDialogueController] \"{t.name}\" 启用了门控 {t.gateMode} 但未指定 gateWorkItem，跳过门控。", this);
            yield break;
        }

        yield return WaitUntilWorkItemIdle(t.gateWorkItem);

        if (t.gateMode == TutorialWorkItemGateMode.ForceBreak)
        {
            Log($"[TutorialDialogueController] 门控 Break() → {t.gateWorkItem.name}");
            t.gateWorkItem.Break();
        }
        else if (t.gateMode == TutorialWorkItemGateMode.ForceBait)
        {
            Log($"[TutorialDialogueController] 门控 Bait() → {t.gateWorkItem.name}");
            t.gateWorkItem.Bait();
        }

        yield return WaitUntilWorkItemIdle(t.gateWorkItem);
        Log($"[TutorialDialogueController] 门控完成（{t.gateWorkItem.name} 已恢复）。");
    }

    // ─── 工具 ──────────────────────────────────────────────────

    /// <summary>切换 Animation 子物体的 Animator Controller（controller 为空则不改动）。</summary>
    void ApplyAnimator(RuntimeAnimatorController controller)
    {
        if (controller == null || animationAnimator == null)
            return;
        if (animationAnimator.runtimeAnimatorController == controller)
            return; // 已是该动画，避免重置

        animationAnimator.runtimeAnimatorController = controller;
        animationAnimator.Rebind();
        animationAnimator.Update(0f);
    }

    void HideBox()
    {
        SetHoldHidden(null, false); // 若在 extra hold 中途被打断，恢复被临时隐藏的物体
        if (voiceSource != null)
            voiceSource.Stop();
        if (boxRoot != null)
            boxRoot.SetActive(false);
    }

    /// <summary>extra hold 期间临时隐藏/恢复指定物体。hide=true 隐藏 objs 并记录；hide=false 恢复所有已记录的物体（objs 忽略）。</summary>
    void SetHoldHidden(List<GameObject> objs, bool hide)
    {
        if (hide)
        {
            if (objs == null) return;
            for (int i = 0; i < objs.Count; i++)
            {
                GameObject go = objs[i];
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    _holdHidden.Add(go);
                }
            }
        }
        else
        {
            for (int i = 0; i < _holdHidden.Count; i++)
            {
                if (_holdHidden[i] != null)
                    _holdHidden[i].SetActive(true);
            }
            _holdHidden.Clear();
        }
    }

    static IEnumerator WaitUntilWorkItemIdle(WorkItem item)
    {
        yield return new WaitUntil(() => item != null && !item.IsBroken && !item.IsBaiting);
    }

    /// <summary>先等对方进入 Broke/Bait，再等其修好，避免一帧误判为已修好。</summary>
    static IEnumerator WaitUntilBrokeOrBaitThenRepaired(WorkItem item)
    {
        if (item == null) yield break;
        yield return new WaitUntil(() => item == null || item.IsBroken || item.IsBaiting);
        if (item == null) yield break;
        yield return new WaitUntil(() => item == null || (!item.IsBroken && !item.IsBaiting));
    }

    void Log(string msg)
    {
        if (debugLog) Debug.Log(msg, this);
    }

    /// <summary>
    /// 结算演出：在对话框上逐句显示文字并播放语音。使用真实时间，timeScale 为 0 时也能播完。
    /// 父物体被关掉时会临时打开到对话框这一支，播完后恢复。
    /// </summary>
    public IEnumerator PlayLinesRealtime(List<DialoguePlaybackLine> lines)
    {
        if (!HasPlayableLine(lines) || boxRoot == null)
            yield break;

        if (_runRoutine != null)
        {
            StopCoroutine(_runRoutine);
            _runRoutine = null;
            _currentTrigger = null;
        }

        List<GameObject> turnedOn = ActivateWithAncestors(boxRoot);

        if (voiceSource != null)
        {
            voiceSource.playOnAwake = false;
            voiceSource.ignoreListenerPause = true;
            voiceSource.Stop();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            DialoguePlaybackLine line = lines[i];
            if (line == null || !line.HasContent)
                continue;

            if (dialogueText != null)
            {
                dialogueText.text = line.text ?? string.Empty;
                dialogueText.ForceMeshUpdate();
            }

            float wait = Mathf.Max(0f, line.extraHoldSeconds);
            if (line.voiceClip != null && voiceSource != null)
            {
                voiceSource.Stop();
                voiceSource.PlayOneShot(line.voiceClip);
                wait = Mathf.Max(wait, line.voiceClip.length + Mathf.Max(0f, line.extraHoldSeconds));
            }
            else if (wait <= 0f)
            {
                wait = 1.5f;
            }

            if (wait > 0f)
                yield return new WaitForSecondsRealtime(wait);
        }

        HideBox();
        RestoreActivatedAncestors(turnedOn, boxRoot);
    }

    static bool HasPlayableLine(List<DialoguePlaybackLine> lines)
    {
        if (lines == null) return false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i] != null && lines[i].HasContent)
                return true;
        }
        return false;
    }

    static List<GameObject> ActivateWithAncestors(GameObject leaf)
    {
        var chain = new List<GameObject>();
        Transform t = leaf.transform;
        while (t != null)
        {
            chain.Add(t.gameObject);
            t = t.parent;
        }

        var turnedOn = new List<GameObject>();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            GameObject go = chain[i];
            if (go != null && !go.activeSelf)
            {
                go.SetActive(true);
                turnedOn.Add(go);
            }
        }
        return turnedOn;
    }

    static void RestoreActivatedAncestors(List<GameObject> turnedOn, GameObject keepHandledByHide)
    {
        if (turnedOn == null) return;
        for (int i = turnedOn.Count - 1; i >= 0; i--)
        {
            GameObject go = turnedOn[i];
            if (go == null || go == keepHandledByHide)
                continue;
            go.SetActive(false);
        }
    }
}

/// <summary>对话框一句：文字 + 语音。结算演出和以后的单次播放都用这个。</summary>
[System.Serializable]
public class DialoguePlaybackLine
{
    [TextArea(2, 4)]
    public string text;

    public AudioClip voiceClip;

    [Tooltip("语音结束后再停留的秒数。没有语音时，整句至少显示这么久（短于 0 则按 1.5 秒）。")]
    [Min(0f)]
    public float extraHoldSeconds = 0.35f;

    public bool HasContent => voiceClip != null || !string.IsNullOrWhiteSpace(text);
}
