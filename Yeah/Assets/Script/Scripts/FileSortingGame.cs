using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 文件分类小游戏主控制器（游戏流程版本）。
///
/// 继承自 <see cref="BaseFileSortingGame"/>，在公共逻辑之上增加：
///   · GameManager 集成（work 减少、UltraPunishment）
///   · BossWindowPerformance 演出触发
///   · 无限刷新循环（游戏结束时自动停止）
///
/// IntroScene 中请使用 <see cref="StoryFileSortingGame"/> 代替。
///
/// 场景层级建议：
///   Mini Game Panel (FileSortingGame)
///   ├── Spawn Area        (spawnArea 引用)
///   ├── Left Drop Zone    (DropZone, acceptedType = TypeA)
///   └── Right Drop Zone   (DropZone, acceptedType = TypeB)
///
/// File Prefab 要求：Image + SortableFile（CanvasGroup 运行时自动添加）。
/// </summary>
[AddComponentMenu("MiniGame/File Sorting Game")]
public class FileSortingGame : BaseFileSortingGame
{
    // ─── Work 减少 ─────────────────────────────────────────────

    [Header("Work Reduction")]
    [Tooltip("每次正确分类减少的 work 压力（GameManager 无 phase 配置时使用此值）")]
    [Min(0f)]
    public float workReductionPerCorrectSort = 5f;

    // ─── 初始化 ────────────────────────────────────────────────

    protected override IEnumerator OnAfterBaseInit()
    {
        int initialCount = Mathf.Min(maxFileCount, 3);
        Log($"[FileSortingGame] 初始刷新 {initialCount} 张");
        for (int i = 0; i < initialCount; i++)
            SpawnFile();

        _spawnCoroutine = StartCoroutine(SpawnLoop());
        Log("[FileSortingGame] 初始化完成，SpawnLoop 已启动。");
        yield break;
    }

    // ─── 刷新循环 ──────────────────────────────────────────────

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnIntervalSeconds);

            // 游戏结束时停止刷新
            if (GameManager.Instance != null &&
                (GameManager.Instance.IsGameOver || GameManager.Instance.IsVictory))
                yield break;

            _activeFiles.RemoveAll(f => f == null);
            if (_activeFiles.Count < maxFileCount)
                SpawnFile();
        }
    }

    // ─── 投放回调 ──────────────────────────────────────────────

    /// <summary>
    /// 玩家正确分类后由 SortableFile.OnEndDrag 调用。
    /// 销毁文件，减少 work 压力，触发 Boss 窗口正确分类演出。
    /// </summary>
    public override void OnCorrectDrop(SortableFile file)
    {
        DestroyFile(file);

        float reduction = workReductionPerCorrectSort;
        if (GameManager.Instance != null)
            reduction = GameManager.Instance.GetActiveSortWorkReduction(workReductionPerCorrectSort);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReduceWork(reduction); // 走统一入口，按 workDecreaseMultiplier 缩小回跳
            if (GameManager.Instance.ui != null)
                GameManager.Instance.ui.SetWork(GameManager.Instance.work);
        }

        // 触发 Boss 窗口正确分类演出（带冷却）
        BossWindowPerformance.Instance?.TriggerCorrectSort();

        Log($"[FileSortingGame] 正确分类 work-={reduction}");
    }

    /// <summary>
    /// 玩家错误分类后由 SortableFile.OnEndDrag 调用。
    /// 销毁文件，给予 workUltraPunishment，在 Boss 窗口播放对应区域的错误分类演出。
    /// 同时开启输入遮挡防止连续误操作。
    /// </summary>
    /// <param name="hitZone">玩家实际投入的 DropZone（用于区分左/右区演出）</param>
    public override void OnWrongDrop(SortableFile file, DropZone hitZone)
    {
        Log("[FileSortingGame] 错误分类，销毁文件 + 触发 Boss 窗口演出 + UltraPunishment。");

        SortableFile.FileType fileType = file.fileType;
        DestroyFile(file);

        if (GameManager.Instance != null)
            GameManager.Instance.UltraPunishment();

        // 根据命中区域选用对应演出：0 = 左区，1 = 右区
        int zoneIndex = (hitZone == leftDropZone) ? 0 : 1;
        BossWindowPerformance.Instance?.TriggerWrongSort(zoneIndex);

        // 按错误文件类型替换 Wrong Block Sprite，并遮挡输入
        StartWrongDropBlock(fileType);
    }
}
