using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 窗口演出管理器（单例）。
///
/// 处理三类 Boss 窗口演出事件：
///
///   a) 错误分类（Wrong Sort）：
///      总是触发（会中断当前正在进行的 Hack / 正确分类 演出）。
///      两段式演出：播放 Clip1 + Active Object1 → 等音频结束 → Deactive Object1 →
///      等 wrongSortWaitBetweenSeconds → 播放 Clip2 + Active Object2 → 等音频结束 → Deactive Object2。
///
///   b) Hack 触发（Hack Performance）：
///      有内置冷却；演出进行中不触发。
///      播放 Clip + Active Object → 等音频结束 → Deactive Object。
///
///   c) 正确分类（Correct Sort Performance）：
///      有内置冷却；演出进行中不触发。
///      播放 Clip + Active Object → 等音频结束 → Deactive Object。
///
/// 所有协程使用 WaitForSecondsRealtime 确保在 timeScale = 0 时仍能完成，
/// 并在结束时检查 GameManager 状态以清理 GameObject。
/// </summary>
[AddComponentMenu("Game/Boss Window Performance")]
public class BossWindowPerformance : MonoBehaviour
{
    public static BossWindowPerformance Instance { get; private set; }

    // ── 共享音频源 ────────────────────────────────────────────────────────

    [Header("Audio Source")]
    [Tooltip("Boss 窗口所有演出共用的 AudioSource；留空则自动查找本 GameObject 上的 AudioSource")]
    public AudioSource bossWindowAudioSource;

    // ── 错误分类演出 ──────────────────────────────────────────────────────

    [Header("─── 错误分类演出 (Wrong Sort) ───")]
    [Tooltip("演出第一段播放的音频；留空则跳过第一段音频等待")]
    public AudioClip wrongSortClip1;

    [Tooltip("演出第一段 Active 的 GameObject（音频播完后自动 Deactive）；留空则忽略")]
    public GameObject wrongSortObject1;

    [Tooltip("第一段演出结束后等待此时间（秒）再开始第二段")]
    [Min(0f)]
    public float wrongSortWaitBetweenSeconds = 1f;

    [Tooltip("演出第二段播放的音频；留空则跳过第二段音频等待")]
    public AudioClip wrongSortClip2;

    [Tooltip("演出第二段 Active 的 GameObject（音频播完后自动 Deactive）；留空则忽略")]
    public GameObject wrongSortObject2;

    // ── Hack 演出 ─────────────────────────────────────────────────────────

    [Header("─── Hack 触发演出 ───")]
    [Tooltip("Hack 触发时播放的音频；留空则跳过音频等待")]
    public AudioClip hackClip;

    [Tooltip("Hack 触发时 Active 的 GameObject（音频播完后自动 Deactive）；留空则忽略")]
    public GameObject hackObject;

    [Tooltip("Hack 演出的内置冷却（秒）；冷却期间即使再次 Hack 也不重复播放")]
    [Min(0f)]
    public float hackCooldownSeconds = 5f;

    // ── 正确分类演出 ──────────────────────────────────────────────────────

    [Header("─── 正确分类演出 (Correct Sort) ───")]
    [Tooltip("正确分类时播放的音频；留空则跳过音频等待")]
    public AudioClip correctSortClip;

    [Tooltip("正确分类时 Active 的 GameObject（音频播完后自动 Deactive）；留空则忽略")]
    public GameObject correctSortObject;

    [Tooltip("正确分类演出的内置冷却（秒）；冷却期间不重复触发")]
    [Min(0f)]
    public float correctSortCooldownSeconds = 3f;

    // ── 运行时状态 ────────────────────────────────────────────────────────

    float _hackCooldownRemaining;
    float _correctSortCooldownRemaining;

    Coroutine _wrongSortRoutine;
    Coroutine _hackRoutine;
    Coroutine _correctSortRoutine;

    /// <summary>当前是否有演出正在进行（Hack / 正确分类 演出会检查此标志）。</summary>
    bool _performanceRunning;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // 确保所有演出 GameObject 初始状态为隐藏
        if (wrongSortObject1 != null) wrongSortObject1.SetActive(false);
        if (wrongSortObject2 != null) wrongSortObject2.SetActive(false);
        if (hackObject       != null) hackObject.SetActive(false);
        if (correctSortObject != null) correctSortObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (_hackCooldownRemaining > 0f)
            _hackCooldownRemaining -= Time.deltaTime;

        if (_correctSortCooldownRemaining > 0f)
            _correctSortCooldownRemaining -= Time.deltaTime;
    }

    // ── 公共触发接口 ──────────────────────────────────────────────────────

    /// <summary>
    /// 触发错误分类 Boss 窗口演出。
    /// 总是触发（中断当前 Hack / 正确分类 演出）。
    /// 由 FileSortingGame.OnWrongDrop() 在执行 UltraPunishment 后调用。
    /// </summary>
    public void TriggerWrongSort()
    {
        CancelHackAndCorrectSortPerformances();

        if (_wrongSortRoutine != null)
            StopCoroutine(_wrongSortRoutine);

        _wrongSortRoutine = StartCoroutine(WrongSortRoutine());
    }

    /// <summary>
    /// 触发 Hack Boss 窗口演出。
    /// 有内置冷却；当前有演出进行时不触发。
    /// 由 GameManager.OnWorkItemEnteredHackedState() 调用。
    /// </summary>
    public void TriggerHack()
    {
        if (_hackCooldownRemaining > 0f) return;
        if (_performanceRunning) return;

        _hackCooldownRemaining = hackCooldownSeconds;

        if (_hackRoutine != null)
            StopCoroutine(_hackRoutine);

        _hackRoutine = StartCoroutine(HackRoutine());
    }

    /// <summary>
    /// 触发正确分类 Boss 窗口演出。
    /// 有内置冷却；当前有演出进行时不触发。
    /// 由 FileSortingGame.OnCorrectDrop() 调用。
    /// </summary>
    public void TriggerCorrectSort()
    {
        if (_correctSortCooldownRemaining > 0f) return;
        if (_performanceRunning) return;

        _correctSortCooldownRemaining = correctSortCooldownSeconds;

        if (_correctSortRoutine != null)
            StopCoroutine(_correctSortRoutine);

        _correctSortRoutine = StartCoroutine(CorrectSortRoutine());
    }

    // ── 协程实现 ──────────────────────────────────────────────────────────

    IEnumerator WrongSortRoutine()
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        // ── 第一段 ────────────────────────────────────────────────────────
        bool hasClip1 = wrongSortClip1 != null;

        if (hasClip1 && src != null)
            src.PlayOneShot(wrongSortClip1);

        if (wrongSortObject1 != null)
            wrongSortObject1.SetActive(true);

        if (hasClip1)
            yield return new WaitForSecondsRealtime(wrongSortClip1.length);

        if (wrongSortObject1 != null)
            wrongSortObject1.SetActive(false);

        // 游戏结束则清理退出
        if (ShouldAbort())
        {
            CleanupWrongSort();
            yield break;
        }

        // ── 间隔等待 ──────────────────────────────────────────────────────
        float wait = Mathf.Max(0f, wrongSortWaitBetweenSeconds);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait);

        if (ShouldAbort())
        {
            CleanupWrongSort();
            yield break;
        }

        // ── 第二段 ────────────────────────────────────────────────────────
        bool hasClip2 = wrongSortClip2 != null;

        if (hasClip2 && src != null)
            src.PlayOneShot(wrongSortClip2);

        if (wrongSortObject2 != null)
            wrongSortObject2.SetActive(true);

        if (hasClip2)
            yield return new WaitForSecondsRealtime(wrongSortClip2.length);

        if (wrongSortObject2 != null)
            wrongSortObject2.SetActive(false);

        _performanceRunning = false;
        _wrongSortRoutine = null;
    }

    IEnumerator HackRoutine()
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        bool hasClip = hackClip != null;

        if (hasClip && src != null)
            src.PlayOneShot(hackClip);

        if (hackObject != null)
            hackObject.SetActive(true);

        if (hasClip)
            yield return new WaitForSecondsRealtime(hackClip.length);

        if (hackObject != null)
            hackObject.SetActive(false);

        _performanceRunning = false;
        _hackRoutine = null;
    }

    IEnumerator CorrectSortRoutine()
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        bool hasClip = correctSortClip != null;

        if (hasClip && src != null)
            src.PlayOneShot(correctSortClip);

        if (correctSortObject != null)
            correctSortObject.SetActive(true);

        if (hasClip)
            yield return new WaitForSecondsRealtime(correctSortClip.length);

        if (correctSortObject != null)
            correctSortObject.SetActive(false);

        _performanceRunning = false;
        _correctSortRoutine = null;
    }

    // ── 内部辅助 ──────────────────────────────────────────────────────────

    AudioSource GetAudioSource()
    {
        if (bossWindowAudioSource != null)
            return bossWindowAudioSource;
        return GetComponent<AudioSource>();
    }

    bool ShouldAbort()
    {
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.IsGameOver || GameManager.Instance.IsVictory;
    }

    void CleanupWrongSort()
    {
        if (wrongSortObject1 != null) wrongSortObject1.SetActive(false);
        if (wrongSortObject2 != null) wrongSortObject2.SetActive(false);
        _performanceRunning = false;
        _wrongSortRoutine = null;
    }

    void CancelHackAndCorrectSortPerformances()
    {
        if (_hackRoutine != null)
        {
            StopCoroutine(_hackRoutine);
            _hackRoutine = null;
        }

        if (_correctSortRoutine != null)
        {
            StopCoroutine(_correctSortRoutine);
            _correctSortRoutine = null;
        }

        if (hackObject        != null) hackObject.SetActive(false);
        if (correctSortObject != null) correctSortObject.SetActive(false);

        _performanceRunning = false;
    }
}
