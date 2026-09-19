/*
 * ESP32 + Uduino + NeoPixel LED 灯环控制
 * 与 Unity JiU.UduinoLEDHelper / UduinoGameEventOutput 配套使用。
 *
 * 硬件映射：
 *   Monitor 0 (Skull) → GPIO 21, 12 颗 LED
 *   Monitor 3 (Lamp)  → GPIO 33, 12 颗 LED
 *
 * 依赖库：
 *   - Uduino（已安装）
 *   - Adafruit NeoPixel：库管理器搜索 "Adafruit NeoPixel" 安装
 *
 * Unity 发送格式：sendCommand("setled", "index r g b")
 * 例："0 255 255 0" 表示 Monitor0 设为黄色
 */

#include <Uduino.h>
#include <Adafruit_NeoPixel.h>

// ---------- 配置 ----------
#define NUM_PIXELS    12
#define LED_PIN_0     21   // Monitor 0 (Skull)
#define LED_PIN_3     33   // Monitor 3 (Lamp)
#define SERIAL_BAUD   9600

// Uduino 实例（名称需与 Unity 中一致，默认可留空或 "uduinoBoard"）
Uduino uduino;

// NeoPixel 灯环（NEO_GRB 表示数据顺序为 G-R-B，常见 WS2812B）
Adafruit_NeoPixel ring0(NUM_PIXELS, LED_PIN_0, NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel ring3(NUM_PIXELS, LED_PIN_3, NEO_GRB + NEO_KHZ800);

// 当前颜色缓存，避免频繁 show()
uint8_t lastR0 = 255, lastG0 = 0, lastB0 = 0;
uint8_t lastR3 = 255, lastG3 = 0, lastB3 = 0;

// ---------- 前向声明 ----------
void setLedColor(int monitorIndex, uint8_t r, uint8_t g, uint8_t b);
void cmdSetLed();

// ---------- Setup ----------
void setup() {
  Serial.begin(SERIAL_BAUD);

  // 初始化 NeoPixel
  ring0.begin();
  ring3.begin();
  setLedColor(0, 255, 0, 0);   // 默认红色
  setLedColor(3, 255, 0, 0);

  // 注册 Uduino 命令（与 Unity UduinoLEDHelper.SetLedCommand 一致）
  uduino.addCommand("setled", cmdSetLed);

  // 若你有其他 Uduino 命令（如 HIT_1 等），在此继续 addCommand
  // uduino.addCommand("HIT_1", onHit1);
}

// ---------- Loop ----------
void loop() {
  uduino.update();
  delay(10);
}

// ---------- 命令处理：setled ----------
// Unity 发送：sendCommand("setled", "index r g b")，例如 "0 255 255 0"
void cmdSetLed() {
  int numParams = uduino.getNumberOfParameters();
  int index = 0, r = 0, g = 0, b = 0;

  if (numParams >= 4) {
    // 四个独立参数：index, r, g, b
    index = uduino.charToInt(uduino.getParameter(0));
    r     = uduino.charToInt(uduino.getParameter(1));
    g     = uduino.charToInt(uduino.getParameter(2));
    b     = uduino.charToInt(uduino.getParameter(3));
  } else if (numParams >= 1) {
    // 一个参数字符串 "index r g b"
    char* str = uduino.getParameter(0);
    if (str != nullptr && sscanf(str, "%d %d %d %d", &index, &r, &g, &b) != 4) {
      Serial.println("[setled] parse error");
      return;
    }
  } else {
    Serial.println("[setled] need 1 or 4 params");
    return;
  }

  r = constrain(r, 0, 255);
  g = constrain(g, 0, 255);
  b = constrain(b, 0, 255);

  setLedColor(index, (uint8_t)r, (uint8_t)g, (uint8_t)b);

#ifdef UDUINO_DEBUG
  Serial.print("[setled] ");
  Serial.print(index);
  Serial.print(" ");
  Serial.print(r);
  Serial.print(" ");
  Serial.print(g);
  Serial.print(" ");
  Serial.println(b);
#endif
}

// ---------- 设置指定 monitor 的灯环颜色 ----------
void setLedColor(int monitorIndex, uint8_t r, uint8_t g, uint8_t b) {
  Adafruit_NeoPixel* ring = nullptr;
  uint8_t* lr = nullptr, * lg = nullptr, * lb = nullptr;

  switch (monitorIndex) {
    case 0:
      ring = &ring0;
      lr = &lastR0; lg = &lastG0; lb = &lastB0;
      break;
    case 3:
      ring = &ring3;
      lr = &lastR3; lg = &lastG3; lb = &lastB3;
      break;
    default:
      // 无效索引不操作，避免越界
      return;
  }

  if (ring == nullptr) return;

  // 仅当颜色变化时才刷新，减少 show() 调用
  if (lr != nullptr && *lr == r && *lg == g && *lb == b)
    return;
  if (lr) { *lr = r; *lg = g; *lb = b; }

  for (int i = 0; i < NUM_PIXELS; i++) {
    ring->setPixelColor(i, ring->Color(r, g, b));
  }
  ring->show();
}
