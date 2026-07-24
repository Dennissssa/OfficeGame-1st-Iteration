using UnityEngine;
using UnityEngine.Events;
using System;
using System.IO.Ports;
using System.Threading;

/// <summary>
/// StartScene 专用的 Arduino 串口桥接器（仅在 StartScene 生命周期内存活）。
///
/// ── 工作原理 ──────────────────────────────────────────────
///   监听主游戏 Arduino（ArduinoUnityBridge_with_monitor_and_bulb.ino）
///   发来的 "BADGE:SCANNED" 消息。
///
///   玩家插入工号牌 → Arduino 发送 "BADGE:SCANNED"（边缘触发，仅一次）
///   → 本脚本在主线程触发 onArduinoTriggered 事件
///   → StartGame.cs 调用 LoadNextScene() 加载游戏场景
///   → StartScene 卸载，本脚本 OnDestroy 关闭串口
///   → Gameplay 场景的 ArduinoSerialBridge.cs 重新打开同一串口
///
///   游戏结束后，ArduinoSerialBridge.ResetSystem() 发送 "SYSTEM:RESET"，
///   Arduino 内部 waitingForBadge 重置为 true，为下一局做好准备。
///
/// ── 串口说明 ──────────────────────────────────────────────
///   与 Gameplay 场景的 ArduinoSerialBridge.cs 使用同一个物理 Arduino。
///   两者不会同时运行，通过场景生命周期自然分离。
///   Port Name 和 Baud Rate 须与 ArduinoSerialBridge 设置保持一致。
///
/// ── Inspector 设置 ────────────────────────────────────────
///   1. 将本脚本挂到 StartScene 中的任意 GameObject 上
///   2. Port Name 填写 Arduino 串口号（与 ArduinoSerialBridge 一致）
///   3. 将 onArduinoTriggered 事件连接到 StartGame.LoadNextScene()，
///      或留空由 StartGame.cs 通过 IsTriggered 属性轮询
/// </summary>
public class StartSceneArduinoBridge : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────

    [Header("串口设置  （与 ArduinoSerialBridge 保持一致）")]
    [Tooltip("Arduino 串口号。Windows: COM3   Mac: /dev/cu.usbserial-...\n" +
             "必须与 Gameplay 场景的 ArduinoSerialBridge 使用相同的串口号。")]
    [SerializeField] private string portName = "COM3";
    [Tooltip("波特率须与 Arduino 固件及 ArduinoSerialBridge 保持一致（默认 9600）。")]
    [SerializeField] private int baudRate = 9600;

    [Header("触发消息")]
    [Tooltip("Arduino 发送的触发字符串，与 .ino 固件中 BADGE:SCANNED 保持一致。")]
    [SerializeField] private string triggerMessage = "BADGE:SCANNED";

    [Header("事件")]
    [Tooltip("Arduino 信号到达时触发。可选：直接在此处连接场景加载方法")]
    public UnityEvent onArduinoTriggered;

    [Header("调试")]
    [SerializeField] private bool debugLog = true;
    [Tooltip("开启后将把串口收到的每一行都打印到 Console，方便排查收到了什么内容。")]
    [SerializeField] private bool debugLogAllLines = true;

    // ── 状态 ──────────────────────────────────────────────

    private SerialPort _serial;
    private Thread _readThread;
    private volatile bool _isRunning;
    private volatile bool _triggered;

    /// <summary>Arduino 是否已发送触发信号（主线程可安全读取）。</summary>
    public bool IsTriggered => _triggered;

    // ── Lifecycle ─────────────────────────────────────────

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            Debug.LogWarning("[StartSceneArduinoBridge] 未配置串口号，Arduino 输入已禁用。", this);
            return;
        }

        OpenSerial();
    }

    private void Update()
    {
        if (!_triggered) return;
        _triggered = false;

        if (debugLog)
            Debug.Log("[StartSceneArduinoBridge] 收到 Arduino 触发信号，正在触发事件。", this);

        onArduinoTriggered?.Invoke();
    }

    private void OnDestroy()
    {
        _isRunning = false;
        _readThread?.Join(500);

        try
        {
            if (_serial != null && _serial.IsOpen)
                _serial.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StartSceneArduinoBridge] 关闭串口时出错：{ex.Message}");
        }
    }

    // ── 串口 ──────────────────────────────────────────────

    private void OpenSerial()
    {
        try
        {
            _serial = new SerialPort(portName, baudRate)
            {
                ReadTimeout  = 100,
                WriteTimeout = 100,
                NewLine      = "\n",
                DtrEnable    = true
            };
            _serial.Open();

            _isRunning  = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            if (debugLog)
                Debug.Log(
                    $"[StartSceneArduinoBridge] 已连接到 {portName}，波特率 {baudRate}。\n" +
                    $"正在等待触发消息：\"{triggerMessage}\"", this);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[StartSceneArduinoBridge] 无法打开串口 '{portName}'：{ex.Message}\n" +
                "Arduino 输入已禁用。请检查串口号，并确认 Arduino 串口监视器未占用该端口。",
                this);
        }
    }

    private void ReadLoop()
    {
        while (_isRunning)
        {
            try
            {
                string line = _serial.ReadLine().Trim();

                if (debugLogAllLines && line.Length > 0)
                    Debug.Log($"[StartSceneArduinoBridge] 串口收到：\"{line}\"");

                if (string.Equals(line, triggerMessage, StringComparison.OrdinalIgnoreCase))
                    _triggered = true;
                else if (debugLog && line.Length > 0)
                    Debug.Log($"[StartSceneArduinoBridge] 消息不匹配 — 收到 \"{line}\"，期望 \"{triggerMessage}\"");
            }
            catch (TimeoutException) { /* 正常空闲超时，继续等待 */ }
            catch (Exception ex)
            {
                if (_isRunning)
                    Debug.LogError($"[StartSceneArduinoBridge] 读取串口时出错：{ex.Message}");
                _isRunning = false;
            }
        }
    }
}
