using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    private enum FocusMode { Input, List }

    private class ProcessInfo
    {
        public string name;
        public int ram;
        public VisualElement item;
        public Label label;
    }

    // === Настройки (видны в инспекторе) ===
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(6f, 10f); // для теста реже
    [SerializeField] private float firstSpawnDelay = 4f;

    // === Игровые переменные ===
    private float ramCurrent = 200;
    private float ramMax = 1024;
    private float uploadProgress = 0;
    private bool isGameOver = false;

    private float spawnTimer = 0;
    private float spawnInterval;

    // Список процессов. Номер на экране = индекс + 1, поэтому при удалении нумерация сама сдвигается
    private readonly List<ProcessInfo> processes = new List<ProcessInfo>();

    // Фокус и выделение
    private FocusMode focusMode = FocusMode.Input;
    private int selectedIndex = 0; // запоминается, когда уходим в поле ввода

    // Блокировка ввода при ошибке
    private bool isInputBlocked = false;
    private float blockTimer = 0f;

    // Лог
    private const int MaxLogLines = 7;
    private readonly Queue<string> logLines = new Queue<string>();

    // === UI ===
    private VisualElement root;
    private VisualElement ramFill;
    private VisualElement uploadFill;
    private Label ramText;
    private Label uploadText;
    private Label logOutput;
    private Label statusText;
    private ScrollView processList;
    private TextField commandInput;

    void Start()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        ramFill = root.Q<VisualElement>("ram-fill");
        uploadFill = root.Q<VisualElement>("upload-fill");
        ramText = root.Q<Label>("ram-text");
        uploadText = root.Q<Label>("upload-text");
        logOutput = root.Q<Label>("log-output");
        statusText = root.Q<Label>("status-text");
        processList = root.Q<ScrollView>("process-list");
        commandInput = root.Q<TextField>("command-input");

        spawnInterval = firstSpawnDelay;

        if (logOutput != null) logOutput.text = "";
        Log("System initialized. Monitoring RAM...");
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

        // TAB: переключение между полем ввода и списком
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
                Consume(evt); // TextField не должен сам обрабатывать Enter
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
                Consume(evt); // чтобы стандартная навигация не сдвинула фокус
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

    // Страховка: если фокус оказался не там (клик мышью, навигация), возвращаем его
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

    // ================== ПРОЦЕССЫ ==================

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
            // Игра НЕ ставится на паузу, RAM продолжает расти
        }

        EnsureFocus();

        if (isGameOver) return;

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            SpawnProcess();
            spawnTimer = 0;
            spawnInterval = Random.Range(spawnIntervalRange.x, spawnIntervalRange.y);
        }

        if (ramCurrent >= ramMax)
        {
            ramCurrent = ramMax;
            TriggerGameOver("OUT OF MEMORY");
        }

        UpdateUI();
    }

    void SpawnProcess()
    {
        int ramCost = Random.Range(50, 150);

        // Уникальное имя, чтобы KILL -n работал однозначно
        string procName;
        do { procName = $"PROC_{Random.Range(1000, 9999)}.exe"; }
        while (processes.Exists(p => p.name == procName));

        ramCurrent += ramCost;

        var item = new VisualElement();
        item.AddToClassList("process-item");

        var label = new Label();
        label.AddToClassList("process-label");
        item.Add(label);

        processList.Add(item);
        processes.Add(new ProcessInfo { name = procName, ram = ramCost, item = item, label = label });

        RefreshProcessLabels();
        UpdateSelectionVisual();

        Log($"New threat detected: {procName} (+{ramCost}KB RAM)");
    }

    // Перенумерация: номер = позиция в списке
    void RefreshProcessLabels()
    {
        for (int i = 0; i < processes.Count; i++)
        {
            var p = processes[i];
            p.label.text = $"[{i + 1}] {p.name} ... {p.ram}KB [CMD: KILL -i {i + 1}]";
        }
    }

    void KillProcess(int index)
    {
        var proc = processes[index];

        ramCurrent -= proc.ram;
        if (ramCurrent < 0) ramCurrent = 0;

        processList.Remove(proc.item);
        processes.RemoveAt(index);

        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, processes.Count - 1));

        RefreshProcessLabels();
        UpdateSelectionVisual();

        Log($"Process {proc.name} terminated. Freed {proc.ram}KB.");
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
                uploadProgress += 5;
                ramCurrent += 128;
                Log("Uploading core... +5% progress, +128KB RAM");
                break;

            case "ALLOCATE_MEMORY":
                ramCurrent -= 256;
                if (ramCurrent < 0) ramCurrent = 0;
                uploadProgress -= 3;
                if (uploadProgress < 0) uploadProgress = 0;
                Log("Memory allocated. -256KB RAM, -3% upload");
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
        // Ожидаем: KILL -I <число>  или  KILL -N <имя>  (команда уже в верхнем регистре)
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

        commandInput.isReadOnly = true;               // фокус остаётся, но печатать нельзя
        commandInput.AddToClassList("input-error");   // красный цвет задаётся в USS
        commandInput.value = "INVALID COMMAND";
    }

    // ================== UI ==================

    void UpdateUI()
    {
        float ramPercent = (ramCurrent / ramMax) * 100f;

        if (ramFill != null)
        {
            ramFill.style.width = Length.Percent(ramPercent);
            if (ramPercent < 60) ramFill.style.backgroundColor = new Color(0, 1, 0);
            else if (ramPercent < 85) ramFill.style.backgroundColor = new Color(1, 1, 0);
            else ramFill.style.backgroundColor = new Color(1, 0, 0);
        }

        if (ramText != null) ramText.text = $"{(int)ramCurrent} / {(int)ramMax} KB ({(int)ramPercent}%)";

        if (uploadProgress < 100) uploadProgress += Time.deltaTime * 2;
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