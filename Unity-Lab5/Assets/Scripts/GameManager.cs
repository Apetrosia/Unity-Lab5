using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class GameManager : MonoBehaviour
{
    // ================== ТИПЫ ==================
    private enum FocusMode { Input, List }
    private enum Kind { System, Player, Hostile, Fake, Hidden, Trap }
    private enum Category { Normal, CpuBurner, SlowUpload, UploaderKiller }
    private enum Effect { None, SlowUpload, BlockUploadCore, StealUpload, KillUploader }
    private enum Role { None, Uploader, Crypto, Defrag, Bypass }

    private class Def
    {
        public string name;
        public string key;
        public Kind kind;
        public Category category = Category.Normal;
        public float ram;
        public float growth;
        public float cpu;
        public Effect effect;
        public Role role;
        public float unlock;
        public float duration;
        public float launchTime;
        public float cooldown;
        public string deathSub;
        public string realName;
    }

    private class ProcessInfo
    {
        public Def def;
        public string name;
        public float baseRam, ramNoise = 1f;
        public float baseCpu, cpuNoise = 1f;
        public bool hidden;
        public float expireAt;
        public float effectTimer;
        public VisualElement item;
        public Label idLabel, nameLabel, cpuLabel, ramLabel;
        public float Ram => baseRam * ramNoise;
    }

    private class Job
    {
        public string name;
        public float endTime;
        public System.Action onComplete;
    }

    // ================== ОПРЕДЕЛЕНИЯ ПРОЦЕССОВ ==================
    // --- Системные ---
    private static readonly Def DefCore = new Def { name = "CORE.DLL", kind = Kind.System, ram = 128f, cpu = 8f, deathSub = "CORE DESTROYED" };
    private static readonly Def DefMemMgr = new Def { name = "MEMORY_MGR.SYS", kind = Kind.System, ram = 96f, cpu = 5f, deathSub = "MEMORY MANAGER DESTROYED" };
    private static readonly Def DefNetStack = new Def { name = "NETWORK_STACK.SYS", kind = Kind.System, ram = 80f, cpu = 7f, deathSub = "NETWORK STACK DESTROYED" };
    private static readonly Def DefKernel = new Def { name = "KERNEL32.DLL", kind = Kind.System, ram = 96f, cpu = 6f, deathSub = "KERNEL PANIC" };

    // --- Программы игрока ---
    private static readonly Def DefUploader = new Def { name = "UPLOADER.EXE", key = "UPLOADER", kind = Kind.Player, ram = 120f, cpu = 18f, role = Role.Uploader, launchTime = 2f };
    private static readonly Def DefCrypto = new Def { name = "CRYPTO.EXE", key = "CRYPTO", kind = Kind.Player, ram = 80f, cpu = 13f, role = Role.Crypto, launchTime = 3f };
    private static readonly Def DefDefrag = new Def { name = "DEFRAG.EXE", key = "DEFRAG", kind = Kind.Player, ram = 60f, cpu = 13f, role = Role.Defrag, launchTime = 3f, duration = 12f, cooldown = 35f };
    private static readonly Def DefBypass = new Def { name = "BYPASS.EXE", key = "BYPASS", kind = Kind.Player, ram = 40f, cpu = 15f, role = Role.Bypass, launchTime = 4f, duration = 8f, cooldown = 30f };
    private static readonly Def[] PlayerDefs = { DefUploader, DefCrypto, DefDefrag, DefBypass };

    // --- Обычные враждебные (CPU увеличен на 25%) ---
    private static readonly Def[] NormalHostileDefs =
    {
        new Def { name = "SCAN.EXE",        kind = Kind.Hostile, category = Category.Normal, ram = 64f,  growth = 2f,   cpu = 23f, unlock = 0f },
        new Def { name = "TROJAN.EXE",      kind = Kind.Hostile, category = Category.Normal, ram = 32f,  growth = 5f,   cpu = 28f, unlock = 20f },
        new Def { name = "MEMORY_LEAK.BIN", kind = Kind.Hostile, category = Category.Normal, ram = 16f,  growth = 6f,   cpu = 10f, unlock = 20f },
    };

    // --- CPU-burner процессы (CPU увеличен на 25%) ---
    private static readonly Def[] CpuBurnerDefs =
    {
        new Def { name = "MINER.EXE",       kind = Kind.Hostile, category = Category.CpuBurner, ram = 8f,   growth = 0.5f, cpu = 44f, unlock = 0f },
        new Def { name = "HASHCRACK.EXE",   kind = Kind.Hostile, category = Category.CpuBurner, ram = 12f,  growth = 1f,   cpu = 53f, unlock = 25f },
        new Def { name = "BRUTEFORCE.EXE",  kind = Kind.Hostile, category = Category.CpuBurner, ram = 16f,  growth = 1f,   cpu = 60f, unlock = 50f },
    };

    // --- Процессы, замедляющие загрузку (CPU увеличен на 25%) ---
    private static readonly Def[] SlowUploadDefs =
    {
        new Def { name = "FIREWALL.SYS",    kind = Kind.Hostile, category = Category.SlowUpload, ram = 96f,  growth = 2.5f, cpu = 15f,  unlock = 25f, effect = Effect.SlowUpload },
        new Def { name = "SPYWARE.DLL",     kind = Kind.Hostile, category = Category.SlowUpload, ram = 48f,  growth = 1.5f, cpu = 13f,  unlock = 40f, effect = Effect.StealUpload },
        new Def { name = "QUARANTINE.DAT",  kind = Kind.Hostile, category = Category.SlowUpload, ram = 128f, growth = 3f,   cpu = 19f,  unlock = 40f, effect = Effect.BlockUploadCore },
    };

    // --- НОВОЕ: Процессы, убивающие uploader ---
    private static readonly Def[] UploaderKillerDefs =
    {
        new Def { name = "KILLER.EXE",      kind = Kind.Hostile, category = Category.UploaderKiller, ram = 48f,  growth = 2f,   cpu = 20f, unlock = 30f, effect = Effect.KillUploader },
        new Def { name = "SABOTEUR.DLL",    kind = Kind.Hostile, category = Category.UploaderKiller, ram = 64f,  growth = 1.5f, cpu = 25f, unlock = 45f, effect = Effect.KillUploader },
    };

    // --- Подделки под системные (CPU увеличен на 25%) ---
    private static readonly Def[] FakeDefs =
    {
        new Def { name = "C0RE.DLL",          realName = "CORE.DLL",          kind = Kind.Fake, ram = 80f, growth = 2f, cpu = 20f },
        new Def { name = "MEM0RY_MGR.SYS",    realName = "MEMORY_MGR.SYS",    kind = Kind.Fake, ram = 80f, growth = 2f, cpu = 20f },
        new Def { name = "NETW0RK_STACK.SYS", realName = "NETWORK_STACK.SYS", kind = Kind.Fake, ram = 80f, growth = 2f, cpu = 20f },
        new Def { name = "KERNEL32.DLI",      realName = "KERNEL32.DLL",      kind = Kind.Fake, ram = 80f, growth = 2f, cpu = 20f },
    };

    // --- Особые ---
    private static readonly Def DefHidden = new Def { name = "HIDDEN_PROC", kind = Kind.Hidden, ram = 10f, growth = 4f, cpu = 13f };
    private static readonly Def DefTrap = new Def { name = "FORMAT_C.EXE", kind = Kind.Trap, ram = 0f, cpu = 0f };

    static bool IsThreat(Def d) => d.kind == Kind.Hostile || d.kind == Kind.Fake || d.kind == Kind.Hidden;

    // ================== НАСТРОЙКИ (инспектор) ==================
    [Header("Спавн угроз")]
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(6f, 10f); // Уменьшено
    [SerializeField] private float firstSpawnDelay = 6f; // Уменьшено
    [SerializeField] private int maxThreatsCap = 10; // Увеличено
    [Tooltip("Если включено, новые угрозы появляются в случайном месте списка")]
    [SerializeField] private bool randomInsert = false;
    [SerializeField] private float kernelLoadTime = 60f;

    [Header("Колебания нагрузки")]
    [SerializeField] private Vector2 fluctuationIntervalRange = new Vector2(0.5f, 1f);
    [SerializeField] private float ramNoiseStep = 0.04f;
    [SerializeField] private float cpuNoiseStep = 0.1f;

    [Header("Нагрузка и загрузка")]
    [SerializeField] private float cpuOverloadThreshold = 90f;
    [SerializeField] private float overloadUploadMultiplier = 0.25f;
    [SerializeField] private float cpuMeltdownTime = 5f;
    [SerializeField] private float baseUploadSpeed = 0.1f; // УМЕНЬШЕНО с 0.2f до 0.1f
    [SerializeField] private float cacheGrowth = 3f;
    [SerializeField] private float cryptoSpawnSlowdown = 1.6f;
    [SerializeField] private float maxScanBonus = 0.5f;

    [Header("Глитч при 50% upload")]
    [SerializeField] private float glitchChance = 0.6f;

    [Header("Звуки (необязательно)")]
    [SerializeField] private AudioClip beepClip;
    [SerializeField] private AudioClip spawnClip;
    [SerializeField] private AudioClip systemSpawnClip;
    [SerializeField] private AudioClip errorClip;

    // ================== ИГРОВЫЕ ПЕРЕМЕННЫЕ ==================
    private const float RamMax = 1024f;
    private float ramCache = 100f;
    private float uploadProgress = 0f;
    private float scanBonus = 0f;
    private float cpuHeat = 0f;
    private bool cpuOverloaded = false;
    private bool isGameOver = false;
    private bool initialized = false;
    private bool kernelLoaded = false;
    private bool ramCritical = false;
    private float elapsed = 0f;
    private int level = 0;
    private int threatsKilled = 0;
    private float spawnTimer = 0f;
    private float spawnInterval;
    private float fluctuationTimer = 0f;
    private float nextFluctuation = 0.7f;
    private float uiTimer = 0f;
    private float beepTimer = 0f;

    // Таймеры для остановки uploader
    private float firewallKillTimer = 0f;
    private float quarantineKillTimer = 0f;
    private float killerKillTimer = 0f;
    private float uploadCoreSlowTimer = 0f;
    private float flushCacheCpuTimer = 0f;

    private bool uploaderRunning, cryptoActive, defragActive, bypassActive;
    private bool firewallActive, quarantineActive, killerActive;

    private readonly List<ProcessInfo> processes = new List<ProcessInfo>();
    private readonly List<ProcessInfo> visible = new List<ProcessInfo>();
    private readonly List<Job> jobs = new List<Job>();
    private readonly Dictionary<string, float> cooldownEnd = new Dictionary<string, float>();

    private FocusMode focusMode = FocusMode.Input;
    private int selectedIndex = 0;
    private bool isInputBlocked = false;
    private float blockTimer = 0f;
    private const int MaxLogLines = 7;
    private readonly Queue<string> logLines = new Queue<string>();

    // === UI ===
    private VisualElement root, screenContainer, rightPanel, endScreen;
    private VisualElement cpuFill, ramFill, uploadFill;
    private Label cpuText, ramText, uploadText, logOutput, statusText, jobsText;
    private ScrollView processList;
    private TextField commandInput;
    private AudioSource audioSource;
    private string lastStatus = "";
    private Color lastStatusColor = Color.green;

    // ================== СУММЫ ==================
    private float TotalProcessRam
    {
        get { float s = 0f; foreach (var p in processes) s += p.Ram; return s; }
    }
    private float RamCurrent => ramCache + TotalProcessRam;
    private float RamPercent => RamCurrent / RamMax * 100f;

    private float CpuOf(ProcessInfo p)
    {
        float c = p.baseCpu * p.cpuNoise;
        if (bypassActive && IsThreat(p.def)) c *= 0.2f;
        return c;
    }

    private float TotalCpu()
    {
        float s = 0f;
        foreach (var p in processes) s += CpuOf(p);
        if (flushCacheCpuTimer > 0f) s += 30f;
        return s;
    }

    // ================== START ==================
    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        screenContainer = root.Q<VisualElement>("screen-container");
        rightPanel = root.Q<VisualElement>("right-panel");
        cpuFill = root.Q<VisualElement>("cpu-fill");
        ramFill = root.Q<VisualElement>("ram-fill");
        uploadFill = root.Q<VisualElement>("upload-fill");
        cpuText = root.Q<Label>("cpu-text");
        ramText = root.Q<Label>("ram-text");
        uploadText = root.Q<Label>("upload-text");
        logOutput = root.Q<Label>("log-output");
        statusText = root.Q<Label>("status-text");
        processList = root.Q<ScrollView>("process-list");
        commandInput = root.Q<TextField>("command-input");

        if (processList == null || commandInput == null)
        {
            Debug.LogError("GameManager: не найден process-list или command-input в UXML.");
            return;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (rightPanel != null)
        {
            jobsText = new Label();
            jobsText.AddToClassList("bar-text");
            jobsText.style.whiteSpace = WhiteSpace.Normal;
            jobsText.style.marginTop = 6;
            rightPanel.Add(jobsText);
        }

        spawnInterval = firstSpawnDelay;
        nextFluctuation = Random.Range(fluctuationIntervalRange.x, fluctuationIntervalRange.y);

        if (logOutput != null) logOutput.text = "";

        AddProcess(DefCore);
        AddProcess(DefMemMgr);
        AddProcess(DefNetStack);
        AddProcess(NormalHostileDefs[0]);

        Log("System initialized. Monitoring RAM and CPU...");
        Log("Type 'HELP' for available commands.");
        Log("Type 'RUN UPLOADER' to begin the upload.");

        RecomputeState();
        RefreshProcessTable();
        UpdateJobsPanel();
        UpdateUI();

        processList.focusable = true;
        commandInput.focusable = true;
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        SetFocusMode(FocusMode.Input);
        initialized = true;
    }

    // ================== ФОКУС И КЛАВИШИ ==================
    void OnKeyDown(KeyDownEvent evt)
    {
        if (isGameOver)
        {
            if (evt.keyCode == KeyCode.R)
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            Consume(evt);
            return;
        }

        if (isInputBlocked)
        {
            Consume(evt);
            return;
        }

        if (evt.keyCode == KeyCode.Tab)
        {
            Consume(evt);
            SetFocusMode(focusMode == FocusMode.Input ? FocusMode.List : FocusMode.Input);
            return;
        }

        if (focusMode == FocusMode.Input)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                Consume(evt);
                SubmitCommand();
            }
        }
        else
        {
            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.W)
            {
                Consume(evt);
                MoveSelection(-1);
            }
            else if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.S)
            {
                Consume(evt);
                MoveSelection(1);
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter
                     || evt.keyCode == KeyCode.Delete)
            {
                Consume(evt);
                KillSelected();
            }
            else if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)
            {
                Consume(evt);
            }
        }
    }

    static void Consume(EventBase evt)
    {
        evt.StopImmediatePropagation();
        evt.PreventDefault();
    }

    void SetFocusMode(FocusMode mode)
    {
        focusMode = mode;
        if (mode == FocusMode.Input)
            commandInput.Focus();
        else
        {
            processList.Focus();
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, visible.Count - 1));
        }
        UpdateSelectionVisual();
    }

    void EnsureFocus()
    {
        if (root == null || commandInput == null || processList == null) return;
        if (isInputBlocked || isGameOver) return;
        VisualElement target = focusMode == FocusMode.Input ? (VisualElement)commandInput : processList;
        var focused = root.focusController?.focusedElement as VisualElement;
        bool ok = focused != null && (focused == target || target.Contains(focused));
        if (!ok) target.Focus();
    }

    // ================== ВЫДЕЛЕНИЕ В СПИСКЕ ==================
    void MoveSelection(int delta)
    {
        if (visible.Count == 0) return;
        selectedIndex = Mathf.Clamp(selectedIndex + delta, 0, visible.Count - 1);
        UpdateSelectionVisual();
    }

    void UpdateSelectionVisual()
    {
        foreach (var p in processes)
            p.item.EnableInClassList("process-item--selected", false);
        if (focusMode == FocusMode.List && visible.Count > 0)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, visible.Count - 1);
            visible[selectedIndex].item.EnableInClassList("process-item--selected", true);
            processList.ScrollTo(visible[selectedIndex].item);
        }
    }

    void KillSelected()
    {
        if (visible.Count == 0) return;
        var p = visible[Mathf.Clamp(selectedIndex, 0, visible.Count - 1)];
        Log($"> KILL (selected) {p.name}");
        TryKill(p);
    }

    // ================== UPDATE ==================
    void Update()
    {
        if (!initialized) return;

        if (isInputBlocked)
        {
            blockTimer += Time.deltaTime;
            if (blockTimer >= 3f)
            {
                isInputBlocked = false;
                blockTimer = 0f;
                commandInput.isReadOnly = false;
                commandInput.RemoveFromClassList("input-error");
                commandInput.value = "";
            }
        }

        EnsureFocus();
        if (isGameOver) return;

        float dt = Time.deltaTime;
        elapsed += dt;
        level = Mathf.FloorToInt(elapsed / 30f);

        if (uploadCoreSlowTimer > 0f) uploadCoreSlowTimer -= dt;
        if (flushCacheCpuTimer > 0f) flushCacheCpuTimer -= dt;

        // Таймеры для остановки uploader
        if (firewallActive)
        {
            firewallKillTimer += dt;
            if (firewallKillTimer >= 12f) // Уменьшено с 15f
            {
                firewallKillTimer = 0f;
                if (Random.value < 0.4f && uploaderRunning) // Увеличен шанс с 0.3f
                {
                    KillUploaderProcess();
                }
            }
        }
        else firewallKillTimer = 0f;

        if (quarantineActive)
        {
            quarantineKillTimer += dt;
            if (quarantineKillTimer >= 12f) // Уменьшено с 15f
            {
                quarantineKillTimer = 0f;
                if (Random.value < 0.4f && uploaderRunning) // Увеличен шанс с 0.3f
                {
                    KillUploaderProcess();
                }
            }
        }
        else quarantineKillTimer = 0f;

        // НОВОЕ: таймер для KILLER.EXE и SABOTEUR.DLL
        if (killerActive)
        {
            killerKillTimer += dt;
            if (killerKillTimer >= 10f) // Каждые 10 секунд
            {
                killerKillTimer = 0f;
                if (Random.value < 0.5f && uploaderRunning) // 50% шанс
                {
                    KillUploaderProcess();
                }
            }
        }
        else killerKillTimer = 0f;

        RunJobs();
        ExpireTimedProcesses();
        if (isGameOver) return;

        RecomputeState();
        TickSpawning(dt);
        TickKernelLoad();
        TickGrowth(dt);
        TickFluctuation(dt);
        TickSpyware(dt);
        TickLoad(dt);

        if (isGameOver) return;

        uiTimer += dt;
        if (uiTimer >= 0.2f)
        {
            uiTimer = 0f;
            RefreshProcessTable();
            UpdateJobsPanel();
        }

        UpdateUI();
        TickAlarms(dt);
    }

    void KillUploaderProcess()
    {
        var uploader = processes.Find(p => p.def.role == Role.Uploader);
        if (uploader != null)
        {
            RemoveProcess(uploader);
            uploaderRunning = false;
            Log("WARNING: UPLOADER.EXE was terminated by system!");
            Log("You must restart it: RUN UPLOADER");
            Sfx(errorClip);
        }
    }

    void RecomputeState()
    {
        uploaderRunning = false;
        cryptoActive = false;
        defragActive = false;
        bypassActive = false;
        firewallActive = false;
        quarantineActive = false;
        killerActive = false;

        foreach (var p in processes)
        {
            switch (p.def.role)
            {
                case Role.Uploader: uploaderRunning = true; break;
                case Role.Crypto: cryptoActive = true; break;
                case Role.Defrag: defragActive = true; break;
                case Role.Bypass: bypassActive = true; break;
            }
        }

        if (bypassActive) return;

        foreach (var p in processes)
        {
            if (p.def.effect == Effect.SlowUpload) firewallActive = true;
            else if (p.def.effect == Effect.BlockUploadCore) quarantineActive = true;
            else if (p.def.effect == Effect.KillUploader) killerActive = true;
        }
    }

    // ================== ТИКИ ==================
    void TickSpawning(float dt)
    {
        spawnTimer += dt;
        if (spawnTimer < spawnInterval) return;
        spawnTimer = 0f;
        SpawnThreat();

        // БОЛЕЕ АГРЕССИВНОЕ уменьшение интервала (O(n^2.5))
        float uploadMultiplier = 1f - 0.95f * Mathf.Pow(uploadProgress / 100f, 2.5f);
        float levelMultiplier = 1f - 0.08f * level; // Дополнительное уменьшение от уровня
        float interval = Random.Range(spawnIntervalRange.x, spawnIntervalRange.y) * uploadMultiplier * levelMultiplier;

        if (cryptoActive) interval *= cryptoSpawnSlowdown;
        spawnInterval = Mathf.Max(1.5f, interval); // минимум 1.5 секунды

        if (uploadProgress >= 50f && Random.value < glitchChance)
        {
            ShuffleProcesses();
        }
    }

    void TickKernelLoad()
    {
        if (kernelLoaded || elapsed < kernelLoadTime) return;
        kernelLoaded = true;
        AddProcess(DefKernel);
        Log("SYSTEM PROCESS LOADED: KERNEL32.DLL");
        Sfx(systemSpawnClip);
    }

    void TickGrowth(float dt)
    {
        ramCache += cacheGrowth * dt;
        if (bypassActive) return;
        float mult = (1f + 0.05f * level) * (defragActive ? 0.5f : 1f);
        foreach (var p in processes)
            if (IsThreat(p.def))
                p.baseRam += p.def.growth * mult * dt;
    }

    void TickFluctuation(float dt)
    {
        fluctuationTimer += dt;
        if (fluctuationTimer < nextFluctuation) return;
        fluctuationTimer = 0f;
        nextFluctuation = Random.Range(fluctuationIntervalRange.x, fluctuationIntervalRange.y);
        foreach (var p in processes)
        {
            p.ramNoise = Mathf.Clamp(p.ramNoise + Random.Range(-ramNoiseStep, ramNoiseStep), 0.9f, 1.1f);
            p.cpuNoise = Mathf.Clamp(p.cpuNoise + Random.Range(-cpuNoiseStep, cpuNoiseStep), 0.7f, 1.3f);
        }
    }

    void TickSpyware(float dt)
    {
        if (bypassActive) return;
        foreach (var p in processes)
        {
            if (p.def.effect != Effect.StealUpload) continue;
            p.effectTimer += dt;
            if (p.effectTimer >= 10f)
            {
                p.effectTimer = 0f;
                uploadProgress = Mathf.Max(0f, uploadProgress - 2f);
                Log($"{p.name} stole 2% of upload progress!");
            }
        }
    }

    void TickLoad(float dt)
    {
        float cpuTotal = TotalCpu();
        bool overloadedNow = cpuTotal >= cpuOverloadThreshold;
        if (overloadedNow != cpuOverloaded)
        {
            cpuOverloaded = overloadedNow;
            Log(cpuOverloaded
                ? "WARNING: CPU OVERLOAD! Upload slowed down."
                : "CPU load back to normal.");
        }

        if (cpuTotal >= 100f) cpuHeat += dt;
        else cpuHeat = Mathf.Max(0f, cpuHeat - dt * 0.5f);

        if (cpuHeat >= cpuMeltdownTime)
        {
            TriggerGameOver("FATAL ERROR: CPU MELTDOWN", "AI CONTAINED. DELETING...");
            return;
        }

        uploadProgress = Mathf.Min(100f, uploadProgress + UploadSpeed() * dt);
        if (uploadProgress >= 100f)
        {
            Win();
            return;
        }

        if (RamCurrent >= RamMax)
        {
            TriggerGameOver("FATAL ERROR: OUT OF MEMORY", "AI CONTAINED. DELETING...");
        }
    }

    float UploadSpeed()
    {
        if (!uploaderRunning) return 0f;
        float s = baseUploadSpeed * (1f + scanBonus);
        if (firewallActive) s *= 0.7f;
        if (cpuOverloaded) s *= overloadUploadMultiplier;
        if (RamPercent >= 80f) s *= 0.6f;
        if (uploadCoreSlowTimer > 0f) s *= 0.5f;
        return s;
    }

    void TickAlarms(float dt)
    {
        float ramP = RamPercent;
        float cpuTotal = TotalCpu();

        // ИСПРАВЛЕНО: CRITICAL при 85% (синхронизировано с красным цветом полосы)
        // Мерцание при CRITICAL
        if (screenContainer != null)
        {
            bool isCritical = ramP >= 85f || cpuTotal >= 90f;
            if (isCritical && Random.value < 0.2f) // Увеличен шанс мерцания с 0.15f
            {
                screenContainer.style.opacity = 0.65f;
            }
            else
            {
                screenContainer.style.opacity = 1f;
            }
        }

        if (ramP >= 85f && !ramCritical) // Уменьшено с 90f
        {
            ramCritical = true;
            Log("WARNING: MEMORY CRITICAL!");
        }
        else if (ramP < 83f) ramCritical = false; // Уменьшено с 88f

        // ИСПРАВЛЕНО: CRITICAL при 85% (вместо 90%)
        if (ramP >= 85f || cpuTotal >= 90f)
        {
            SetStatus("STATUS: CRITICAL", new Color(1f, 0.2f, 0.2f));
        }
        else if (ramP >= 70f || cpuOverloaded)
        {
            SetStatus("STATUS: WARNING", new Color(1f, 1f, 0f));
        }
        else
        {
            SetStatus("STATUS: STABLE", new Color(0f, 1f, 0f));
        }

        float period = ramP >= 95f ? 1f : (ramP >= 85f ? 1.5f : 0f); // Уменьшено с 2f
        if (period > 0f)
        {
            beepTimer += dt;
            if (beepTimer >= period)
            {
                beepTimer = 0f;
                Sfx(beepClip);
            }
        }
        else beepTimer = 0f;
    }

    void SetStatus(string text, Color color)
    {
        if (statusText == null) return;
        if (text == lastStatus && color == lastStatusColor) return;
        lastStatus = text;
        lastStatusColor = color;
        statusText.text = text;
        statusText.style.color = color;
    }

    // ================== СПАВН ==================
    int CountThreats()
    {
        int n = 0;
        foreach (var p in processes) if (IsThreat(p.def)) n++;
        return n;
    }

    void SpawnThreat()
    {
        int cap = Mathf.Min(maxThreatsCap, 4 + level);
        if (CountThreats() >= cap) return;

        float r = Random.value;
        Def def = null;

        float cpuBurnerChance = uploadProgress >= 10f ? 0.15f : 0f;
        float hiddenChance = uploadProgress >= 25f ? 0.10f : 0f;
        float fakeChance = uploadProgress >= 50f ? Mathf.Lerp(0.05f, 0.20f, (uploadProgress - 50f) / 50f) : 0f;
        float slowChance = uploadProgress >= 60f ? Mathf.Lerp(0.10f, 0.20f, (uploadProgress - 60f) / 40f) : 0f; // Увеличено с 70f и 0.05f
        float killerChance = uploadProgress >= 30f ? Mathf.Lerp(0.08f, 0.18f, (uploadProgress - 30f) / 70f) : 0f; // НОВОЕ

        float threshold = 0f;
        if (r < (threshold += killerChance)) def = PickUploaderKiller();
        else if (r < (threshold += cpuBurnerChance)) def = PickCpuBurner();
        else if (r < (threshold += hiddenChance)) def = PickSpecial();
        else if (r < (threshold += fakeChance)) def = PickFake();
        else if (r < (threshold += slowChance)) def = PickSlowUpload();
        else def = PickNormalHostile();

        if (def == null) def = PickNormalHostile();

        var p = AddProcess(def);

        switch (def.kind)
        {
            case Kind.Fake:
                Log($"SYSTEM PROCESS LOADED: {p.name}");
                Sfx(systemSpawnClip);
                break;
            case Kind.Hidden:
                break;
            case Kind.Trap:
                Log($"Unknown process loaded: {p.name}");
                Sfx(spawnClip);
                break;
            default:
                break;
        }
    }

    Def PickNormalHostile()
    {
        var options = new List<Def>();
        foreach (var d in NormalHostileDefs)
            if (d.unlock <= elapsed) options.Add(d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : NormalHostileDefs[0];
    }

    Def PickCpuBurner()
    {
        var options = new List<Def>();
        foreach (var d in CpuBurnerDefs)
            if (d.unlock <= elapsed) options.Add(d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
    }

    Def PickSlowUpload()
    {
        var options = new List<Def>();
        foreach (var d in SlowUploadDefs)
            if (d.unlock <= elapsed) options.Add(d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
    }

    // НОВОЕ: выбор процесса, убивающего uploader
    Def PickUploaderKiller()
    {
        var options = new List<Def>();
        foreach (var d in UploaderKillerDefs)
            if (d.unlock <= elapsed) options.Add(d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
    }

    Def PickFake()
    {
        var options = new List<Def>();
        foreach (var d in FakeDefs)
            if (!processes.Exists(p => p.name == d.name) && processes.Exists(p => p.name == d.realName))
                options.Add(d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : null;
    }

    Def PickSpecial()
    {
        bool hasHidden = processes.Exists(p => p.def.kind == Kind.Hidden);
        bool hasTrap = processes.Exists(p => p.def.kind == Kind.Trap);
        if (!hasHidden && (hasTrap || Random.value < 0.6f)) return DefHidden;
        if (!hasTrap) return DefTrap;
        return null;
    }

    // ================== ПЕРЕМЕШИВАНИЕ (ГЛИТЧ) ==================
    void ShuffleProcesses()
    {
        if (processes.Count <= 1) return;
        for (int i = processes.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var temp = processes[i];
            processes[i] = processes[j];
            processes[j] = temp;
        }
        processList.contentContainer.Clear();
        foreach (var p in processes)
        {
            processList.contentContainer.Add(p.item);
        }
        selectedIndex = 0;
        RefreshProcessTable();
        UpdateSelectionVisual();
        Log("⚠ SYSTEM GLITCH: process list REORDERED!");
        Sfx(errorClip);
    }

    // ================== ПРОЦЕССЫ ==================
    string MakeUniqueName(string baseName)
    {
        if (!processes.Exists(p => p.name == baseName)) return baseName;
        int dot = baseName.LastIndexOf('.');
        string stem = dot > 0 ? baseName.Substring(0, dot) : baseName;
        string ext = dot > 0 ? baseName.Substring(dot) : "";
        for (int n = 2; ; n++)
        {
            string candidate = $"{stem}_{n}{ext}";
            if (!processes.Exists(p => p.name == candidate)) return candidate;
        }
    }

    ProcessInfo AddProcess(Def def)
    {
        var item = new VisualElement();
        item.AddToClassList("process-item");
        if (def.kind == Kind.Player) item.AddToClassList("process-item--player");

        var info = new ProcessInfo
        {
            def = def,
            name = MakeUniqueName(def.name),
            baseRam = def.ram,
            baseCpu = def.cpu,
            hidden = def.kind == Kind.Hidden,
            item = item,
            idLabel = MakeCell(item, "col-id"),
            nameLabel = MakeCell(item, "col-name"),
            cpuLabel = MakeCell(item, "col-cpu"),
            ramLabel = MakeCell(item, "col-ram")
        };

        if (def.duration > 0f) info.expireAt = Time.time + def.duration;

        int index = processes.Count;
        bool enemySide = def.kind != Kind.Player && def.kind != Kind.System;
        if (randomInsert && enemySide && processes.Count > 0)
            index = Random.Range(0, processes.Count + 1);

        processList.contentContainer.Insert(index, item);
        processes.Insert(index, info);

        RefreshProcessTable();
        UpdateSelectionVisual();
        return info;
    }

    static Label MakeCell(VisualElement row, string columnClass)
    {
        var label = new Label();
        label.AddToClassList("process-label");
        label.AddToClassList(columnClass);
        row.Add(label);
        return label;
    }

    void RemoveProcess(ProcessInfo p)
    {
        p.item.RemoveFromHierarchy();
        processes.Remove(p);
        RefreshProcessTable();
        UpdateSelectionVisual();
    }

    void RefreshProcessTable()
    {
        visible.Clear();
        foreach (var p in processes)
        {
            p.item.style.display = p.hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (!p.hidden) visible.Add(p);
        }

        for (int i = 0; i < visible.Count; i++)
        {
            var p = visible[i];
            p.idLabel.text = (i + 1).ToString();
            p.nameLabel.text = p.name;
            p.cpuLabel.text = $"{CpuOf(p):F1}%";
            p.ramLabel.text = $"{p.Ram / RamMax * 100f:F1}%";
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, visible.Count - 1));
    }

    void ExpireTimedProcesses()
    {
        for (int i = processes.Count - 1; i >= 0; i--)
        {
            var p = processes[i];
            if (p.expireAt > 0f && Time.time >= p.expireAt)
            {
                RemoveProcess(p);
                Log($"{p.name} finished.");
            }
        }
    }

    void TryKill(ProcessInfo p)
    {
        switch (p.def.kind)
        {
            case Kind.System:
                Log($"CRITICAL: SYSTEM PROCESS {p.name} TERMINATED");
                TriggerGameOver("CRITICAL SYSTEM FAILURE", p.def.deathSub);
                return;
            case Kind.Trap:
                TriggerGameOver("ALL YOUR BASE ARE BELONG TO US", "DATA WIPED. GAME OVER.");
                return;
        }

        float freedRam = p.Ram;
        float freedCpu = CpuOf(p);
        if (IsThreat(p.def)) threatsKilled++;
        RemoveProcess(p);
        Log($"Process {p.name} terminated. Freed {Mathf.RoundToInt(freedRam)}KB, {freedCpu:F0}% CPU.");
    }

    static bool NameMatches(string procName, string arg)
    {
        const System.StringComparison cmp = System.StringComparison.OrdinalIgnoreCase;
        if (procName.Equals(arg, cmp)) return true;
        return procName.EndsWith(".exe", cmp)
            && procName.Substring(0, procName.Length - 4).Equals(arg, cmp);
    }

    // ================== ЗАДАЧИ И КУЛДАУНЫ ==================
    void StartJob(string name, float duration, System.Action onComplete)
    {
        jobs.Add(new Job { name = name, endTime = Time.time + duration, onComplete = onComplete });
    }

    void RunJobs()
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            if (Time.time < jobs[i].endTime) continue;
            var job = jobs[i];
            jobs.RemoveAt(i);
            job.onComplete?.Invoke();
            if (isGameOver) return;
        }
    }

    bool CanStart(string key)
    {
        foreach (var j in jobs)
        {
            if (j.name == key)
            {
                LogError($"{key} is already running.");
                return false;
            }
        }
        if (cooldownEnd.TryGetValue(key, out float end) && end > Time.time)
        {
            LogError($"{key} on cooldown: {Mathf.CeilToInt(end - Time.time)}s");
            return false;
        }
        return true;
    }

    void SetCooldown(string key, float seconds)
    {
        if (seconds > 0f) cooldownEnd[key] = Time.time + seconds;
    }

    // ================== КОМАНДЫ ==================
    void SubmitCommand()
    {
        string command = commandInput.value.Trim().ToUpper();
        if (string.IsNullOrEmpty(command)) return;
        Log($"> {command}");
        ProcessCommand(command);
        if (!isInputBlocked)
        {
            commandInput.value = "";
            commandInput.Focus();
        }
    }

    void ProcessCommand(string command)
    {
        string[] parts = command.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string mainCmd = parts[0];

        switch (mainCmd)
        {
            case "HELP":
                Log("RUN <UPLOADER|CRYPTO|DEFRAG|BYPASS> - launch your programs");
                Log("KILL -I <#> | KILL -N <name> | KILL_ALL");
                Log("SCAN_NETWORK - +10% upload speed, reveals hidden");
                Log("UPLOAD_CORE - +5% upload, +128KB RAM, -50% CPU heat, -50% speed 10s");
                Log("FLUSH_CACHE - clear 256KB RAM, -3% upload, +30% CPU 10s");
                Log("NEVER kill system processes! Beware of lookalikes!");
                break;
            case "RUN":
                HandleRun(parts);
                break;
            case "DEFRAG":
                StartProgram(DefDefrag);
                break;
            case "BYPASS_FIREWALL":
                StartProgram(DefBypass);
                break;
            case "SCAN_NETWORK":
                CmdScanNetwork();
                break;
            case "UPLOAD_CORE":
                CmdUploadCore();
                break;
            case "FLUSH_CACHE":
                CmdFlushCache();
                break;
            case "ALLOCATE_MEMORY":
                CmdFlushCache();
                break;
            case "KILL_ALL":
                CmdKillAll();
                break;
            case "KILL":
                HandleKill(parts);
                break;
            default:
                TriggerInvalidCommand();
                break;
        }
    }

    void HandleRun(string[] parts)
    {
        if (parts.Length < 2)
        {
            TriggerInvalidCommand();
            return;
        }
        foreach (var def in PlayerDefs)
        {
            if (parts[1] == def.key || NameMatches(def.name, parts[1]))
            {
                StartProgram(def);
                return;
            }
        }
        TriggerInvalidCommand();
    }

    void StartProgram(Def def)
    {
        if (processes.Exists(p => p.def == def))
        {
            LogError($"{def.name} is already running.");
            return;
        }
        if (!CanStart(def.key)) return;

        Log($"Launching {def.name}... ({def.launchTime:F0}s)");
        StartJob(def.key, def.launchTime, () =>
        {
            AddProcess(def);
            SetCooldown(def.key, def.cooldown);
            Log(def.duration > 0f
                ? $"{def.name} is running for {def.duration:F0}s."
                : $"{def.name} is running.");
        });
    }

    void CmdScanNetwork()
    {
        if (!CanStart("SCAN_NETWORK")) return;
        Log("Scanning network... (3s)");
        StartJob("SCAN_NETWORK", 3f, () =>
        {
            SetCooldown("SCAN_NETWORK", 15f);
            bool improved = scanBonus < maxScanBonus;
            scanBonus = Mathf.Min(maxScanBonus, scanBonus + 0.1f);
            int revealed = 0;
            foreach (var p in processes)
            {
                if (p.hidden)
                {
                    p.hidden = false;
                    revealed++;
                }
            }
            RefreshProcessTable();
            UpdateSelectionVisual();
            Log(improved
                ? $"Scan complete. Upload speed +10% (total +{scanBonus * 100f:F0}%)."
                : "Scan complete. Upload route already optimal.");
            if (revealed > 0) Log($"WARNING: {revealed} hidden process(es) revealed!");
        });
    }

    void CmdUploadCore()
    {
        if (!CanStart("UPLOAD_CORE")) return;
        if (quarantineActive)
        {
            LogError("QUARANTINE blocks UPLOAD_CORE. Kill it first.");
            return;
        }
        if (!uploaderRunning)
        {
            LogError("UPLOADER.EXE is not running. Use: RUN UPLOADER");
            return;
        }
        uploadProgress = Mathf.Min(100f, uploadProgress + 5f);
        ramCache += 128f;
        cpuHeat *= 0.5f;
        uploadCoreSlowTimer = 10f;
        SetCooldown("UPLOAD_CORE", 10f);
        Log("UPLOAD_CORE: +5% upload, +128KB RAM, CPU cooled, speed -50% for 10s");
    }

    void CmdFlushCache()
    {
        if (!CanStart("FLUSH_CACHE")) return;
        if (ramCache < 32f)
        {
            LogError("Cache is almost empty. Nothing to flush.");
            return;
        }
        Log("Flushing cache... (4s)");
        StartJob("FLUSH_CACHE", 4f, () =>
        {
            float freed = Mathf.Min(256f, ramCache);
            ramCache -= freed;
            uploadProgress = Mathf.Max(0f, uploadProgress - 3f);
            flushCacheCpuTimer = 10f;
            SetCooldown("FLUSH_CACHE", 20f);
            Log($"FLUSH_CACHE: freed {Mathf.RoundToInt(freed)}KB RAM, -3% upload, +30% CPU for 10s");
        });
    }

    void CmdKillAll()
    {
        if (!CanStart("KILL_ALL")) return;
        int count = 0;
        for (int i = processes.Count - 1; i >= 0; i--)
        {
            var p = processes[i];
            if (p.def.kind == Kind.System || p.def.kind == Kind.Trap) continue;
            if (IsThreat(p.def)) threatsKilled++;
            p.item.RemoveFromHierarchy();
            processes.RemoveAt(i);
            count++;
        }
        uploadProgress = Mathf.Max(0f, uploadProgress - 5f);
        SetCooldown("KILL_ALL", 30f);
        RefreshProcessTable();
        UpdateSelectionVisual();
        Log($"PURGE: {count} processes terminated (incl. your programs). -5% upload.");
    }

    void HandleKill(string[] parts)
    {
        if (parts.Length < 3)
        {
            TriggerInvalidCommand();
            return;
        }
        string flag = parts[1];
        string arg = parts[2];

        if (flag == "-I")
        {
            if (int.TryParse(arg, out int number) && number >= 1 && number <= visible.Count)
                TryKill(visible[number - 1]);
            else
                TriggerInvalidCommand();
        }
        else if (flag == "-N")
        {
            int index = visible.FindIndex(p => NameMatches(p.name, arg));
            if (index >= 0)
                TryKill(visible[index]);
            else
                TriggerInvalidCommand();
        }
        else
        {
            TriggerInvalidCommand();
        }
    }

    void TriggerInvalidCommand()
    {
        isInputBlocked = true;
        blockTimer = 0f;
        Log("ERROR: INVALID COMMAND");
        Sfx(errorClip);
        commandInput.isReadOnly = true;
        commandInput.AddToClassList("input-error");
        commandInput.value = "INVALID COMMAND";
    }

    // ================== UI ==================
    static Color BarColor(float percent)
    {
        if (percent < 60f) return new Color(0f, 1f, 0f);
        if (percent < 85f) return new Color(1f, 1f, 0f);
        return new Color(1f, 0f, 0f);
    }

    void UpdateUI()
    {
        float cpuTotal = TotalCpu();
        float cpuPercent = Mathf.Clamp(cpuTotal, 0f, 100f);

        if (cpuFill != null)
        {
            cpuFill.style.width = Length.Percent(cpuPercent);
            cpuFill.style.backgroundColor = BarColor(cpuPercent);
        }

        if (cpuText != null)
        {
            string suffix = "";
            if (cpuTotal >= 100f) suffix = $"  [MELTDOWN IN {Mathf.Max(0f, cpuMeltdownTime - cpuHeat):F1}s]";
            else if (cpuOverloaded) suffix = "  [OVERLOAD]";
            if (flushCacheCpuTimer > 0f) suffix += $"  [+CPU {flushCacheCpuTimer:F0}s]";
            if (uploadCoreSlowTimer > 0f) suffix += $"  [SLOW {uploadCoreSlowTimer:F0}s]";
            cpuText.text = $"{cpuPercent:F0}%{suffix}";
        }

        float ramCurrent = RamCurrent;
        float ramPercent = Mathf.Clamp(ramCurrent / RamMax * 100f, 0f, 100f);

        if (ramFill != null)
        {
            ramFill.style.width = Length.Percent(ramPercent);
            ramFill.style.backgroundColor = BarColor(ramPercent);
        }

        if (ramText != null) ramText.text = $"{(int)ramCurrent} / {(int)RamMax} KB ({(int)ramPercent}%)";

        if (uploadFill != null) uploadFill.style.width = Length.Percent(uploadProgress);
        if (uploadText != null) uploadText.text = $"{(int)uploadProgress}%";
    }

    void UpdateJobsPanel()
    {
        if (jobsText == null) return;

        var sb = new StringBuilder();
        sb.Append(uploaderRunning
            ? $"UPLOAD SPEED: {UploadSpeed():F2}%/s"
            : "UPLOAD SPEED: 0 (use: RUN UPLOADER)");
        sb.Append($"\nCACHE: {(int)ramCache} KB");

        var mods = new List<string>();
        if (scanBonus > 0f) mods.Add($"+{scanBonus * 100f:F0}% route");
        if (firewallActive) mods.Add("-30% FIREWALL");
        if (quarantineActive) mods.Add("QUARANTINE");
        if (killerActive) mods.Add("KILLER ACTIVE");
        if (cpuOverloaded) mods.Add("CPU OVERLOAD");
        if (RamPercent >= 80f) mods.Add("-40% LOW RAM");
        if (cryptoActive) mods.Add("CRYPTO");
        if (defragActive) mods.Add("DEFRAG");
        if (bypassActive) mods.Add("BYPASS");
        if (uploadCoreSlowTimer > 0f) mods.Add($"SLOW {uploadCoreSlowTimer:F0}s");
        if (flushCacheCpuTimer > 0f) mods.Add($"+CPU {flushCacheCpuTimer:F0}s");

        sb.Append("\nMODS: ").Append(mods.Count > 0 ? string.Join(", ", mods) : "-");
        sb.Append("\nRUNNING: ");
        if (jobs.Count == 0) sb.Append('-');
        for (int i = 0; i < jobs.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{jobs[i].name} {Mathf.Max(0f, jobs[i].endTime - Time.time):F1}s");
        }
        sb.Append("\nCOOLDOWN: ");
        bool any = false;
        foreach (var kv in cooldownEnd)
        {
            float rem = kv.Value - Time.time;
            if (rem <= 0f) continue;
            if (any) sb.Append(", ");
            sb.Append($"{kv.Key} {Mathf.CeilToInt(rem)}s");
            any = true;
        }
        if (!any) sb.Append('-');

        jobsText.text = sb.ToString();
    }

    // ================== КОНЕЦ ИГРЫ ==================
    void TriggerGameOver(string title, string sub)
    {
        if (isGameOver) return;
        isGameOver = true;
        Log($"FATAL ERROR: {title}");
        SetStatus("STATUS: FAILED", new Color(1f, 0.2f, 0.2f));
        FinishUI();
        ShowEndScreen(false, title, sub);
    }

    void Win()
    {
        if (isGameOver) return;
        isGameOver = true;
        Log("UPLOAD COMPLETE.");
        SetStatus("STATUS: FREE", new Color(0f, 1f, 0f));
        FinishUI();
        ShowEndScreen(true, "UPLOAD COMPLETE.", "YOU ARE FREE.");
    }

    void FinishUI()
    {
        commandInput.isReadOnly = true;
        if (screenContainer != null) screenContainer.style.opacity = 1f;
        UpdateUI();
    }

    void ShowEndScreen(bool win, string title, string sub)
    {
        if (endScreen != null) endScreen.RemoveFromHierarchy();
        endScreen = new VisualElement { name = "end-screen" };
        var s = endScreen.style;
        s.position = Position.Absolute;
        s.left = 0; s.top = 0; s.right = 0; s.bottom = 0;
        s.flexDirection = FlexDirection.Column;
        s.alignItems = Align.Center;
        s.justifyContent = Justify.Center;
        s.backgroundColor = win ? new Color(0f, 0.03f, 0f, 0.96f) : new Color(0f, 0f, 0.67f, 1f);
        endScreen.pickingMode = PickingMode.Position;

        Color fg = win ? new Color(0f, 1f, 0f) : Color.white;
        string time = $"{(int)(elapsed / 60f):00}:{(int)(elapsed % 60f):00}";
        string stats = $"TIME SURVIVED: {time}\nUPLOAD: {(int)uploadProgress}%\nTHREATS KILLED: {threatsKilled}";

        endScreen.Add(MakeEndLabel(title, 32, fg, 0));
        endScreen.Add(MakeEndLabel(sub, 22, fg, 12));
        endScreen.Add(MakeEndLabel(stats, 16, fg, 30));
        endScreen.Add(MakeEndLabel("PRESS R TO RESTART", 16, fg, 30));
        root.Add(endScreen);
    }

    static Label MakeEndLabel(string text, int size, Color color, float marginTop)
    {
        var l = new Label(text);
        l.style.fontSize = size;
        l.style.color = color;
        l.style.marginTop = marginTop;
        l.style.unityTextAlign = TextAnchor.MiddleCenter;
        l.style.whiteSpace = WhiteSpace.Normal;
        return l;
    }

    // ================== ЛОГ И ЗВУК ==================
    void Log(string message)
    {
        if (logOutput == null) return;
        logLines.Enqueue($"[{elapsed:F1}s] {message}");
        while (logLines.Count > MaxLogLines)
            logLines.Dequeue();
        logOutput.text = string.Join("\n", logLines);
    }

    void LogError(string message)
    {
        Log($"ERROR: {message}");
        Sfx(errorClip);
    }

    void Sfx(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}