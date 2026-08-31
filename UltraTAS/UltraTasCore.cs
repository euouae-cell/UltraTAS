using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace _UltraTAS
{
    [BepInPlugin("UltraTAS", "UltraTAS", "1.0.0")]
    public class UltraTasCore : BaseUnityPlugin
    {
        private bool recording = false;
        private bool playing = false;

        private int frame = 0;
        private int playbackFrame = 0;

        private string currentFile = "";

        private readonly List<FrameData> recordedFrames = new();

        private void Awake()
        {
            Logger.LogInfo("================================");
            Logger.LogInfo("ULTRATAS CORE ACTUALLY WORKS");
            Logger.LogInfo("Simple recorder/playback version");
            Logger.LogInfo("F6 = Record");
            Logger.LogInfo("F7 = Playback");
            Logger.LogInfo("================================");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (!recording)
                    StartRecording();
                else
                    StopRecording();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (!playing)
                    StartPlayback();
                else
                    StopPlayback();
            }

            if (recording)
            {
                RecordFrame();
            }
        }

        private void StartRecording()
        {
            if (playing)
            {
                Logger.LogWarning("Cannot record while playing.");
                return;
            }

            recordedFrames.Clear();
            frame = 0;

            string folder = Path.Combine(Paths.PluginPath, "UltraTAS");
            Directory.CreateDirectory(folder);

            currentFile = Path.Combine(folder, "test.tas");

            recording = true;

            Logger.LogInfo("=== RECORDING STARTED ===");
            Logger.LogInfo($"Saving to: {currentFile}");
        }

        private void StopRecording()
        {
            recording = false;

            SaveRecording();

            Logger.LogInfo("=== RECORDING STOPPED ===");
            Logger.LogInfo($"Frames recorded: {recordedFrames.Count}");
        }

        private void RecordFrame()
        {
            FrameData data = new FrameData();

            data.frame = frame++;

            data.cameraX = GetCameraX();
            data.cameraY = GetCameraY();

            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKey(key))
                {
                    data.keys.Add(key);
                }
            }

            if (Input.GetMouseButton(0))
                data.mouse0 = true;

            if (Input.GetMouseButton(1))
                data.mouse1 = true;

            recordedFrames.Add(data);
        }

        private void SaveRecording()
        {
            try
            {
                using StreamWriter writer = new StreamWriter(currentFile);

                foreach (FrameData frameData in recordedFrames)
                {
                    writer.WriteLine($"FRAME {frameData.frame}");
                    writer.WriteLine($"CAMERA_X {frameData.cameraX}");
                    writer.WriteLine($"CAMERA_Y {frameData.cameraY}");

                    foreach (KeyCode key in frameData.keys)
                    {
                        writer.WriteLine($"KEY {key}");
                    }

                    if (frameData.mouse0)
                        writer.WriteLine("MOUSE0");

                    if (frameData.mouse1)
                        writer.WriteLine("MOUSE1");

                    writer.WriteLine("END");
                }

                Logger.LogInfo("TAS saved successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save TAS: {ex}");
            }
        }

        private void StartPlayback()
        {
            if (recording)
            {
                Logger.LogWarning("Cannot play while recording.");
                return;
            }

            if (!File.Exists(currentFile))
            {
                Logger.LogWarning("No TAS file exists yet.");
                return;
            }

            LoadRecording();

            if (recordedFrames.Count == 0)
            {
                Logger.LogWarning("TAS contains no frames.");
                return;
            }

            playing = true;
            playbackFrame = 0;

            Logger.LogInfo("=== PLAYBACK STARTED ===");

            StartCoroutine(PlaybackCoroutine());
        }

        private void StopPlayback()
        {
            playing = false;
            Logger.LogInfo("=== PLAYBACK STOPPED ===");
        }

        private IEnumerator PlaybackCoroutine()
        {
            while (playing && playbackFrame < recordedFrames.Count)
            {
                FrameData data = recordedFrames[playbackFrame];

                ApplyCamera(data);

                Logger.LogInfo(
                    $"Playback frame {data.frame} | " +
                    $"Keys: {data.keys.Count} | " +
                    $"Mouse0: {data.mouse0} | " +
                    $"Mouse1: {data.mouse1}"
                );

                playbackFrame++;

                yield return null;
            }

            if (playing)
            {
                Logger.LogInfo("=== PLAYBACK FINISHED ===");
            }

            playing = false;
        }

        private void LoadRecording()
        {
            recordedFrames.Clear();

            try
            {
                string[] lines = File.ReadAllLines(currentFile);

                FrameData current = null;

                foreach (string line in lines)
                {
                    if (line.StartsWith("FRAME "))
                    {
                        current = new FrameData();
                        current.frame = int.Parse(line.Substring(6));
                    }
                    else if (line.StartsWith("CAMERA_X "))
                    {
                        current.cameraX = float.Parse(line.Substring(9));
                    }
                    else if (line.StartsWith("CAMERA_Y "))
                    {
                        current.cameraY = float.Parse(line.Substring(9));
                    }
                    else if (line.StartsWith("KEY "))
                    {
                        if (current != null)
                        {
                            string keyName = line.Substring(4);

                            if (Enum.TryParse(
                                keyName,
                                out KeyCode key))
                            {
                                current.keys.Add(key);
                            }
                        }
                    }
                    else if (line == "MOUSE0")
                    {
                        if (current != null)
                            current.mouse0 = true;
                    }
                    else if (line == "MOUSE1")
                    {
                        if (current != null)
                            current.mouse1 = true;
                    }
                    else if (line == "END")
                    {
                        if (current != null)
                        {
                            recordedFrames.Add(current);
                            current = null;
                        }
                    }
                }

                Logger.LogInfo(
                    $"Loaded {recordedFrames.Count} frames."
                );
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"Failed to load TAS: {ex}"
                );
            }
        }

        private float GetCameraX()
        {
            try
            {
                return MonoSingleton<CameraController>
                    .Instance.rotationX;
            }
            catch
            {
                return 0f;
            }
        }

        private float GetCameraY()
        {
            try
            {
                return MonoSingleton<CameraController>
                    .Instance.rotationY;
            }
            catch
            {
                return 0f;
            }
        }

        private void ApplyCamera(FrameData data)
        {
            try
            {
                MonoSingleton<CameraController>
                    .Instance.rotationX = data.cameraX;

                MonoSingleton<CameraController>
                    .Instance.rotationY = data.cameraY;
            }
            catch
            {
            }
        }

        private class FrameData
        {
            public int frame;

            public float cameraX;
            public float cameraY;

            public List<KeyCode> keys = new();

            public bool mouse0;
            public bool mouse1;
        }
    }
}