using UnityEngine;
using System.IO.Ports;

public class SimpleSerialTest : MonoBehaviour
{
    // Keeping your specific Mac/Linux port path
    SerialPort dataStream = new SerialPort("COM6", 9600);
    public bool isDevilActive = false;

    void Start()
    {
        try
        {
            dataStream.Open();
            dataStream.ReadTimeout = 10; // Reduced to 10ms for snappier performance
            Debug.Log("Serial Port Opened.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Could not open serial port: " + e.Message);
        }
    }

    void Update()
    {
        if (dataStream.IsOpen)
        {
            // Only try to read if there's actually something in the buffer
            if (dataStream.BytesToRead > 0)
            {
                try
                {
                    string input = dataStream.ReadLine().Trim();

                    if (input == "ON")
                    {
                        isDevilActive = true;
                        Debug.Log("The Devil is here.");
                        dataStream.Write("1");
                    }
                    else if (input == "OFF")
                    {
                        isDevilActive = false;
                        Debug.Log("The Devil is GONE!!!");
                        dataStream.Write("0");
                    }
                }
                catch (System.TimeoutException)
                {
                    // This catches the error so it doesn't stop your game
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        if (dataStream != null && dataStream.IsOpen)
        {
            dataStream.Close();
        }
    }
}