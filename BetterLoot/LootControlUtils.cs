using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BetterRaft.LootControl
{
    public static class FileUtils
    {
        private static readonly string Root = Path.Combine(HLib.path_modsFolder, "ModData", "BetterRaft");

        static FileUtils()
        {
            Directory.CreateDirectory(Root);
        }

        internal static string ModDataPath(string fileName) => Path.Combine(Root, fileName);

        public static void CreateIfMissing(string fileName, string content)
        {
            string path = ModDataPath(fileName);
            if (!File.Exists(path))
                File.WriteAllText(path, content);
        }

        public static string ReadOrCreateDefault(string fileName, string defaultContent)
        {
            CreateIfMissing(fileName, defaultContent);
            return File.ReadAllText(ModDataPath(fileName));
        }
    }

    public static class JsonUtils
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            Converters = new JsonConverter[]
            {
                new StringEnumConverter(),
                new ItemConverter(),
                new MapItemConverter()
            }
        };

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static string Serialize(object obj, bool pretty = true)
        {
            Settings.Formatting = pretty ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(obj, Settings);
        }

        private class ItemConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value is Item_Base item)
                    writer.WriteValue(item.name);
                else
                    throw new JsonSerializationException($"Invalid value {value} for ItemConverter");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var name = (string)reader.Value;
                var item = ItemUtils.BestItemMatch(name);
                if (item == null)
                    throw new JsonSerializationException($"Invalid item name: {name}");
                return item;
            }

            public override bool CanConvert(Type objectType) => typeof(Item_Base).IsAssignableFrom(objectType);
        }

        private class MapItemConverter : JsonConverter
        {
            private static readonly Dictionary<string, HashSet<string>> ValidItemNamesByDropper;

            static MapItemConverter()
            {
                ValidItemNamesByDropper = new Dictionary<string, HashSet<string>>();
                var defaultDroppers = JsonUtils.Deserialize<JObject>(DropperManager.DefaultDroppers);
                foreach (var dropper in defaultDroppers.Properties())
                {
                    var dropperName = dropper.Name;
                    var items = dropper.Value["items"] as JObject;
                    var validItems = new HashSet<string>();
                    if (items != null)
                    {
                        foreach (var item in items.Properties())
                        {
                            validItems.Add(item.Name);
                        }
                    }
                    ValidItemNamesByDropper[dropperName] = validItems;
                }
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                if (value is System.Collections.IEnumerable obj)
                {
                    var it = obj.GetEnumerator();
                    if (it.MoveNext())
                    {
                        var keyAccess = it.Current.GetType().GetProperty("Key");
                        var valueAccess = it.Current.GetType().GetProperty("Value");
                        if (keyAccess != null && valueAccess != null)
                            foreach (var e in obj)
                                if (keyAccess.GetValue(e) is Item_Base key)
                                {
                                    writer.WritePropertyName(key.name);
                                    serializer.Serialize(writer, valueAccess.GetValue(e));
                                }
                    }
                }
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var valueType = objectType.GetGenericArguments()[1];
                var dict = Activator.CreateInstance(objectType);
                var add = objectType.GetMethod("Add");
                if (add == null) return dict;

                var jObject = JObject.Load(reader);
                var dropperName = GetCurrentDropperName(serializer);
                if (!ValidItemNamesByDropper.TryGetValue(dropperName, out var validItems))
                {
                    if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                        Debug.LogWarning($"[BetterRaft] No valid items defined for dropper {dropperName}. Skipping deserialization for this dropper's items in MapItemConverter.");
                    return dict;
                }

                foreach (var property in jObject.Properties())
                {
                    if (!validItems.Contains(property.Name))
                    {
                        if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                            Debug.LogWarning($"[BetterRaft] Item {property.Name} not valid for dropper {dropperName} according to DefaultDroppers definition. Skipping.");
                        continue;
                    }
                    var key = ItemUtils.BestItemMatch(property.Name);
                    if (key == null)
                    {
                        if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                            Debug.LogWarning($"[BetterRaft] Invalid item name (could not match): {property.Name} for dropper {dropperName}. Skipping.");
                        continue;
                    }
                    try
                    {
                        var value = property.Value.ToObject(valueType, serializer);
                        add.Invoke(dict, new object[] { key, value });
                    }
                    catch (Exception ex)
                    {
                        if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                            Debug.LogWarning($"[BetterRaft] Failed to add item {property.Name} with value {property.Value} for dropper {dropperName}: {ex.Message}. Skipping.");
                        continue;
                    }
                }
                return dict;
            }

            private string GetCurrentDropperName(JsonSerializer serializer)
            {
                if (serializer.Context.Context is JsonUtils.DropperContext dropperContext)
                {
                    return dropperContext.DropperName ?? "Unknown";
                }
                return "Unknown";
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                       objectType.GetGenericArguments()[0] == typeof(Item_Base);
            }
        }

        public class DropperContext
        {
            public string DropperName { get; set; }
        }
    }

    public static class ItemUtils
    {
        public static Item_Base BestItemMatch(string name)
        {
            var all = ItemManager.GetAllItems();
            var bestDist = int.MaxValue;
            Item_Base best = null;
            var tester = new SlidingLevenshtein(name.ToLower().Replace(' ', '_'));
            foreach (var item in all)
            {
                var t = tester.Distance(item.UniqueName.ToLower().Replace(' ', '_'));
                if (t < bestDist)
                {
                    best = item;
                    bestDist = t;
                }
                t = tester.Distance(item.settings_Inventory.DisplayName.ToLower().Replace(' ', '_'));
                if (t < bestDist)
                {
                    best = item;
                    bestDist = t;
                }
                if (t == 0)
                    break;
            }
            return bestDist <= name.Length / 2 ? best : null;
        }
    }

    public class SlidingLevenshtein
    {
        private readonly string _target;
        private readonly int[] _costs;

        public SlidingLevenshtein(string target)
        {
            _target = target ?? "";
            _costs = string.IsNullOrEmpty(_target) ? Array.Empty<int>() : new int[_target.Length];
        }

        public int Distance(string against)
        {
            if (string.IsNullOrEmpty(against))
                return _costs.Length == 0 ? 0 : int.MaxValue;
            if (_costs.Length == 0 || _costs.Length > against.Length)
                return int.MaxValue;

            int best = int.MaxValue;
            int delta = against.Length - _costs.Length;

            for (int w = 0; w <= delta; ++w)
            {
                string window = against.Substring(w, _costs.Length);
                for (int i = 0; i < _costs.Length; ++i)
                    _costs[i] = i;

                for (int i = 0; i < window.Length; i++)
                {
                    int topCost = i;
                    int previousCost = i;
                    char c = window[i];
                    for (int j = 0; j < _target.Length; j++)
                    {
                        int cost = topCost;
                        topCost = _costs[j];
                        if (c != _target[j])
                        {
                            if (previousCost < cost)
                                cost = previousCost;
                            if (topCost < cost)
                                cost = topCost;
                            ++cost;
                        }
                        _costs[j] = cost;
                        previousCost = cost;
                    }
                }

                if (_costs[_costs.Length - 1] >= best)
                    continue;
                best = _costs[_costs.Length - 1];
                if (best == 0)
                    break;
            }
            return best + delta / 2;
        }
    }

    [HarmonyPatch(typeof(RandomDropper))]
    public class DropperManager
    {

        public bool Logging => BetterLootAndUnderwaterResources.EnableGlobalLogging;
        public Dictionary<string, DropperEx> Extensions = new Dictionary<string, DropperEx>();
        private bool _active = false;
        public bool Active => _active;
        private readonly string _file;

        public static readonly string DefaultDroppers = @"{
    ""Pickup_Floating_Barrel"": {
        ""min"": 4,
        ""max"": 6,
        ""replace"": false,
        ""items"": {
            ""Empty_Bottle"": 1, ""Empty_Cup"": 1, ""Clay_Bowl"": 2, ""Drinking_Glass"": 1, ""Bucket"": 3, ""Nail"": 4,
            ""Seed_Mango"": 1, ""Seed_Pine"": 1, ""Seed_Birch"": 1, ""Seed_Pineapple"": 1, ""Seed_Watermelon"": 1,
            ""Seed_Strawberry"": 1, ""Seed_Banana"": 1, ""Seed_Palm"": 1, ""Healing_Salve"": 2, ""Good_Healing_Salve"": 1
        }
    },
    ""Pickup_Floating_Box"": {
        ""min"": 5, ""max"": 9, ""replace"": false,
        ""items"": {
            ""Sand"": 20, ""Clay"": 20, ""Vine_Goo"": 10, ""Empty_Bottle"": 10, ""Empty_Cup"": 20, ""Clay_Bowl"": 40,
            ""Drinking_Glass"": 40, ""Bucket"": 40, ""Bolt"": 25, ""Nail"": 25, ""Seed_Mango"": 10, ""Seed_Pine"": 10,
            ""Seed_Birch"": 10, ""Seed_Pineapple"": 10, ""Seed_Watermelon"": 10, ""Seed_Strawberry"": 10,
            ""Seed_Banana"": 10, ""Seed_Palm"": 10, ""Healing_Salve"": 15, ""Good_Healing_Salve"": 5
        }
    },
    ""Pickup_Landmark_LandmarkCrateRaft"": {
        ""min"": 8, ""max"": 12, ""replace"": false,
        ""items"": {
            ""Sand"": 10, ""Clay"": 10, ""Glass"": 10, ""Brick_Dry"": 5, ""MetalOre"": 5, ""MetalIngot"": 4, ""CopperOre"": 5,
            ""CopperIngot"": 4, ""Titanium_Ore"": 1, ""TitaniumIngot"": 1, ""VineGoo"": 40, ""SeaVine"": 20, ""Feather"": 30,
            ""Leather"": 30, ""Wool"": 20, ""Dirt"": 40, ""ExplosiveGoo"": 15, ""ExplosivePowder"": 5, ""Circuit_Board"": 10,
            ""Battery"": 10, ""BioFuel"": 15, ""Bolt"": 25, ""Hinge"": 25, ""Honeycomb"": 15, ""Jar_Honey"": 10,
            ""TradeToken"": 5, ""Trashcube"": 20, ""Axe"": 25, ""Stone_Axe"": 25, ""Axe_Titanium"": 3, ""Hammer"": 25,
            ""Hook_Plastic"": 25, ""Hook_Scrap"": 15, ""Hook_Titanium"": 3, ""Spear_Scrap"": 15, ""Spear_Plank"": 15,
            ""Arrow_Stone"": 25, ""Metal_Arrow"": 10, ""Titanium_Arrow"": 3, ""Fishing_Rod"": 25, ""Metal_Fishing_Rod"": 10,
            ""Paddle"": 25, ""PaintBrush"": 15, ""Shears"": 15, ""Shovel"": 15, ""SweepNet"": 10, ""NetCanister"": 5,
            ""NetGun"": 3, ""SharkBait"": 15, ""Machete"": 10, ""Metal_Detector"": 5, ""Sword_Titanium"": 1,
            ""ThrowableAnchor"": 5, ""Bow"": 10, ""Binoculars"": 10, ""Compass"": 10, ""Backpack"": 10,
            ""Equipment_LeatherChest"": 10, ""Equipment_LeatherHelmet"": 10, ""Equipment_LeatherLegs"": 10, ""Flippers"": 10,
            ""Oxygen_Bottle"": 10, ""Head_Lamp"": 10, ""Helmet"": 5, ""Body_Armor"": 5, ""Greaves"": 5, ""Hat_Captain"": 5,
            ""Hat_Chef"": 5, ""Hat_Construction"": 5, ""Hat_Dev"": 3, ""Hat_Diving"": 5, ""Hat_Fishing"": 5,
            ""Hat_Glasses_Aviator"": 3, ""Hat_Glasses_Disguise"": 3, ""Hat_Mayor"": 3, ""Hat_Pilot"": 3, ""Hat_Pirate"": 3,
            ""Hat_Sailor"": 3, ""Hat_Tiki"": 3, ""Canteen_Empty"": 15, ""PlasticBottle_Empty"": 15, ""PlasticCup_Empty"": 15,
            ""Bucket"": 15, ""Claybowl_Empty"": 15, ""DrinkingGlass"": 15, ""HealingSalve_Good"": 5, ""FishingBait_Simple"": 15,
            ""FishingBait_Advanced"": 10, ""FishingBait_Expert"": 5, ""Blueprint_Battery"": 10, ""Blueprint_Headlight"": 10,
            ""Blueprint_Machete"": 10, ""Blueprint_Engine"": 10, ""Blueprint_Steering_Wheel"": 10, ""Blueprint_Recycler"": 10,
            ""Blueprint_Electric_Purifier"": 10, ""Blueprint_Electric_Grill"": 10, ""Blueprint_Electric_Smelter"": 10,
            ""Blueprint_Titanium_Tools"": 5, ""Blueprint_Large_Backpack"": 5, ""Blueprint_Zipline"": 5,
            ""Blueprint_Wind_Turbine"": 5, ""Blueprint_Solar_Panel"": 5, ""Blueprint_Electric_Zipline_Tool"": 3,
            ""Blueprint_Zipline_Tool"": 5, ""Blueprint_Large_Storage"": 5, ""Blueprint_Water_Pipe"": 5,
            ""Blueprint_Fuel_Pipe"": 5, ""Blueprint_Metal_Detector"": 5, ""Blueprint_Fuel_Tank"": 5, ""Blueprint_Firework"": 5,
            ""Blueprint_Engine_Controls"": 5, ""Blueprint_Advanced_Small_Crop_Plot"": 5, ""Blueprint_Biofuel_Refiner"": 5,
            ""Blueprint_Battery_Charger"": 5, ""Blueprint_Advanced_Head_Light"": 3, ""Blueprint_Advanced_Medium_Crop_Plot"": 3,
            ""Blueprint_Advanced_Large_Crop_Plot"": 3, ""Blueprint_Canteen"": 5, ""Blueprint_Advanced_Battery"": 3,
            ""Blueprint_Advanced_Stationary_Anchor"": 3, ""Blueprint_Antenna"": 3, ""Blueprint_Biofuel_Extractor"": 3,
            ""Blueprint_Biofuel_Extractor_Advanced"": 1, ""Blueprint_DetailPlank"": 5, ""Blueprint_MotorWheel"": 3,
            ""Blueprint_Reciever"": 3, ""Blueprint_WaterTank"": 3, ""Blueprint_ZiplineBase"": 3, ""SilverAlgae"": 15,
            ""Cassette_Classical"": 5, ""Cassette_EDM"": 5, ""Cassette_Elevator"": 5, ""Cassette_Pop"": 5,
            ""Cassette_Rock"": 5, ""Cassette_TradingPost"": 5
        }
    }
}";

        public DropperManager(string file)
        {
            _file = file;
        }

        public void Load()
        {
            try
            {
                string filePath = FileUtils.ModDataPath(_file);
                if (!File.Exists(filePath))
                {
                    if (Logging)
                        Debug.LogWarning($"[BetterRaft] Dropper file {_file} not found. Creating with default content.");
                    FileUtils.CreateIfMissing(_file, DefaultDroppers);
                }

                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    if (Logging)
                        Debug.LogWarning($"[BetterRaft] Dropper file {_file} is empty. Using default droppers.");
                    json = DefaultDroppers;
                }

                var serializer = JsonSerializer.Create(JsonUtils.Settings);
                Extensions = new Dictionary<string, DropperEx>();
                var jObject = JObject.Parse(json);
                foreach (var property in jObject.Properties())
                {
                    serializer.Context = new System.Runtime.Serialization.StreamingContext(
                        System.Runtime.Serialization.StreamingContextStates.All,
                        new JsonUtils.DropperContext { DropperName = property.Name });
                    try
                    {
                        var dropperEx = property.Value.ToObject<DropperEx>(serializer);
                        Extensions[property.Name] = dropperEx;
                    }
                    catch (Exception ex)
                    {
                        if (Logging)
                            Debug.LogWarning($"[BetterRaft] Failed to deserialize dropper {property.Name}: {ex.Message}. Skipping.");
                    }
                }
                serializer.Context = new System.Runtime.Serialization.StreamingContext(System.Runtime.Serialization.StreamingContextStates.All, null);

                if (Extensions.Count == 0 && !string.IsNullOrWhiteSpace(json) && json != "{}")
                {
                    if (Logging)
                        Debug.LogError($"[BetterRaft] Failed to deserialize any droppers from {_file}. Check format. Initializing with default droppers from code.");
                    Extensions = JsonUtils.Deserialize<Dictionary<string, DropperEx>>(DefaultDroppers) ?? new Dictionary<string, DropperEx>();
                }
                else if (Extensions.Count == 0)
                {
                    if (Logging)
                        Debug.LogWarning($"[BetterRaft] No droppers loaded from {_file} (file might be empty or contain no valid entries). Using default droppers from code.");
                    Extensions = JsonUtils.Deserialize<Dictionary<string, DropperEx>>(DefaultDroppers) ?? new Dictionary<string, DropperEx>();
                }
            }
            catch (JsonException ex)
            {
                if (Logging)
                    Debug.LogError($"[BetterRaft] JSON Error deserializing {_file}: {ex.Message}. Using default droppers.");
                Extensions = JsonUtils.Deserialize<Dictionary<string, DropperEx>>(DefaultDroppers) ?? new Dictionary<string, DropperEx>();
            }
            catch (Exception ex)
            {
                if (Logging)
                    Debug.LogError($"[BetterRaft] Unexpected error loading {_file}: {ex.Message}. Using default droppers.");
                Extensions = JsonUtils.Deserialize<Dictionary<string, DropperEx>>(DefaultDroppers) ?? new Dictionary<string, DropperEx>();
            }
        }

        public void Enable(bool state)
        {
            _active = state;
        }

        public class DropperEx
        {
            public int? min;
            public int? max;
            public bool? replace;
            public Dictionary<Item_Base, float> items;

            public Item_Base[] Patch(RandomDropper dropper)
            {
                var dr = Traverse.Create(dropper).Field("amountOfItems").GetValue<Interval_Int>();
                var range = new Interval_Int
                {
                    minValue = min ?? dr.minValue,
                    maxValue = max ?? dr.maxValue
                };
                int itemCount = range.GetRandomValue();
                List<Item_Base> result = new List<Item_Base>();
                var drops = Traverse.Create(dropper).Field("randomDropperAsset").GetValue<SO_RandomDropper>();

                if (replace == true)
                {
                    foreach (var item in items)
                    {
                        if (UnityEngine.Random.value * 100f < item.Value)
                        {
                            result.Add(item.Key);
                        }
                    }
                }
                else
                {
                    var defaultItems = drops.randomizer.GetRandomItems<Item_Base>(itemCount);
                    result.AddRange(defaultItems);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (UnityEngine.Random.value * 100f < item.Value)
                            {
                                result.Add(item.Key);
                            }
                        }
                    }
                }

                while (result.Count > itemCount)
                {
                    result.RemoveAt(UnityEngine.Random.Range(0, result.Count));
                }

                return result.ToArray();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetRandomItems")]
        static bool Prefix_GetRandomItems(RandomDropper __instance, ref Item_Base[] __result)
        {

            if (BetterLootAndUnderwaterResources.DropperManager.Logging)
                Debug.Log($"[BetterRaft] Processing dropper: {__instance.gameObject.name}. Serialized state: {JsonUtils.Serialize(DropperManager.FromDropper(__instance))}");


            if (!BetterLootAndUnderwaterResources.DropperManager.Active || __instance == null || __instance.gameObject == null || __instance.gameObject.GetInstanceID() == 0)
                return true;


            foreach (var p in BetterLootAndUnderwaterResources.DropperManager.Extensions)
            {
                if (__instance.gameObject.name.Contains(p.Key))
                {
                    var t = p.Value.Patch(__instance);
                    if (t == null) continue;


                    if (BetterLootAndUnderwaterResources.DropperManager.Logging)
                    {
                        foreach (var item in t)
                            Debug.Log($"[BetterRaft] Custom drop from {__instance.gameObject.name} ({p.Key}): {item.UniqueName}");
                    }
                    __result = t;
                    return false;
                }
            }
            return true;
        }

        public static DropperEx FromDropper(RandomDropper dropper)
        {
            var o = new DropperEx();
            var range = Traverse.Create(dropper).Field("amountOfItems").GetValue<Interval_Int>();
            o.min = range.minValue;
            o.max = range.maxValue;
            o.replace = true;
            o.items = new Dictionary<Item_Base, float>();
            var drops = Traverse.Create(dropper).Field("randomDropperAsset").GetValue<SO_RandomDropper>();
            if (drops != null && drops.randomizer != null && drops.randomizer.items != null)
            {
                foreach (var ri in drops.randomizer.items)
                    if (ri.obj is Item_Base itemBase)
                        o.items.Add(itemBase, ri.weight);
            }
            return o;
        }
    }

    public static class Commands
    {
        [ConsoleCommand(name: "lootControl_toggleCustomDroppers")]
        public static void ToggleCustomDroppers(string[] args)
        {

            BetterLootAndUnderwaterResources.DropperManager.Enable(!BetterLootAndUnderwaterResources.DropperManager.Active);

            if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                Debug.Log("[BetterRaft] Custom Drops: " + (BetterLootAndUnderwaterResources.DropperManager.Active ? "Enabled" : "Disabled"));
        }

        [ConsoleCommand(name: "lootControl_reloadCustomDroppers")]
        public static void ReloadCustomDroppers(string[] args)
        {

            var active = BetterLootAndUnderwaterResources.DropperManager.Active;

            BetterLootAndUnderwaterResources.DropperManager.Load();

            BetterLootAndUnderwaterResources.DropperManager.Enable(active);

            if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                Debug.Log("[BetterRaft] Custom droppers reloaded.");
        }

        [ConsoleCommand(name: "lootControl_logRandomDroppers")]
        public static void LogRandomDroppers(string[] args)
        {

            BetterLootAndUnderwaterResources.EnableGlobalLogging = !BetterLootAndUnderwaterResources.EnableGlobalLogging;

            Debug.Log("[BetterRaft] Global loot logger: " + (BetterLootAndUnderwaterResources.EnableGlobalLogging ? "Enabled" : "Disabled"));
        }

        [ConsoleCommand(name: "lootControl_searchItem")]
        public static void SearchItem(string[] args)
        {
            if (args.Length == 0)
            {

                if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                    Debug.Log("[BetterRaft] lootControl_searchItem: requires at least one item name part.");
            }
            else
            {
                foreach (var n in args)
                {
                    var f = ItemUtils.BestItemMatch(n);

                    if (BetterLootAndUnderwaterResources.EnableGlobalLogging)
                    {
                        if (f != null)
                            Debug.Log($"[BetterRaft] Item search for '{n}': Found '{f.UniqueName}' (Display: '{f.settings_Inventory.DisplayName}')");
                        else
                            Debug.Log($"[BetterRaft] Item search for '{n}': Could not find item!");
                    }
                }
            }
        }
    }
}