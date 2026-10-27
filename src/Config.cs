using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>Shared JSON (de)serialisation for every file the plugin keeps under tshock/CalamityShock/.</summary>
    public static class JsonStore
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static string Directory => Path.Combine(TShock.SavePath, "CalamityShock");

        public static string PathFor(string fileName) => Path.Combine(Directory, fileName);

        /// <summary>Loads a file, or writes the default when it does not exist yet. Never throws; a broken file is reported and the default used.</summary>
        public static T LoadOrCreate<T>(string fileName, Func<T> makeDefault) where T : class => LoadOrCreate(fileName, makeDefault, out _);

        /// <param name="loadedFromFile">True when the returned object came from an existing, readable file (safe to re-save).</param>
        public static T LoadOrCreate<T>(string fileName, Func<T> makeDefault, out bool loadedFromFile) where T : class
        {
            string path = PathFor(fileName);
            loadedFromFile = false;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    T loaded = JsonSerializer.Deserialize<T>(json, Options);
                    if (loaded != null) { loadedFromFile = true; return loaded; }
                    Log.Warn($"{fileName} was empty; using defaults.");
                }
                T def = makeDefault();
                Save(fileName, def);
                Log.Info($"Wrote default {fileName}");
                return def;
            }
            catch (Exception ex)
            {
                Log.Error($"Could not read {fileName} ({ex.Message}). Using built-in defaults; the file was left untouched.");
                return makeDefault();
            }
        }

        public static void Save<T>(string fileName, T value)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(PathFor(fileName), JsonSerializer.Serialize(value, Options));
            }
            catch (Exception ex)
            {
                Log.Error($"Could not write {fileName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// One difficulty mode. The numbers feed Terraria 1.4.5's own per-NPC "difficulty" value,
    /// which the game syncs to clients: Classic = 1, Expert = 2, Master = 3, Legendary = 4, and the curve
    /// keeps going past that. Enemy max life scales 1:1 with it; enemy damage scales a little faster.
    /// </summary>
    public class ModeSettings
    {
        /// <summary>Shown to players.</summary>
        public string DisplayName { get; set; } = "Normal";

        /// <summary>Order of the modes, used for "this loot only drops in Revengeance or above".</summary>
        public int Rank { get; set; } = 0;

        /// <summary>Added to the world's difficulty for every hostile non-boss NPC (0.5 = half a difficulty tier).</summary>
        public float EnemyDifficultyBonus { get; set; } = 0f;

        /// <summary>Added to the world's difficulty for bosses (vanilla ones, and the base of custom ones).</summary>
        public float BossDifficultyBonus { get; set; } = 0f;

        /// <summary>Server-side defense multiplier for regular enemies (not synced; affects real damage taken).</summary>
        public float EnemyDefenseMultiplier { get; set; } = 1f;

        /// <summary>Server-side defense multiplier for bosses.</summary>
        public float BossDefenseMultiplier { get; set; } = 1f;

        /// <summary>Extra movement for flying bosses listed in MovementBoostNpcs (1.0 = vanilla speed).</summary>
        public float BossMovementSpeedMultiplier { get; set; } = 1f;

        /// <summary>Boss AI steps per tick (1.0 = vanilla; 1.5 = an extra AI step every other tick, so attacks come faster).</summary>
        public float BossAiSpeed { get; set; } = 1f;

        /// <summary>Seconds after which a boss enrages. 0 = never.</summary>
        public int BossEnrageSeconds { get; set; } = 0;

        /// <summary>When enraged, custom attack intervals are divided by this.</summary>
        public float EnrageAttackRateMultiplier { get; set; } = 1.5f;

        /// <summary>When enraged, movement multiplier is multiplied by this.</summary>
        public float EnrageMovementMultiplier { get; set; } = 1.2f;

        /// <summary>Whether vanilla bosses get the extra attack patterns defined in bosses.json ("VanillaAugments").</summary>
        public bool VanillaBossAugments { get; set; } = false;

        /// <summary>Extra items every boss drops in this mode (on top of its normal loot).</summary>
        public List<LootEntry> ExtraBossLoot { get; set; } = new List<LootEntry>();

        /// <summary>
        /// Rage and Adrenaline meters are available in this mode (Calamity: Revengeance and up).
        /// Leave it out (null) and it follows the mode's Rank: on for Rank 1 and above.
        /// </summary>
        public bool? RageAdrenalineEnabled { get; set; } = null;

        [JsonIgnore] public bool MetersEnabled => RageAdrenalineEnabled ?? Rank >= 1;
    }

    public class CalamityConfig
    {
        /// <summary>Which entry of Modes is active. Changed with /cs mode &lt;name&gt;.</summary>
        public string ActiveMode { get; set; } = "normal";

        public Dictionary<string, ModeSettings> Modes { get; set; } = new Dictionary<string, ModeSettings>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Apply the mode's EnemyDifficultyBonus to naturally spawned enemies.</summary>
        public bool ScaleEnemies { get; set; } = true;

        /// <summary>Apply the mode's BossDifficultyBonus / speed / enrage to vanilla bosses.</summary>
        public bool ScaleVanillaBosses { get; set; } = true;

        /// <summary>Tell players the active mode when they join.</summary>
        public bool AnnounceModeOnJoin { get; set; } = true;

        /// <summary>Broadcast when a boss changes phase or enrages.</summary>
        public bool AnnouncePhases { get; set; } = true;

        /// <summary>If true, /cs summon requires the permission calamityshock.summon. Otherwise anyone may summon (progression gating still applies).</summary>
        public bool RequireSummonPermission { get; set; } = false;

        /// <summary>If true, /cs craft requires the permission calamityshock.craft.</summary>
        public bool RequireCraftPermission { get; set; } = false;

        /// <summary>Crafting with server-side characters off takes ingredients from items lying on the ground within this many tiles of the player.</summary>
        public int GroundCraftRadiusTiles { get; set; } = 8;

        /// <summary>How far (tiles) a crafting station may be from the player.</summary>
        public int StationRadiusTiles { get; set; } = 6;

        /// <summary>
        /// false (default): killing a custom boss does NOT count as killing the vanilla boss it is built on (the vanilla
        /// "downed" flag is restored after the kill). true: let the vanilla flag flip, as vanilla would.
        /// </summary>
        public bool CustomBossesSetVanillaFlags { get; set; } = false;

        /// <summary>Enables /cs debug (fake player for testing bosses on an empty server). Leave off on a real server.</summary>
        public bool EnableDebugCommands { get; set; } = false;

        /// <summary>If false, /cs event start needs calamityshock.event (admins always can).</summary>
        public bool AnyoneCanStartEvents { get; set; } = true;

        /// <summary>Rage, Adrenaline, Rogue class and HUD settings.</summary>
        public PlayerSettings Players { get; set; } = new PlayerSettings();

        /// <summary>/cs arena build settings.</summary>
        public ArenaSettings Arena { get; set; } = new ArenaSettings();


        /// <summary>
        /// Bosses whose movement is sped up by BossMovementSpeedMultiplier. Only use flying / no-tile-collision bosses here.
        /// Multi-part bosses (Moon Lord, Golem, Wall of Flesh) must stay out of this list.
        /// </summary>
        public List<string> MovementBoostNpcs { get; set; } = new List<string>
        {
            "EyeofCthulhu", "BrainofCthulhu", "QueenBee", "SkeletronHead", "Retinazer", "Spazmatism",
            "SkeletronPrime", "TheDestroyer", "EaterofWorldsHead", "DukeFishron", "HallowBoss", "CultistBoss",
            "QueenSlimeBoss", "IceQueen", "Pumpking", "MourningWood"
        };

        /// <summary>
        /// Gate vanilla boss spawns behind custom progression, e.g. "Skeletron": ["hivemind"] stops Skeletron from
        /// spawning until the Hive Mind is down. Keys are vanilla NPC names, values are boss ids (custom) or vanilla flags
        /// such as "EyeOfCthulhu" (see /cs checklist for the accepted vanilla flag names).
        /// </summary>
        public Dictionary<string, List<string>> VanillaBossGates { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static CalamityConfig CreateDefault()
        {
            var cfg = new CalamityConfig();
            cfg.Modes["normal"] = new ModeSettings
            {
                DisplayName = "Normal",
                Rank = 0,
            };
            // Difficulty numbers are added to the world's own value (Expert = 2). Each +1 is roughly "one tier":
            // max life x1.5 relative to Expert at +1, and contact damage x1.5 - the damage curve is steeper above Master,
            // so keep bonuses modest and use PlayerCountOverride on a boss when you want more HP without more damage.
            cfg.Modes["revengeance"] = new ModeSettings
            {
                DisplayName = "Revengeance",
                Rank = 1,
                EnemyDifficultyBonus = 0.25f,
                BossDifficultyBonus = 0.5f,
                EnemyDefenseMultiplier = 1.1f,
                BossDefenseMultiplier = 1.15f,
                BossMovementSpeedMultiplier = 1.15f,
                BossAiSpeed = 1.2f,
                BossEnrageSeconds = 600,
                EnrageAttackRateMultiplier = 1.5f,
                EnrageMovementMultiplier = 1.2f,
                VanillaBossAugments = true,
                RageAdrenalineEnabled = true,
                ExtraBossLoot = new List<LootEntry>
                {
                    new LootEntry { Item = "LifeCrystal", MinStack = 1, MaxStack = 1, Chance = 0.5f },
                    new LootEntry { Item = "GoldCoin", MinStack = 5, MaxStack = 15, Chance = 1f },
                }
            };
            cfg.Modes["death"] = new ModeSettings
            {
                DisplayName = "Death",
                Rank = 2,
                EnemyDifficultyBonus = 0.5f,
                BossDifficultyBonus = 1.0f,
                EnemyDefenseMultiplier = 1.2f,
                BossDefenseMultiplier = 1.3f,
                BossMovementSpeedMultiplier = 1.3f,
                BossAiSpeed = 1.5f,
                BossEnrageSeconds = 360,
                EnrageAttackRateMultiplier = 2f,
                EnrageMovementMultiplier = 1.3f,
                VanillaBossAugments = true,
                RageAdrenalineEnabled = true,
                ExtraBossLoot = new List<LootEntry>
                {
                    new LootEntry { Item = "LifeCrystal", MinStack = 1, MaxStack = 2, Chance = 1f },
                    new LootEntry { Item = "PlatinumCoin", MinStack = 1, MaxStack = 1, Chance = 1f },
                }
            };
            cfg.VanillaBossGates["SkeletronHead"] = new List<string> { "desertscourge" };
            return cfg;
        }

        /// <summary>System.Text.Json recreates dictionaries with the default comparer; make the lookups case-insensitive again.</summary>
        public void AfterLoad()
        {
            var modes = new Dictionary<string, ModeSettings>(StringComparer.OrdinalIgnoreCase);
            if (Modes != null)
                foreach (var kv in Modes)
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null) modes[kv.Key] = kv.Value;
                    else Log.Warn($"config.json: mode '{kv.Key}' is empty and was ignored");
            Modes = modes;
            VanillaBossGates = new Dictionary<string, List<string>>(VanillaBossGates ?? new Dictionary<string, List<string>>(), StringComparer.OrdinalIgnoreCase);
            MovementBoostNpcs ??= new List<string>();
            Players ??= new PlayerSettings();
            Arena ??= new ArenaSettings();
            if (!Modes.ContainsKey("normal")) Modes["normal"] = new ModeSettings();
            foreach (var m in Modes.Values) m.ExtraBossLoot ??= new List<LootEntry>();
            if (ActiveMode == null || !Modes.ContainsKey(ActiveMode)) ActiveMode = "normal";
        }

        /// <summary>True when the active mode's rank is at least the named mode's rank ("" = always).</summary>
        public bool ModeAtLeast(string minMode)
        {
            if (string.IsNullOrWhiteSpace(minMode)) return true;
            int need = RankOf(minMode);
            return need < 0 || CurrentMode.Rank >= need;
        }

        /// <summary>The active mode, falling back to "normal" (or a blank mode) if the config names a mode that doesn't exist.</summary>
        [JsonIgnore]
        public ModeSettings CurrentMode
        {
            get
            {
                if (Modes != null && ActiveMode != null && Modes.TryGetValue(ActiveMode, out var m) && m != null) return m;
                if (Modes != null && Modes.TryGetValue("normal", out var n) && n != null) return n;
                return new ModeSettings();
            }
        }

        /// <summary>Rank of a mode name, or -1 when unknown. Used by loot entries with MinMode.</summary>
        public int RankOf(string modeName)
        {
            if (string.IsNullOrEmpty(modeName)) return -1;
            return Modes != null && Modes.TryGetValue(modeName, out var m) && m != null ? m.Rank : -1;
        }
    }
}
