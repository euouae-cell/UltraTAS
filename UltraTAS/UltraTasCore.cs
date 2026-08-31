using BepInEx;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace _UltraTAS
{
[BepInPlugin("UltraTAS", "UltraTAS", "1.0.0")]
public class UltraTasCore : BaseUnityPlugin
{
private bool recording = false;
private int frame = 0;

```
    private readonly List<FrameData> frames = new List<FrameData>();

    private void Awake()
    {
        Logger.LogInfo("[UltraTAS DEBUG] ===== AWAKE START =====");

        try
        {
            Logger.LogInfo("[UltraTAS DEBUG] Step 1: Awake entered.");

            Logger.LogInfo("[UltraTAS DEBUG] Step 2: Initializing variables.");
            recording = false;
            frame = 0;

            Logger.LogInfo("[UltraTAS DEBUG] Step 3: Frame list count = " + frames.Count);

            Logger.LogInfo("[UltraTAS DEBUG] Step 4: Testing KeyCode enum.");
            int keyCount = Enum.GetValues(typeof(KeyCode)).Length;
            Logger.LogInfo("[UltraTAS DEBUG] Step 5: KeyCode count = " + keyCount);

            Logger.LogInfo("[UltraTAS DEBUG] Step 6: Testing Unity Time.");
            Logger.LogInfo("[UltraTAS DEBUG] Time.time = " + Time.time);

            Logger.LogInfo("[UltraTAS DEBUG] Step 7: Testing Input system.");
            bool testKey = Input.GetKey(KeyCode.F6);
            Logger.LogInfo("[UltraTAS DEBUG] Step 8: Input.GetKey(F6) = " + testKey);

            Logger.LogInfo("[UltraTAS DEBUG] ===== AWAKE SUCCESS =====");
        }
        catch (Exception ex)
        {
            Logger.LogError("[UltraTAS DEBUG] !!! AWAKE FAILED !!!");
            Logger.LogError("[UltraTAS DEBUG] Exception type: " + ex.GetType().FullName);
            Logger.LogError("[UltraTAS DEBUG] Exception message: " + ex.Message);
            Logger.LogError("[UltraTAS DEBUG] Stack trace: " + ex.StackTrace);
        }
    }

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Logger.LogInfo("[UltraTAS DEBUG] F6 PRESSED");

                if (recording)
                {
                    Logger.LogInfo("[UltraTAS DEBUG] Currently recording -> stopping.");
                    StopRecording();
                }
                else
                {
                    Logger.LogInfo("[UltraTAS DEBUG] Currently stopped -> starting.");
                    StartRecording();
                }
            }

            if (recording)
            {
                RecordFrame();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("[UltraTAS DEBUG] !!! UPDATE FAILED !!!");
            Logger.LogError("[UltraTAS DEBUG] Exception type: " + ex.GetType().FullName);
            Logger.LogError("[UltraTAS DEBUG] Exception message: " + ex.Message);
            Logger.LogError("[UltraTAS DEBUG] Stack trace: " + ex.StackTrace);

            recording = false;
        }
    }

    private void StartRecording()
    {
        Logger.LogInfo("[UltraTAS DEBUG] ===== START RECORDING =====");

        try
        {
            Logger.LogInfo("[UltraTAS DEBUG] Start step 1: Clearing frames.");
            frames.Clear();

            Logger.LogInfo("[UltraTAS DEBUG] Start step 2: Resetting frame counter.");
            frame = 0;

            Logger.LogInfo("[UltraTAS DEBUG] Start step 3: Setting recording = true.");
            recording = true;

            Logger.LogInfo("[UltraTAS DEBUG] ===== RECORDING STARTED =====");
        }
        catch (Exception ex)
        {
            Logger.LogError("[UltraTAS DEBUG] !!! START RECORDING FAILED !!!");
            Logger.LogError("[UltraTAS DEBUG] Exception type: " + ex.GetType().FullName);
            Logger.LogError("[UltraTAS DEBUG] Exception message: " + ex.Message);
            Logger.LogError("[UltraTAS DEBUG] Stack trace: " + ex.StackTrace);

            recording = false;
        }
    }

    private void StopRecording()
    {
        Logger.LogInfo("[UltraTAS DEBUG] ===== STOP RECORDING =====");

        try
        {
            Logger.LogInfo("[UltraTAS DEBUG] Stop step 1: Current frame = " + frame);
            Logger.LogInfo("[UltraTAS DEBUG] Stop step 2: Stored frames = " + frames.Count);

            recording = false;

            Logger.LogInfo("[UltraTAS DEBUG] Stop step 3: recording = false");
            Logger.LogInfo("[UltraTAS DEBUG] ===== RECORDING STOPPED =====");
            Logger.LogInfo("[UltraTAS DEBUG] Total frames = " + frames.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError("[UltraTAS DEBUG] !!! STOP RECORDING FAILED !!!");
            Logger.LogError("[UltraTAS DEBUG] Exception type: " + ex.GetType().FullName);
            Logger.LogError("[UltraTAS DEBUG] Exception message: " + ex.Message);
            Logger.LogError("[UltraTAS DEBUG] Stack trace: " + ex.StackTrace);
        }
    }

    private void RecordFrame()
    {
        try
        {
            if (frame % 60 == 0)
            {
                Logger.LogInfo(
                    "[UltraTAS DEBUG] Recording frame " +
                    frame +
                    " | Stored frames = " +
                    frames.Count
                );
            }

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: creating FrameData.");

            FrameData data = new FrameData();

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: setting frame number.");
            data.frame = frame++;

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: enumerating keys.");

            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKey(key))
                {
                    data.keys.Add(key);
                }
            }

            Logger.LogDebug(
                "[UltraTAS DEBUG] RecordFrame: keys recorded = " +
                data.keys.Count
            );

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: reading mouse.");

            data.mouse0 = Input.GetMouseButton(0);
            data.mouse1 = Input.GetMouseButton(1);

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: adding frame.");

            frames.Add(data);

            Logger.LogDebug("[UltraTAS DEBUG] RecordFrame: SUCCESS.");
        }
        catch (Exception ex)
        {
            Logger.LogError("[UltraTAS DEBUG] !!! RECORD FRAME FAILED !!!");
            Logger.LogError("[UltraTAS DEBUG] Exception type: " + ex.GetType().FullName);
            Logger.LogError("[UltraTAS DEBUG] Exception message: " + ex.Message);
            Logger.LogError("[UltraTAS DEBUG] Stack trace: " + ex.StackTrace);

            recording = false;
        }
    }

    private class FrameData
    {
        public int frame;
        public List<KeyCode> keys = new List<KeyCode>();
        public bool mouse0;
        public bool mouse1;
    }
}
```

}
