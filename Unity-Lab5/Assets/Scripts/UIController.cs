using UnityEngine;
using UnityEngine.UIElements;

public class UIController : MonoBehaviour
{
    void Start()
    {
        var uiDocument = GetComponent<UIDocument>();
        var rootElement = uiDocument.rootVisualElement;

        StyleSheet styleSheet = Resources.Load<StyleSheet>("TerminalStyle");
        if (styleSheet != null)
        {
            rootElement.styleSheets.Add(styleSheet);
        }
    }
}