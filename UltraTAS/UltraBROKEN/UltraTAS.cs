using BepInEx;
using UnityEngine;

namespace UltraTAS
{
[BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.0.0")]
public class UltraTAS : BaseUnityPlugin
{
private int updateCount = 0;

    private void Awake()
    {
        Logger.LogInfo("========================================");
        Logger.LogInfo("UltraTAS TEST START");
        Logger.LogInfo("AWAKE reached!");
        Logger.LogInfo("Unity version: " + Application.unityVersion);
        Logger.LogInfo("GameObject: " + gameObject.name);
        Logger.LogInfo("========================================");
    }

    private void OnEnable()
    {
        Logger.LogInfo("UltraTAS OnEnable reached!");
    }

    private void Update()
    {
        updateCount++;

        if (updateCount == 1)
        {
            Logger.LogInfo("FIRST UPDATE REACHED!");
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            Logger.LogInfo("F6 PRESSED!");
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            Logger.LogInfo("F7 PRESSED!");
        }

        if (updateCount % 300 == 0)
        {
            Logger.LogInfo("Update is alive. Count = " + updateCount);
        }
    }

    private void OnDisable()
    {
        Logger.LogInfo("UltraTAS OnDisable reached!");
        Logger.LogInfo("Final update count: " + updateCount);
    }

    private void OnDestroy()
    {
        Logger.LogInfo("UltraTAS OnDestroy reached!");
    }
}

}
