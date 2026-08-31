using BepInEx;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace UltraTAS
{
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.0.0")]
    public class UltraTAS : BaseUnityPlugin
    {
        // This mirrors the actions exposed by ULTRAKILL's PlayerInput.
        // We are keeping the TAS frame format independent of the actual
        // InputActionState API until that class has been inspected.
        private class TASFrame
        {
            public Vector2 Move;
            public Vector2 Look;
            public Vector2 WheelLook;

            public bool Punch;
            public bool Hook;
            public bool Fire1;
            public bool Fire2;
            public bool Jump;
            public bool Slide;
            public bool Dodge;
            public bool ChangeFist;

            public bool NextVariation;
            public bool PreviousVariation;
            public bool NextWeapon;
            public bool PrevWeapon;
            public bool LastWeapon;

            public bool SelectVariant1;
            public bool SelectVariant2;
            public bool SelectVariant3;

            public bool Pause;
            public bool Stats;

            public bool Slot1;
            public bool Slot2;
            public bool Slot3;
            public bool Slot4;
            public bool Slot5;
            public bool Slot6;
        }

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private bool recording;
        private bool playing;
        private int playbackFrame;
        private string tasPath;

        private void Awake()
        {
            tasPath = Path.Combine(Paths.ConfigPath, "ultratas.tas");

            Logger.LogInfo("========================================");
            Logger.LogInfo("UltraTAS loaded.");
            Logger.LogInfo("Recording/playback core initialized.");
            Logger.LogInfo("F6 = start/stop recording");
            Logger.LogInfo("F7 = start/stop playback");
            Logger.LogInfo("F8 = clear recording");
            Logger.LogInfo("========================================");
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
                ClearRecording();
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
            playbackFrame = 0;
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

        private void ClearRecording()
        {
            frames.Clear();
            recording = false;
            playing = false;
            playbackFrame = 0;
            Logger.LogInfo("TAS recording cleared.");
        }

        private void RecordFrame()
        {
            // Temporary compatibility capture. The actual source of truth will
            // be ULTRAKILL's PlayerInput/InputActionState once that class is mapped.
            // Do not remove the TASFrame fields above: they correspond to every
            // action currently exposed by PlayerInput.
            TASFrame frame = new TASFrame
            {
                Move = new Vector2(
                    Input.GetAxisRaw("Horizontal"),
                    Input.GetAxisRaw("Vertical")),
                Look = Vector2.zero,
                WheelLook = Vector2.zero,

                Punch = Input.GetMouseButton(0),
                Hook = Input.GetKey(KeyCode.E),
                Fire1 = Input.GetMouseButton(0),
                Fire2 = Input.GetMouseButton(1),
                Jump = Input.GetKey(KeyCode.Space),
                Slide = Input.GetKey(KeyCode.LeftControl),
                Dodge = Input.GetKey(KeyCode.LeftShift),
                ChangeFist = Input.GetKey(KeyCode.F),

                NextVariation = Input.GetKey(KeyCode.X),
                PreviousVariation = Input.GetKey(KeyCode.Z),
                NextWeapon = Input.GetKey(KeyCode.Q),
                PrevWeapon = Input.GetKey(KeyCode.Q),
                LastWeapon = Input.GetKey(KeyCode.R),

                SelectVariant1 = Input.GetKey(KeyCode.Alpha1),
                SelectVariant2 = Input.GetKey(KeyCode.Alpha2),
                SelectVariant3 = Input.GetKey(KeyCode.Alpha3),

                Pause = Input.GetKey(KeyCode.Escape),
                Stats = Input.GetKey(KeyCode.Tab),

                Slot1 = Input.GetKey(KeyCode.Alpha1),
                Slot2 = Input.GetKey(KeyCode.Alpha2),
                Slot3 = Input.GetKey(KeyCode.Alpha3),
                Slot4 = Input.GetKey(KeyCode.Alpha4),
                Slot5 = Input.GetKey(KeyCode.Alpha5),
                Slot6 = Input.GetKey(KeyCode.Alpha6)
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

            // Input injection intentionally remains disabled here.
            // We need PlayerInput's InputActionState implementation before
            // playback can safely feed values into ULTRAKILL.
            playbackFrame++;
        }

        private void SaveRecording()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(tasPath, false))
                {
                    writer.WriteLine("UltraTAS v1");
                    writer.WriteLine("Frames=" + frames.Count);

                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            frame.Move.x + "," + frame.Move.y + "," +
                            frame.Look.x + "," + frame.Look.y + "," +
                            frame.WheelLook.x + "," + frame.WheelLook.y + "," +
                            Bool(frame.Punch) + "," +
                            Bool(frame.Hook) + "," +
                            Bool(frame.Fire1) + "," +
                            Bool(frame.Fire2) + "," +
                            Bool(frame.Jump) + "," +
                            Bool(frame.Slide) + "," +
                            Bool(frame.Dodge) + "," +
                            Bool(frame.ChangeFist) + "," +
                            Bool(frame.NextVariation) + "," +
                            Bool(frame.PreviousVariation) + "," +
                            Bool(frame.NextWeapon) + "," +
                            Bool(frame.PrevWeapon) + "," +
                            Bool(frame.LastWeapon) + "," +
                            Bool(frame.SelectVariant1) + "," +
                            Bool(frame.SelectVariant2) + "," +
                            Bool(frame.SelectVariant3) + "," +
                            Bool(frame.Pause) + "," +
                            Bool(frame.Stats) + "," +
                            Bool(frame.Slot1) + "," +
                            Bool(frame.Slot2) + "," +
                            Bool(frame.Slot3) + "," +
                            Bool(frame.Slot4) + "," +
                            Bool(frame.Slot5) + "," +
                            Bool(frame.Slot6));
                    }
                }

                Logger.LogInfo("TAS saved to: " + tasPath);
            }
            catch (System.Exception ex)
            {
                Logger.LogError("Failed to save TAS: " + ex);
            }
        }

        private static int Bool(bool value)
        {
            return value ? 1 : 0;
        }
    }
}
