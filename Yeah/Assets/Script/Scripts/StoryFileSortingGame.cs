using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// IntroScene 专用文件分类演出小游戏。
///
/// 与 GameManager 完全解耦，无 work 条、无 Boss、无胜负逻辑。
/// 分两段：
///   1. 先按 typeACount / typeBCount 生成规定数量的文件
///   2. 规定文件全部分完 → 触发 <see cref="onAllFilesSorted"/>（故事开始），
///      同时切到正常刷新（按权重持续补到 maxFileCount）
///
/// 正确/错误投放仍播放反馈演出，无扣分。
/// </summary>
[AddComponentMenu("MiniGame/Story File Sorting Game")]
public class StoryFileSortingGame : BaseFileSortingGame
{
    // ─── 关卡设计 ──────────────────────────────────────────────

    [Header("Story Mode — 文件数量分配")]
    [Tooltip("TypeA 文件（左侧投放区）的总数量")]
    [Min(0)]
    public int typeACount = 3;

    [Tooltip("TypeB 文件（右侧投放区）的总数量")]
    [Min(0)]
    public int typeBCount = 2;

    /// <summary>本局需处理的文件总数（= typeACount + typeBCount）。</summary>
    int TotalFilesToSort => typeACount + typeBCount;

    // ─── 正确反馈 ──────────────────────────────────────────────

    [Header("Correct Drop Feedback — 正确反馈")]
    [Tooltip("正确投放时播放的音频；留空则静默")]
    public AudioClip correctFeedbackClip;

    [Tooltip("正确投放时 SetActive(true) 的演出对象（音频播完后自动隐藏）；留空则忽略")]
    public GameObject correctFeedbackObject;

    [Tooltip("音频播完后额外保持显示时长（秒）；之后隐藏 correctFeedbackObject 并检测完成")]
    [Min(0f)]
    public float correctFeedbackHoldSeconds = 0f;

    // ─── 错误反馈 ──────────────────────────────────────────────

    [Header("Wrong Drop Feedback — 错误反馈")]
    [Tooltip("错误投放时播放的音频；留空则静默")]
    public AudioClip wrongFeedbackClip;

    [Tooltip("错误投放时 SetActive(true) 的演出对象（音频播完后自动隐藏）；留空则忽略")]
    public GameObject wrongFeedbackObject;

    [Tooltip("音频播完后额外保持显示时长（秒）；之后隐藏 wrongFeedbackObject 并检测完成")]
    [Min(0f)]
    public float wrongFeedbackHoldSeconds = 0f;

    // ─── 音频 ──────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("播放反馈音频的 AudioSource；留空则自动查找本 GameObject 上的 AudioSource")]
    public AudioSource feedbackAudioSource;

    // ─── 完成事件 ──────────────────────────────────────────────

    [Header("Completion — 完成事件")]
    [Tooltip("所有文件处理完毕后触发（在此连接故事剧情开始方法，例如 IntroManager.OnAllFilesSorted）")]
    public UnityEvent onAllFilesSorted;

    [Tooltip("最后一次反馈演出结束后、触发 onAllFilesSorted 前的额外延迟（秒）；\n" +
             "可用于让最后一个反馈动画自然结束再切剧情")]
    [Min(0f)]
    public float completionDelaySeconds = 0.5f;

    // ─── 运行时状态 ────────────────────────────────────────────

    int _sortedCount;    // 已处理（正确 + 错误）文件数
    int _spawnedTotal;   // 规定阶段已生成的文件数
    bool _storyTriggered;
    bool _endlessMode;
    AudioSource _audio;

    // 预先打乱好的文件类型队列，SpawnOneFile 每次从头取一个
    readonly System.Collections.Generic.Queue<SortableFile.FileType> _spawnQueue
        = new System.Collections.Generic.Queue<SortableFile.FileType>();

    // ─── 初始化 ────────────────────────────────────────────────

    protected override IEnumerator OnAfterBaseInit()
    {
        _audio = feedbackAudioSource != null
            ? feedbackAudioSource
            : GetComponent<AudioSource>();

        // 确保演出对象初始隐藏
        if (correctFeedbackObject != null) correctFeedbackObject.SetActive(false);
        if (wrongFeedbackObject   != null) wrongFeedbackObject.SetActive(false);

        // 构建并打乱文件类型队列
        BuildSpawnQueue();

        int total = TotalFilesToSort;
        if (total == 0)
        {
            LogError("[StoryFileSortingGame] typeACount + typeBCount = 0，没有文件可生成！");
            yield break;
        }

        // 初始刷新：不超过场内上限，也不超过总文件数
        int initialCount = Mathf.Min(maxFileCount, total);
        for (int i = 0; i < initialCount; i++)
            SpawnOneFile();

        _spawnCoroutine = StartCoroutine(SpawnLoop());
        Log($"[StoryFileSortingGame] 初始化完成，TypeA×{typeACount} TypeB×{typeBCount}，共 {total} 张。");
        yield break;
    }

    /// <summary>将 typeACount 个 TypeA 和 typeBCount 个 TypeB 打乱顺序放入队列。</summary>
    void BuildSpawnQueue()
    {
        // 先建列表再 Fisher-Yates 洗牌
        var list = new System.Collections.Generic.List<SortableFile.FileType>(typeACount + typeBCount);
        for (int i = 0; i < typeACount; i++) list.Add(SortableFile.FileType.TypeA);
        for (int i = 0; i < typeBCount; i++) list.Add(SortableFile.FileType.TypeB);

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        _spawnQueue.Clear();
        foreach (var t in list) _spawnQueue.Enqueue(t);
    }

    /// <summary>规定阶段从预设队列取类型；之后按基类权重随机。</summary>
    protected override SortableFile.FileType ChooseFileType()
    {
        if (_spawnQueue.Count > 0)
            return _spawnQueue.Dequeue();

        return base.ChooseFileType();
    }

    // ─── 刷新循环 ──────────────────────────────────────────────

    /// <summary>规定阶段只补齐配额；配额完成后按间隔持续补到场内上限。</summary>
    IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnIntervalSeconds);

            _activeFiles.RemoveAll(f => f == null);
            int slotsFree = maxFileCount - _activeFiles.Count;
            if (slotsFree <= 0) continue;

            if (!_endlessMode)
            {
                int remaining = TotalFilesToSort - _spawnedTotal;
                int toSpawn = Mathf.Min(remaining, slotsFree);
                for (int i = 0; i < toSpawn; i++)
                    SpawnOneFile();
            }
            else
            {
                for (int i = 0; i < slotsFree; i++)
                    SpawnFile();
            }
        }
    }

    void SpawnOneFile()
    {
        if (_spawnedTotal >= TotalFilesToSort) return;
        _spawnedTotal++;
        SpawnFile();
    }

    // ─── 投放回调 ──────────────────────────────────────────────

    /// <summary>正确投放：销毁文件，播放正确反馈，计数，检测完成。</summary>
    public override void OnCorrectDrop(SortableFile file)
    {
        DestroyFile(file);
        _sortedCount++;
        Log($"[StoryFileSortingGame] ✅ 正确分类 {_sortedCount}/{TotalFilesToSort}");
        StartCoroutine(CorrectFeedbackRoutine());
    }

    /// <summary>
    /// 错误投放：销毁文件，播放错误反馈，计数，检测完成。
    /// 无任何 GameManager 惩罚，仅演出。
    /// </summary>
    public override void OnWrongDrop(SortableFile file, DropZone hitZone)
    {
        SortableFile.FileType fileType = file.fileType;
        DestroyFile(file);
        _sortedCount++;
        Log($"[StoryFileSortingGame] ❌ 错误分类 {_sortedCount}/{TotalFilesToSort} type={fileType}");
        StartWrongDropBlock(fileType); // Wrong Block 显隐与输入遮挡都走 wrongDropBlockDuration
        StartCoroutine(WrongFeedbackRoutine());
    }

    // ─── 反馈演出协程 ──────────────────────────────────────────

    IEnumerator CorrectFeedbackRoutine()
    {
        if (correctFeedbackClip != null && _audio != null)
            _audio.PlayOneShot(correctFeedbackClip);

        if (correctFeedbackObject != null)
            correctFeedbackObject.SetActive(true);

        // 等音频播完
        if (correctFeedbackClip != null)
            yield return new WaitForSecondsRealtime(correctFeedbackClip.length);

        // 额外保持时长
        if (correctFeedbackHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(correctFeedbackHoldSeconds);

        if (correctFeedbackObject != null)
            correctFeedbackObject.SetActive(false);

        CheckCompletion();
    }

    IEnumerator WrongFeedbackRoutine()
    {
        if (wrongFeedbackClip != null && _audio != null)
            _audio.PlayOneShot(wrongFeedbackClip);

        if (wrongFeedbackObject != null)
            wrongFeedbackObject.SetActive(true);

        // 等音频播完
        if (wrongFeedbackClip != null)
            yield return new WaitForSecondsRealtime(wrongFeedbackClip.length);

        // 额外保持时长
        if (wrongFeedbackHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(wrongFeedbackHoldSeconds);

        if (wrongFeedbackObject != null)
            wrongFeedbackObject.SetActive(false);

        CheckCompletion();
    }

    // ─── 完成检测 ──────────────────────────────────────────────

    void CheckCompletion()
    {
        if (_storyTriggered) return;
        if (_sortedCount < TotalFilesToSort) return;

        _storyTriggered = true;
        _endlessMode = true;
        FillEmptySlots();
        StartCoroutine(CompletionRoutine());
    }

    void FillEmptySlots()
    {
        _activeFiles.RemoveAll(f => f == null);
        int slotsFree = maxFileCount - _activeFiles.Count;
        for (int i = 0; i < slotsFree; i++)
            SpawnFile();
    }

    IEnumerator CompletionRoutine()
    {
        Log("[StoryFileSortingGame] 规定文件已处理完毕，开始正常刷新并触发故事。");

        if (completionDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(completionDelaySeconds);

        Log("[StoryFileSortingGame] 触发 onAllFilesSorted → 故事剧情开始。");
        onAllFilesSorted?.Invoke();
    }
}
