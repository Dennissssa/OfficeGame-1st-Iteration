# Baiting 逻辑说明（GameManager & WorkItem）

本文档描述现有 Baiting（诱饵/假故障）相关逻辑，便于在 JiU 下做扩展时参考。

---

## 一、WorkItem 中的 Baiting

### 1. 状态与外观

| 成员 | 说明 |
|------|------|
| `IsBaiting` | 只读属性，表示当前是否为“诱饵”状态（假故障，绿色） |
| `baitColor` | 诱饵状态下的着色（默认绿色 `0.2, 1, 0.2`） |
| `OnBaiting` | UnityEvent，进入诱饵状态时触发（可接音效、Uduino 等） |

### 2. 触发方式（BreakLoop）

- 与普通故障共用同一协程 `BreakLoop()`。
- 在 **未冻结**（`!GameManager.FreezeFailures`）且等待随机时间后：
  - `Random.Range(0, 6)`：**0~4 → Break()**，**5 → Bait()**  
  即约 **1/6 概率** 为 Bait，5/6 为真实故障。
- 当 `IsBroken || IsBaiting` 为真时，只 `yield return null`，不会触发新的 Break/Bait。

### 3. Bait() 流程

1. 若已 `IsBaiting` 则直接 return。
2. `IsBaiting = true`。
3. 用 `baitColor` 做一次着色（`ApplyTintOverride(baitColor)`）。
4. 调用 `OnBaiting?.Invoke()`。
5. 启动协程 **BaitSelfFix()**。

### 4. BaitSelfFix()（自愈）

- `yield return new WaitForSeconds(3);` 固定 **3 秒**。
- 若仍 `IsBaiting`：置 `IsBaiting = false`，清除着色，再调用 `Fix()`。  
  （`Fix()` 内会再次把 `IsBroken = false`，并清 tint、触发 `OnFixed`。）

即：诱饵状态持续约 3 秒后自动“修好”并恢复外观。

### 5. 与修理的交互（TryRepair → Punishment）

- 玩家按修理键会走 `TryRepair()`。
- **若当前并未坏（!IsBroken）**：
  - **若正在诱饵（IsBaiting）**：调用 `GameManager.Instance.UltraPunishment()`（误修诱饵，重罚）。
  - **否则**：调用 `GameManager.Instance.Punishment()`（误修正常物品，普通惩罚）。
- 然后再根据 `requirePlayerInRange` 等条件决定是否执行 `Fix()`。  
  诱饵状态下 `IsBroken` 为 false，所以不会真的修东西，只是触发惩罚。

### 6. Fix() 中的 Baiting

- `Fix()` 内：若 `IsBaiting`，会顺带置 `IsBaiting = false`，再清 tint、触发 `OnFixed`。

---

## 二、GameManager 中的相关逻辑

- **不直接维护 Baiting 状态**，只通过 WorkItem 的 `TryRepair()` 被间接调用。
- **FreezeFailures**：Boss 预警开始到 Boss 离开期间为 `true`，此期间 `WorkItem.BreakLoop()` 不会执行 `Break()` 或 `Bait()`，即既不产生新故障也不产生新诱饵。
- **Punishment / UltraPunishment**：  
  - `Punishment()`：扣 `workPunishment`。  
  - `UltraPunishment()`：扣 `workUltraPunishment`。  
  当玩家误修（修了没坏的东西）时由 WorkItem 决定调用哪一个；误修诱饵时用 UltraPunishment。

---

## 三、扩展思路（不改原脚本）

1. **事件驱动**：通过 WorkItem 的 `OnBaiting`（及可选 `OnFixed`）在 JiU 下挂自定义逻辑（音效、特效、UI、Arduino 等）。
2. **集中监听**：使用 JiU 下的 `BaitingExtension` 组件，对场景中的 WorkItem 注册监听，统一处理诱饵开始/结束。
3. **参数与规则**：诱饵概率（1/6）、自愈时间（3 秒）等目前写在 WorkItem 内部；若不改原代码，只能通过事件在 JiU 侧做“表现层”扩展；若以后允许小改动，可考虑把数值放到 Config 或通过接口由 JiU 提供。

当前在 JiU 下提供的 `BaitingExtension.cs` 采用事件监听方式，不修改 GameManager 与 WorkItem 的原有实现。
