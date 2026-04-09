using System.Drawing;
using UnityEngine;
using Color = UnityEngine.Color;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

public class ButtonFlash : MonoBehaviour
{
    public Color darkColor;
    public Color lightColor;

    public bool gettingLighter;
    public Image buttonBackground;
    
    public float colorChangeSpeed;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (buttonBackground.color == lightColor)
        {
            gettingLighter = false;
        }
        else if (buttonBackground.color == darkColor)
        {
            gettingLighter = true;
        }

        if (gettingLighter)
        {
            buttonBackground.color = Color.Lerp(buttonBackground.color, lightColor, (colorChangeSpeed));
        }
        else
        {
            buttonBackground.color = Color.Lerp(buttonBackground.color, darkColor, (colorChangeSpeed));
        }
    }
}
