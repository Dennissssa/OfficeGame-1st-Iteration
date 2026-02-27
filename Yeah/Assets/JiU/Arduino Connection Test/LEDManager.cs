using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using System.IO.Ports;

/// <summary>
/// 单板多灯：通过一个固定串口连接一块 Arduino。LED 可与 WorkItem 绑定：物品进入 Broke/Bait 时改灯颜色，玩家修好（打击）时恢复原本颜色。
/// </summary>
public class LEDManager : MonoBehaviour
{
    [Header("板子串口（所有 LED 共用同一端口）")]
    [Tooltip("Arduino 连接的端口，例如 COM4 (Windows) 或 /dev/ttyUSB0 (Mac/Linux)")]
    [SerializeField] private string portName = "COM4";

    [Tooltip("与 Arduino 串口监视器一致，常用 9600")]
    [SerializeField] private int baudRate = 9600;

    [Header("LED 列表")]
    [Tooltip("每个元素对应一个 LED：Pin、默认颜色、Broke/Bait 状态颜色；可绑定 WorkItem 以随物品状态自动变灯")]
    [SerializeField] private LEDConfig[] leds = Array.Empty<LEDConfig>();

    private SerialPort serial;
    private readonly List<WorkItemListener> workItemListeners = new List<WorkItemListener>();

    private void Start()
    {
        if (string.IsNullOrEmpty(portName))
        {
            Debug.LogWarning("[LEDManager] 未配置 Port Name，LED 控制已禁用。");
            return;
        }

        try
        {
            serial = new SerialPort(portName, baudRate);
            serial.ReadTimeout = 10;
            serial.Open();
            Debug.Log($"[LEDManager] Opened port: {portName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LEDManager] Failed to open {portName}: {ex.Message}\n" +
                "请检查：1) 端口号是否正确；2) 是否已关闭 Arduino IDE 串口监视器；3) 是否有其他程序占用该端口。");
            return;
        }

        // 游戏开始时将所有 LED 点亮为配置的默认颜色
        for (int i = 0; i < leds.Length; i++)
            SetLEDColor(i, leds[i].OnColor);

        // 绑定 WorkItem：Broke/Bait 时改灯颜色；玩家修好(OnFixed)或 Bait 时间到(OnBaitingEnded)时恢复原本颜色
        for (int i = 0; i < leds.Length; i++)
        {
            var config = leds[i];
            if (config.WorkItem == null) continue;

            int idx = i;
            UnityAction onBroken = () => SetLEDToBrokeState(idx);
            UnityAction onBaiting = () => SetLEDToBaitState(idx);
            UnityAction onFixed = () => RestoreLEDColor(idx);
            UnityAction onBaitingEnded = () => RestoreLEDColor(idx);

            config.WorkItem.OnBroken.AddListener(onBroken);
            config.WorkItem.OnBaiting.AddListener(onBaiting);
            config.WorkItem.OnFixed.AddListener(onFixed);
            config.WorkItem.OnBaitingEnded.AddListener(onBaitingEnded);

            workItemListeners.Add(new WorkItemListener(config.WorkItem, onBroken, onBaiting, onFixed, onBaitingEnded));
        }
    }

    /// <summary>
    /// 将指定 LED 设为 Broke 状态颜色（物品损坏时由绑定逻辑或外部调用）。
    /// </summary>
    public void SetLEDToBrokeState(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].BrokeColor);
    }

    /// <summary>
    /// 将指定 LED 设为 Bait 状态颜色（物品进入 Bait 时由绑定逻辑或外部调用）。
    /// </summary>
    public void SetLEDToBaitState(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].BaitColor);
    }

    /// <summary>
    /// 恢复指定 LED 为原本颜色（玩家修好/打击时由绑定逻辑或外部调用）。
    /// </summary>
    public void RestoreLEDColor(int index)
    {
        if (index < 0 || index >= leds.Length) return;
        SendLEDOnWithColor(index, leds[index].OnColor);
    }

    /// <summary>
    /// 设置指定 LED 的颜色并发送“点亮”指令，供其他脚本在运行时调用；同时更新该 LED 的“原本颜色”（恢复时使用）。
    /// </summary>
    public void SetLEDColor(int index, Color color)
    {
        if (index < 0 || index >= leds.Length) return;
        leds[index].OnColor = color;
        SendLEDOnWithColor(index, color);
    }

    /// <summary>
    /// 获取指定 LED 当前配置的“点亮颜色”（可写回 SetLEDColor 保持一致性）。
    /// </summary>
    public Color GetLEDColor(int index)
    {
        if (index < 0 || index >= leds.Length)
            return Color.black;
        return leds[index].OnColor;
    }

    /// <summary>
    /// 发送“点亮”指令。协议：一行 "ON pin r g b\n"，pin 区分灯珠，r/g/b 为 0-255。
    /// </summary>
    private void SendLEDOnWithColor(int index, Color color)
    {
        if (serial == null || !serial.IsOpen) return;

        int pin = leds[index].Pin;
        byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
        byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
        string line = $"ON {pin} {r} {g} {b}\n";

        try
        {
            serial.Write(line);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LEDManager] Write ON failed for LED {index} (pin {pin}): {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        foreach (var entry in workItemListeners)
        {
            if (entry.Item == null) continue;
            entry.Item.OnBroken.RemoveListener(entry.OnBroken);
            entry.Item.OnBaiting.RemoveListener(entry.OnBaiting);
            entry.Item.OnFixed.RemoveListener(entry.OnFixed);
            entry.Item.OnBaitingEnded.RemoveListener(entry.OnBaitingEnded);
        }
        workItemListeners.Clear();

        try
        {
            if (serial != null && serial.IsOpen)
                serial.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LEDManager] Close port error: {ex.Message}");
        }
    }

    private struct WorkItemListener
    {
        public WorkItem Item;
        public UnityAction OnBroken;
        public UnityAction OnBaiting;
        public UnityAction OnFixed;
        public UnityAction OnBaitingEnded;

        public WorkItemListener(WorkItem item, UnityAction onBroken, UnityAction onBaiting, UnityAction onFixed, UnityAction onBaitingEnded)
        {
            Item = item;
            OnBroken = onBroken;
            OnBaiting = onBaiting;
            OnFixed = onFixed;
            OnBaitingEnded = onBaitingEnded;
        }
    }

    [Serializable]
    public class LEDConfig
    {
        [Header("引脚 / 灯珠索引")]
        [Tooltip("Arduino 上的数字引脚号（如 5、6、7），或 NeoPixel 条带上的灯珠索引（0、1、2…），与 Arduino 程序约定一致")]
        public int Pin;

        [Header("颜色")]
        [Tooltip("默认/原本颜色；游戏开始时点亮，玩家修好物品时恢复为此颜色")]
        public Color OnColor = Color.green;

        [Tooltip("物品进入 Broke（损坏）状态时 LED 显示的颜色")]
        public Color BrokeColor = new Color(1f, 0.2f, 0.2f, 1f);

        [Tooltip("物品进入 Bait 状态时 LED 显示的颜色")]
        public Color BaitColor = new Color(0.2f, 1f, 0.2f, 1f);

        [Header("绑定 WorkItem（可选）")]
        [Tooltip("若指定，则随该物品的 Broke/Bait/修好 自动切换 LED 颜色；不填则仅通过脚本调用 SetLEDToBrokeState / SetLEDToBaitState / RestoreLEDColor")]
        public WorkItem WorkItem;
    }
}
