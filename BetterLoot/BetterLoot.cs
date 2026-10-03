using UnityEngine;
using System.IO;
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using HMLLibrary;
using RaftModLoader;
using System.Runtime.CompilerServices;
using System.Reflection;

public class BetterLoot_Component
{
    static public Dictionary<ObjectSpawnerAssetSettings, (float, float)> editedValues;
    static public Dictionary<SpawnableFloatingObject, float> originalItemSpawnChances;
    static public bool scheduleCheck;

    private static Harmony dynamicOceanHarmony;
    private static bool dynamicOceanPatched;
    private static float nextDynamicOceanPatchAttempt;

    static public float multiplier
    {
        get
        {
            return BetterLootAndUnderwaterResources.Check ? float.PositiveInfinity : BetterLootAndUnderwaterResources.Delay;
        }
    }

    private static string GetCurrentGlobalRateString()
    {
        if (BetterLootAndUnderwaterResources.Check || float.IsPositiveInfinity(BetterLootAndUnderwaterResources.Delay)) return "0% (Disabled / Off)";
        float percentage = (BetterLootAndUnderwaterResources.Delay > 0.0000001f) ? (100.0f / BetterLootAndUnderwaterResources.Delay) : 0f;
        if (Mathf.Approximately(percentage, 0f) || float.IsInfinity(percentage)) return "Effectively 0% (Extremely high delay or rate too high)";
        return $"{percentage:F0}% (Delay Multiplier: {BetterLootAndUnderwaterResources.Delay:F4})";
    }


    public static void LogError(object message) { BetterLootAndUnderwaterResources.LogError(message); }
    public static void WarningLog(object message) { BetterLootAndUnderwaterResources.WarningLog(message); }
    public static void Log(object message) { BetterLootAndUnderwaterResources.Log(message); }

    public static bool Quaternion_LookRotation_(Vector3 forward, Vector3 upwards, out Quaternion result)
    {
        if (forward.sqrMagnitude < 0.0001f)
        {
            result = Quaternion.identity;
            return false;
        }
        result = Quaternion.LookRotation(forward, upwards);
        return true;
    }


    public static void ModifyCurrentSettings()
    {
        var manager = ComponentManager<ObjectSpawnerManager>.Value;
        if (manager != null)
            foreach (var objectSpawner in new ObjectSpawner_RaftDirection[] { manager.plankSpawner, manager.itemSpawner })
                if (objectSpawner != null)
                    ModifySettings(Traverse.Create(objectSpawner).Field("currentSettings").GetValue<ObjectSpawnerAssetSettings>());
    }

    public static void ModifySettings(ObjectSpawnerAssetSettings set)
    {
        if (set == null) { return; }
        if (editedValues == null) { editedValues = new Dictionary<ObjectSpawnerAssetSettings, (float, float)>(); }
        if (!editedValues.ContainsKey(set))
        {
            editedValues.Add(set, (set.spawnRateInterval.minValue, set.spawnRateInterval.maxValue));
            set.spawnRateInterval.minValue *= multiplier;
            set.spawnRateInterval.maxValue *= multiplier;
        }
        else
        {
            RemodifySettings(set);
        }
    }

    public static void RemodifySettings(ObjectSpawnerAssetSettings set)
    {
        if (editedValues != null && editedValues.ContainsKey(set))
        {
            set.spawnRateInterval.minValue = editedValues[set].Item1 * multiplier;
            set.spawnRateInterval.maxValue = editedValues[set].Item2 * multiplier;
        }
    }

    public static void RemodifyAll()
    {
        if (editedValues == null || editedValues.Count <= 0) { return; }
        foreach (var pair in editedValues)
        {
            if (pair.Key == null) continue;
            pair.Key.spawnRateInterval.minValue = pair.Value.Item1 * multiplier;
            pair.Key.spawnRateInterval.maxValue = pair.Value.Item2 * multiplier;
        }
        var spawnerManager = ComponentManager<ObjectSpawnerManager>.Value;
        if (spawnerManager != null)
        {
            foreach (var objectSpawner in new ObjectSpawner_RaftDirection[] { spawnerManager.plankSpawner, spawnerManager.itemSpawner })
            {
                if (objectSpawner == null) continue;
                ObjectSpawnerAssetSettings currentSettings = Traverse.Create(objectSpawner).Field("currentSettings").GetValue<ObjectSpawnerAssetSettings>();
                if (currentSettings != null) Traverse.Create(objectSpawner).Field("spawnDelay").SetValue(currentSettings.spawnRateInterval.GetRandomValue());
            }
        }
    }

    public static void ApplyIndividualSpawnChanceSettings(ObjectSpawner spawner)
    {
        if (spawner == null || spawner.spawnableObjects == null)
        {
            if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"ApplyIndividualSpawnChanceSettings: Spawner or spawnableObjects list is null for spawner: {(spawner != null ? spawner.ToString() : "Unknown")}. Skipping.");
            return;
        }
        if (originalItemSpawnChances == null) { originalItemSpawnChances = new Dictionary<SpawnableFloatingObject, float>(); }

        foreach (SpawnableFloatingObject sfo in spawner.spawnableObjects)
        {
            if (sfo == null) continue;

            string sfoLogDisplay = sfo.ToString();

            Traverse sfoTraverse = Traverse.Create(sfo);
            string poolName = null;
            float gameOriginalSpawnChance = -1f;

            try
            {
                poolName = sfoTraverse.Field("poolName").GetValue<string>();
            }
            catch (Exception ex)
            {
                if (BetterLootAndUnderwaterResources.EnableGlobalLogging) WarningLog($"Error getting poolName for SFO ({sfoLogDisplay}). Exception: {ex.Message}");
                continue;
            }
            try
            {
                gameOriginalSpawnChance = sfoTraverse.Field("spawnChance").GetValue<float>();
            }
            catch (Exception ex)
            {
                if (BetterLootAndUnderwaterResources.EnableGlobalLogging) WarningLog($"Error getting spawnChance for SFO ({sfoLogDisplay}), Pool: {poolName ?? "N/A"}. Exception: {ex.Message}");
                continue;
            }

            if (!originalItemSpawnChances.ContainsKey(sfo))
            {
                originalItemSpawnChances.Add(sfo, gameOriginalSpawnChance);
                if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"--- Storing original game spawnChance for SFO ({sfoLogDisplay}), pool '{poolName ?? "N/A"}': {gameOriginalSpawnChance}");
            }

            float actualOriginalChance = originalItemSpawnChances.TryGetValue(sfo, out float storedOriginal) ? storedOriginal : gameOriginalSpawnChance;
            float currentSFOValue = sfoTraverse.Field("spawnChance").GetValue<float>();
            float newTargetSpawnChance = actualOriginalChance;
            bool isControlledBySetting = false;

            if (poolName == "Barrel")
            {
                newTargetSpawnChance = BetterLootAndUnderwaterResources.barrelSpawnChancePercentage;
                isControlledBySetting = true;
                if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"--- Pool '{poolName}' ({sfoLogDisplay}): Target spawnChance from slider: {newTargetSpawnChance}");
            }
            else if (poolName == "Box")
            {
                newTargetSpawnChance = BetterLootAndUnderwaterResources.boxSpawnChancePercentage;
                isControlledBySetting = true;
                if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"--- Pool '{poolName}' ({sfoLogDisplay}): Target spawnChance from slider: {newTargetSpawnChance}");
            }

            if (isControlledBySetting)
            {
                newTargetSpawnChance = Mathf.Clamp(newTargetSpawnChance, 0f, 100f);
                if (!Mathf.Approximately(currentSFOValue, newTargetSpawnChance))
                {
                    sfoTraverse.Field("spawnChance").SetValue(newTargetSpawnChance);
                    if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"--- Pool '{poolName}' ({sfoLogDisplay}): Set spawnChance from {currentSFOValue} to {newTargetSpawnChance}. Original was {actualOriginalChance}");
                }
            }
        }
    }

    public static void ApplyAllIndividualSpawnChanceSettings()
    {
        var manager = ComponentManager<ObjectSpawnerManager>.Value;
        if (manager != null)
        {
            if (manager.itemSpawner != null) ApplyIndividualSpawnChanceSettings(manager.itemSpawner);
            if (manager.plankSpawner != null) ApplyIndividualSpawnChanceSettings(manager.plankSpawner);
        }
        else
        {
            if (BetterLootAndUnderwaterResources.EnableGlobalLogging) WarningLog("ApplyAllIndividualSpawnChanceSettings: ObjectSpawnerManager not found.");
        }
    }

    public static void UnmodifyAll()
    {
        if (editedValues != null && editedValues.Count > 0)
        {
            foreach (var pair in editedValues)
            {
                if (pair.Key == null) continue;
                pair.Key.spawnRateInterval.minValue = pair.Value.Item1;
                pair.Key.spawnRateInterval.maxValue = pair.Value.Item2;
            }
            editedValues.Clear();
            if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log("Unmodified all spawn rate intervals.");
        }

        if (originalItemSpawnChances != null && originalItemSpawnChances.Count > 0)
        {
            foreach (var pair in originalItemSpawnChances)
            {
                if (pair.Key == null) continue;

                string pairKeyLogDisplay = pair.Key.ToString();

                try
                {
                    Traverse.Create(pair.Key).Field("spawnChance").SetValue(pair.Value);
                    if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log($"--- Restored original spawnChance for SFO ({pairKeyLogDisplay}), pool '{Traverse.Create(pair.Key).Field("poolName").GetValue<string>() ?? "N/A"}' to {pair.Value}");
                }
                catch (Exception ex)
                {
                    if (BetterLootAndUnderwaterResources.EnableGlobalLogging) LogError($"Error restoring SFO ({pairKeyLogDisplay}) spawn chance: {ex.Message}");
                }
            }
            originalItemSpawnChances.Clear();
            if (BetterLootAndUnderwaterResources.EnableGlobalLogging) Log("Unmodified all individual item spawn chances.");
        }
    }

    static float Parse(string value, float fallback = 1f)
    {
        if (string.IsNullOrWhiteSpace(value)) { return fallback; }
        if (value.Contains(",") && !value.Contains(".")) value = value.Replace(',', '.');
        if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        { return v; }
        return fallback;
    }
    public static void TryPatchDynamicOceanCompatibility()
    {
        if (dynamicOceanPatched ||
            Time.realtimeSinceStartup < nextDynamicOceanPatchAttempt)
        {
            return;
        }

        nextDynamicOceanPatchAttempt =
            Time.realtimeSinceStartup + 1f;

        Type dynamicOceanType =
            AccessTools.TypeByName("DynamicOcean");

        if (dynamicOceanType == null)
            return;

        MethodInfo getSpawnInterval =
            AccessTools.Method(
                dynamicOceanType,
                "GetSpawnInterval"
            );

        MethodInfo spawnRandomFlowItem =
            AccessTools.Method(
                dynamicOceanType,
                "SpawnRandomFlowItem"
            );

        if (getSpawnInterval == null ||
            spawnRandomFlowItem == null)
        {
            return;
        }

        dynamicOceanHarmony =
            new Harmony(
                "com.DeadSigma.BetterLoot.DynamicOceanCompat"
            );

        dynamicOceanHarmony.Patch(
            getSpawnInterval,
            postfix: new HarmonyMethod(
                AccessTools.Method(
                    typeof(BetterLoot_Component),
                    nameof(DynamicOcean_GetSpawnInterval_Postfix)
                )
            )
        );

        dynamicOceanHarmony.Patch(
            spawnRandomFlowItem,
            prefix: new HarmonyMethod(
                AccessTools.Method(
                    typeof(BetterLoot_Component),
                    nameof(DynamicOcean_SpawnRandomFlowItem_Prefix)
                )
            )
        );

        dynamicOceanPatched = true;

        if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
            Log("Dynamic Ocean compatibility enabled");
    }

    public static void UnpatchDynamicOceanCompatibility()
    {
        if (dynamicOceanHarmony != null)
        {
            dynamicOceanHarmony.UnpatchAll(
                dynamicOceanHarmony.Id
            );
        }

        dynamicOceanHarmony = null;
        dynamicOceanPatched = false;
        nextDynamicOceanPatchAttempt = 0f;
    }

    private static bool DynamicOcean_SpawnRandomFlowItem_Prefix()
    {
        return !BetterLootAndUnderwaterResources.Check &&
               !float.IsPositiveInfinity(
                   BetterLootAndUnderwaterResources.Delay
               );
    }

    private static void DynamicOcean_GetSpawnInterval_Postfix(
        ref float __result)
    {
        if (BetterLootAndUnderwaterResources.Check ||
            float.IsPositiveInfinity(
                BetterLootAndUnderwaterResources.Delay
            ))
        {
            __result = 1f;
            return;
        }

        float factor =
            BetterLootAndUnderwaterResources.Delay;

        if (float.IsNaN(factor) ||
            factor <= 0f)
        {
            factor = 1f;
        }

        __result =
            Mathf.Max(
                0.05f,
                __result * factor
            );
    }

    public static bool TryGetRaftTravelSpawnPosition(
        ObjectSpawner spawner,
        out Vector3 result)
    {
        result = Vector3.zero;

        if (spawner == null)
            return false;

        Transform spawnerTransform =
            spawner.transform;

        Vector3 center =
            spawnerTransform.position +
            Vector3.up *
            BetterLootAndUnderwaterResources.clumpSpawnDistanceUpward;

        float minRadius =
            Mathf.Max(
                0f,
                BetterLootAndUnderwaterResources.minSpawnDistanceFromPlayer
            );

        float ringWidth =
            Mathf.Max(
                0f,
                BetterLootAndUnderwaterResources.spawnRingWidth
            );

        float radius =
            ringWidth > 0.001f
                ? UnityEngine.Random.Range(
                    minRadius,
                    minRadius + ringWidth
                )
                : minRadius;

        if (radius <= 0.001f)
        {
            result = center;
            return true;
        }

        float angle =
            UnityEngine.Random.Range(
                -90f,
                90f
            );

        Vector3 localPoint =
            new Vector3(
                radius *
                Mathf.Sin(
                    angle *
                    Mathf.Deg2Rad
                ),
                0f,
                radius *
                Mathf.Cos(
                    angle *
                    Mathf.Deg2Rad
                )
            );

        Vector3 worldOffset;
        ObjectSpawner_RaftDirection raftSpawner =
            spawner as ObjectSpawner_RaftDirection;

        if (raftSpawner != null)
        {
            Vector3 direction =
                Traverse.Create(
                    raftSpawner
                )
                .Field(
                    "spawnDirectionFromRaft"
                )
                .GetValue<Vector3>();

            Quaternion orientation;

            if (direction.sqrMagnitude > 0.001f &&
                Quaternion_LookRotation_(
                    direction.normalized,
                    Vector3.up,
                    out orientation
                ))
            {
                worldOffset =
                    orientation *
                    localPoint;
            }
            else
            {
                worldOffset =
                    spawnerTransform.TransformDirection(
                        localPoint
                    );
            }
        }
        else
        {
            worldOffset =
                spawnerTransform.TransformDirection(
                    localPoint
                );
        }

        result =
            center +
            worldOffset;

        return true;
    }

}

[HarmonyPatch(typeof(SO_ObjectSpawner), "GetSettings")]
public class Patch_GetObjectSpawnerSettings
{
    static void Postfix()
    {
        BetterLoot_Component.scheduleCheck = true;
    }
}

public static class BetterLoot_RaftTravelSpawnContext
{
    [ThreadStatic]
    private static int depth;

    [ThreadStatic]
    private static bool hasPendingPosition;

    [ThreadStatic]
    private static Vector3 pendingPosition;

    [ThreadStatic]
    private static uint pendingSpawnerIndex;

    public static bool Active
    {
        get { return depth > 0; }
    }

    public static void Enter()
    {
        depth++;
    }

    public static void Exit()
    {
        if (depth > 0)
            depth--;

        if (depth == 0)
            hasPendingPosition = false;
    }

    public static void Remember(
        uint spawnerIndex,
        Vector3 position)
    {
        pendingSpawnerIndex =
            spawnerIndex;

        pendingPosition =
            position;

        hasPendingPosition =
            true;
    }

    public static bool TryConsume(
        uint spawnerIndex,
        out Vector3 position)
    {
        position =
            Vector3.zero;

        if (!Active ||
            !hasPendingPosition ||
            pendingSpawnerIndex != spawnerIndex)
        {
            return false;
        }

        position =
            pendingPosition;

        hasPendingPosition =
            false;

        return true;
    }
}

[HarmonyPatch(
    typeof(ObjectSpawner_RaftDirection),
    "SpawnNewItems"
)]
public class Patch_ObjectSpawner_RaftDirection_SpawnNewItems
{
    static void Prefix()
    {
        BetterLoot_RaftTravelSpawnContext.Enter();
    }

    static Exception Finalizer(
        Exception __exception)
    {
        BetterLoot_RaftTravelSpawnContext.Exit();
        return __exception;
    }
}

[HarmonyPatch(
    typeof(ObjectSpawner),
    "SpawnItem",
    new Type[]
    {
        typeof(Vector3),
        typeof(Vector3),
        typeof(SpawnableFloatingObject),
        typeof(uint),
        typeof(uint),
        typeof(int)
    },
    new ArgumentType[]
    {
        ArgumentType.Normal,
        ArgumentType.Normal,
        ArgumentType.Normal,
        ArgumentType.Normal,
        ArgumentType.Normal,
        ArgumentType.Normal
    }
)]
public class Patch_ObjectSpawner_SpawnItem
{
    static void Prefix(
        ObjectSpawner __instance,
        ref Vector3 position)
    {
        if (!BetterLoot_RaftTravelSpawnContext.Active)
            return;

        ObjectSpawnerManager manager =
            ComponentManager<ObjectSpawnerManager>.Value;

        if (manager == null ||
            __instance == null ||
            (__instance != manager.itemSpawner &&
             __instance != manager.plankSpawner))
        {
            return;
        }

        Vector3 changedPosition;

        if (!BetterLoot_Component.TryGetRaftTravelSpawnPosition(
                __instance,
                out changedPosition))
        {
            return;
        }

        position =
            changedPosition;

        BetterLoot_RaftTravelSpawnContext.Remember(
            __instance.UniqueIndex,
            changedPosition
        );
    }
}

[HarmonyPatch(
    typeof(Message_ObjectSpawner_SpawnItem),
    MethodType.Constructor,
    new Type[]
    {
        typeof(Messages),
        typeof(MonoBehaviour_Network),
        typeof(Vector3),
        typeof(Vector3),
        typeof(uint),
        typeof(uint),
        typeof(uint),
        typeof(uint),
        typeof(int)
    }
)]
public class Patch_Message_ObjectSpawner_SpawnItem_Constructor
{
    static void Prefix(
        ref Vector3 spawnPosition,
        uint uniqueSpawnerIndex)
    {
        Vector3 changedPosition;

        if (BetterLoot_RaftTravelSpawnContext.TryConsume(
                uniqueSpawnerIndex,
                out changedPosition))
        {
            spawnPosition =
                changedPosition;
        }
    }
}
