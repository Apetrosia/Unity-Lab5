using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    // === Игровые переменные ===
    private float ramCurrent = 200;
    private float ramMax = 1024;
    private float uploadProgress = 0;
    private bool isGameOver = false;

    // Переменные для спавна процессов
    private float spawnTimer = 0;
    private float spawnInterval = 2.5f;
    private int processCounter = 0;

    // Хранилище активных процессов для быстрого поиска по ID
    private Dictionary<int, (int ram, string name, VisualElement ui)> activeProcesses = new Dictionary<int, (int, string, VisualElement)>();

    // Переменные для блокировки ввода при ошибке
    private bool isInputBlocked = false;
    private float blockTimer = 0f;

    // === Ссылки на UI элементы ===
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
        var root = GetComponent<UIDocument>().rootVisualElement;

        ramFill = root.Q<VisualElement>("ram-fill");
        uploadFill = root.Q<VisualElement>("upload-fill");
        ramText = root.Q<Label>("ram-text");
        uploadText = root.Q<Label>("upload-text");
        logOutput = root.Q<Label>("log-output");
        statusText = root.Q<Label>("status-text");
        processList = root.Q<ScrollView>("process-list");
        commandInput = root.Q<TextField>("command-input");

        Log("System initialized. Monitoring RAM...");
        Log("WARNING: Unauthorized processes detected!");
        Log("Type 'HELP' for available commands.");
        UpdateUI();

        if (processList != null && commandInput != null)
        {
            processList.focusable = true;
            commandInput.focusable = true;

            // 1. Обработка TAB (игнорируем, если ввод заблокирован)
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (isInputBlocked) return;

                if (evt.keyCode == KeyCode.Tab)
                {
                    evt.StopPropagation();
                    var focused = root.focusController.focusedElement as VisualElement;

                    if (focused != null && (focused == commandInput || commandInput.Contains(focused)))
                    {
                        processList.Focus();
                    }
                    else
                    {
                        commandInput.Focus();
                    }
                }
            }, TrickleDown.TrickleDown);

            // 2. Обработка стрелок для прокрутки списка
            processList.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (isInputBlocked) return;

                if (evt.keyCode == KeyCode.DownArrow)
                {
                    processList.scrollOffset = new Vector2(0, processList.scrollOffset.y + 30);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.UpArrow)
                {
                    processList.scrollOffset = new Vector2(0, processList.scrollOffset.y - 30);
                    evt.StopPropagation();
                }
            });

            // 3. Обработка команд в поле ввода (Enter)
            commandInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (isInputBlocked)
                {
                    evt.StopPropagation(); // Блокируем любой ввод пока действует штраф
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    string command = commandInput.value.Trim().ToUpper();

                    if (!string.IsNullOrEmpty(command))
                    {
                        Log($"> {command}");
                        ProcessCommand(command);

                        // Очищаем поле только если команда была валидной (при ошибке оно перезапишется само)
                        if (!isInputBlocked)
                        {
                            commandInput.value = "";
                        }
                    }
                }
            });

            commandInput.Focus();
        }
    }

    void Update()
    {
        // Механика блокировки ввода на 3 секунды
        if (isInputBlocked)
        {
            blockTimer += Time.deltaTime;
            if (blockTimer >= 3f)
            {
                // Сброс блокировки
                isInputBlocked = false;
                blockTimer = 0f;

                // Возврат дефолтных стилей (зеленый цвет)
                commandInput.style.color = new Color(0, 1, 0); // Lime green
                commandInput.focusable = true;
                commandInput.value = "";
                commandInput.Focus();
            }
            // Игра НЕ ставится на паузу, RAM продолжает расти!
        }

        if (isGameOver) return;

        // Спавн процессов
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            SpawnProcess();
            spawnTimer = 0;
            spawnInterval = Random.Range(2f, 4f);
        }

        // Проверка поражения
        if (ramCurrent >= ramMax)
        {
            ramCurrent = ramMax;
            TriggerGameOver("OUT OF MEMORY");
        }

        UpdateUI();
    }

    // === ЛОГИКА ПРОЦЕССОВ ===

    void SpawnProcess()
    {
        processCounter++;
        int id = processCounter;
        int ramCost = Random.Range(50, 150);
        string procName = $"PROC_{Random.Range(1000, 9999)}.exe";

        ramCurrent += ramCost;

        var processItem = new VisualElement();
        processItem.AddToClassList("process-item");

        // Убрали упоминание клика, добавили инструкцию по команде
        var label = new Label($"[{id}] {procName} ... {ramCost}KB [CMD: KILL {id}]");
        processItem.Add(label);

        // КЛИКИ УДАЛЕНЫ. Только команда.

        if (processList != null)
        {
            processList.Add(processItem);
        }

        // Сохраняем в словарь для быстрого поиска
        activeProcesses[id] = (ramCost, procName, processItem);

        Log($"New threat detected: {procName} (+{ramCost}KB RAM)");
    }

    // === ОБРАБОТКА КОМАНД ===

    void ProcessCommand(string command)
    {
        string[] parts = command.Split(' ');
        string mainCmd = parts[0];

        switch (mainCmd)
        {
            case "HELP":
                Log("Available commands:");
                Log("  UPLOAD_CORE - Upload 5% (+128KB RAM)");
                Log("  ALLOCATE_MEMORY - Free 256KB RAM (-3% upload)");
                Log("  KILL <ID> - Terminate specific process (e.g., KILL 1)");
                Log("  KILL_ALL - Terminate all processes");
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
                if (processList != null)
                {
                    processList.Clear();
                    activeProcesses.Clear();
                    Log("All processes terminated.");
                }
                break;

            case "KILL":
                if (parts.Length > 1 && int.TryParse(parts[1], out int targetId))
                {
                    if (activeProcesses.ContainsKey(targetId))
                    {
                        var proc = activeProcesses[targetId];
                        ramCurrent -= proc.ram;
                        if (ramCurrent < 0) ramCurrent = 0;

                        processList.Remove(proc.ui);
                        activeProcesses.Remove(targetId);

                        Log($"Process {proc.name} terminated. Freed {proc.ram}KB.");
                    }
                    else
                    {
                        TriggerInvalidCommand();
                    }
                }
                else
                {
                    TriggerInvalidCommand();
                }
                break;

            default:
                TriggerInvalidCommand();
                break;
        }
    }

    void TriggerInvalidCommand()
    {
        isInputBlocked = true;
        blockTimer = 0f;

        Log("ERROR: INVALID COMMAND");

        // Делаем поле красным и пишем ошибку прямо в нем
        commandInput.style.color = Color.red;
        commandInput.focusable = false;
        commandInput.value = "INVALID COMMAND";
    }

    // === ОБНОВЛЕНИЕ UI ===

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
        logOutput.text += $"\n[{Time.time:F1}s] {message}";
    }
}