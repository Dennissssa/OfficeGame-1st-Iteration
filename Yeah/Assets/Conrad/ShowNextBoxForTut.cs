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

    void Start()
    {
        StartCoroutine(beginDialogueNext());
    }

    IEnumerator beginDialogueNext()
    {
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
                    gateWorkItem.Break();
                else if (workItemGateMode == TutorialWorkItemGateMode.ForceBait)
                    gateWorkItem.Bait();

                yield return WaitUntilWorkItemIdle(gateWorkItem);
            }
        }

        yield return new WaitForSeconds(waitTime);
        if (nextBox != null)
            nextBox.SetActive(true);
        Destroy(gameObject);
    }

    static IEnumerator WaitUntilWorkItemIdle(WorkItem item)
    {
        yield return new WaitUntil(() => item != null && !item.IsBroken && !item.IsBaiting);
    }
}
