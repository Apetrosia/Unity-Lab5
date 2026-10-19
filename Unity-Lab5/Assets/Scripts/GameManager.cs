using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    private enum FocusMode { Input, List }

    private class ProcessInfo
    {
        public string name;

        // Базовые значения (вокруг них "гуляют" текущие)
        public float baseRam;
        public float baseCpu;

        // Текущие значения
        public float ram;   // KB
        public float cpu;   // % от всего CPU

        public VisualElement item;
        public Label idLabel, nameLabel, cpuLabel, ramLabel;
    }

    // === Настройки (видны в инспекторе) ===
    [Header("Спавн процессов")]
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(6f, 10f);
    [SerializeField] private float firstSpawnDelay = 4f;

    [Header("Колебания нагрузки")]
    [SerializeField] private Vector2 fluctuationIntervalRange = new Vector2(0.5f, 1f);
    [SerializeField] private float ramStep = 5f;     // макс. шаг изменения RAM (KB) за тик
    [SerializeField] private float cpuStep = 2f;     // макс. шаг изменения CPU (% ) за тик

    [Header("Перегрузка CPU")]
    [SerializeField] private float cpuOverloadThreshold = 90f;
    [SerializeField] private float overloadUploadMultiplier = 0.25f;

    // === Игровые переменные ===
    private float ramSystem = 200f;   // память системы вне процессов
    private float ramMax = 1024f;
    private float uploadProgress = 0f;
    private bool isGameOver = false;
    private bool cpuOverloaded = false;

    private float spawnTimer = 0f;
    private float spawnInterval;
    private float fluctuationTimer = 0f;
    private float nextFluctuation = 0.7f;

    // Номер на экране = индекс + 1
    private readonly List<ProcessInfo> processes = new List<ProcessInfo>();

    // Фокус и выделение
    private FocusMode focusMode = FocusMode.Input;
    private int selectedIndex = 0;

    // Блокировка ввода при ошибке
    private bool isInputBlocked = false;
    private float blockTimer = 0f;

    // Лог
    private const int MaxLogLines = 7;
    private readonly Queue<string> logLines = new Queue<string>();

    // === UI ===
    private VisualElement root;
    private VisualElement cpuFill, ramFill, uploadFill;
    private Label cpuText, ramText, uploadText;
    private Label logOutput;
    private Label statusText;
    private ScrollView processList;
    private TextField commandInput;

    // Суммарные значения (считаются из процессов)
    private float TotalProcessRam
    {
        get { float s = 0f; foreach (var p in processes) s += p.ram; return s; }
    }

    private float TotalProcessCpu
    {
        get { float s = 0f; foreach (var p in processes) s += p.cpu; return s; }
    }

    private float RamCurrent => ramSystem + TotalProcessRam;

    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

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

        spawnInterval = firstSpawnDelay;
        nextFluctuation = Random.Range(fluctuationIntervalRange.x, fluctuationIntervalRange.y);

        if (logOutput != null) logOutput.text = "";
        Log("System initialized. Monitoring RAM and CPU...");
        Log("WARNING: Unauthorized processes detected!");
        Log("Type 'HELP' for available commands.");
        UpdateUI();

        if (processList == null || commandInput == null) return;

        processList.focusable = true;
        commandInput.focusable = true;

        // Все клавиши обрабатываем на корне в фазе TrickleDown: раньше TextField/ScrollView
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

        SetFocusMode(FocusMode.Input);
    }

    // ================== ФОКУС И КЛАВИШИ ==================

    void OnKeyDown(KeyDownEvent evt)
    {
        // Пока действует штраф, глушим всё, включая Tab
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
        else // FocusMode.List
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
            else if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow
                     || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
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
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, processes.Count - 1));
        }

        UpdateSelectionVisual();
    }

    // Страховка: если фокус оказался не там, возвращаем его
    void EnsureFocus()
    {
        if (root == null || commandInput == null || processList == null) return;

        VisualElement target = focusMode == FocusMode.Input ? (VisualElement)commandInput : processList;
        var focused = root.focusController?.focusedElement as VisualElement;

        bool ok = focused != null && (focused == target || target.Contains(focused));
        if (!ok) target.Focus();
    }

    // ================== ВЫДЕЛЕНИЕ В СПИСКЕ ==================

    void MoveSelection(int delta)
    {
        if (processes.Count == 0) return;
        selectedIndex = Mathf.Clamp(selectedIndex + delta, 0, processes.Count - 1);
        UpdateSelectionVisual();
    }

    void UpdateSelectionVisual()
    {
        for (int i = 0; i < processes.Count; i++)
        {
            bool selected = focusMode == FocusMode.List && i == selectedIndex;
            processes[i].item.EnableInClassList("process-item--selected", selected);
        }

        if (focusMode == FocusMode.List && processes.Count > 0)
            processList.ScrollTo(processes[selectedIndex].item);
    }

    // ================== UPDATE ==================

    void Update()
    {
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
            // Игра НЕ ставится на паузу
        }

        EnsureFocus();

        if (isGameOver) return;

        // Спавн
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            SpawnProcess();
            spawnTimer = 0f;
            spawnInterval = Random.Range(spawnIntervalRange.x, spawnIntervalRange.y);
        }

        // Колебания нагрузки
        fluctuationTimer += Time.deltaTime;
        if (fluctuationTimer >= nextFluctuation)
        {
            fluctuationTimer = 0f;
            nextFluctuation = Random.Range(fluctuationIntervalRange.x, fluctuationIntervalRange.y);
            FluctuateProcesses();
        }

        // Перегрузка CPU: штраф к скорости загрузки
        float cpuTotal = TotalProcessCpu;
        bool overloadedNow = cpuTotal >= cpuOverloadThreshold;
        if (overloadedNow != cpuOverloaded)
        {
            cpuOverloaded = overloadedNow;
            Log(cpuOverloaded
                ? "WARNING: CPU OVERLOAD! Upload slowed down."
                : "CPU load back to normal.");
        }

        if (uploadProgress < 100f)
        {
            float speed = cpuOverloaded ? overloadUploadMultiplier : 1f;
            uploadProgress = Mathf.Min(100f, uploadProgress + Time.deltaTime * 2f * speed);
        }

        // Поражение по памяти
        if (RamCurrent >= ramMax)
        {
            ramSystem = Mathf.Max(0f, ramMax - TotalProcessRam);
            TriggerGameOver("OUT OF MEMORY");
        }

        UpdateUI();
    }

    // ================== ПРОЦЕССЫ ==================

    void SpawnProcess()
    {
        float ramCost = Random.Range(50f, 150f);
        float cpuCost = Random.Range(5f, 30f);

        // Профили: часть процессов "тяжёлая" по одному ресурсу, часть сбалансирована
        int profile = Random.Range(0, 3);
        if (profile == 0) { ramCost *= 1.4f; cpuCost *= 0.4f; }      // жрёт память
        else if (profile == 1) { ramCost *= 0.5f; cpuCost *= 1.5f; } // жрёт процессор

        // Уникальное имя, чтобы KILL -n работал однозначно
        string procName;
        do { procName = $"PROC_{Random.Range(1000, 9999)}.exe"; }
        while (processes.Exists(p => p.name == procName));

        var item = new VisualElement();
        item.AddToClassList("process-item");

        var info = new ProcessInfo
        {
            name = procName,
            baseRam = ramCost,
            baseCpu = cpuCost,
            ram = ramCost,
            cpu = cpuCost,
            item = item,
            idLabel = MakeCell(item, "col-id"),
            nameLabel = MakeCell(item, "col-name"),
            cpuLabel = MakeCell(item, "col-cpu"),
            ramLabel = MakeCell(item, "col-ram")
        };

        processList.Add(item);
        processes.Add(info);

        NormalizeCpu();
        RefreshProcessTable();
        UpdateSelectionVisual();

        Log($"New threat detected: {procName} (+{Mathf.RoundToInt(ramCost)}KB RAM, {info.cpu:F0}% CPU)");
    }

    static Label MakeCell(VisualElement row, string columnClass)
    {
        var label = new Label();
        label.AddToClassList("process-label");
        label.AddToClassList(columnClass);
        row.Add(label);
        return label;
    }

    // Случайное блуждание вокруг базовых значений
    void FluctuateProcesses()
    {
        foreach (var p in processes)
        {
            p.ram = Mathf.Clamp(p.ram + Random.Range(-ramStep, ramStep),
                                p.baseRam * 0.7f, p.baseRam * 1.3f);

            p.cpu = Mathf.Clamp(p.cpu + Random.Range(-cpuStep, cpuStep),
                                Mathf.Max(0.5f, p.baseCpu * 0.5f), p.baseCpu * 1.5f);
        }

        NormalizeCpu();
        RefreshProcessTable();
    }

    // Сумма CPU по процессам не может быть больше 100%
    void NormalizeCpu()
    {
        float sum = TotalProcessCpu;
        if (sum <= 100f) return;

        float k = 100f / sum;
        foreach (var p in processes) p.cpu *= k;
    }

    // Пересчёт номеров и процентов
    void RefreshProcessTable()
    {
        // Сумма RAM процессов ограничена ramMax (иначе игра уже закончилась)
        float ramBase = Mathf.Max(ramMax, TotalProcessRam);

        for (int i = 0; i < processes.Count; i++)
        {
            var p = processes[i];
            p.idLabel.text = (i + 1).ToString();
            p.nameLabel.text = p.name;
            p.cpuLabel.text = $"{p.cpu:F1}%";
            p.ramLabel.text = $"{p.ram / ramBase * 100f:F1}%";
        }
    }

    void KillProcess(int index)
    {
        var proc = processes[index];

        processList.Remove(proc.item);
        processes.RemoveAt(index);

        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, processes.Count - 1));

        RefreshProcessTable();
        UpdateSelectionVisual();

        Log($"Process {proc.name} terminated. Freed {Mathf.RoundToInt(proc.ram)}KB, {proc.cpu:F0}% CPU.");
    }

    void KillAll()
    {
        processList.Clear();
        processes.Clear();
        selectedIndex = 0;
        Log("All processes terminated.");
    }

    static bool NameMatches(string procName, string arg)
    {
        const System.StringComparison cmp = System.StringComparison.OrdinalIgnoreCase;
        if (procName.Equals(arg, cmp)) return true;

        // Разрешаем писать имя без ".exe"
        return procName.EndsWith(".exe", cmp)
            && procName.Substring(0, procName.Length - 4).Equals(arg, cmp);
    }

    // ================== КОМАНДЫ ==================

    void SubmitCommand()
    {
        string command = commandInput.value.Trim().ToUpper();
        if (string.IsNullOrEmpty(command)) return;

        Log($"> {command}");
        ProcessCommand(command);

        if (!isInputBlocked)
            commandInput.value = "";
    }

    void ProcessCommand(string command)
    {
        string[] parts = command.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string mainCmd = parts[0];

        switch (mainCmd)
        {
            case "HELP":
                Log("Commands: UPLOAD_CORE | ALLOCATE_MEMORY | KILL_ALL");
                Log("  KILL -i <number>  - kill by list number");
                Log("  KILL -n <name>    - kill by name (e.g. KILL -n PROC_1234)");
                break;

            case "UPLOAD_CORE":
                uploadProgress = Mathf.Min(100f, uploadProgress + 5f);
                ramSystem += 128f;
                Log("Uploading core... +5% progress, +128KB RAM");
                break;

            case "ALLOCATE_MEMORY":
                ramSystem = Mathf.Max(0f, ramSystem - 256f);
                uploadProgress = Mathf.Max(0f, uploadProgress - 3f);
                Log("Memory allocated. -256KB system RAM, -3% upload");
                break;

            case "KILL_ALL":
                KillAll();
                break;

            case "KILL":
                HandleKill(parts);
                break;

            default:
                TriggerInvalidCommand();
                break;
        }
    }

    void HandleKill(string[] parts)
    {
        // KILL -I <число>  или  KILL -N <имя>  (команда уже в верхнем регистре)
        if (parts.Length < 3)
        {
            TriggerInvalidCommand();
            return;
        }

        string flag = parts[1];
        string arg = parts[2];

        if (flag == "-I")
        {
            if (int.TryParse(arg, out int number) && number >= 1 && number <= processes.Count)
                KillProcess(number - 1);
            else
                TriggerInvalidCommand();
        }
        else if (flag == "-N")
        {
            int index = processes.FindIndex(p => NameMatches(p.name, arg));
            if (index >= 0)
                KillProcess(index);
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
        // CPU
        float cpuPercent = Mathf.Clamp(TotalProcessCpu, 0f, 100f);
        if (cpuFill != null)
        {
            cpuFill.style.width = Length.Percent(cpuPercent);
            cpuFill.style.backgroundColor = BarColor(cpuPercent);
        }
        if (cpuText != null) cpuText.text = $"{cpuPercent:F0}%" + (cpuOverloaded ? "  [OVERLOAD]" : "");

        // RAM
        float ramCurrent = RamCurrent;
        float ramPercent = Mathf.Clamp(ramCurrent / ramMax * 100f, 0f, 100f);
        if (ramFill != null)
        {
            ramFill.style.width = Length.Percent(ramPercent);
            ramFill.style.backgroundColor = BarColor(ramPercent);
        }
        if (ramText != null) ramText.text = $"{(int)ramCurrent} / {(int)ramMax} KB ({(int)ramPercent}%)";

        // Upload
        if (uploadFill != null) uploadFill.style.width = Length.Percent(uploadProgress);
        if (uploadText != null) uploadText.text = $"{(int)uploadProgress}%";
    }

    void TriggerGameOver(string reason)
    {
        isGameOver = true;
        Log($"FATAL ERROR: {reason}");
        if (statusText != null) statusText.text = "STATUS: FAILED";
    }

    void Log(string message)
    {
        if (logOutput == null) return;

        logLines.Enqueue($"[{Time.time:F1}s] {message}");
        while (logLines.Count > MaxLogLines)
            logLines.Dequeue();

        logOutput.text = string.Join("\n", logLines);
    }
}