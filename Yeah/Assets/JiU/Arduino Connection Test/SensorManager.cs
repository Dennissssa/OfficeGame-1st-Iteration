using UnityEngine;
using System;
using System.IO.Ports;
using System.Collections.Generic;

/// <summary>
/// 管理多个压电传感器串口输入，将超过阈值的模拟量映射为虚拟按键，
/// 供其他脚本通过 GetKeyDown(KeyCode) 轮询使用。
/// </summary>
public class SensorManager : MonoBehaviour
{
    [Header("传感器配置")]
    [Tooltip("每个传感器可配置独立串口、波特率、阈值和映射的按键")]
    [SerializeField] private SensorConfig[] sensors = Array.Empty<SensorConfig>();

    // 本帧因传感器超阈值而触发的虚拟按键（仅本帧有效，模拟 GetKeyDown）
    private HashSet<KeyCode> keysDownThisFrame = new HashSet<KeyCode>();

    private void Update()
    {
        keysDownThisFrame.Clear();

        for (int i = 0; i < sensors.Length; i++)
        {
            var config = sensors[i];
            if (config.Serial == null || !config.Serial.IsOpen)
                continue;

            if (config.Serial.BytesToRead <= 0)
                continue;

            try
            {
                string line = config.Serial.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                line = line.Trim();
                if (!int.TryParse(line, out int value))
                    continue;

                if (value > config.Threshold)
                    keysDownThisFrame.Add(config.MappedKey);
            }
            catch (TimeoutException) { /* 忽略超时 */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SensorManager] Sensor {i} read error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 其他脚本可调用：当指定按键在本帧被“按下”（某传感器超过阈值）时返回 true，行为类似 Input.GetKeyDown。
    /// </summary>
    public bool GetKeyDown(KeyCode key)
    {
        return keysDownThisFrame.Contains(key);
    }

    /// <summary>
    /// 当前帧内是否有任意映射按键被触发（任一传感器超阈值）。
    /// </summary>
    public bool AnyKeyDown()
    {
        return keysDownThisFrame.Count > 0;
    }

    private void Start()
    {
        foreach (var config in sensors)
        {
            if (string.IsNullOrEmpty(config.PortName))
                continue;
            try
            {
                config.Serial = new SerialPort(config.PortName, config.BaudRate);
                config.Serial.ReadTimeout = 10;
                config.Serial.Open();
                Debug.Log($"[SensorManager] Opened sensor port: {config.PortName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SensorManager] Failed to open {config.PortName}: {ex.Message}\n" +
                    "请检查：1) 端口号是否正确（设备管理器/Arduino IDE 中查看）；2) 是否已关闭 Arduino IDE 串口监视器；3) 是否有其他程序占用该端口。");
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var config in sensors)
        {
            try
            {
                if (config.Serial != null && config.Serial.IsOpen)
                    config.Serial.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SensorManager] Close port error: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class SensorConfig
    {
        [Header("串口")]
        [Tooltip("例如 COM3 (Windows) 或 /dev/ttyUSB0 (Mac/Linux)")]
        public string PortName = "COM3";

        [Tooltip("与 Arduino 串口监视器一致，常用 9600")]
        public int BaudRate = 9600;

        [Header("触发")]
        [Tooltip("接收到的模拟量超过此值时触发映射按键")]
        public int Threshold = 512;

        [Tooltip("触发时模拟的按键，其他脚本可通过 SensorManager.GetKeyDown(此按键) 检测")]
        public KeyCode MappedKey = KeyCode.Space;

        [NonSerialized] public SerialPort Serial;
    }
}
