using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class WorkItem : MonoBehaviour
{
    [Header("Failure Timing (seconds)")]
    public float minTimeToBreak = 4f;
    public float maxTimeToBreak = 10f;

    [Tooltip("关闭后 BreakLoop 不会自动进入 Broke/Bait（教程或脚本控制时用）")]
    public bool enableAutoBreak = true;

    [Header("Broke / Bait 触发概率")]
    [Tooltip("下一次故障时触发 Broke 的权重，与 Bait 权重共同决定概率。例如 8 和 2 表示约 80% Broke、20% Bait")]
    [Min(0f)]
    public float breakWeight = 5f;
    [Tooltip("下一次故障时触发 Bait 的权重")]
    [Min(0f)]
    public float baitWeight = 1f;

    [Header("Hotkey Repair (Input System)")]
    [Tooltip("关闭后：本物体的 Input System 绑定与键盘轮询都不会调用 TryRepair()；Uduino / Inspector 仍可显式调用 TryRepair")]
    public bool enableHotkeyRepair = true;

    [Tooltip("Examples: <Keyboard>/1  <Keyboard>/2  <Keyboard>/numpad1  <Keyboard>/q")]
    public string repairBindingPath = "<Keyboard>/1";
    [Tooltip("与上面按键一致，用于 Uduino/模拟按键时的备用轮询；若使用 UduinoPinToKeyTrigger 建议同时把该脚本的「直接修好目标」指向本物体")]
    public KeyCode repairKeyCodeFallback = KeyCode.Alpha1;

    [Header("Optional Distance Requirement")]
    public bool requirePlayerInRange = false;
    public float interactRange = 2.0f;
    public Transform player; // 不填会自动找 Tag=Player

    [Header("Colors")]
    public Color brokenColor = new Color(1f, 0.2f, 0.2f, 1f);
    public Color baitColor = new Color(0.2f, 1f, 0.2f, 1f);

    [Header("外观与逻辑同步")]
    [Tooltip("勾选时：每帧在 Broke/Bait 状态下重新写入 MaterialPropertyBlock，避免其它脚本、Animator 或材质实例化盖掉着色，造成「看起来像已修好但逻辑仍损坏」。")]
    public bool keepVisualSyncedWithLogic = true;

    [Header("Uduino / 外部输出（可选）")]
    [Tooltip("故障发生时触发，可连到 Uduino 输出等")]
    public UnityEvent OnBroken;
    [Tooltip("I'm scared")] 
    public UnityEvent OnBaiting;
    [Tooltip("恢复为正常态时触发：玩家修好 Broke（Fix）、或 Bait 倒计时自然结束（与 OnBaitingEnded 同帧稍后）；可连材质还原 / LED / 停损坏音等")]
    public UnityEvent OnFixed;
    [Tooltip("Bait 时间到自行结束时触发（玩家未击打），可连到 LED 恢复等")]
    public UnityEvent OnBaitingEnded;

    [Tooltip("击打判定为「修好」时触发（在 Fix 之前）；可接 PlaySoundOnEventAudioManager 的维修正确音")]
    public UnityEvent OnRepairCorrect;
    [Tooltip("击打判定为「错误」（Bait 上乱按或未故障惩罚）时触发；可接 PlaySoundOnEventAudioManager 的维修错误音")]
    public UnityEvent OnRepairIncorrect;

    [Header("Debug")]
    public bool debugLogs = false;
    public KeyCode debugBreakKeyOldInput = KeyCode.None; // 旧Input不用（留空）
    public string debugBreakBindingPath = "<Keyboard>/b"; // 新Input：按B强制故障（排查用）

    public bool IsBroken { get; private set; } = false;
    public bool IsBaiting {get; private set;} = false;

    /// <summary>是否按「电话」规则处理 Bait（Inspector 勾选或 itemName 不区分大小写为 phone）。</summary>
    public bool PhoneBaitRulesActive =>
        usePhoneBaitBehavior
        || (!string.IsNullOrWhiteSpace(itemName) && itemName.Trim().Equals("phone", System.StringComparison.OrdinalIgnoreCase));

    /// <summary>供 GameManager：电话式 Bait 期间是否应按秒扣表现分。</summary>
    public bool ShouldApplyPhoneBaitPerformanceDecay => IsBaiting && PhoneBaitRulesActive;

    /// <summary>供音效：电话规则下是否在挂机位（未摘机）。非电话物体恒为 false。</summary>
    public bool PhoneIsOnCradleForSfx => PhoneBaitRulesActive && !_phonePhysicallyOffHook;

    private Renderer[] allRenderers;
    private MaterialPropertyBlock mpb;

    private InputAction repairAction;
    private InputAction debugBreakAction;
    bool _lastEnableHotkeyRepair;
    bool _warnedNoSupportedColorProperty;

    Coroutine _baitSelfFixCoroutine;
    Coroutine _phoneBaitAfterPickupCoroutine;
    bool _phoneLiftedDuringCurrentBait;

    /// <summary>由摘机/挂机事件维护：true 表示当前认为听筒已离开挂机位（已摘机）。</summary>
    bool _phonePhysicallyOffHook;

    /// <summary>挂机后短时间内忽略 TryRepair（防振动误触 piezo）。</summary>
    float _phoneTryRepairSuppressedUntilTime;

    /// <summary>曾在「仍处于 Broke」时摘机过（损坏音被摘机逻辑停掉）；挂机且未修好时需恢复循环。</summary>
    bool _hadPhonePickupWhileBroken;

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

    public string itemName;

    [Header("Phone · Bait（与普通 Bait 区分）")]
    [Tooltip("勾选后：Bait 不会 3 秒自愈；玩家不摘机则一直保持 Bait。结束方式：摘机后再挂机，或摘机后等待「摘机后自动结束秒数」。")]
    public bool usePhoneBaitBehavior;

    [Tooltip("摘机后经过此时长（秒）自动结束 Bait；0 则仅能通过摘机后再挂机结束（一直拿着不挂机则保持 Bait）。")]
    [Min(0f)]
    public float phoneBaitAutoResolveSecondsAfterPickup = 8f;

    [Tooltip("挂机后的若干秒内忽略 TryRepair（不惩罚、不修好），减轻放下电话时振动触发 piezo 的误触。")]
    [Min(0f)]
    public float phonePutDownTryRepairDebounceSeconds = 0.5f;

    public GameObject tutorialBox;
    public bool isLast;
    public GameObject finalBox;
    private static readonly int[] ColorPropIds =
    {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_UnlitColor"),
        Shader.PropertyToID("_MainColor"),
    };

    void Awake()
    {
        allRenderers = GetComponentsInChildren<Renderer>(true);
        mpb = new MaterialPropertyBlock();

        repairAction = new InputAction(
            name: $"{gameObject.name}_FixHotkey",
            type: InputActionType.Button,
            binding: repairBindingPath
        );

        debugBreakAction = new InputAction(
            name: $"{gameObject.name}_DebugBreak",
            type: InputActionType.Button,
            binding: debugBreakBindingPath
        );
    }

    void OnEnable()
    {
        _lastEnableHotkeyRepair = enableHotkeyRepair;
        ConfigureHotkeyRepairInput();

        debugBreakAction.Enable();
        debugBreakAction.performed += OnDebugBreakPerformed;
    }

    void OnDisable()
    {
        StopAllBaitCoroutines();

        if (repairAction != null)
        {
            repairAction.performed -= OnRepairPerformed;
            repairAction.Disable();
        }

        debugBreakAction.performed -= OnDebugBreakPerformed;
        debugBreakAction.Disable();
    }

    void ConfigureHotkeyRepairInput()
    {
        if (repairAction == null) return;

        repairAction.performed -= OnRepairPerformed;

        if (enableHotkeyRepair)
        {
            repairAction.performed += OnRepairPerformed;
            repairAction.Enable();
        }
        else
            repairAction.Disable();
    }

    void Update()
    {
        if (enableHotkeyRepair != _lastEnableHotkeyRepair)
        {
            _lastEnableHotkeyRepair = enableHotkeyRepair;
            if (isActiveAndEnabled)
                ConfigureHotkeyRepairInput();
        }

        // 备用：Uduino/模拟按键有时不会触发 InputAction.performed，在 Broke/Bait 时轮询键盘（受 enableHotkeyRepair 控制）
        if (!enableHotkeyRepair)
            return;

        if ((IsBroken || IsBaiting) && repairKeyCodeFallback != KeyCode.None && UnityEngine.InputSystem.Keyboard.current != null)
        {
            var key = KeyCodeToKey(repairKeyCodeFallback);
            if (key != UnityEngine.InputSystem.Key.None && UnityEngine.InputSystem.Keyboard.current[key].wasPressedThisFrame)
                TryRepair();
        }
    }

    void LateUpdate()
    {
        if (!keepVisualSyncedWithLogic)
            return;

        if (IsBroken)
            ApplyTintOverride(brokenColor);
        else if (IsBaiting)
            ApplyTintOverride(baitColor);
    }

    static UnityEngine.InputSystem.Key KeyCodeToKey(KeyCode kc)
    {
        switch (kc)
        {
            case KeyCode.Alpha0: return UnityEngine.InputSystem.Key.Digit0;
            case KeyCode.Alpha1: return UnityEngine.InputSystem.Key.Digit1;
            case KeyCode.Alpha2: return UnityEngine.InputSystem.Key.Digit2;
            case KeyCode.Alpha3: return UnityEngine.InputSystem.Key.Digit3;
            case KeyCode.Alpha4: return UnityEngine.InputSystem.Key.Digit4;
            case KeyCode.Alpha5: return UnityEngine.InputSystem.Key.Digit5;
            case KeyCode.Alpha6: return UnityEngine.InputSystem.Key.Digit6;
            case KeyCode.Alpha7: return UnityEngine.InputSystem.Key.Digit7;
            case KeyCode.Alpha8: return UnityEngine.InputSystem.Key.Digit8;
            case KeyCode.Alpha9: return UnityEngine.InputSystem.Key.Digit9;
            case KeyCode.Space: return UnityEngine.InputSystem.Key.Space;
            case KeyCode.Return: return UnityEngine.InputSystem.Key.Enter;
            case KeyCode.Escape: return UnityEngine.InputSystem.Key.Escape;
            case KeyCode.A: return UnityEngine.InputSystem.Key.A;
            case KeyCode.B: return UnityEngine.InputSystem.Key.B;
            case KeyCode.C: return UnityEngine.InputSystem.Key.C;
            case KeyCode.D: return UnityEngine.InputSystem.Key.D;
            case KeyCode.E: return UnityEngine.InputSystem.Key.E;
            case KeyCode.F: return UnityEngine.InputSystem.Key.F;
            case KeyCode.G: return UnityEngine.InputSystem.Key.G;
            case KeyCode.H: return UnityEngine.InputSystem.Key.H;
            case KeyCode.I: return UnityEngine.InputSystem.Key.I;
            case KeyCode.J: return UnityEngine.InputSystem.Key.J;
            case KeyCode.K: return UnityEngine.InputSystem.Key.K;
            case KeyCode.L: return UnityEngine.InputSystem.Key.L;
            case KeyCode.M: return UnityEngine.InputSystem.Key.M;
            case KeyCode.N: return UnityEngine.InputSystem.Key.N;
            case KeyCode.O: return UnityEngine.InputSystem.Key.O;
            case KeyCode.P: return UnityEngine.InputSystem.Key.P;
            case KeyCode.Q: return UnityEngine.InputSystem.Key.Q;
            case KeyCode.R: return UnityEngine.InputSystem.Key.R;
            case KeyCode.S: return UnityEngine.InputSystem.Key.S;
            case KeyCode.T: return UnityEngine.InputSystem.Key.T;
            case KeyCode.U: return UnityEngine.InputSystem.Key.U;
            case KeyCode.V: return UnityEngine.InputSystem.Key.V;
            case KeyCode.W: return UnityEngine.InputSystem.Key.W;
            case KeyCode.X: return UnityEngine.InputSystem.Key.X;
            case KeyCode.Y: return UnityEngine.InputSystem.Key.Y;
            case KeyCode.Z: return UnityEngine.InputSystem.Key.Z;
            default: return UnityEngine.InputSystem.Key.None;
        }
    }

    void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.RegisterItem(this);

        ClearTintOverride();
        //StartCoroutine(BaitLoop());
        StartCoroutine(BreakLoop());

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} renderers found: {allRenderers.Length}");
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterItem(this);

        StopAllBaitCoroutines();

        repairAction?.Dispose();
        debugBreakAction?.Dispose();
    }

    void StopAllBaitCoroutines()
    {
        if (_baitSelfFixCoroutine != null)
        {
            StopCoroutine(_baitSelfFixCoroutine);
            _baitSelfFixCoroutine = null;
        }

        if (_phoneBaitAfterPickupCoroutine != null)
        {
            StopCoroutine(_phoneBaitAfterPickupCoroutine);
            _phoneBaitAfterPickupCoroutine = null;
        }
    }

    /// <summary>由 Arduino <c>onPhonePickup</c> 等绑定：更新摘机状态，并在处于电话 Bait 时推进 Bait 流程。</summary>
    public void NotifyPhonePickedUpForBaitFlow()
    {
        if (!PhoneBaitRulesActive)
            return;

        _phonePhysicallyOffHook = true;

        if (IsBroken)
            _hadPhonePickupWhileBroken = true;

        if (!IsBaiting)
            return;
        if (_phoneLiftedDuringCurrentBait)
            return;

        _phoneLiftedDuringCurrentBait = true;

        if (_phoneBaitAfterPickupCoroutine != null)
        {
            StopCoroutine(_phoneBaitAfterPickupCoroutine);
            _phoneBaitAfterPickupCoroutine = null;
        }

        if (phoneBaitAutoResolveSecondsAfterPickup > 0f)
            _phoneBaitAfterPickupCoroutine = StartCoroutine(PhoneBaitAutoResolveAfterPickupRoutine());
    }

    /// <summary>由 Arduino <c>onPhonePutdown</c> 绑定：更新挂机状态、启动 TryRepair 防抖；若处于电话 Bait 且曾摘机则结束 Bait。</summary>
    public void NotifyPhonePutDownForBaitFlow()
    {
        if (!PhoneBaitRulesActive)
            return;

        _phonePhysicallyOffHook = false;
        _phoneTryRepairSuppressedUntilTime = Time.time + Mathf.Max(0f, phonePutDownTryRepairDebounceSeconds);

        if (IsBaiting && _phoneLiftedDuringCurrentBait)
            ResolveBaitLikeSelfFix();

        if (IsBroken && _hadPhonePickupWhileBroken)
        {
            _hadPhonePickupWhileBroken = false;
            JiU.PlaySoundOnEventAudioManager.ResumeBrokenLoopAfterPhonePutdownForWorkItem(this);
            JiU.PlaySoundOnEvent.ResumeBrokenClipAfterPhonePutdownForWorkItem(this);
        }
    }

    bool ShouldDeferPhoneAutoBreakDuringBossWarning()
    {
        return PhoneBaitRulesActive && GameManager.Instance != null && GameManager.Instance.BossWarning;
    }

    /*private IEnumerator BaitLoop()
    {
        while (true)
        {
            if (!IsBaiting)
            {
                float t = Random.Range(minTimeToBreak, maxTimeToBreak);
                yield return new WaitForSeconds(t);
                
                if (GameManager.FreezeFailures)
                    continue;
                
                Bait();
            }
        }
    }*/
    
    private IEnumerator BreakLoop()
    {
        while (true)
        {
            if (!IsBroken || !IsBaiting)
            {
                if (!enableAutoBreak)
                {
                    yield return null;
                    continue;
                }

                float minT = minTimeToBreak;
                float maxT = maxTimeToBreak;
                if (GameManager.Instance != null && GameManager.Instance.UsePhaseBreakTiming())
                {
                    minT = GameManager.Instance.GetActiveBreakIntervalMin();
                    maxT = GameManager.Instance.GetActiveBreakIntervalMax();
                }

                float t = Random.Range(minT, maxT);
                yield return new WaitForSeconds(t);

                if (GameManager.FreezeFailures)
                    continue;

                float total = breakWeight + baitWeight;
                if (total <= 0f)
                {
                    if (Random.value < 0.5f)
                    {
                        if (GameManager.Instance != null && GameManager.Instance.BlockNewHackEventsNow())
                        {
                            if (baitWeight > 0f)
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && GameManager.Instance.BlockNewBrokeDuringBossStay())
                        {
                            if (baitWeight > 0f)
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && !GameManager.Instance.CanStartNewBrokeState())
                            continue;
                        else if (ShouldDeferPhoneAutoBreakDuringBossWarning())
                            continue;
                        else
                            Break();
                    }
                    else
                        Bait();
                }
                else
                {
                    float roll = Random.Range(0f, total);
                    if (roll < breakWeight)
                    {
                        if (GameManager.Instance != null && GameManager.Instance.BlockNewHackEventsNow())
                        {
                            if (baitWeight > 0f)
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && GameManager.Instance.BlockNewBrokeDuringBossStay())
                        {
                            if (baitWeight > 0f)
                                Bait();
                            else
                                continue;
                        }
                        else if (GameManager.Instance != null && !GameManager.Instance.CanStartNewBrokeState())
                            continue;
                        else if (ShouldDeferPhoneAutoBreakDuringBossWarning())
                            continue;
                        else
                            Break();
                    }
                    else
                        Bait();
                }
            }
            else
            {
                yield return null;
            }
        }
    }

    /// <summary>尝试修好（按键或 Uduino 触发）。可从 Inspector 中 UduinoPinToKeyTrigger 的 onTriggered 或「直接修好目标」调用。</summary>
    public void TryRepair()
    {
        //Debug.Log($"I am trying to fix {this.itemName}!");

        // 电话：优先于下方通用逻辑 — 挂机后防抖窗口内完全忽略 TryRepair（不误判惩罚）
        if (PhoneBaitRulesActive)
        {
            if (Time.time < _phoneTryRepairSuppressedUntilTime)
                return;

            // 未摘机时任意 TryRepair 一律视为错误修复（Bait 走 Ultra，其余走 Punishment）
            if (!_phonePhysicallyOffHook)
            {
                ApplyPhoneTryRepairWhileOnHookPunishment();
                return;
            }
        }

        // 仅真正「损坏」且通过距离等校验后才会 Win + Fix；Bait / 空闲乱按只走 Lose 并 return
        if (!IsBroken)
        {
            if (IsBaiting)
            {
                if (GameManager.Instance != null)
                    GameManager.Instance.UltraPunishment();
                // Punishment 可能本帧触发 Game Over；若再 Invoke，Inspector 里绑定的逻辑可能关掉刚显示的失败面板
                if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                    OnRepairIncorrect?.Invoke();
                return;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        if (requirePlayerInRange)
        {
            if (player == null) return;
            if (Vector3.Distance(player.position, transform.position) > interactRange) return;
        }

        OnRepairCorrect?.Invoke();
        Fix();

        if (GameManager.Instance != null)
        {
            string label = string.IsNullOrWhiteSpace(itemName) ? name : itemName.Trim();
            GameManager.Instance.DebugLogPerformanceAfterSuccessfulRepair(label);
        }
    }

    void ApplyPhoneTryRepairWhileOnHookPunishment()
    {
        if (IsBaiting)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
                OnRepairIncorrect?.Invoke();
            return;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.Punishment();
        if (GameManager.Instance == null || !GameManager.Instance.IsGameOver)
            OnRepairIncorrect?.Invoke();
    }

    private void OnRepairPerformed(InputAction.CallbackContext ctx)
    {
        TryRepair();
    }

    private void OnDebugBreakPerformed(InputAction.CallbackContext ctx)
    {
        Break();
    }

    public void Break()
    {
        if (IsBroken) return;
        _hadPhonePickupWhileBroken = false;
        IsBroken = true;

        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.isTutorialing == true)
            {
                tutorialBox.SetActive(true);
            }
            GameManager.Instance.OnWorkItemEnteredHackedState(this);
            GameManager.Instance.ApplyWorkPressureOnItemBroke();
        }

        WarnIfTintDidNotApply(ApplyTintOverride(brokenColor), "Broke");
        OnBroken?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BROKE -> tint applied");
    }

    public void Bait()
    {
        if (IsBaiting) return;
        IsBaiting = true;
        _phoneLiftedDuringCurrentBait = false;
        StopAllBaitCoroutines();

        WarnIfTintDidNotApply(ApplyTintOverride(baitColor), "Bait");
        OnBaiting?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BAIT -> tint applied");

        if (PhoneBaitRulesActive)
        {
            // 来电 Bait 默认视为在挂机位（避免沿用上一通摘机状态）
            _phonePhysicallyOffHook = false;
            // 不启动 3 秒自愈；等待摘机+挂机或摘机后计时
        }
        else
            _baitSelfFixCoroutine = StartCoroutine(BaitSelfFix());
    }

    IEnumerator BaitSelfFix()
    {
        yield return new WaitForSeconds(3);
        _baitSelfFixCoroutine = null;
        if (!IsBaiting)
            yield break;

        ResolveBaitLikeSelfFix();
    }

    IEnumerator PhoneBaitAutoResolveAfterPickupRoutine()
    {
        yield return new WaitForSeconds(phoneBaitAutoResolveSecondsAfterPickup);
        _phoneBaitAfterPickupCoroutine = null;
        if (!IsBaiting || !PhoneBaitRulesActive || !_phoneLiftedDuringCurrentBait)
            yield break;

        ResolveBaitLikeSelfFix();
    }

    void ResolveBaitLikeSelfFix()
    {
        if (!IsBaiting)
            return;

        StopAllBaitCoroutines();
        _phoneLiftedDuringCurrentBait = false;

        IsBaiting = false;
        ClearTintOverride();
        OnBaitingEnded?.Invoke();

        // 纯 Bait 结束时原先调用 Fix() 会因「已非 Broke/Bait」直接 return，导致 OnFixed 不触发；
        // 若在 Inspector 里用 OnFixed 换回「正常材质」，会出现外观已像修好、事件链却缺一步，与击打修好不同步。
        if (!IsBroken)
            OnFixed?.Invoke();
    }

    public void Fix()
    {
        // 仅当当前处于 Broke 或 Bait 时才执行修好逻辑并触发 OnFixed；正常状态下按键不触发
        if (!IsBroken && !IsBaiting) return;
        StopAllBaitCoroutines();
        _phoneLiftedDuringCurrentBait = false;

        bool wasBroken = IsBroken;
        IsBroken = false;
        IsBaiting = false;
        if (wasBroken)
            _hadPhonePickupWhileBroken = false;

        if (wasBroken && GameManager.Instance != null)
        {
            GameManager.Instance.ApplyWorkPressureOnBrokeRepaired();
            tutorialBox.SetActive(false);
            if (isLast)
            {
                finalBox.SetActive(true);
            }
        }
            

        ClearTintOverride();
        OnFixed?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} FIXED -> tint cleared");
    }

    void WarnIfTintDidNotApply(bool applied, string context)
    {
        if (applied || _warnedNoSupportedColorProperty)
            return;

        _warnedNoSupportedColorProperty = true;
        Debug.LogWarning(
            $"[WorkItem] 「{name}」进入 {context} 时未能在任何 Renderer 材质上写入颜色属性（需含 _BaseColor / _Color / _TintColor / _UnlitColor / _MainColor 之一）。" +
            "逻辑上仍会损坏，但外观可能不变，易与击打修好时的反馈混淆。请换用支持上述属性的 Shader，或用 OnBroken 自行换材质。",
            this);
    }

    /// <returns>是否至少对一个材质槽写入了颜色</returns>
    bool ApplyTintOverride(Color c)
    {
        bool anySlot = false;

        for (int r = 0; r < allRenderers.Length; r++)
        {
            Renderer rend = allRenderers[r];
            if (rend == null) continue;

            bool wroteAny = false;

            int matCount = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
            matCount = Mathf.Max(1, matCount);

            for (int matIndex = 0; matIndex < matCount; matIndex++)
            {
                Material m = null;
                if (rend.sharedMaterials != null && matIndex < rend.sharedMaterials.Length)
                    m = rend.sharedMaterials[matIndex];

                if (m == null) continue;

                bool wroteThisSlot = false;

                for (int i = 0; i < ColorPropIds.Length; i++)
                {
                    int propId = ColorPropIds[i];
                    if (m.HasProperty(propId))
                    {
                        rend.GetPropertyBlock(mpb, matIndex);
                        mpb.SetColor(propId, c);
                        rend.SetPropertyBlock(mpb, matIndex);
                        wroteAny = true;
                        wroteThisSlot = true;
                    }
                }

                if (debugLogs && !wroteThisSlot)
                    Debug.Log($"[WorkItem] {name} renderer({rend.name}) slot({matIndex}) has NO color property.");
            }

            if (debugLogs && !wroteAny)
                Debug.LogWarning($"[WorkItem] {name} renderer({rend.name}) could not be tinted (no supported color props).");

            if (wroteAny)
                anySlot = true;
        }

        return anySlot;
    }

    private void ClearTintOverride()
    {
        for (int r = 0; r < allRenderers.Length; r++)
        {
            Renderer rend = allRenderers[r];
            if (rend == null) continue;

            int matCount = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
            matCount = Mathf.Max(1, matCount);

            for (int matIndex = 0; matIndex < matCount; matIndex++)
                rend.SetPropertyBlock(null, matIndex);
        }
    }
}
