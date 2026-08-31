using BepInEx;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace UltraTAS
{
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.0.0")]
    public class UltraTAS : BaseUnityPlugin
    {
        private class TASFrame
        {
            public bool W;
            public bool A;
            public bool S;
            public bool D;
            public bool Jump;
            public bool Dash;
            public bool Slide;
            public bool Punch;
            public bool Fire;
            public bool AltFire;
            public bool ChangeWeapon;
        }

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private bool recording;
        private bool playing;
        private int playbackFrame;
        private string tasPath;

        private void Awake()
        {
            tasPath = Path.Combine(Paths.ConfigPath, "ultratas.tas");
            Logger.LogInfo("UltraTAS loaded.");
            Logger.LogInfo("F6 = start/stop recording, F7 = start/stop playback, F8 = clear recording.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (recording)
                    StopRecording();
                else
                    StartRecording();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (playing)
                    StopPlayback();
                else
                    StartPlayback();
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                frames.Clear();
                recording = false;
                playing = false;
                playbackFrame = 0;
                Logger.LogInfo("TAS recording cleared.");
            }

            if (recording)
                RecordFrame();

            if (playing)
                PlayFrame();
        }

        private void StartRecording()
        {
            playing = false;
            frames.Clear();
            recording = true;
            Logger.LogInfo("TAS recording started.");
        }

        private void StopRecording()
        {
            recording = false;
            SaveRecording();
            Logger.LogInfo("TAS recording stopped. Frames: " + frames.Count);
        }

        private void StartPlayback()
        {
            if (frames.Count == 0)
            {
                Logger.LogWarning("Cannot start playback: no recorded frames.");
                return;
            }

            recording = false;
            playing = true;
            playbackFrame = 0;
            Logger.LogInfo("TAS playback started. Frames: " + frames.Count);
        }

        private void StopPlayback()
        {
            playing = false;
            Logger.LogInfo("TAS playback stopped at frame " + playbackFrame + ".");
        }

        private void RecordFrame()
        {
            TASFrame frame = new TASFrame
            {
                W = Input.GetKey(KeyCode.W),
                A = Input.GetKey(KeyCode.A),
                S = Input.GetKey(KeyCode.S),
                D = Input.GetKey(KeyCode.D),
                Jump = Input.GetKey(KeyCode.Space),
                Dash = Input.GetKey(KeyCode.LeftShift),
                Slide = Input.GetKey(KeyCode.LeftControl),
                Punch = Input.GetMouseButton(0),
                Fire = Input.GetMouseButton(0),
                AltFire = Input.GetMouseButton(1),
                ChangeWeapon = Input.GetKey(KeyCode.Q)
            };

            frames.Add(frame);
        }

        private void PlayFrame()
        {
            if (playbackFrame >= frames.Count)
            {
                StopPlayback();
                return;
            }

            // Playback input injection will be implemented after recording is verified.
            playbackFrame++;
        }

        private void SaveRecording()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(tasPath, false))
                {
                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            (frame.W ? "1" : "0") + "," +
                            (frame.A ? "1" : "0") + "," +
                            (frame.S ? "1" : "0") + "," +
                            (frame.D ? "1" : "0") + "," +
                            (frame.Jump ? "1" : "0") + "," +
                            (frame.Dash ? "1" : "0") + "," +
                            (frame.Slide ? "1" : "0") + "," +
                            (frame.Punch ? "1" : "0") + "," +
                            (frame.Fire ? "1" : "0") + "," +
                            (frame.AltFire ? "1" : "0") + "," +
                            (frame.ChangeWeapon ? "1" : "0"));
                    }
                }

                Logger.LogInfo("TAS saved to: " + tasPath);
            }
            catch (System.Exception ex)
            {
                Logger.LogError("Failed to save TAS: " + ex);
            }
        }
    }
}
