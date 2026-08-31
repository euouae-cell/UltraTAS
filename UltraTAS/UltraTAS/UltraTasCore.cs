using BepInEx;
using UnityEngine;

namespace _UltraTAS
{
[BepInPlugin("UltraTAS", "UltraTAS", "1.0.0")]
public class UltraTasCore : BaseUnityPlugin
{
private bool recording;

    private void Awake()
    {
        Logger.LogInfo("[UltraTAS TEST] AWAKE!");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Logger.LogInfo("[UltraTAS TEST] F6!");

            recording = !recording;

            if (recording)
                Logger.LogInfo("[UltraTAS TEST] RECORDING ON");
            else
                Logger.LogInfo("[UltraTAS TEST] RECORDING OFF");
        }

        if (recording)
        {
            Logger.LogDebug("[UltraTAS TEST] RECORDING FRAME");
        }
    }
}

}
