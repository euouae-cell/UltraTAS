using BepInEx;
using System;
using System.IO;
using UnityEngine;

namespace _UltraTAS
{
[BepInPlugin("UltraTAS", "UltraTAS", "1.0.0")]
public class UltraTasCore : BaseUnityPlugin
{
private string logFile;
private int updateCount = 0;

    private void Awake()
    {
        string folder = Path.Combine(Paths.PluginPath, "UltraTAS");
        Directory.CreateDirectory(folder);

        logFile = Path.Combine(folder, "UltraTAS_debug.log");

        File.WriteAllText(
            logFile,
            "========================================\n" +
            "UltraTAS debug log\n" +
            $"Started: {DateTime.Now}\n" +
            "========================================\n"
        );

        DebugLog("AWAKE() reached.");
        DebugLog("UltraTAS successfully instantiated.");
    }

    private void Update()
    {
        updateCount++;

        if (updateCount == 1)
        {
            DebugLog("UPDATE() reached for the first time.");
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            DebugLog("F6 DETECTED!");
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            DebugLog("F7 DETECTED!");
        }
    }

    private void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(
                logFile,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n"
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"UltraTAS debug logger failed: {ex}"
            );
        }
    }
}

}
