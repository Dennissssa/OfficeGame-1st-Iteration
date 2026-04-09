using System.Collections;
using UnityEngine;

namespace JiU
{
    /// <summary>
    /// After play starts, wait a configurable time, then show dialogue root and start <see cref="DialogueController.StartDialogue"/>.
    /// Delay uses realtime, not affected by <see cref="Time.timeScale"/>.
    /// </summary>
    public class DialogueAutoStartAfterDelay : MonoBehaviour
    {
        [Tooltip("If unset, looks for DialogueController on this object")]
        public DialogueController dialogueController;

        [Min(0f)]
        [Tooltip("Seconds after this script's Start before dialogue begins")]
        public float delaySeconds = 1f;

        Coroutine _routine;

        void Start()
        {
            if (dialogueController == null)
                dialogueController = GetComponent<DialogueController>();

            if (dialogueController == null)
            {
                Debug.LogWarning($"{nameof(DialogueAutoStartAfterDelay)}: {nameof(DialogueController)} is not assigned.", this);
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
