using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class KeyboardInterceptor : MonoBehaviour
{
    public List<Keyboard> keyboards = new List<Keyboard>();
    public Keyboard auxKeyboard;
    public Keyboard mainKeyboard;
    void Start()
    {
        //foreach (Keyboard keyboard in InputSystem.devices)
        //{
           // keyboards.Add(keyboard);
        //}

        //if (keyboards.Count > 1)
        //{
            //mainKeyboard = keyboards[0];
            //auxKeyboard = keyboards[1];
        //}
        mainKeyboard = Keyboard.current;
    }

    // Update is called once per frame
    void Update()
    {
        if (mainKeyboard.spaceKey.isPressed)
        {
            return;
        }

        if (mainKeyboard.anyKey.isPressed)
        {
            GameManager.Instance.Reward();
        }
    }
}
