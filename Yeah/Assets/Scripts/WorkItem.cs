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

    [Header("Broke / Bait 触发概率")]
    [Tooltip("下一次故障时触发 Broke 的权重，与 Bait 权重共同决定概率。例如 8 和 2 表示约 80% Broke、20% Bait")]
    [Min(0f)]
    public float breakWeight = 5f;
    [Tooltip("下一次故障时触发 Bait 的权重")]
    [Min(0f)]
    public float baitWeight = 1f;

    [Header("Hotkey Repair (Input System)")]
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

    [Header("Uduino / 外部输出（可选）")]
    [Tooltip("故障发生时触发，可连到 Uduino 输出等")]
    public UnityEvent OnBroken;
    [Tooltip("I'm scared")] 
    public UnityEvent OnBaiting;
    [Tooltip("修好时触发（玩家击打修好时），可连到 Uduino / LED 恢复等")]
    public UnityEvent OnFixed;
    [Tooltip("Bait 时间到自行结束时触发（玩家未击打），可连到 LED 恢复等")]
    public UnityEvent OnBaitingEnded;

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

    //this doesn't really have a point but it's fun lol
    public GameObject smokeParticles;

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
        repairAction.Enable();
        repairAction.performed += OnRepairPerformed;

        debugBreakAction.Enable();
        debugBreakAction.performed += OnDebugBreakPerformed;
    }

    void OnDisable()
    {
        repairAction.performed -= OnRepairPerformed;
        repairAction.Disable();

        debugBreakAction.performed -= OnDebugBreakPerformed;
        debugBreakAction.Disable();
    }

    void Update()
    {
        // 备用：Uduino/模拟按键有时不会触发 InputAction.performed，在 Broke/Bait 时轮询键盘
        if ((IsBroken || IsBaiting) && repairKeyCodeFallback != KeyCode.None && UnityEngine.InputSystem.Keyboard.current != null)
        {
            var key = KeyCodeToKey(repairKeyCodeFallback);
            if (key != UnityEngine.InputSystem.Key.None && UnityEngine.InputSystem.Keyboard.current[key].wasPressedThisFrame)
                TryRepair();
        }
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
                float t = Random.Range(minTimeToBreak, maxTimeToBreak);
                yield return new WaitForSeconds(t);

                // Boss 预警开始到离开期间：不产生新故障
                if (GameManager.FreezeFailures)
                    continue;

                // 按 Inspector 中的 breakWeight / baitWeight 决定本次是 Broke 还是 Bait
                float total = breakWeight + baitWeight;
                if (total <= 0f)
                {
                    if (Random.value < 0.5f) Break();
                    else Bait();
                }
                else
                {
                    float roll = Random.Range(0f, total);
                    if (roll < breakWeight)
                        Break();
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
        if (!IsBroken)
        {
            if (IsBaiting && GameManager.Instance != null)
                GameManager.Instance.UltraPunishment();
            else if (GameManager.Instance != null)
                GameManager.Instance.Punishment();
        }
        if (requirePlayerInRange)
        {
            if (player == null) return;
            if (Vector3.Distance(player.position, transform.position) > interactRange) return;
        }
        Fix();
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

        ApplyTintOverride(brokenColor);
        OnBroken?.Invoke();

        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BROKE -> tint applied");
    }

    public void Bait()
    {
        if (IsBaiting) return;
        IsBaiting = true;
        
        ApplyTintOverride(baitColor);
        OnBaiting?.Invoke();
        
        if (debugLogs)
            Debug.Log($"[WorkItem] {name} BAIT -> tint applied");
        StartCoroutine(BaitSelfFix());
    }

    IEnumerator BaitSelfFix()
    {
        yield return new WaitForSeconds(3);
        if (IsBaiting)
        {
            IsBaiting = false;
            ClearTintOverride();
            OnBaitingEnded?.Invoke();
        }
        Fix();
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

    private void ApplyTintOverride(Color c)
    {
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
        }
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
