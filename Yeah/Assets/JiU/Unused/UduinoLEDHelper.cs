using UnityEngine;
using Uduino;

namespace JiU
{
    /// <summary>
    /// LED color presets matching Arduino firmware.
    /// </summary>
    public enum LEDColorPreset
    {
        [Tooltip("Red (255,0,0) - calm/idle")]
        Red,
        [Tooltip("Yellow (255,255,0) - haunted/anomaly")]
        Yellow,
        [Tooltip("Green (0,255,0)")]
        Green,
        [Tooltip("Blue (0,0,255)")]
        Blue,
        [Tooltip("Off (0,0,0)")]
        Off,
        [Tooltip("Use custom R/G/B below")]
        Custom
    }

    /// <summary>
    /// Sends setled over Uduino for ESP32 NeoPixel rings.
    /// Static API; no UduinoGameEventOutput reference needed.
    /// </summary>
    public static class UduinoLEDHelper
    {
        /// <summary>setled command name; must match Arduino.</summary>
        public const string SetLedCommand = "setled";

        /// <summary>
        /// Set ring color from preset.
        /// </summary>
        /// <param name="monitorIndex">Ring index per hardware map (e.g. 0=Skull/GPIO21, 3=Lamp/GPIO33)</param>
        /// <param name="preset">Color preset</param>
        public static void SetLED(int monitorIndex, LEDColorPreset preset)
        {
            int r, g, b;
            PresetToRGB(preset, 0, 0, 0, out r, out g, out b);
            SetLED(monitorIndex, r, g, b);
        }

        /// <summary>
        /// Set ring from Unity Color (0~1 scaled to 0~255).
        /// </summary>
        public static void SetLED(int monitorIndex, Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            SetLED(monitorIndex, r, g, b);
        }

        /// <summary>
        /// Set ring from RGB 0~255.
        /// </summary>
        public static void SetLED(int monitorIndex, int r, int g, int b)
        {
            if (UduinoManager.Instance == null || !UduinoManager.Instance.IsRunning())
            {
                Debug.LogWarning("[JiU.UduinoLEDHelper] Uduino not running; skipping SetLED.");
                return;
            }
            r = Mathf.Clamp(r, 0, 255);
            g = Mathf.Clamp(g, 0, 255);
            b = Mathf.Clamp(b, 0, 255);
            // Four params; Arduino can read getParameter(0)..(3)
            UduinoManager.Instance.sendCommand(SetLedCommand, monitorIndex, r, g, b);
        }

        /// <summary>
        /// Set several monitors to the same RGB.
        /// </summary>
        public static void SetLEDRange(int[] monitorIndices, int r, int g, int b)
        {
            if (monitorIndices == null) return;
            for (int i = 0; i < monitorIndices.Length; i++)
                SetLED(monitorIndices[i], r, g, b);
        }

        /// <summary>
        /// Preset to RGB; Custom uses customR/G/B.
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
