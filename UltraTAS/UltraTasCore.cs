using BepInEx;
using UnityEngine;

namespace _UltraTAS
{
[BepInPlugin("UltraTAS", "UltraTAS", "1.0.0")]
public class UltraTasCore : BaseUnityPlugin
{
private void Awake()
{
Logger.LogInfo("ULTRATAS TEST COMPONENT LOADED");
}

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Logger.LogInfo("F6 WORKS");
        }
    }
}

}
