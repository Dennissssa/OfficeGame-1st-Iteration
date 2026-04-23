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

        [Tooltip("Wait max(this, voice length) seconds before next line. No audio: wait this only")]
        [Min(0f)]
        public float advanceDelaySeconds = 2f;

        [Tooltip("Portrait/UI sprite swaps when this line ends (before next line)")]
        public List<DialogueSpriteSwap> spriteSwapsWhenLineEnds = new List<DialogueSpriteSwap>();
    }

    /// <summary>
    /// Dialogue: TMP per line, voice, wait, sprite swaps; end hides UI or loads a scene.
    /// With Time.timeScale=0 during dialogue, this script waits using realtime.
    /// </summary>
    public class DialogueController : MonoBehaviour
    {
        [Header("UI")]
        [Tooltip("Root for whole dialogue; hidden on end action Hide")]
        public GameObject dialogueRoot;

        public TMP_Text dialogueText;

        [Header("Lines")]
        public List<DialogueLine> lines = new List<DialogueLine>();

        [Header("Voice")]
        [Tooltip("If unset, adds AudioSource on this object")]
        public AudioSource voiceSource;

        [Header("When finished")]
        public DialogueEndAction endAction = DialogueEndAction.HideDialogueUI;

        [Tooltip("Used when endAction is LoadSceneByBuildIndex (Build Settings order)")]
        public int sceneBuildIndexToLoad;

        [Tooltip("Pause game during dialogue (Time.timeScale=0); restore to previous on end")]
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

        /// <summary>Play from first line; if already playing, stop and restart.</summary>
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
                            $"{nameof(DialogueController)}: invalid scene build index {sceneBuildIndexToLoad}",
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
