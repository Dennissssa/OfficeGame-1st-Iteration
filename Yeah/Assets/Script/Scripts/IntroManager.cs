using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// IntroScene 开场流程管理器。
///
/// 负责协调文件分类演出小游戏与故事剧情之间的衔接：
///
///   开场延迟（introDelaySeconds）
///   → 播放小游戏前自述（IntroController.PlayPreamble，使用 DialogueBox）
///   → 隐藏 DialogueBox，激活/隐藏开场对象（小游戏窗口出现）
///   → 小游戏可操作（StoryFileSortingGame 此时才开始）
///   → 规定文件处理完毕（OnAllFilesSorted 回调）
///   → 隐藏/激活故事对象
///   → 触发 onStoryBegin UnityEvent（BeginDialogue 重新唤醒 DialogueBox）
///
/// 推荐挂载到 IntroScene 的管理器空 GameObject 上。
/// </summary>
[AddComponentMenu("Game/Intro Manager")]
public class IntroManager : MonoBehaviour
{
    // ─── 小游戏引用 ────────────────────────────────────────────

    [Header("Mini Game")]
    [Tooltip("IntroScene 中的 StoryFileSortingGame 组件；留空则跳过小游戏直接触发故事")]
    public StoryFileSortingGame sortingGame;

    [Tooltip("对话控制器；留空则在本物体上查找 IntroController")]
    public IntroController introController;

    // ─── 开场序列 ──────────────────────────────────────────────

    [Header("Intro Sequence — 开场序列")]
    [Tooltip("场景加载后等待多少秒再开始（用于等待开场 Logo、黑场淡入等）")]
    [Min(0f)]
    public float introDelaySeconds = 0f;

    [Tooltip("introDelay 结束后 SetActive(false) 的对象（如黑屏遮罩、开场 Logo）")]
    public GameObject[] objectsToHideBeforeGame;

    [Tooltip("introDelay 结束后 SetActive(true) 的对象（如小游戏 Panel）")]
    public GameObject[] objectsToShowBeforeGame;

    // ─── 故事开始 ──────────────────────────────────────────────

    [Header("Story Begin — 故事开始")]
    [Tooltip("小游戏结束后 SetActive(false) 的对象（如小游戏整体 Panel）")]
    public GameObject[] objectsToHideOnStoryBegin;

    [Tooltip("小游戏结束后 SetActive(true) 的对象（如对话框 Panel、过场背景）")]
    public GameObject[] objectsToShowOnStoryBegin;

    [Tooltip("所有文件分类完成、演出就绪后触发。\n" +
             "可在此绑定：对话系统启动、Animator 触发、场景切换等。")]
    public UnityEvent onStoryBegin;

    // ─── 音频 ──────────────────────────────────────────────────

    [Header("Audio (optional)")]
    [Tooltip("小游戏阶段播放的背景音乐；留空则忽略")]
    public AudioSource gameplayMusic;

    [Tooltip("故事开始时切换到的背景音乐；留空则忽略")]
    public AudioSource storyMusic;

    // ─── 运行时状态 ────────────────────────────────────────────

    bool _storyStarted;

    // ─── 生命周期 ──────────────────────────────────────────────

    void Start()
    {
        if (introController == null)
            introController = GetComponent<IntroController>();

        // 小游戏窗口等到自述结束后再显示，避免文件提前刷新
        SetActiveAll(objectsToShowBeforeGame, false);
        // 将"故事阶段"对象初始隐藏，防止闪烁
        SetActiveAll(objectsToShowOnStoryBegin, false);

        StartCoroutine(IntroSequence());
    }

    // ─── 开场序列 ──────────────────────────────────────────────

    IEnumerator IntroSequence()
    {
        // ── 1. 开场延迟（等待 Logo 动画、黑场等）────────────────
        if (introDelaySeconds > 0f)
            yield return new WaitForSeconds(introDelaySeconds);

        // ── 2. 小游戏前自述（DialogueBox）────────────────────────
        if (introController != null)
            yield return introController.PlayPreamble();

        // ── 3. 切换开场对象（显示小游戏窗口）────────────────────
        SetActiveAll(objectsToHideBeforeGame, false);
        SetActiveAll(objectsToShowBeforeGame, true);

        // ── 4. 播放小游戏音乐 ────────────────────────────────────
        if (gameplayMusic != null && !gameplayMusic.isPlaying)
            gameplayMusic.Play();

        // ── 5. 订阅小游戏完成事件 ───────────────────────────────
        if (sortingGame != null)
        {
            sortingGame.onAllFilesSorted.AddListener(OnAllFilesSorted);
        }
        else
        {
            // 未配置小游戏时直接触发故事（方便测试或跳过演出）
            Debug.LogWarning("[IntroManager] sortingGame 未赋值，将跳过小游戏直接触发故事开始。", this);
            OnAllFilesSorted();
        }
    }

    // ─── 小游戏完成回调 ────────────────────────────────────────

    /// <summary>
    /// 由 <see cref="StoryFileSortingGame.onAllFilesSorted"/> 触发，
    /// 或可直接在 Inspector 中连接到其他按钮/事件。
    /// </summary>
    public void OnAllFilesSorted()
    {
        if (_storyStarted) return;
        _storyStarted = true;
        StartCoroutine(StoryBeginSequence());
    }

    IEnumerator StoryBeginSequence()
    {
        // ── 1. 隐藏小游戏相关 UI ─────────────────────────────────
        SetActiveAll(objectsToHideOnStoryBegin, false);

        // ── 2. 切换音乐 ──────────────────────────────────────────
        if (gameplayMusic != null)
            gameplayMusic.Stop();
        if (storyMusic != null && !storyMusic.isPlaying)
            storyMusic.Play();

        // ── 3. 显示故事 UI ───────────────────────────────────────
        SetActiveAll(objectsToShowOnStoryBegin, true);

        // ── 4. 触发外部事件（对话系统、过场动画等）──────────────
        onStoryBegin?.Invoke();

        yield break;
    }

    // ─── 辅助 ──────────────────────────────────────────────────

    static void SetActiveAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        foreach (var obj in objects)
            if (obj != null) obj.SetActive(active);
    }
}
