using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster;
using UnityEngine;

namespace FurnaceControlMod
{
    public class FurnaceControlPlugin : MonoBehaviour
    {
        public const string Version = "0.4.0";

        public static readonly Mod MOD = new Mod("FurnaceControl", Version);

        public void OnLoaded(List<GameObject> prefabs, ConfigFile config)
        {
            // Saves with the board hold a prefab only this mod knows, so every player needs it.
            MOD.Networking.Required = true;
            MOD.AddSaveDataType<FurnaceControlSaveData>();
            new Harmony("tasermonkey.furnacecontrol").PatchAll();
            Debug.Log($"[FurnaceControl] {Version} loaded");
        }
    }
}
