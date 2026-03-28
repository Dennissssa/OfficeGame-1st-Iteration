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

    private Renderer[] allRenderers;
    private MaterialPropertyBlock mpb;

    private InputAction repairAction;
    private InputAction debugBreakAction;
    bool _lastEnableHotkeyRepair;
    bool _warnedNoSupportedColorProperty;

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

    public string itemName;
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

        repairAction?.Dispose();
        debugBreakAction?.Dispose();
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

        // 仅真正「损坏」且通过距离等校验后才会 Win + Fix；Bait / 空闲乱按只走 Lose 并 return
        if (!IsBroken)
        {
            if (IsBaiting)
            {
                if (GameManager.Instance != null)
                    GameManager.Instance.UltraPunishment();
                OnRepairIncorrect?.Invoke();
                return;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
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
        IsBroken = true;

        if (GameManager.Instance != null)
            GameManager.Instance.OnWorkItemEnteredHackedState(this);

        WarnIfTintDidNotApply(ApplyTintOverride(brokenColor), "Broke");
        OnBroken?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BROKE -> tint applied");
    }

    public void Bait()
    {
        if (IsBaiting) return;
        IsBaiting = true;
        
        WarnIfTintDidNotApply(ApplyTintOverride(baitColor), "Bait");
        OnBaiting?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BAIT -> tint applied");
        StartCoroutine(BaitSelfFix());
    }

    IEnumerator BaitSelfFix()
    {
        yield return new WaitForSeconds(3);
        if (!IsBaiting)
            yield break;

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
        IsBroken = false;
        IsBaiting = false;

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
