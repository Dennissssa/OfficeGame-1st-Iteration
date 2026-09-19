# Unity + Uduino + ESP32 NeoPixel LED 控制

## 一、Arduino 库安装

### 1. Adafruit NeoPixel

1. 打开 Arduino IDE → **工具** → **管理库**。
2. 搜索 **Adafruit NeoPixel**。
3. 安装 **Adafruit NeoPixel**（作者 Adafruit）。
4. 若提示依赖 **Adafruit BusIO**，一并安装。

### 2. 为何用 Adafruit NeoPixel 而不是 FastLED？

| 项目         | Adafruit NeoPixel | FastLED |
|--------------|-------------------|---------|
| 易用性       | 简单，示例多       | API 更底层 |
| 灯环/灯带    | 直接支持           | 支持，需选芯片类型 |
| 性能/特效    | 一般够用           | 更擅长复杂动画 |
| 本项目建议   | 推荐，配置少       | 需要更多调优可用 FastLED |

本示例使用 **Adafruit NeoPixel**，若你已有 FastLED 工程，只需把 `setPixelColor` / `show` 换成 FastLED 的 `leds[i]=CRGB(r,g,b); FastLED.show()` 即可，Unity 端不变。

---

## 二、Arduino 端代码

### 1. 使用现成工程

- 路径：`Assets/JiU/Arduino/Esp32NeoPixelUduino/Esp32NeoPixelUduino.ino`
- 用 Arduino IDE 打开该 `.ino` 所在文件夹（或把 `.ino` 内容复制进一个新建的 ESP32 工程）。

### 2. 硬件接线与配置

- **Monitor 0 (Skull)**：GPIO **21**，12 颗 LED。
- **Monitor 3 (Lamp)**：GPIO **33**，12 颗 LED。
- 在 `.ino` 顶部可改：
  - `LED_PIN_0`、`LED_PIN_3`
  - `NUM_PIXELS`
  - `SERIAL_BAUD`（需与 Unity Uduino 一致，默认 9600）。

### 3. 命令协议

- Unity 发送命令名：**setled**
- 参数（4 个）：**灯环索引**、**R**、**G**、**B**（0–255）。
- 例：`setled 0 255 255 0` → Monitor 0 设为黄色。

---

## 三、Unity 端配置与使用

### 1. Inspector 配置（事件驱动）

1. 场景里放一个空物体，挂 **Uduino Game Event Output**。
2. 在 **Outputs** 里增加一条：
   - **Type**：**SetNeoPixel**
   - **Led Monitor Index**：0 或 3（对应 Skull / Lamp）
   - **Neo Pixel Color Preset**：Red / Yellow / Off / Custom 等
   - 若选 **Custom**，设置 **Custom R/G/B**。
3. 把 **WorkItem** 的 **OnBroken** 或 **OnFixed** 拖到该物体，选择 **UduinoGameEventOutput → Trigger()**。

这样“闹鬼/修好”时就会自动发 setled 到 ESP32，灯环变色。

### 2. 代码调用（无需拖事件）

**方式 A：静态方法（推荐）**

```csharp
using JiU;

// 预设颜色
UduinoLEDHelper.SetLED(0, LEDColorPreset.Yellow);   // Skull 黄
UduinoLEDHelper.SetLED(3, LEDColorPreset.Red);      // Lamp 红
UduinoLEDHelper.SetLED(0, LEDColorPreset.Off);     // 关灯

// Unity Color（0~1 会转成 0~255）
UduinoLEDHelper.SetLED(0, Color.yellow);

// 直接 RGB
UduinoLEDHelper.SetLED(3, 255, 0, 128);
```

**方式 B：通过 UduinoGameEventOutput 组件**

```csharp
public UduinoGameEventOutput ledOutput;

void StartAnomaly()
{
    ledOutput.SetLED(0, Color.yellow);
    // 或
    ledOutput.SetLED(0, LEDColorPreset.Yellow);
}
```

**方式 C：批量**

```csharp
int[] monitors = { 0, 3 };
UduinoLEDHelper.SetLEDRange(monitors, 255, 255, 0);
```

### 3. 预设颜色枚举

在 **JiU** 命名空间下：

- `LEDColorPreset.Red` → (255, 0, 0)
- `LEDColorPreset.Yellow` → (255, 255, 0)
- `LEDColorPreset.Green` / `Blue` / `Off`
- `LEDColorPreset.Custom` → 在 OutputEntry 里填 Custom R/G/B，或代码里用 `SetLED(index, r, g, b)`。

---

## 四、常见问题

### 1. 灯环不亮 / 颜色不对

- 检查接线：数据线接对 GPIO（21/33），电源共地。
- 确认板子类型：在 Arduino IDE 里选 **ESP32 Dev Module**（或你用的型号）。
- 若颜色通道错位（红绿蓝反了），在 `.ino` 里把 `NEO_GRB` 改成 `NEO_RGB` 等再试。

### 2. Unity 里改了颜色，灯不变

- 确认 **Uduino 已连接**（运行游戏后设备列表里能看到板子）。
- 看 Unity Console 是否有 `[JiU.UduinoLEDHelper] Uduino 未运行，跳过 SetLED`。
- 确认 Arduino 已烧录带 **setled** 的工程，且串口波特率与 Unity 一致（默认 9600）。

### 3. 只有某一个灯环能控

- 检查 **Led Monitor Index**：0 对应 GPIO21，3 对应 GPIO33；其它索引在 `.ino` 里未映射会直接 return，需在 `setLedColor` 里增加 case。

### 4. 想增加更多灯环（例如 Monitor 1、2）

- 在 `.ino` 里增加 `ring1`、`ring2` 和对应 `#define LED_PIN_1`、`LED_PIN_2`。
- 在 `setLedColor` 的 `switch (monitorIndex)` 里增加 `case 1:`、`case 2:`，并指定对应 `ring` 和 `begin()`。

---

## 五、文件一览

| 文件 | 说明 |
|------|------|
| `JiU/UduinoGameEventOutput.cs` | 事件输出 + SetNeoPixel 条目 + SetLED 方法 |
| `JiU/UduinoLEDHelper.cs` | 静态 API：SetLED / SetLEDRange / 预设颜色 |
| `JiU/Arduino/Esp32NeoPixelUduino/Esp32NeoPixelUduino.ino` | ESP32 + Uduino + NeoPixel 示例工程 |

以上内容为 Unity → Uduino → ESP32 → NeoPixel 的完整链路说明，按步骤接线、烧录、在 Unity 里用 Inspector 或代码调用即可。
