# AudioManager 列表与 JiU 脚本对照

**推荐**：物品相关音效一律用 **`PlaySoundOnEventAudioManager`**（绑定 `WorkItem` + 各 `effectIndex`），不要从 `WorkItem` 里直接调 `PlaySound`。

仍可直接调用的场景（已做越界保护，无效下标不会抛异常）：

- 音效：`AudioManager.Instance.PlaySound(下标);` — 使用 **`EffectsSource` + `EffectsList[下标]`**（与 JiU 脚本一致）。
- 音乐：`AudioManager.Instance.PlayMusic(下标);` — 使用 **`MusicSource` + `MusicList[下标]`**，`Play()` 播放。

在 **AudioManager** 组件上把 `EffectsList`、`MusicList` 按顺序拖入音频；务必保证 **`EffectsSource` 已赋值**（**Boss 等全局音效**仍走该声道）；`EffectsSourceList` 仅作无 `EffectsSource` 时的可选回退。

**物品**：`PlaySoundOnEventAudioManager` 在未指定 **`localSfxSource`** 时会 **Add 专用 AudioSource**（每个组件一条声道，避免多个组件误共用同一 `AudioSource` 时，`OnFixed` 互相 `Stop` 把 Win/Lose 掐掉）。若物体上已有调好的 `AudioSource`，请**手动拖到** `Local Sfx Source`。同一物体挂**多个**本组件时，要么各拖不同声道，要么依赖自动各 Add 一条。

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
| 可选 | **Win** `repairCorrectEffectIndex`、**Lose** `repairWrongEffectIndex` | 仅 **真正损坏**（`IsBroken`）且击打修好 → Win；Bait/空闲乱按 → Lose（见 `WorkItem.TryRepair`） |
| — | **`skipBrokenAndBaitSounds`** | 勾选后本物品不自动播损坏/Bait/结束音，Win/Lose 仍可用；脚本在 **`OnFixed` 里不再对本声道 `Stop`**，避免修好瞬间把 Win/Lose 掐掉（同型 Lamp/Printer 等易踩坑） |

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
3. 物品音效：在每个 **WorkItem** 物体上挂 **`PlaySoundOnEventAudioManager`**，指定同一个 `WorkItem`，填写 `effectIndex`、`baitEffectIndex`（及可选的维修音下标）；或用 **UnityEvent** 调用 `Play()` / `PlayAtIndex`。

**注意**：**Boss / `JiUGameManagerBossAudio`** 仍共用全局 `EffectsSource`；**每个 WorkItem 上的 `PlaySoundOnEventAudioManager`** 使用自己的 `AudioSource`，与全局轨无关。
