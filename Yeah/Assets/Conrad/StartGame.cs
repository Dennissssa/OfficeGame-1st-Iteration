using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class StartGame : MonoBehaviour
{
    private Mouse ms;

    private Keyboard kb;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ms = Mouse.current;
        kb = Keyboard.current;
    }

    // Update is called once per frame
    void Update()
    {
        if (ms.leftButton.wasPressedThisFrame || kb.anyKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(1);
        }
    }
}
