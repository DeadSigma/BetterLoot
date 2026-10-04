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

    static public float Delay;
    static public bool Check;
    static public float clumpSpawnDistanceUpward = -1f;
    static public float minSpawnDistanceFromPlayer = 200f;
    static public float spawnRingWidth = 10f;

    public const string SettingBarrelSpawnChance = "barrelSpawnChance";
    public const string SettingBoxSpawnChance = "boxSpawnChance";


    public static float barrelSpawnChancePercentage = 5f;
    public static float boxSpawnChancePercentage = 5f;

    public const string SettingClayChance = "claySpawnChance";
    public const string SettingCopperChance = "copperSpawnChance";
    public const string SettingClamChance = "giantClamSpawnChance";
    public const string SettingIronChance = "ironSpawnChance";
    public const string SettingRockChance = "rockSpawnChance";
    public const string SettingSandChance = "sandSpawnChance";
    public const string SettingScrapChance = "scrapSpawnChance";

    public static bool ExtraSettingsAPI_Loaded = false;
    public static bool EnableGlobalLogging = false;

    private const int CurrentSettingsVersion = 2;
    private const string SettingsVersionData = "__ConfigVersion";
    private const string SettingsVersionKey = "version";
    public static readonly BetterRaft.LootControl.DropperManager DropperManager = new BetterRaft.LootControl.DropperManager("Loot_Manager.json");

    public void Start()
    {
        modInfo = modlistEntry.jsonmodinfo;

        BetterLoot_Component.editedValues = new System.Collections.Generic.Dictionary<ObjectSpawnerAssetSettings, (float, float)>();
        BetterLoot_Component.originalItemSpawnChances = new System.Collections.Generic.Dictionary<SpawnableFloatingObject, float>();

        GetBetterLootSettings();
        BetterLoot_Component.ModifyCurrentSettings();
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
            GetBetterLootSettings();
            BetterLoot_Component.ModifyCurrentSettings();
            BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
            BetterLoot_Component.scheduleCheck = false;
        }
    }

    public void OnModUnload()
    {
        BetterLoot_Component.UnmodifyAll();
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
        Patch_UnderwaterSpawns.UpdateSpawnChances();

        GetBetterLootSettings();
        BetterLoot_Component.ModifyCurrentSettings();
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
        BetterLoot_Component.TryPatchDynamicOceanCompatibility();
    }

    public static void LogError(object message) { Debug.LogError("[" + modInfo.name + "]: " + message.ToString()); }
    public static void WarningLog(object message) { Debug.LogWarning("[" + modInfo.name + "]: " + message.ToString()); }
    public static new void Log(object message) { Debug.Log("[" + modInfo.name + "]: " + message.ToString()); }

    public void GetBetterLootSettings()
    {
        if (!ExtraSettingsAPI_Loaded) { return; }

        float globalRatePercent = ExtraSettingsAPI_GetSliderValue("Spawn Rate Percent");
        bool currentGlobalDisableCheckbox = ExtraSettingsAPI_GetCheckboxState("Disable Item Spawn");

        if (Mathf.Approximately(globalRatePercent, 0f))
        {
            Check = true;
            Delay = float.PositiveInfinity;
            if (!currentGlobalDisableCheckbox)
            {
                ExtraSettingsAPI_SetCheckboxState("Disable Item Spawn", true);
            }
        }
        else
        {
            Check = currentGlobalDisableCheckbox;
            if (Check)
            {
                Delay = float.PositiveInfinity;
            }
            else
            {
                Delay = 100.0f / globalRatePercent;
                if (Delay < 0.00001f) Delay = 0.00001f;
            }
        }
        try
        {
            minSpawnDistanceFromPlayer = ExtraSettingsAPI_GetSliderValue("Min Spawn Distance");
            if (minSpawnDistanceFromPlayer < 0f) minSpawnDistanceFromPlayer = 0f;
        }
        catch (Exception)
        {
            minSpawnDistanceFromPlayer = 0f;
        }
        try
        {
            spawnRingWidth = ExtraSettingsAPI_GetSliderValue("Spawn Ring Width");
            if (spawnRingWidth < 0f) spawnRingWidth = 0f;
        }
        catch (Exception)
        {
            spawnRingWidth = 50f;
        }



        try
        {
            barrelSpawnChancePercentage = ExtraSettingsAPI_GetSliderValue(SettingBarrelSpawnChance);
            if (barrelSpawnChancePercentage > 0f)
            {
                barrelSpawnChancePercentage /= 10.0f;
            }
            if (barrelSpawnChancePercentage < 0f) barrelSpawnChancePercentage = 0f;
            if (barrelSpawnChancePercentage > 100f) barrelSpawnChancePercentage = 100f;
        }
        catch (Exception ex)
        {
            WarningLog($"Failed to get '{SettingBarrelSpawnChance}' from ExtraSettingsAPI: {ex.Message}. Using default/current value: {barrelSpawnChancePercentage}%.");
        }

        try
        {
            boxSpawnChancePercentage = ExtraSettingsAPI_GetSliderValue(SettingBoxSpawnChance);
            if (boxSpawnChancePercentage > 0f)
            {
                boxSpawnChancePercentage /= 10.0f;
            }
            if (boxSpawnChancePercentage < 0f) boxSpawnChancePercentage = 0f;
            if (boxSpawnChancePercentage > 100f) boxSpawnChancePercentage = 100f;
        }
        catch (Exception ex)
        {
            WarningLog($"Failed to get '{SettingBoxSpawnChance}' from ExtraSettingsAPI: {ex.Message}. Using default/current value: {boxSpawnChancePercentage}%.");
        }
        if (EnableGlobalLogging) Log($"Barrel Chance (after division): {barrelSpawnChancePercentage}%, Box Chance (after division): {boxSpawnChancePercentage}%");
    }

    public void ExtraSettingsAPI_SettingsClose()
    {
        GetBetterLootSettings();
        BetterLoot_Component.RemodifyAll();
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
        ExtraSettingsAPI_SaveSettings();

        Patch_UnderwaterSpawns.UpdateSpawnChances();

        EnableGlobalLogging = ExtraSettingsAPI_GetCheckboxState("lootControl_logging");
        if (EnableGlobalLogging)
            Log($"[BetterLootAndUnderwaterResources] Global Logging set to: {EnableGlobalLogging}");

        UpdateDropperSettings("Pickup_Floating_Barrel", "barrel");
        UpdateDropperSettings("Pickup_Floating_Box", "box");
        UpdateDropperSettings("Pickup_Landmark_LandmarkCrateRaft", "crate");
    }

    private void EnsureExtraSettingsVersion()
    {
        string savedVersion =
            ExtraSettingsAPI_GetDataValue(
                SettingsVersionData,
                SettingsVersionKey
            );

        int version;

        if (!int.TryParse(
                savedVersion,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out version))
        {
            version = 0;
        }

        if (version == CurrentSettingsVersion)
            return;

        ExtraSettingsAPI_ResetAllSettings();

        ExtraSettingsAPI_SetDataValue(
            SettingsVersionData,
            SettingsVersionKey,
            CurrentSettingsVersion.ToString(
                CultureInfo.InvariantCulture
            )
        );

        ExtraSettingsAPI_SaveSettings();

        Log(
            "[BetterLootAndUnderwaterResources] Extra Settings reset for config version " +
            CurrentSettingsVersion
        );
    }

    public void ExtraSettingsAPI_Load()
    {
        ExtraSettingsAPI_Loaded = true;
        EnsureExtraSettingsVersion();
        GetBetterLootSettings();
        BetterLoot_Component.RemodifyAll();
        BetterLoot_Component.ApplyAllIndividualSpawnChanceSettings();
        Patch_UnderwaterSpawns.UpdateSpawnChances();



        ExtraSettingsAPI_SettingsClose();
    }

    public void ExtraSettingsAPI_Unload()
    {
        ExtraSettingsAPI_Loaded = false;
        BetterLoot_Component.UnmodifyAll();
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
    public static void ExtraSettingsAPI_SaveSettings()
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API SaveSettings stub called");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string ExtraSettingsAPI_GetDataValue(
        string SettingName,
        string SubName)
    {
        return "";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ExtraSettingsAPI_SetDataValue(
        string SettingName,
        string SubName,
        string Value)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ExtraSettingsAPI_ResetAllSettings()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float ExtraSettingsAPI_GetSliderValue(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded && EnableGlobalLogging) LogError("API GetSliderValue stub called for " + SettingName);
        if (SettingName == "Spawn Rate Percent") return 100f;
        if (SettingName == "Spawn Ring Width") return 50f;
        if (SettingName == SettingBarrelSpawnChance) return 10f;
        if (SettingName == SettingBoxSpawnChance) return 5f;
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