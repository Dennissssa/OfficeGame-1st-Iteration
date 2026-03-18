# AudioManager 列表与 JiU 脚本对照

合作方用法：

- 音效：`AudioManager.Instance.PlaySound(下标);`
- 音乐：`AudioManager.Instance.PlayMusic(下标);`

在 **AudioManager** 组件上把 `EffectsList`、`MusicList` 按顺序拖入音频；再在 JiU 脚本里填**相同的下标**。

---

## 推荐 EffectsList 顺序（可按项目改名，只要下标一致）

| 下标 | 建议用途 | JiU 脚本字段 |
|------|----------|----------------|
| 0 | Boss 预警循环（如 `Boss Coming`） | `JiUGameManagerBossAudio.bossWarningSfxIndex` |
| 1 | Boss 到达（如 `Boss Laughing`） | `bossArrivedSfxIndex` |
| 2 | Boss 导致 Game Over（如 `Boss Anger`） | `bossGameOverAngerSfxIndex` |
| 3+ | 物品 **损坏** `effectIndex`（可选 `loopBrokenSound` 循环至修好） | `PlaySoundOnEventAudioManager` |
| 4+ | 物品 **Bait** 开始 `baitEffectIndex` | 同上，可与损坏不同下标 |
| 可选 | Bait 倒计时自然结束 `baitEndedEffectIndex` | 如“时间到”短音 |

可选：`bossLeftSfxIndex` 指向任意一个 Effects 下标（例如离开提示音）。

**Bait 音频**：`baitEffectIndex` 进入 Bait 时播；勾选 `loopBaitSound` 可循环至修好或时间到。玩家提前修好会走 `OnFixed` 并 Stop。时间到会先播 `baitEndedEffectIndex`（若有），再触发 `OnFixed`（脚本会避免同一帧 Stop 把结束音切掉）。

---

## MusicList

| 下标 | 建议用途 |
|------|----------|
| 0 | 平时 BGM → 填到 `musicAfterBossLeavesIndex` |
| 1 | Boss 检查期间紧张 BGM → 填到 `musicDuringBossStayIndex` |

不需要换 BGM 时，两个音乐下标都设为 **-1**。

---

## 场景挂载

1. 场景里已有 **AudioManager**（Effects / Music 两个 AudioSource + 两个 List 已填好）。
2. 挂 **`JiUGameManagerBossAudio`** 到任意物体，按上表填 Inspector 下标。
3. 物品音效：在物件上挂 **`PlaySoundOnEventAudioManager`**，指定 `WorkItem` 与 `effectIndex`；或用 **UnityEvent** 调用 `Play()` / `PlayAtIndex`。

**注意**：所有特效共用一个 `EffectsSource`，同时只能播一条特效轨；预警 Boss 会临时打开 loop，Boss 到达时会自动关掉。
