using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.IO;

namespace UltraTAS
{
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.0.0")]
    public class UltraTAS : BaseUnityPlugin
    {
        // ULTRAKILL's PlayerInput exposes these InputActionState-backed actions.
        // Keep the recorder's representation aligned with that list so we can
        // replace temporary polling with the game's existing Input System.
        private class TASFrame
        {
            public Vector2 Move;
            public Vector2 Look;
            public Vector2 WheelLook;
            public bool Punch, Hook, Fire1, Fire2, Jump, Slide, Dodge, ChangeFist;
            public bool NextVariation, PreviousVariation, NextWeapon, PrevWeapon, LastWeapon;
            public bool SelectVariant1, SelectVariant2, SelectVariant3;
            public bool Pause, Stats;
            public bool Slot1, Slot2, Slot3, Slot4, Slot5, Slot6;
        }

        // InputActionState information recovered from Assembly-CSharp.
        // TriggerState stores phase/time/magnitude/control/binding state.
        // BindingState stores action/control/interaction/processor relationships.
        // UnmanagedMemory contains the contiguous native state arrays.
        // This is documented here intentionally: playback should eventually feed
        // the existing Input System rather than replacing it with OS key injection.
        private static readonly string[] PlayerInputActions =
        {
            "Move", "Look", "WheelLook", "Punch", "Hook", "Fire1", "Fire2",
            "Jump", "Slide", "Dodge", "ChangeFist", "NextVariation",
            "PreviousVariation", "NextWeapon", "PrevWeapon", "LastWeapon",
            "SelectVariant1", "SelectVariant2", "SelectVariant3", "Pause", "Stats",
            "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6"
        };

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
            Logger.LogInfo("PlayerInput/InputActionState TAS groundwork loaded.");
            Logger.LogInfo("Tracked actions: " + PlayerInputActions.Length);
            Logger.LogInfo("F6 = start/stop recording | F7 = playback | F8 = clear");
            Logger.LogInfo("========================================");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (recording) StopRecording();
                else StartRecording();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (playing) StopPlayback();
                else StartPlayback();
            }

            if (Input.GetKeyDown(KeyCode.F8)) ClearRecording();

            if (recording) RecordFrame();
            if (playing) PlayFrame();
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
            // TEMPORARY capture only. The source of truth remains PlayerInput's
            // InputAction/InputActionState chain. Do not treat these legacy Unity
            // Input calls as the final TAS implementation.
            TASFrame frame = new TASFrame
            {
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
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
                PrevWeapon = false,
                LastWeapon = Input.GetKey(KeyCode.R),
                SelectVariant1 = false,
                SelectVariant2 = false,
                SelectVariant3 = false,
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

            // Deliberately no fake keyboard/mouse injection here.
            // Next step: connect recorded values to the actual Input System state
            // after the remaining InputActionState implementation is mapped.
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
                            Bool(frame.Punch) + "," + Bool(frame.Hook) + "," +
                            Bool(frame.Fire1) + "," + Bool(frame.Fire2) + "," +
                            Bool(frame.Jump) + "," + Bool(frame.Slide) + "," +
                            Bool(frame.Dodge) + "," + Bool(frame.ChangeFist) + "," +
                            Bool(frame.NextVariation) + "," + Bool(frame.PreviousVariation) + "," +
                            Bool(frame.NextWeapon) + "," + Bool(frame.PrevWeapon) + "," +
                            Bool(frame.LastWeapon) + "," + Bool(frame.SelectVariant1) + "," +
                            Bool(frame.SelectVariant2) + "," + Bool(frame.SelectVariant3) + "," +
                            Bool(frame.Pause) + "," + Bool(frame.Stats) + "," +
                            Bool(frame.Slot1) + "," + Bool(frame.Slot2) + "," +
                            Bool(frame.Slot3) + "," + Bool(frame.Slot4) + "," +
                            Bool(frame.Slot5) + "," + Bool(frame.Slot6));
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
