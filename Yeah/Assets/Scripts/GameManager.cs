using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ✅ 新增：Boss预警开始到Boss离开期间冻结“产生新故障”
    public static bool FreezeFailures { get; private set; } = false;

    [Header("Work Value")]
    public float work = 0f;
    public float maxWork = 100f; // 顶峰值：到这个值不再加，但可以减
    public float workPunishment;
    public float workGainPerSecondPerWorkingItem = 1f;
    public float workLossPerSecondPerBrokenItem = 3f;
    public float bossMinWorkThreshold = 20f; // 上司检查时 work 必须 >= 这个值，否则输

    [Header("Boss Timing")]
    public float bossMinArriveInterval = 10f;
    public float bossMaxArriveInterval = 25f;
    public float bossWarningDuration = 3f;  // ✅ 你要“来之前3秒”：这里直接默认 3（你也可在Inspector调）
    public float bossStayDuration = 6f;     // 上司检查停留时长

    [Header("References")]
    public UIManager ui;
    public ScreenVignetteTint screenTint;

    [Header("Boss 事件（可选）")]
    [Tooltip("Boss 预警开始时触发（可用来播预警音效）")]
    public UnityEvent OnBossWarningStarted;
    [Tooltip("Boss 实际到达时触发（可用来关预警音、换 UI 图）")]
    public UnityEvent OnBossArrived;
    [Tooltip("Boss 离开时触发（可用来把 UI 图换回）")]
    public UnityEvent OnBossLeft;

    [Tooltip("因 Boss 检查导致游戏失败时触发（可用来播 Boss 生气音效）")]
    public UnityEvent OnGameOverBossCaused;

    [Header("Optional")]
    public bool autoFindItemsOnStart = true;

    private readonly List<WorkItem> items = new List<WorkItem>();

    private float surviveTime = 0f;
    private bool isGameOver = false;

    public bool BossIsHere { get; private set; } = false;
    public bool BossWarning { get; private set; } = false;
    public float BossWarningTimeLeft { get; private set; } = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        work = Mathf.Clamp(work, 0f, maxWork);

        if (autoFindItemsOnStart)
        {
            items.Clear();
            items.AddRange(FindObjectsOfType<WorkItem>());
        }

        if (ui != null)
        {
            ui.InitWorkSlider(maxWork);
            ui.SetWork(work);
            ui.SetTime(0f);
            ui.HideBossCountdown();
            ui.HideGameOver();
        }

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.01f);

        FreezeFailures = false;
        StartCoroutine(BossLoop());
    }

    void Update()
    {
        if (isGameOver) return;

        surviveTime += Time.deltaTime;

        int working = 0;
        int broken = 0;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;
            if (items[i].IsBroken) broken++;
            else working++;
        }

        // 增加：不超过 maxWork
        if (work < maxWork)
        {
            work += working * workGainPerSecondPerWorkingItem * Time.deltaTime;
            if (work > maxWork) work = maxWork;
        }

        // 减少：始终允许（即使满了也可以往下掉）
        work -= broken * workLossPerSecondPerBrokenItem * Time.deltaTime;
        if (work < 0f) work = 0f;

        // 上司在场：任何故障 或 work 低于阈值 => 输
        if (BossIsHere)
        {
            if (broken > 0)
                GameOver("Boss saw broken items!");
            else if (work < bossMinWorkThreshold)
                GameOver("Work is too low!");
        }

        // UI
        if (ui != null)
        {
            ui.SetWork(work);
            ui.SetTime(surviveTime);

            if (BossWarning) ui.SetBossCountdown(BossWarningTimeLeft);
            else ui.HideBossCountdown();
        }
    }

    public void Punishment()
    {
        work = work - 10;
    }

    IEnumerator BossLoop()
    {
        while (!isGameOver)
        {
            float wait = Random.Range(bossMinArriveInterval, bossMaxArriveInterval);
            yield return new WaitForSeconds(wait);
            if (isGameOver) yield break;

            // ✅ 从预警开始就冻结“产生新故障”
            FreezeFailures = true;

            // 预警阶段：倒计时 + 屏幕逐渐变红
            BossWarning = true;
            BossWarningTimeLeft = bossWarningDuration;
            OnBossWarningStarted?.Invoke();

            if (screenTint != null)
                screenTint.SetTarget(1f, bossWarningDuration);

            while (BossWarningTimeLeft > 0f && !isGameOver)
            {
                BossWarningTimeLeft -= Time.deltaTime;
                yield return null;
            }

            BossWarning = false;
            BossWarningTimeLeft = 0f;

            if (isGameOver) yield break;

            // 上司到达：检查停留
            BossIsHere = true;
            OnBossArrived?.Invoke();
            if (screenTint != null)
                screenTint.SetTarget(0.35f, 0.15f);

            float stay = bossStayDuration;
            while (stay > 0f && !isGameOver)
            {
                stay -= Time.deltaTime;
                yield return null;
            }

            BossIsHere = false;
            OnBossLeft?.Invoke();

            // ✅ Boss 离开后解除冻结
            FreezeFailures = false;

            // 恢复原状
            if (screenTint != null)
                screenTint.SetTarget(0f, 0.6f);
        }
    }

    public void RegisterItem(WorkItem item)
    {
        if (item == null) return;
        if (!items.Contains(item)) items.Add(item);
    }

    public void UnregisterItem(WorkItem item)
    {
        if (item == null) return;
        items.Remove(item);
    }

    public void GameOver(string reason)
    {
        if (isGameOver) return;
        isGameOver = true;

        StopAllCoroutines();
        BossWarning = false;
        BossIsHere = false;

        FreezeFailures = false;

        // 因 Boss 检查导致失败时触发（先触发以便播生气音效，再暂停）
        bool bossCaused = reason != null && (reason.Contains("Boss") || reason.Contains("Work is too low"));
        if (bossCaused)
            OnGameOverBossCaused?.Invoke();

        if (screenTint != null)
            screenTint.SetTarget(0f, 0.1f);

        if (ui != null)
            ui.ShowGameOver(surviveTime, work, reason);

        // 暂停游戏场景，仅 UI 可交互，保证失败音效能正常播完
        Time.timeScale = 0f;
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Quit()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }
}
