﻿using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.IO.Ports;
using System;
using System.Linq;

public enum ConnectionState
{
    WAIT,
    DISCOVER,
    CONNECTED,
    TERMINATED
}

public class Interface : MonoBehaviour
{
    public Transform testTarget;
    public Dropdown portDropdown;
    public Text outputLog;
    public Vector3 acclData = Vector3.zero;
    public Vector3 acclCalibrated = Vector3.zero;
    bool isCalibrated = false;
    Matrix4x4 calibrationMatrix;
    Vector3 wantedDeadZone = Vector3.zero;
    string[] stringDelimiters = new string[] { ":", "XYZ" };
    SerialPort sp; 
    ushort timeOut = 1; //Important value. If not set, code will check for serial input forever.
    public ConnectionState connectionState = ConnectionState.WAIT;
    // Use this for initialization
    void Start ()
    {
        AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);
        String[] ports = SerialPort.GetPortNames();
        portDropdown.AddOptions(ports.ToList());
    }

    public void CheckPort()
    {
        connectionState = ConnectionState.DISCOVER;
        isCalibrated = false;
        outputLog.color = Color.white;
        string port = portDropdown.options[portDropdown.value].text;
        if (sp != null)
            sp.Close();
        sp = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
        
        sp.Open();
        sp.ReadTimeout = timeOut;
        StartCoroutine(CheckPortForAck());
    }

    IEnumerator CheckPortForAck()
    {
        while(connectionState == ConnectionState.DISCOVER)
        {
            float connectTime = 0;
            SendToArduino("DISCOVER");
            string reply;
            do
            {
                reply = CheckForRecievedData();
                connectTime += Time.deltaTime;
                outputLog.text = "Send DISCOVER to port. T+: " + connectTime.ToString("0.0");
                yield return null;
            } while (reply == string.Empty && connectTime < 5);
            //string reply = CheckForRecievedData();
            outputLog.text = "Device reply: " + reply;
            if (reply == "ACKNOWLEDGE")
            {
                connectionState = ConnectionState.CONNECTED;
                outputLog.color = Color.green;
            }
            else
            {
                sp.Close();
                outputLog.text = "Connection failed.";
                outputLog.color = Color.red;
                break;
            }
        }
    }

	void Update ()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            CloseConnection();
    }

    private void FixedUpdate() //Check for commands 60 times a second
    {
        if(connectionState == ConnectionState.CONNECTED)
        {
            string cmd = CheckForRecievedData();
            if (cmd == string.Empty)
                return;
            if(cmd.StartsWith("XYZ:"))//Recieved Accelerometer Command
            {
                acclData = ParseAccelerometerData(cmd);
                if(!isCalibrated)
                {
                    isCalibrated = true;
                    CalibrateAccelerometer();
                }
            }
            if (cmd == "BUTTON_DOWN")
                Debug.LogError("STOP");
            acclCalibrated = GetAccelerometer(acclData);
            acclCalibrated = new Vector3(0, 0, -acclCalibrated.y);
            testTarget.transform.rotation = Quaternion.Slerp(testTarget.transform.rotation, Quaternion.Euler(acclCalibrated), Time.deltaTime*10);
        }
    }

    Vector3 ParseAccelerometerData(string data)
    {
        string[] splitResult = data.Split(stringDelimiters, StringSplitOptions.RemoveEmptyEntries);
        int x = int.Parse(splitResult[0]);
        int y = int.Parse(splitResult[1]);
        int z = int.Parse(splitResult[2]);
        return new Vector3(x, y, z);
    }

    void CalibrateAccelerometer()
    {
        wantedDeadZone = acclData;
        Quaternion rotateQuaternion = Quaternion.FromToRotation(new Vector3(0f, 0f, -1f), wantedDeadZone);
        //create identity matrix ... rotate our matrix to match up with down vec
        Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, rotateQuaternion, new Vector3(1f, 1f, 1f));
        //get the inverse of the matrix
        calibrationMatrix = matrix.inverse;

    }

    Vector3 GetAccelerometer(Vector3 accelerator)
    {
        Vector3 accel = this.calibrationMatrix.MultiplyVector(accelerator);
        return accel;
    }

    public void SendToArduino(string cmd)
    {
        if (sp.IsOpen)
        {
            cmd = cmd.ToUpper();
            sp.Write(cmd);
        }
        else
            Debug.LogError("Serial Port: " + sp.PortName + " is unavailable");
    }

    void OnProcessExit(object sender, EventArgs e)
    {
        CloseConnection();
    }
    
    void CloseConnection()
    {
        SendToArduino("TERMINATE");
        outputLog.color = Color.yellow;
        outputLog.text = "Connection Terminated";
        connectionState = ConnectionState.TERMINATED;
        sp.Close();
    }

    public string CheckForRecievedData()
    {
        try
        {
            string inData = sp.ReadLine();
            int inSize = inData.Count();
            if (inSize > 0)
            {
                Debug.Log("ARDUINO->|| " + inData + " ||MSG SIZE:" + inSize.ToString());
            }
            inSize = 0;
            
            sp.BaseStream.Flush();
            sp.DiscardInBuffer();
            return inData;
        }
        catch { return string.Empty; }
    }

}
