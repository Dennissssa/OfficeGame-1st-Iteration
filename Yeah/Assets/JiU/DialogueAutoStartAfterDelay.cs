using System.Collections;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// 游戏开始后等待可编辑的秒数，再显示 Dialogue Root 并开始播放对话（调用 <see cref="DialogueController.StartDialogue"/>）。
    /// 延迟使用真实时间，不受 <see cref="Time.timeScale"/> 影响。
    /// </summary>
    public class DialogueAutoStartAfterDelay : MonoBehaviour
    {
        [Tooltip("留空则在本物体上查找 DialogueController")]
        public DialogueController dialogueController;

        [Min(0f)]
        [Tooltip("从本脚本 Start 起，等待多少秒再开始对话")]
        public float delaySeconds = 1f;

        Coroutine _routine;

        void Start()
        {
            if (dialogueController == null)
                dialogueController = GetComponent<DialogueController>();

            if (dialogueController == null)
            {
                Debug.LogWarning($"{nameof(DialogueAutoStartAfterDelay)}: 未指定 {nameof(DialogueController)}。", this);
                return;
            }

            _routine = StartCoroutine(RunAfterDelay());
        }

        void OnDestroy()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        IEnumerator RunAfterDelay()
        {
            if (delaySeconds > 0f)
                yield return new WaitForSecondsRealtime(delaySeconds);

            if (dialogueController != null)
                dialogueController.StartDialogue();
        }
    }
}
