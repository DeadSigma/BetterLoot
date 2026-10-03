using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine.UI;
using HMLLibrary;
using UnityEngine.SceneManagement;
using System.Runtime.CompilerServices;


class ModLoadException : System.Exception
{
    public ModLoadException(string message) : base(message) { }
}

[HarmonyPatch(typeof(Landmark), "Initialize")]
public class Patch_UnderwaterSpawns
{
    public static float ClayChance = 100f;
    public static float CopperChance = 1f;
    public static float GiantClamChance = 100f;
    public static float IronChance = 100f;
    public static float RockChance = 100f;
    public static float SandChance = 100f;
    public static float ScrapChance = 100f;


    public static void UpdateSpawnChances()
    {

        if (BetterLootAndUnderwaterResources.ExtraSettingsAPI_Loaded)
        {
            ClayChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingClayChance);
            CopperChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingCopperChance);
            GiantClamChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingClamChance);
            IronChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingIronChance);
            RockChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingRockChance);
            SandChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingSandChance);
            ScrapChance = BetterLootAndUnderwaterResources.ExtraSettingsAPI_GetSliderValue(BetterLootAndUnderwaterResources.SettingScrapChance);
        }
        else
        {
            ClayChance = 10f;
            CopperChance = 10f;
            GiantClamChance = 10f;
            IronChance = 10f;
            RockChance = 10f;
            SandChance = 10f;
            ScrapChance = 10f;
        }
    }

    static void Prefix(ref Landmark __instance, bool ___initialized)
    {
        if (BetterLootAndUnderwaterResources.modInfo == null)
        {
            Debug.LogError("[BetterUnderwaterLoot.Patch_Prefix] FATAL: modInfo is null! Cannot proceed.");
            return;
        }

        if (___initialized)
        {
            return;
        }

        if (!BetterLootAndUnderwaterResources.ExtraSettingsAPI_Loaded)
        {
            return;
        }

        var rand = new System.Random(System.BitConverter.ToInt32(System.BitConverter.GetBytes(__instance.uniqueLandmarkIndex), 0));

        foreach (var itemComponent in __instance.GetComponentsInChildren<LandmarkItem_PickupItem>())
        {
            string itemName = itemComponent.name;
            float configuredChance = -1f;

            if (itemName.StartsWith("Pickup_Landmark_Clay"))
                configuredChance = ClayChance;
            else if (itemName.StartsWith("Pickup_Landmark_Copper"))
                configuredChance = CopperChance;
            else if (itemName.StartsWith("Pickup_Landmark_GiantClam"))
                configuredChance = GiantClamChance;
            else if (itemName.StartsWith("Pickup_Landmark_Iron"))
                configuredChance = IronChance;
            else if (itemName.StartsWith("Pickup_Landmark_Rock"))
                configuredChance = RockChance;
            else if (itemName.StartsWith("Pickup_Landmark_Sand"))
                configuredChance = SandChance;
            else if (itemName.StartsWith("Pickup_Landmark_Scrap") && itemName.Contains("OceanBottom"))
                configuredChance = ScrapChance;
            else
                continue;

            if (rand.NextDouble() * 100.0 >= configuredChance)
            {
                var modelTransform = itemComponent.transform.Find("Model");
                if (modelTransform != null)
                {
                    var meshRenderer = modelTransform.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.enabled = false;
                    }
                }

                var collider = itemComponent.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }
    }
}