using UnityEngine;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using BetterRaft.LootControl;

public class BetterLootAndUnderwaterResources : Mod
{
    Harmony harmonyBetterLoot;
    static public JsonModInfo modInfo;

    static public float Delay = 1f;
    static public bool Check;
    public const string SettingVanillaLootSystem = "Enable Vanilla Loot System";
    public static bool EnableVanillaLootSystem;
    static public float clumpSpawnDistanceUpward = -1f;
    static public float minSpawnDistanceFromPlayer = 200f;
    static public float spawnRingWidth = 10f;

    public const string SettingBarrelSpawnChance = "barrelSpawnChance";
    public const string SettingBoxSpawnChance = "boxSpawnChance";


    public static float barrelSpawnChancePercentage = 2f;
    public static float boxSpawnChancePercentage = 1f;

    public const string SettingClayChance = "claySpawnChance";
    public const string SettingCopperChance = "copperSpawnChance";
    public const string SettingClamChance = "giantClamSpawnChance";
    public const string SettingIronChance = "ironSpawnChance";
    public const string SettingRockChance = "rockSpawnChance";
    public const string SettingSandChance = "sandSpawnChance";
    public const string SettingScrapChance = "scrapSpawnChance";

    public static bool ExtraSettingsAPI_Loaded = false;
    public static bool EnableGlobalLogging = false;

    public static readonly BetterRaft.LootControl.DropperManager DropperManager = new BetterRaft.LootControl.DropperManager("Loot_Manager.json");

    public void Start()
    {
        modInfo = modlistEntry.jsonmodinfo;

        BetterLoot_Component.editedValues = new System.Collections.Generic.Dictionary<ObjectSpawnerAssetSettings, (float, float)>();
        BetterLoot_Component.originalItemSpawnChances = new System.Collections.Generic.Dictionary<SpawnableFloatingObject, float>();

        GetBetterLootSettings();
        BetterLoot_Component.ApplyCurrentSpawnRate();
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();

        harmonyBetterLoot =
            new Harmony(
                "com.DeadSigma.BetterLoot"
            );

        harmonyBetterLoot.PatchAll(
            Assembly.GetExecutingAssembly()
        );

        try
        {
            DropperManager.Load();
            DropperManager.Enable(true);

            BetterLoot_Component.TryPatchDynamicOceanCompatibility();

            Log("[BetterLootAndUnderwaterResources] Better Raft integration loaded successfully!");
        }
        catch (Exception ex)
        {
            if (EnableGlobalLogging)
                LogError($"[BetterLootAndUnderwaterResources] Failed to initialize Better Raft integration: {ex.Message}\n{ex.StackTrace}");
        }

        Log("[BetterLootAndUnderwaterResources] Mod Loaded!");
    }

    public void Update()
    {
        BetterLoot_Component.TryPatchDynamicOceanCompatibility();

        if (BetterLoot_Component.scheduleCheck)
        {
            BetterLoot_Component.scheduleCheck = false;
            if (!EnableVanillaLootSystem)
            {
                BetterLoot_Component.RefreshSpawnChancesIfNeeded();
                BetterLoot_Component.ApplyCurrentSpawnRate();
            }
        }

        BetterLoot_Component.UpdateSpawnRateDiagnostics();
    }

    public void OnModUnload()
    {
        BetterLoot_Component.scheduleCheck = false;
        BetterLoot_Component.RestoreVanillaSurfaceSpawning(false);
        BetterLoot_Component.ResetSpawnRateTracking();
        BetterLoot_Component.UnpatchDynamicOceanCompatibility();

        if (harmonyBetterLoot != null)
            harmonyBetterLoot.UnpatchAll(harmonyBetterLoot.Id);

        try
        {
            DropperManager.Enable(false);
            Destroy(gameObject);

            if (EnableGlobalLogging)
                Log("[BetterLootAndUnderwaterResources] Better Raft integration unloaded!");
        }
        catch (Exception ex)
        {
            if (EnableGlobalLogging)
                LogError($"[BetterLootAndUnderwaterResources] Error during Better Raft integration unload: {ex.Message}");
        }

        Log("[BetterLootAndUnderwaterResources] Mod Unloaded!");
    }

    public override void WorldEvent_WorldLoaded()
    {
        GetBetterLootSettings();
        BetterLoot_Component.ResetSpawnRateDiagnostics();
        BetterLoot_Component.ApplyCurrentSpawnRate(true);
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
        Patch_UnderwaterSpawns.UpdateSpawnChances();
        BetterLoot_Component.TryPatchDynamicOceanCompatibility();
    }

    public static void LogError(object message) { Debug.LogError("[" + modInfo.name + "]: " + message.ToString()); }
    public static void WarningLog(object message) { Debug.LogWarning("[" + modInfo.name + "]: " + message.ToString()); }
    public static new void Log(object message) { Debug.Log("[" + modInfo.name + "]: " + message.ToString()); }

    private float GetSpawnRatePercent()
    {
        try
        {
            return ExtraSettingsAPI_GetSliderValue("Spawn Rate Percent");
        }
        catch (Exception ex)
        {
            WarningLog("Failed to read 'Spawn Rate Percent': " + ex.Message);
            return 5f;
        }
    }

    public void GetBetterLootSettings()
    {
        if (!ExtraSettingsAPI_Loaded)
            return;

        float globalRatePercent = GetSpawnRatePercent();
        EnableVanillaLootSystem = ExtraSettingsAPI_GetCheckboxState(SettingVanillaLootSystem);

        if (float.IsNaN(globalRatePercent) || float.IsInfinity(globalRatePercent))
            globalRatePercent = 100f;
        globalRatePercent = Mathf.Clamp(globalRatePercent, 0f, 500f);

        // Нулевой процент отключает только модовый спавн, но не ванильный
        Check = !EnableVanillaLootSystem && globalRatePercent <= 0f;
        Delay = EnableVanillaLootSystem || globalRatePercent <= 0f
            ? 1f
            : Mathf.Max(0.01f, 100f / globalRatePercent);

        try
        {
            minSpawnDistanceFromPlayer = Mathf.Max(0f, ExtraSettingsAPI_GetSliderValue("Min Spawn Distance"));
        }
        catch (Exception)
        {
            minSpawnDistanceFromPlayer = 85f;
        }

        try
        {
            spawnRingWidth = Mathf.Max(0f, ExtraSettingsAPI_GetSliderValue("Spawn Ring Width"));
        }
        catch (Exception)
        {
            spawnRingWidth = 43f;
        }

        try
        {
            barrelSpawnChancePercentage = Mathf.Clamp(ExtraSettingsAPI_GetSliderValue(SettingBarrelSpawnChance), 0f, 100f);
        }
        catch (Exception ex)
        {
            WarningLog("Failed to read '" + SettingBarrelSpawnChance + "': " + ex.Message);
        }

        try
        {
            boxSpawnChancePercentage = Mathf.Clamp(ExtraSettingsAPI_GetSliderValue(SettingBoxSpawnChance), 0f, 100f);
        }
        catch (Exception ex)
        {
            WarningLog("Failed to read '" + SettingBoxSpawnChance + "': " + ex.Message);
        }

        if (EnableGlobalLogging)
            Log($"Loot mode: {(EnableVanillaLootSystem ? "Vanilla" : "Modded")}, spawn rate: {globalRatePercent:F0}%, disabled: {Check}, delay: {Delay:F4}, barrel: {barrelSpawnChancePercentage:F0}%, box: {boxSpawnChancePercentage:F0}%");
    }

    private float lastAppliedRate = float.NaN;
    private bool lastAppliedDisabled;
    private bool lastAppliedVanilla;

    private void ApplyExtraSettings()
    {
        GetBetterLootSettings();
        float newRate = Check ? 0f : 100f / Mathf.Max(0.01f, Delay);
        bool changed = float.IsNaN(lastAppliedRate) ||
                       !Mathf.Approximately(lastAppliedRate, newRate) ||
                       lastAppliedDisabled != Check ||
                       lastAppliedVanilla != EnableVanillaLootSystem;
        lastAppliedRate = newRate;
        lastAppliedDisabled = Check;
        lastAppliedVanilla = EnableVanillaLootSystem;
        if (changed)
            Debug.Log($"[BetterLoot][LootSystem] {(EnableVanillaLootSystem ? "VANILLA" : "MODDED")} | rate={newRate:F0}% | factor={Delay:F3} | host={Raft_Network.IsHost} | disabled={Check}");

        BetterLoot_Component.ApplyCurrentSpawnRate(changed);
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
        Patch_UnderwaterSpawns.UpdateSpawnChances();

        EnableGlobalLogging = ExtraSettingsAPI_GetCheckboxState("lootControl_logging");

        UpdateDropperSettings("Pickup_Floating_Barrel", "barrel");
        UpdateDropperSettings("Pickup_Floating_Box", "box");
        UpdateDropperSettings("Pickup_Landmark_LandmarkCrateRaft", "crate");
    }

    public void ExtraSettingsAPI_SettingsClose()
    {
        ApplyExtraSettings();
    }

    public void ExtraSettingsAPI_Load()
    {
        ExtraSettingsAPI_Loaded = true;
        // Настройки уже загружены API, повторный сброс и сохранение не нужны
        ApplyExtraSettings();
    }

    public void ExtraSettingsAPI_WorldLoad()
    {
        if (ExtraSettingsAPI_Loaded)
            ApplyExtraSettings();
    }

    public void ExtraSettingsAPI_Unload()
    {
        ExtraSettingsAPI_Loaded = false;
        BetterLoot_Component.RestoreVanillaSurfaceSpawning(false);
        EnableVanillaLootSystem = false;
        Check = false;
        Delay = 1f;
        BetterLoot_Component.ResetSpawnRateTracking();
    }

    public void ExtraSettingsAPI_ButtonPress(string name)
    {
        if (name != "Clear Items")
        {
            return;
        }

        if (!Raft_Network.IsHost)
        {
            WarningLog("Только хост может удалять существующий лут");
            return;
        }

        int removedCount = 0;

        foreach (ObjectSpawner_RaftDirection objectSpawner in UnityEngine.Object.FindObjectsOfType<ObjectSpawner_RaftDirection>())
        {
            if (objectSpawner == null || objectSpawner.spawnedObjects == null)
            {
                continue;
            }

            var spawnedObjects = objectSpawner.spawnedObjects.ToArray();

            foreach (var pickup in spawnedObjects)
            {
                if (pickup == null)
                {
                    continue;
                }

                PickupObjectManager.RemovePickupItemNetwork(pickup);
                removedCount++;
            }
        }

        Log($"Удалено объектов лута: {removedCount}");
    }

    private void UpdateDropperSettings(string dropperKey, string settingsPrefix)
    {
        if (BetterLootAndUnderwaterResources.DropperManager.Extensions.TryGetValue(dropperKey, out var dropper))
        {
            dropper.min = Mathf.Max(1, (int)ExtraSettingsAPI_GetInputValue($"{settingsPrefix}_minItems").ParseFloat(dropper.min ?? 1));
            dropper.max = Mathf.Max(dropper.min.Value, (int)ExtraSettingsAPI_GetInputValue($"{settingsPrefix}_maxItems").ParseFloat(dropper.max ?? 1));
            dropper.replace = ExtraSettingsAPI_GetCheckboxState($"{settingsPrefix}_replace");

            var defaultDroppers = BetterRaft.LootControl.JsonUtils.Deserialize<JObject>(BetterRaft.LootControl.DropperManager.DefaultDroppers);
            var items = dropper.items ?? new Dictionary<Item_Base, float>();
            var dropperData = defaultDroppers[dropperKey]?["items"] as JObject;

            if (dropperData != null)
            {
                foreach (var itemProperty in dropperData.Properties())
                {
                    string itemName = itemProperty.Name;
                    float weight = ExtraSettingsAPI_GetInputValue($"{settingsPrefix}_{itemName}_percentage").ParseFloat(0f);
                    var itemBase = BetterRaft.LootControl.ItemUtils.BestItemMatch(itemName);
                    if (itemBase != null && weight > 0)
                    {
                        items[itemBase] = weight;
                        if (EnableGlobalLogging)
                            Log($"[BetterLootAndUnderwaterResources] Setting {dropperKey} item {itemName} to weight {weight}");
                    }
                    else if (itemBase != null)
                    {
                        items.Remove(itemBase);
                        if (EnableGlobalLogging)
                            Log($"[BetterLootAndUnderwaterResources] Removing {dropperKey} item {itemName} due to zero weight or invalid.");
                    }
                }
            }
            dropper.items = items;
        }
        else
        {
            if (EnableGlobalLogging)
                WarningLog($"[BetterLootAndUnderwaterResources] Dropper key {dropperKey} not found in extensions during settings update.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ExtraSettingsAPI_GetCheckboxState(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API GetCheckboxState stub called for " + SettingName);
        if (SettingName == "lootControl_logging") return false;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ExtraSettingsAPI_GetInputValue(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API GetInputValue stub called for " + SettingName);
        return "";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ExtraSettingsAPI_SetCheckboxState(string SettingName, bool value)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API SetCheckboxState stub called for " + SettingName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ExtraSettingsAPI_SetInputValue(string SettingName, string value)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API SetInputValue stub called for " + SettingName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float ExtraSettingsAPI_GetSliderValue(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API GetSliderValue stub called for " + SettingName);
        if (SettingName == "Spawn Rate Percent") return 5f;
        if (SettingName == "Min Spawn Distance") return 85f;
        if (SettingName == "Spawn Ring Width") return 43f;
        if (SettingName == SettingBarrelSpawnChance) return 2f;
        if (SettingName == SettingBoxSpawnChance) return 1f;
        return 0f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ExtraSettingsAPI_SetSliderValue(string SettingName, float value)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API SetSliderValue stub called for " + SettingName + " with value " + value);
    }

    public string ExtraSettingsAPI_HandleSliderText(string name, float value)
    {

        if (name == SettingBarrelSpawnChance || name == SettingBoxSpawnChance || name == "Spawn Rate Percent" ||
            name == SettingClayChance || name == SettingCopperChance || name == SettingClamChance ||
            name == SettingIronChance || name == SettingRockChance || name == SettingSandChance || name == SettingScrapChance)
        {
            return $"{value:F0}%";
        }
        return $"{value:F0}";
    }
    public bool ExtraSettingsAPI_HandleSettingVisible(string SettingName)
    {
        if (SettingName == "Clear Items")
        {
            return Raft_Network.IsHost;
        }

        return true;
    }
    public void ExtraSettingsAPI_SettingsOpen() { }
    public void ExtraSettingsAPI_SettingsCreate() { }
    public void ExtraSettingsAPI_ButtonPress(string SettingName, int Index) { }
}

public static class ExtensionMethods
{
    public static float ParseFloat(this string value, float fallback = 1f)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        value = value.Replace(',', '.');
        return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float result) ? result : fallback;
    }
}