using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 教程「一步」的数据 + 触发器（轻量标记对象）。
///
/// 用法：
///   · 每个教程步骤放一个空 GameObject，挂上此组件，填好 sentences / 门控 / phone 标志。
///   · 让对应的 <see cref="WorkItem.tutorialBox"/> 或 GameManager 的 phoneTutStarter 指向这个 GameObject。
///   · 该对象被 SetActive(true) 时（沿用旧的 WorkItem.Break / GameManager 激活逻辑），
///     它会通知 <see cref="TutorialDialogueController"/> 在共享的单 prefab 上播放本步内容；
///     被 SetActive(false) 时（WorkItem.Fix），通知控制器隐藏。
///
/// 本对象自身不含任何 UI，真正的对话框视觉由 TutorialDialogueController 持有的唯一 prefab 实例呈现。
/// </summary>
[AddComponentMenu("Tutorial/Tutorial Step Trigger")]
public class TutorialStepTrigger : MonoBehaviour
{
    // ─── 演出内容 ──────────────────────────────────────────────

    [Header("Animation — 本步起始动画（默认）")]
    [Tooltip("步骤开始时赋给 Animation 子物体 Animator 的 Controller。\n" +
             "作为「默认动画」：某句 sentence 未单独指定 animatorController 时沿用它。\n" +
             "留空则保持上一步的动画不变。")]
    public RuntimeAnimatorController animatorController;

    [Header("Sentences — 多段话（Sam 的台词）")]
    [Tooltip("按顺序播放的句子。每句可含独立音频与结束后开放门控的标志。\n" +
             "单句步骤（如 Monitor/Skull）填 1 条即可；复合步骤（如 Phone）填多条。")]
    public List<TutorialSentence> sentences = new List<TutorialSentence>();

    // ─── 门控（触发判定条件）──────────────────────────────────

    [Header("Trigger Gate — 触发判定条件")]
    [Tooltip("Disabled = 无门控（框只是显示，靠外部 WorkItem 修复来隐藏）。\n" +
             "ForceBreak / ForceBait = 在被标记 openGateAfterThisSentence 的句子结束后，\n" +
             "强制 gateWorkItem 进入 Broke/Bait，并等待玩家（Arduino/键盘）修好后再继续。")]
    public TutorialWorkItemGateMode gateMode = TutorialWorkItemGateMode.Disabled;

    [Tooltip("要被强制 Broke/Bait 并等待恢复的 WorkItem（须与 ArduinoSerialBridge 绑定的为同一引用，硬件才会联动）")]
    public WorkItem gateWorkItem;

    // ─── Phone 教程（GameManager 联动）────────────────────────

    [Header("Phone Tutorial — 与 GameManager 联动")]
    [Tooltip("本步为电话教程链「首句」时勾选：播放开始时调用 GameManager.RegisterPhoneTutorialStartLine()")]
    public bool isPhoneTutorialStartLine;

    [Tooltip("本步为电话教程链「末句」时勾选：全部句子/门控结束后调用 GameManager.RegisterPhoneTutorialLastLineCompleted()")]
    public bool isPhoneTutorialLastLine;

    [Tooltip("仅末句：通知 GameManager 后，等待此 WorkItem 被玩家修好（脱离 Broke/Bait）后本框才隐藏。\n" +
             "不填则退回使用 gateWorkItem；二者皆空则通知后立即完成。")]
    public WorkItem keepVisibleUntilRepaired;

    // ─── 完成行为 ──────────────────────────────────────────────

    [Header("Completion — 完成后")]
    [Tooltip("勾选：本步所有句子（含门控与末句等待）结束后，自动隐藏对话框并 SetActive(false) 本触发器。\n" +
             "  · Phone 这类自完成的链式步骤 → 勾选\n" +
             "  · WorkItem 驱动的单句框（靠 WorkItem.Fix 隐藏）→ 不勾选")]
    public bool hideBoxOnComplete = false;

    // ─── Debug ─────────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField] bool debugLog = false;

    public bool DebugLog => debugLog;

    // ─── 生命周期 ──────────────────────────────────────────────

    void OnEnable()
    {
        if (TutorialDialogueController.Instance != null)
            TutorialDialogueController.Instance.PlayStep(this);
        else
            Debug.LogWarning($"[TutorialStepTrigger] \"{name}\" 已激活，但场景中找不到 TutorialDialogueController。", this);
    }

    void OnDisable()
    {
        if (TutorialDialogueController.Instance != null)
            TutorialDialogueController.Instance.StopStep(this);
    }
}

/// <summary>教程步骤的门控方式（对齐旧 ShowNextBoxForTut.TutorialWorkItemGateMode）。</summary>
public enum TutorialWorkItemGateMode
{
    /// <summary>无门控。</summary>
    Disabled,
    /// <summary>强制 gateWorkItem 进入 Broke，等待修复。</summary>
    ForceBreak,
    /// <summary>强制 gateWorkItem 进入 Bait，等待恢复。</summary>
    ForceBait,
}

/// <summary>教程中的一句台词（+ 音频 + 是否在其后开放门控）。</summary>
[System.Serializable]
public class TutorialSentence
{
    [Tooltip("本句对应的 Animation Controller（如 Phone / Phone_D 等）。\n" +
             "非空则在本句开始时切换动画；留空则沿用当前动画（步骤默认或上一句）。")]
    public RuntimeAnimatorController animatorController;

    [TextArea(2, 4)]
    [Tooltip("Sam 的台词，显示到共享对话框的 DialogueText 上")]
    public string text;

    [Tooltip("这句话对应的语音 AudioClip；由控制器 PlayOneShot 播放。留空则不播音频，改用 noClipHoldSeconds 计时")]
    public AudioClip voiceClip;

    [Tooltip("音频播完后额外停留多少秒再进入下一句")]
    [Min(0f)]
    public float extraHoldSeconds = 0.3f;

    [Tooltip("修复该工位、对话框优雅关闭前的「extra hold seconds」停留期间，只临时隐藏这些 GameObject（此时对话框整体仍显示），停留结束后再整体关闭。\n" +
             "只隐藏这里指定的物体，不影响对话框整体（boxRoot 及其它子物体保持显示）。留空则关闭前不隐藏任何东西。")]
    public List<GameObject> hideDuringExtraHold = new List<GameObject>();

    [Tooltip("当 voiceClip 为空时，本句停留多少秒后进入下一句")]
    [Min(0f)]
    public float noClipHoldSeconds = 2.5f;

    [Tooltip("勾选：本句结束后，运行触发器上配置的门控（ForceBreak/ForceBait + 等待修复）。\n" +
             "即“在这一句之后开放触发判定条件”。")]
    public bool openGateAfterThisSentence;
}
