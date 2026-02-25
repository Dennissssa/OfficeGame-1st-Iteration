using UnityEngine;
using Uduino;

namespace JiU
{
    /// <summary>
    /// 预设 LED 颜色，与 Arduino 端约定一致。
    /// </summary>
    public enum LEDColorPreset
    {
        [Tooltip("红色 (255,0,0) - 正常/休息")]
        Red,
        [Tooltip("黄色 (255,255,0) - 闹鬼/异常")]
        Yellow,
        [Tooltip("绿色 (0,255,0)")]
        Green,
        [Tooltip("蓝色 (0,0,255)")]
        Blue,
        [Tooltip("关闭 (0,0,0)")]
        Off,
        [Tooltip("使用下方 Custom R/G/B 自定义")]
        Custom
    }

    /// <summary>
    /// 通过 Uduino 发送 setled 命令控制 ESP32 上的 NeoPixel 灯环。
    /// 可在任意脚本中静态调用，无需持有 UduinoGameEventOutput 引用。
    /// </summary>
    public static class UduinoLEDHelper
    {
        /// <summary> setled 命令名，需与 Arduino 端一致 </summary>
        public const string SetLedCommand = "setled";

        /// <summary>
        /// 使用预设颜色设置指定 monitor 的 LED 灯环。
        /// </summary>
        /// <param name="monitorIndex">灯环索引，与硬件映射一致（如 0=Skull/GPIO21, 3=Lamp/GPIO33）</param>
        /// <param name="preset">预设颜色</param>
        public static void SetLED(int monitorIndex, LEDColorPreset preset)
        {
            int r, g, b;
            PresetToRGB(preset, 0, 0, 0, out r, out g, out b);
            SetLED(monitorIndex, r, g, b);
        }

        /// <summary>
        /// 使用 Unity Color 设置指定 monitor 的 LED（0~1 会转为 0~255）。
        /// </summary>
        public static void SetLED(int monitorIndex, Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            SetLED(monitorIndex, r, g, b);
        }

        /// <summary>
        /// 使用 RGB(0~255) 设置指定 monitor 的 LED。
        /// </summary>
        public static void SetLED(int monitorIndex, int r, int g, int b)
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
            {
                Debug.LogWarning("[JiU.UduinoLEDHelper] Uduino 未运行，跳过 SetLED。");
                return;
            }
            r = Mathf.Clamp(r, 0, 255);
            g = Mathf.Clamp(g, 0, 255);
            b = Mathf.Clamp(b, 0, 255);
            // 传 4 个参数，Arduino 端可用 getParameter(0)~getParameter(3) 解析
            UduinoManager.Instance.sendCommand(SetLedCommand, monitorIndex, r, g, b);
        }

        /// <summary>
        /// 批量设置多个 monitor 为同一颜色。
        /// </summary>
        public static void SetLEDRange(int[] monitorIndices, int r, int g, int b)
        {
            if (monitorIndices == null) return;
            for (int i = 0; i < monitorIndices.Length; i++)
                SetLED(monitorIndices[i], r, g, b);
        }

        /// <summary>
        /// 将预设枚举转为 RGB；Custom 时使用传入的 customR/G/B。
        /// </summary>
        public static void PresetToRGB(LEDColorPreset preset, int customR, int customG, int customB, out int r, out int g, out int b)
        {
            switch (preset)
            {
                case LEDColorPreset.Red:   r = 255; g = 0;   b = 0;   return;
                case LEDColorPreset.Yellow: r = 255; g = 255; b = 0;   return;
                case LEDColorPreset.Green: r = 0;   g = 255; b = 0;   return;
                case LEDColorPreset.Blue:  r = 0;   g = 0;   b = 255; return;
                case LEDColorPreset.Off:   r = 0;   g = 0;   b = 0;   return;
                default:
                    r = Mathf.Clamp(customR, 0, 255);
                    g = Mathf.Clamp(customG, 0, 255);
                    b = Mathf.Clamp(customB, 0, 255);
                    return;
            }
        }
    }
}
