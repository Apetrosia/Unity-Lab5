using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    // === Игровые переменные ===
    private float ramCurrent = 200;      // Начальная RAM
    private float ramMax = 1024;
    private float uploadProgress = 0;
    private bool isGameOver = false;

    // Переменные для спавна процессов
    private float spawnTimer = 0;
    private float spawnInterval = 2.5f;  // Новый процесс каждые 2.5 секунды
    private int processCounter = 0;

    // === Ссылки на UI элементы ===
    private VisualElement ramFill;
    private VisualElement uploadFill;
    private Label ramText;
    private Label uploadText;
    private Label logOutput;
    private Label statusText;
    private ScrollView processList; // Ссылка на список процессов

    void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // Находим элементы
        ramFill = root.Q<VisualElement>("ram-fill");
        uploadFill = root.Q<VisualElement>("upload-fill");
        ramText = root.Q<Label>("ram-text");
        uploadText = root.Q<Label>("upload-text");
        logOutput = root.Q<Label>("log-output");
        statusText = root.Q<Label>("status-text");

        // Находим ScrollView для процессов
        processList = root.Q<ScrollView>("process-list");

        Log("System initialized. Monitoring RAM...");
        Log("WARNING: Unauthorized processes detected!");
        UpdateUI();
    }

    void Update()
    {
        if (isGameOver) return;

        // 1. Спавн процессов
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            SpawnProcess();
            spawnTimer = 0;
            // Немного рандома в интервале, чтобы было живее
            spawnInterval = Random.Range(2f, 4f);
        }

        // 2. Проверка поражения
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
        int ramCost = Random.Range(50, 150); // Процесс занимает от 50 до 150 KB
        string procName = $"PROC_{Random.Range(1000, 9999)}.exe";

        // Сразу занимаем RAM
        ramCurrent += ramCost;

        // --- Создаем UI элемент динамически ---

        // 1. Создаем контейнер для строки
        var processItem = new VisualElement();
        processItem.AddToClassList("process-item"); // Применяем класс из USS для красоты

        // 2. Создаем текст
        var label = new Label($"[{processCounter}] {procName} ... {ramCost}KB [CLICK TO KILL]");
        processItem.Add(label); // Добавляем текст в контейнер

        // 3. Вешаем обработчик клика!
        processItem.RegisterCallback<ClickEvent>(evt => KillProcess(processItem, ramCost, procName));

        // 4. Добавляем в наш ScrollView
        if (processList != null)
        {
            processList.Add(processItem);
        }

        Log($"New threat detected: {procName} (+{ramCost}KB RAM)");
    }

    void KillProcess(VisualElement element, int ramCost, string name)
    {
        if (isGameOver) return;

        // Освобождаем RAM
        ramCurrent -= ramCost;
        if (ramCurrent < 0) ramCurrent = 0;

        // Удаляем элемент из UI
        if (processList != null)
        {
            processList.Remove(element);
        }

        Log($"Process {name} terminated. Freed {ramCost}KB.");
    }

    // === ОБНОВЛЕНИЕ UI ===

    void UpdateUI()
    {
        float ramPercent = (ramCurrent / ramMax) * 100f;

        // Полоска RAM
        if (ramFill != null)
        {
            ramFill.style.width = Length.Percent(ramPercent);

            if (ramPercent < 60) ramFill.style.backgroundColor = new Color(0, 1, 0);
            else if (ramPercent < 85) ramFill.style.backgroundColor = new Color(1, 1, 0);
            else ramFill.style.backgroundColor = new Color(1, 0, 0);
        }

        if (ramText != null) ramText.text = $"{(int)ramCurrent} / {(int)ramMax} KB ({(int)ramPercent}%)";

        // Полоска UPLOAD (пока просто медленно растет для теста)
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