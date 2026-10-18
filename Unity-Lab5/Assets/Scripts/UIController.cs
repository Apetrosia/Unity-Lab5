using UnityEngine;
using UnityEngine.UIElements;

public class UIController : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement rootElement;

    void Start()
    {
        // Получаем ссылку на UIDocument
        uiDocument = GetComponent<UIDocument>();

        // Получаем корневой элемент
        rootElement = uiDocument.rootVisualElement;

        // Загружаем USS файл
        StyleSheet styleSheet = Resources.Load<StyleSheet>("TerminalStyle");

        if (styleSheet != null)
        {
            rootElement.styleSheets.Add(styleSheet);
            Debug.Log("USS файл подключен успешно!");
        }
        else
        {
            Debug.LogError("Не удалось найти USS файл!");
        }
    }
}