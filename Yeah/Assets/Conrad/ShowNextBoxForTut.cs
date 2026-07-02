using System.Collections;
using UnityEngine;

/// <summary>
/// 教程对话推进：默认仅等待 <see cref="waitTime"/> 后显示 <see cref="nextBox"/>。
/// 可选在显示前强制指定 <see cref="WorkItem"/> 进入 Broke/Bait，等其恢复常态后再走等待与下一步。
/// 串口硬件由场景中 <see cref="ArduinoSerialBridge"/> 对该 WorkItem 的 OnBroken/OnBaiting 绑定驱动（无需在此脚本直接写串口）。
/// </summary>
public class ShowNextBoxForTut : MonoBehaviour
{
    public enum TutorialWorkItemGateMode
    {
        Disabled,
        ForceBreak,
        ForceBait
    }

    public GameObject nextBox;
    public float waitTime;

    [Header("可选：WorkItem + Arduino 门控")]
    [Tooltip("Disabled = 保持旧行为。ForceBreak/ForceBait = 先触发对应状态，等 IsBroken/IsBaiting 均恢复为 false 后再执行 waitTime 与 nextBox。")]
    [SerializeField] private TutorialWorkItemGateMode workItemGateMode = TutorialWorkItemGateMode.Disabled;

    [Tooltip("要进入 Broke/Bait 并等待恢复的 WorkItem（须与 ArduinoSerialBridge 里绑定的为同一引用，串口才会联动）。")]
    [SerializeField] private WorkItem gateWorkItem;

    [Header("Phone 教程（GameManager）")]
    [Tooltip("本框为电话教程链的「首句」时勾选；可选，用于在播放开始时重置“末句已完成”门闩。通常与 phoneTutStarter 根上第一条一致。")]
    [SerializeField] private bool isPhoneTutorialStartLine;

    [Tooltip("本框为电话教程链的「末句」时务必勾选；在本框流程全部结束（门控、wait、激活 next）之后通知 GameManager，其才会在教程中让电话进入 Broke。")]
    [SerializeField] private bool isPhoneTutorialLastLine;

    [Tooltip("仅末句：在通知 GameManager 之后、销毁本对象之前，等待此 WorkItem 被玩家修好（脱离 Broke/Bait），末句框才会从屏幕上消失。不填则对 gateWorkItem 使用同一规则；二者皆空则只通知、随后立刻销毁。")]
    [SerializeField] private WorkItem keepLastLineVisibleUntilRepaired;

    [Tooltip("输出门控/末句/GameManager 通知，与 GameManager.debugLogTutorialPhoneBreakFlow、WorkItem.debugLogBreakBaitSkips 一起查 Bait 与 Broke 竞态。")]
    [SerializeField] private bool debugLog;

    void Start()
    {
        StartCoroutine(beginDialogueNext());
    }

    IEnumerator beginDialogueNext()
    {
        if (isPhoneTutorialStartLine && GameManager.Instance != null)
            GameManager.Instance.RegisterPhoneTutorialStartLine();

        if (workItemGateMode != TutorialWorkItemGateMode.Disabled)
        {
            if (gateWorkItem == null)
            {
                Debug.LogWarning(
                    $"[ShowNextBoxForTut] \"{name}\" 已启用 workItemGateMode={workItemGateMode} 但未指定 gateWorkItem，将跳过门控。",
                    this);
            }
            else
            {
                yield return WaitUntilWorkItemIdle(gateWorkItem);

                if (workItemGateMode == TutorialWorkItemGateMode.ForceBreak)
                {
                    if (debugLog)
                    {
                        Debug.Log(
                            $"[ShowNextBoxForTut] \"{name}\" 调用 gateWorkItem.Break() 前: IsBroken={gateWorkItem.IsBroken} IsBaiting={gateWorkItem.IsBaiting}",
                            this);
                    }
                    gateWorkItem.Break();
                }
                else if (workItemGateMode == TutorialWorkItemGateMode.ForceBait)
                {
                    if (debugLog)
                    {
                        Debug.Log(
                            $"[ShowNextBoxForTut] \"{name}\" 调用 gateWorkItem.Bait() 前: IsBroken={gateWorkItem.IsBroken} IsBaiting={gateWorkItem.IsBaiting}",
                            this);
                    }
                    gateWorkItem.Bait();
                }

                yield return WaitUntilWorkItemIdle(gateWorkItem);
            }
        }

        yield return new WaitForSeconds(waitTime);
        if (nextBox != null)
            nextBox.SetActive(true);

        if (isPhoneTutorialLastLine)
        {
            if (GameManager.Instance != null)
            {
                if (debugLog)
                {
                    Debug.Log(
                        $"[ShowNextBoxForTut] \"{name}\" → RegisterPhoneTutorialLastLineCompleted（本帧后 GameManager 可继续，下一轮将对队尾电话 tutorial Break）",
                        this);
                }
                GameManager.Instance.RegisterPhoneTutorialLastLineCompleted();
            }

            WorkItem waitFix = keepLastLineVisibleUntilRepaired != null
                ? keepLastLineVisibleUntilRepaired
                : gateWorkItem;
            if (waitFix != null)
                yield return WaitUntilBrokeOrBaitThenRepairedForLastLine(waitFix);
            else if (GameManager.Instance != null)
                Debug.LogWarning(
                    $"[ShowNextBoxForTut] \"{name}\" 为电话教程末句但未设置 keepLastLineVisibleUntilRepaired 且无 gateWorkItem，末句 UI 会立刻被销毁。",
                    this);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 等对方 Broke/（或 Bait）出现后再等修好，避免在 GameManager 尚未 <see cref="WorkItem.Break"/> 前一帧就误判为已修好。
    /// </summary>
    static IEnumerator WaitUntilBrokeOrBaitThenRepairedForLastLine(WorkItem item)
    {
        if (item == null)
            yield break;

        yield return new WaitUntil(() => item == null || item.IsBroken || item.IsBaiting);
        if (item == null)
            yield break;

        yield return new WaitUntil(() => item == null || (!item.IsBroken && !item.IsBaiting));
    }

    static IEnumerator WaitUntilWorkItemIdle(WorkItem item)
    {
        yield return new WaitUntil(() => item != null && !item.IsBroken && !item.IsBaiting);
    }
}
