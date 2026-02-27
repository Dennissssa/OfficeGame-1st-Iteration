# Arduino 连接测试场景搭建说明

## 脚本说明

- **SensorManager.cs**：管理多个压电传感器串口，将超过阈值的模拟量映射为虚拟按键，供 `GetKeyDown(KeyCode)` 轮询。
- **LEDManager.cs**：单板多灯，一个固定串口连接一块 Arduino，板上多个 LED 通过不同 **Pin** 分别控制。可与 **WorkItem** 绑定：物品进入 Broke/Bait 时 LED 切换为对应颜色，玩家修好（打击）时恢复原本颜色；也可通过脚本调用 `SetLEDToBrokeState` / `SetLEDToBaitState` / `RestoreLEDColor`。

## 测试场景搭建步骤

### 1. 创建空场景或使用现有场景

- 若新建：`File > New Scene`，保存到例如 `Assets/JiU/Arduino Connection Test/ArduinoTest.unity`。

### 2. 创建管理器 GameObject

1. 在 Hierarchy 中右键 → **Create Empty**，命名为 `ArduinoManagers`（或任意名称）。
2. 在 Inspector 中点击 **Add Component**：
   - 添加 **Sensor Manager (Script)**。
   - 再添加 **LED Manager (Script)**。

### 3. 配置 SensorManager

- **Sensors** 数组：
  - **Size**：设为你的压电传感器数量（每个传感器可对应一个串口）。
  - 每个元素：
    - **Port Name**：该传感器所在 Arduino 的串口（如 `COM3`、`COM4`）。
    - **Baud Rate**：与 Arduino 程序一致（常用 `9600`）。
    - **Threshold**：模拟量超过此值即触发（如 `512`）。
    - **Mapped Key**：触发时模拟的按键（如 `Space`、`Alpha1`），其他脚本用 `sensorManager.GetKeyDown(KeyCode.Space)` 检测。

### 4. 配置 LEDManager

- **板子串口**（所有 LED 共用）：
  - **Port Name**：Arduino 连接的端口（如 `COM4`）。
  - **Baud Rate**：常用 `9600`。
- **Leds** 数组：
  - **Size**：设为板上 LED 数量。
  - 每个元素：
    - **Pin**：该灯在板子上的引脚号或 NeoPixel 灯珠索引，与 Arduino 程序约定一致。
    - **On Color**：默认/原本颜色（游戏开始时点亮，玩家修好时恢复为此色）。
    - **Broke Color**：物品进入 Broke（损坏）状态时 LED 显示的颜色。
    - **Bait Color**：物品进入 Bait 状态时 LED 显示的颜色。
    - **Work Item**（可选）：拖入对应的 **WorkItem**，则随该物品的 Broke/Bait/修好 自动切换 LED 颜色；不填则仅通过脚本调用 API 控制。

### 5. 在其他脚本中使用

- **传感器（虚拟按键）**：  
  获取 `SensorManager` 引用后，在 `Update()` 中调用：
  - `sensorManager.GetKeyDown(KeyCode.Space)` — 当映射为 Space 的传感器本帧超过阈值时为 `true`。
- **LED 控制**：  
  获取 `LEDManager` 引用后调用：
  - `ledManager.SetLEDColor(0, Color.red)` — 设置第 0 个 LED 颜色（并作为“原本颜色”）。
  - `ledManager.SetLEDToBrokeState(0)` — 将该灯设为 Broke 状态颜色。
  - `ledManager.SetLEDToBaitState(0)` — 将该灯设为 Bait 状态颜色。
  - `ledManager.RestoreLEDColor(0)` — 恢复为该 LED 的 On Color。

### 6. Arduino 端协议约定

- **传感器 → Unity**：每行发送一个数字（0–1023 的模拟量），例如：`512` 或 `512\n`。
- **Unity → LED**（单板多灯，每条指令带 pin 区分灯）：  
  - 点亮：一行 `ON pin r g b`，如 `ON 5 255 0 0`（pin 5 红色）。  
  - 关闭：一行 `OFF pin`，如 `OFF 5`。  

Arduino 端根据 `pin` 控制对应引脚或 NeoPixel 灯珠索引，需与 Inspector 中配置的 Pin 一致。

### 7. 注意事项

- **串口“拒绝访问”**：同一时间一个串口只能被一个进程打开。运行 Unity 前请**关闭 Arduino IDE 的串口监视器**，并确认设备管理器中的端口号与 Inspector 中 **Port Name** 一致；若有其他串口调试工具占用，也需关闭。
- 若多传感器/多 LED 在同一块 Arduino 上，可共用同一串口；若在不同板子上，则每个板子一个 Port Name。
- **LED 与 WorkItem**：在 LED 配置中为 **Work Item** 指定场景里的 WorkItem 后，该灯会随物品 Broke/Bait 自动变色，玩家按修好键（WorkItem 上配置的 repair binding）后灯会恢复为 On Color；Broke/Bait 的其他逻辑（扣分、Boss 检查等）仍由 GameManager / WorkItem 照常处理。
