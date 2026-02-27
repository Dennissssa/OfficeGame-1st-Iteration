using UnityEngine;
using System.IO.Ports;

public class ArduinoManager : MonoBehaviour
{
    // Check Arduino IDE > Tools > Port to see if yours is COM3, COM4, etc.
    SerialPort dataStream = new SerialPort("/dev/cu.usbserial-14210", 9600);

    void Start()
    {
        dataStream.Open();
    }

    void Update()
    {
        if (dataStream.IsOpen)
        {
            string message = dataStream.ReadLine();
            Debug.Log(message);
        }
    }

    void OnApplicationQuit()
    {
        dataStream.Close();
    }
}