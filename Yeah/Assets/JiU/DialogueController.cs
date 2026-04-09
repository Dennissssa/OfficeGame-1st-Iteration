using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JiU
{
    public enum DialogueEndAction
    {
        HideDialogueUI,
        LoadSceneByBuildIndex
    }

    [System.Serializable]
    public class DialogueSpriteSwap
    {
        public GameObject target;
        public Sprite sprite;
    }

    [System.Serializable]
    public class DialogueLine
    {
        [TextArea(2, 6)]
        public string text;

        public AudioClip lineAudio;

        [Tooltip("与语音时长取较大值后再进入下一段（秒）。无音频时仅按此时间等待")]
        [Min(0f)]
        public float advanceDelaySeconds = 2f;

        [Tooltip("本句结束（进入下一句之前）执行的立绘/UI 换图")]
        public List<DialogueSpriteSwap> spriteSwapsWhenLineEnds = new List<DialogueSpriteSwap>();
    }

    /// <summary>
    /// 对话：逐句 TMP、配音、等待时间、换图；结束后关闭 UI 或加载场景。
    /// 建议在对话打开时把 Time.timeScale=0，本脚本用非缩放时间等待。
    /// </summary>
    public class DialogueController : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("整段对话根节点，Hide 时关闭")]
        public GameObject dialogueRoot;

        public TMP_Text dialogueText;

        [Header("对话内容")]
        public List<DialogueLine> lines = new List<DialogueLine>();

        [Header("配音")]
        [Tooltip("留空则自动在本物体上添加 AudioSource")]
        public AudioSource voiceSource;

        [Header("结束后")]
        public DialogueEndAction endAction = DialogueEndAction.HideDialogueUI;

        [Tooltip("endAction 为 LoadSceneByBuildIndex 时使用（Build Settings 顺序）")]
        public int sceneBuildIndexToLoad;

        [Tooltip("对话期间暂停游戏（Time.timeScale=0），结束时恢复为 1")]
        public bool pauseGameWhileDialogue = true;

        Coroutine _playRoutine;
        float _timeScaleBefore;

        void Awake()
        {
            if (voiceSource == null)
                voiceSource = GetComponent<AudioSource>();
            if (voiceSource == null)
                voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
        }

        /// <summary>开始从第一句播放；若已在播则先停止再重来。</summary>
        public void StartDialogue()
        {
            if (_playRoutine != null)
                StopCoroutine(_playRoutine);
            _playRoutine = StartCoroutine(PlayRoutine());
        }

        public void StopDialogueWithoutEndAction()
        {
            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }

            if (pauseGameWhileDialogue)
                Time.timeScale = _timeScaleBefore;
        }

        IEnumerator PlayRoutine()
        {
            if (lines == null || lines.Count == 0)
            {
                _playRoutine = null;
                yield break;
            }

            if (dialogueRoot != null)
                dialogueRoot.SetActive(true);

            _timeScaleBefore = Time.timeScale;
            if (pauseGameWhileDialogue)
                Time.timeScale = 0f;

            for (int i = 0; i < lines.Count; i++)
            {
                DialogueLine line = lines[i];
                if (dialogueText != null)
                    dialogueText.text = line.text ?? string.Empty;

                float waitAudio = 0f;
                if (line.lineAudio != null && voiceSource != null)
                {
                    voiceSource.PlayOneShot(line.lineAudio);
                    waitAudio = line.lineAudio.length;
                }

                float wait = Mathf.Max(line.advanceDelaySeconds, waitAudio);
                if (wait <= 0f)
                    wait = 0.05f;

                yield return new WaitForSecondsRealtime(wait);

                ApplySpriteSwaps(line.spriteSwapsWhenLineEnds);
            }

            if (pauseGameWhileDialogue)
                Time.timeScale = _timeScaleBefore;

            switch (endAction)
            {
                case DialogueEndAction.HideDialogueUI:
                    if (dialogueRoot != null)
                        dialogueRoot.SetActive(false);
                    break;
                case DialogueEndAction.LoadSceneByBuildIndex:
                    if (sceneBuildIndexToLoad >= 0 &&
                        sceneBuildIndexToLoad < SceneManager.sceneCountInBuildSettings)
                        SceneManager.LoadScene(sceneBuildIndexToLoad);
                    else
                        Debug.LogWarning(
                            $"{nameof(DialogueController)}: 无效场景索引 {sceneBuildIndexToLoad}",
                            this);
                    break;
            }

            _playRoutine = null;
        }

        static void ApplySpriteSwaps(List<DialogueSpriteSwap> swaps)
        {
            if (swaps == null) return;
            for (int i = 0; i < swaps.Count; i++)
            {
                DialogueSpriteSwap s = swaps[i];
                if (s?.target == null || s.sprite == null) continue;

                Image img = s.target.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = s.sprite;
                    continue;
                }

                SpriteRenderer sr = s.target.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.sprite = s.sprite;
            }
        }
    }
}
