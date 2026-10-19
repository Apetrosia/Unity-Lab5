using UnityEngine;
using UnityEngine.SceneManagement;

public class IntroController : MonoBehaviour
{
    void Update()
    {
        // Переход на уровень при нажатии Enter
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SceneManager.LoadScene("level");
        }
        
        // Выход из игры при нажатии Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
            
            // В редакторе Unity Application.Quit() не работает, поэтому добавляем это для тестирования
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }
    }
}