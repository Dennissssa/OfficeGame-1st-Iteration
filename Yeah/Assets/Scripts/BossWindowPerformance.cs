using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 窗口演出管理器（单例）。
///
/// 处理三类 Boss 窗口演出事件：
///
///   a) 错误分类（Wrong Sort）：
///      总是触发（会中断当前正在进行的 Hack / 正确分类 演出）。
///      根据 zoneIndex（0=左区，1=右区）选用对应的两段式演出配置。
///      两段式：播放 Clip1 + Active Object1 → 等音频结束 → Deactive Object1 →
///              等 WaitBetween → 播放 Clip2 + Active Object2 → 等音频结束 → Deactive Object2。
///      某一段的 Clip 与 Object 均为空时，该段直接跳过；Stage2 为空时也跳过段间等待。
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

    // ── 错误分类演出 · 左区 ───────────────────────────────────────────────

    [Header("─── 错误分类演出 · 左区 (Wrong Sort · Left Zone) ───")]
    [Tooltip("左区错误投放：第一段音频；Clip 与 Object 均留空则跳过整段")]
    public AudioClip wrongSortClip1;

    [Tooltip("左区错误投放：第一段显示的 GameObject；音频播完后自动隐藏")]
    public GameObject wrongSortObject1;

    [Tooltip("左区：第一段结束后到第二段开始的等待时间（秒）；Stage2 为空时忽略")]
    [Min(0f)]
    public float wrongSortWaitBetweenSeconds = 1f;

    [Tooltip("左区错误投放：第二段音频；Clip 与 Object 均留空则跳过整段")]
    public AudioClip wrongSortClip2;

    [Tooltip("左区错误投放：第二段显示的 GameObject；音频播完后自动隐藏")]
    public GameObject wrongSortObject2;

    // ── 错误分类演出 · 右区 ───────────────────────────────────────────────

    [Header("─── 错误分类演出 · 右区 (Wrong Sort · Right Zone) ───")]
    [Tooltip("右区错误投放：第一段音频；Clip 与 Object 均留空则跳过整段")]
    public AudioClip wrongSortRightClip1;

    [Tooltip("右区错误投放：第一段显示的 GameObject；音频播完后自动隐藏")]
    public GameObject wrongSortRightObject1;

    [Tooltip("右区：第一段结束后到第二段开始的等待时间（秒）；Stage2 为空时忽略")]
    [Min(0f)]
    public float wrongSortRightWaitBetweenSeconds = 1f;

    [Tooltip("右区错误投放：第二段音频；Clip 与 Object 均留空则跳过整段")]
    public AudioClip wrongSortRightClip2;

    [Tooltip("右区错误投放：第二段显示的 GameObject；音频播完后自动隐藏")]
    public GameObject wrongSortRightObject2;

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
        if (wrongSortObject1       != null) wrongSortObject1.SetActive(false);
        if (wrongSortObject2       != null) wrongSortObject2.SetActive(false);
        if (wrongSortRightObject1  != null) wrongSortRightObject1.SetActive(false);
        if (wrongSortRightObject2  != null) wrongSortRightObject2.SetActive(false);
        if (hackObject             != null) hackObject.SetActive(false);
        if (correctSortObject      != null) correctSortObject.SetActive(false);
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
    /// <param name="zoneIndex">命中区域：0 = 左区，1 = 右区</param>
    public void TriggerWrongSort(int zoneIndex = 0)
    {
        CancelHackAndCorrectSortPerformances();

        if (_wrongSortRoutine != null)
            StopCoroutine(_wrongSortRoutine);

        if (zoneIndex == 1)
        {
            _wrongSortRoutine = StartCoroutine(WrongSortRoutine(
                wrongSortRightClip1, wrongSortRightObject1,
                wrongSortRightWaitBetweenSeconds,
                wrongSortRightClip2, wrongSortRightObject2,
                suppressObjects: IsBossPhase()));
        }
        else
        {
            _wrongSortRoutine = StartCoroutine(WrongSortRoutine(
                wrongSortClip1, wrongSortObject1,
                wrongSortWaitBetweenSeconds,
                wrongSortClip2, wrongSortObject2,
                suppressObjects: false));
        }
    }

    /// <summary>
    /// 触发 Hack Boss 窗口演出。
    /// 有内置冷却；当前有演出进行时不触发。
    /// Boss 到来阶段（BossWarning 或 BossIsHere）时只播放音频，不显示对象。
    /// 由 GameManager.OnWorkItemEnteredHackedState() 调用。
    /// </summary>
    public void TriggerHack()
    {
        if (_hackCooldownRemaining > 0f) return;
        if (_performanceRunning) return;

        _hackCooldownRemaining = hackCooldownSeconds;

        if (_hackRoutine != null)
            StopCoroutine(_hackRoutine);

        _hackRoutine = StartCoroutine(HackRoutine(suppressObjects: IsBossPhase()));
    }

    /// <summary>
    /// 触发正确分类 Boss 窗口演出。
    /// 有内置冷却；当前有演出进行时不触发。
    /// Boss 到来阶段（BossWarning 或 BossIsHere）时只播放音频，不显示对象。
    /// 由 FileSortingGame.OnCorrectDrop() 调用。
    /// </summary>
    public void TriggerCorrectSort()
    {
        if (_correctSortCooldownRemaining > 0f) return;
        if (_performanceRunning) return;

        _correctSortCooldownRemaining = correctSortCooldownSeconds;

        if (_correctSortRoutine != null)
            StopCoroutine(_correctSortRoutine);

        _correctSortRoutine = StartCoroutine(CorrectSortRoutine(suppressObjects: IsBossPhase()));
    }

    // ── 协程实现 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 通用两段式错误演出协程。
    /// 某段的 clip 与 obj 均为 null 时跳过该段；Stage2 为空时也跳过段间等待。
    /// suppressObjects 为 true 时（Boss 到来阶段）只播放音频，不显示 GameObject。
    /// </summary>
    IEnumerator WrongSortRoutine(
        AudioClip clip1, GameObject obj1, float waitBetween,
        AudioClip clip2, GameObject obj2,
        bool suppressObjects = false)
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        bool hasStage1 = clip1 != null || obj1 != null;
        bool hasStage2 = clip2 != null || obj2 != null;

        // ── 第一段 ────────────────────────────────────────────────────────
        if (hasStage1)
        {
            if (clip1 != null && src != null)
                src.PlayOneShot(clip1);

            if (!suppressObjects && obj1 != null)
                obj1.SetActive(true);

            if (clip1 != null)
                yield return new WaitForSecondsRealtime(clip1.length);

            if (obj1 != null)
                obj1.SetActive(false);

            if (ShouldAbort())
            {
                CleanupWrongSort();
                yield break;
            }
        }

        // ── 段间等待（仅两段都有内容时才等待）────────────────────────────
        if (hasStage1 && hasStage2)
        {
            float wait = Mathf.Max(0f, waitBetween);
            if (wait > 0f)
                yield return new WaitForSecondsRealtime(wait);

            if (ShouldAbort())
            {
                CleanupWrongSort();
                yield break;
            }
        }

        // ── 第二段 ────────────────────────────────────────────────────────
        if (hasStage2)
        {
            if (clip2 != null && src != null)
                src.PlayOneShot(clip2);

            if (!suppressObjects && obj2 != null)
                obj2.SetActive(true);

            if (clip2 != null)
                yield return new WaitForSecondsRealtime(clip2.length);

            if (obj2 != null)
                obj2.SetActive(false);
        }

        _performanceRunning = false;
        _wrongSortRoutine = null;
    }

    IEnumerator HackRoutine(bool suppressObjects = false)
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        bool hasClip = hackClip != null;

        if (hasClip && src != null)
            src.PlayOneShot(hackClip);

        if (!suppressObjects && hackObject != null)
            hackObject.SetActive(true);

        if (hasClip)
            yield return new WaitForSecondsRealtime(hackClip.length);

        if (hackObject != null)
            hackObject.SetActive(false);

        _performanceRunning = false;
        _hackRoutine = null;
    }

    IEnumerator CorrectSortRoutine(bool suppressObjects = false)
    {
        _performanceRunning = true;
        AudioSource src = GetAudioSource();

        bool hasClip = correctSortClip != null;

        if (hasClip && src != null)
            src.PlayOneShot(correctSortClip);

        if (!suppressObjects && correctSortObject != null)
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

    /// <summary>当前是否处于 Boss 到来阶段（BossWarning 或 BossIsHere）。</summary>
    bool IsBossPhase()
    {
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.BossWarning || GameManager.Instance.BossIsHere;
    }

    void CleanupWrongSort()
    {
        if (wrongSortObject1      != null) wrongSortObject1.SetActive(false);
        if (wrongSortObject2      != null) wrongSortObject2.SetActive(false);
        if (wrongSortRightObject1 != null) wrongSortRightObject1.SetActive(false);
        if (wrongSortRightObject2 != null) wrongSortRightObject2.SetActive(false);
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
