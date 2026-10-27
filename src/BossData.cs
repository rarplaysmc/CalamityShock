using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Terraria;

namespace CalamityShock
{
    /// <summary>One line of a loot table.</summary>
    public class LootEntry
    {
        /// <summary>Item name ("LifeCrystal") or numeric id.</summary>
        public string Item { get; set; } = "";
        public int MinStack { get; set; } = 1;
        public int MaxStack { get; set; } = 1;
        /// <summary>0..1</summary>
        public float Chance { get; set; } = 1f;
        /// <summary>0 = no prefix, -1 = random prefix, or a prefix id.</summary>
        public int Prefix { get; set; } = 0;
        /// <summary>Only drops when the active mode's Rank is at least this mode's Rank, e.g. "revengeance". Empty = always.</summary>
        public string MinMode { get; set; } = "";

        [JsonIgnore] public int ItemId;
    }

    /// <summary>A repeating projectile attack a boss fires while a phase is active.</summary>
    public class AttackPattern
    {
        /// <summary>Projectile name ("SandBallFalling") or id. Must be hostile by default to hurt players.</summary>
        public string Projectile { get; set; } = "";

        /// <summary>
        /// aimed  - Count projectiles fanned around the direction to the nearest player (SpreadDegrees wide)
        /// ring   - Count projectiles evenly around the boss
        /// rain   - Count projectiles spawned above the target (RainWidth wide, RainHeight up) falling at Speed
        /// random - Count projectiles in random directions
        /// </summary>
        public string Pattern { get; set; } = "aimed";
        public int Count { get; set; } = 1;
        public float Speed { get; set; } = 8f;
        /// <summary>Base damage; the client scales hostile projectile damage by the world difficulty (x2 Expert, x3 Master).</summary>
        public int Damage { get; set; } = 20;
        public float Knockback { get; set; } = 2f;
        public float SpreadDegrees { get; set; } = 30f;
        /// <summary>Ticks between volleys (60 = one second).</summary>
        public int IntervalTicks { get; set; } = 120;
        /// <summary>Ticks to wait after the phase starts before the first volley.</summary>
        public int StartDelayTicks { get; set; } = 60;
        /// <summary>Volleys per trigger, spaced BurstGapTicks apart.</summary>
        public int BurstCount { get; set; } = 1;
        public int BurstGapTicks { get; set; } = 8;
        public int RainWidth { get; set; } = 900;
        public int RainHeight { get; set; } = 700;
        /// <summary>Offset from the boss centre where projectiles appear.</summary>
        public float OffsetX { get; set; } = 0f;
        public float OffsetY { get; set; } = 0f;
        /// <summary>Passed into the projectile's ai[0] / ai[1] (rarely needed).</summary>
        public float Ai0 { get; set; } = 0f;
        public float Ai1 { get; set; } = 0f;
        /// <summary>Only fire when a player is within this many pixels (0 = always).</summary>
        public float MaxRange { get; set; } = 2400f;

        [JsonIgnore] public int ProjectileId;
    }

    /// <summary>NPCs a boss brings along.</summary>
    public class MinionSpawn
    {
        /// <summary>NPC name or id.</summary>
        public string Npc { get; set; } = "";
        public int Count { get; set; } = 1;
        /// <summary>0 = once when the phase starts; otherwise repeat every N ticks.</summary>
        public int IntervalTicks { get; set; } = 0;
        /// <summary>Don't spawn more while this many of this NPC type are alive.</summary>
        public int MaxAlive { get; set; } = 8;
        /// <summary>Spawn radius around the boss in pixels.</summary>
        public int SpreadPixels { get; set; } = 160;
        /// <summary>true = the minion gets the boss's difficulty instead of the mode's enemy bonus.</summary>
        public bool InheritBossDifficulty { get; set; } = false;
        public string Message { get; set; } = "";

        [JsonIgnore] public int NpcId;
    }

    public class BossPhase
    {
        /// <summary>Phase activates once the boss's health is at or below this percent. The first phase should be 100.</summary>
        public float HealthBelowPercent { get; set; } = 100f;
        public string Message { get; set; } = "";
        public List<AttackPattern> Attacks { get; set; } = new List<AttackPattern>();
        public List<MinionSpawn> Minions { get; set; } = new List<MinionSpawn>();
        /// <summary>0 = inherit the boss/mode value.</summary>
        public float MovementSpeedMultiplier { get; set; } = 0f;
        /// <summary>0 = inherit the boss/mode value.</summary>
        public float AiSpeed { get; set; } = 0f;
    }

    public class BossDefinition
    {
        /// <summary>Short id used in commands and progression ("desertscourge").</summary>
        public string Id { get; set; } = "";
        /// <summary>Name shown to players. Empty = keep the vanilla name (used for vanilla augments).</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>Vanilla NPC the boss is built from. Use a real boss type so the boss health bar and player-count scaling work.</summary>
        public string BaseNpc { get; set; } = "";
        /// <summary>
        /// All NPC types that are "part of" this boss and should get its difficulty: worm segments, Creepers, Moon Lord parts...
        /// For worm bosses every segment type must be listed so health and death are tracked across the whole worm.
        /// </summary>
        public List<string> SegmentNpcs { get; set; } = new List<string>();

        /// <summary>Absolute difficulty (1 Classic, 2 Expert, 3 Master, 4 Legendary, higher allowed). null = world + mode bonus + DifficultyBonus.</summary>
        public float? Difficulty { get; set; } = null;
        /// <summary>Added on top of world difficulty + mode boss bonus when Difficulty is null.</summary>
        public float DifficultyBonus { get; set; } = 0f;
        /// <summary>Bosses get more health per player in Expert+; this overrides the player count used for that (0 = real count).</summary>
        public int PlayerCountOverride { get; set; } = 0;
        /// <summary>Server-side defense multiplier (stacked on the mode's boss multiplier).</summary>
        public float DefenseMultiplier { get; set; } = 1f;
        /// <summary>Movement multiplier for no-tile-collision bases (1 = vanilla, 0 = use mode value).</summary>
        public float MovementSpeedMultiplier { get; set; } = 0f;
        /// <summary>AI steps per tick (1 = vanilla, 0 = use mode value).</summary>
        public float AiSpeed { get; set; } = 0f;
        /// <summary>Seconds until enrage. 0 = mode default, -1 = never.</summary>
        public int EnrageSeconds { get; set; } = 0;

        /// <summary>
        /// Server-side tweaks applied while the base NPC's vanilla AI runs, so a boss built on it doesn't flee from
        /// the wrong biome or time of day:
        ///   InEvil, InJungle, InSnow, InHallow, InDesert, InUndergroundDesert, InBeach, InDungeon, InMushroom, InSky
        ///     - every player counts as being in that biome (Eater of Worlds and Brain of Cthulhu leave otherwise)
        ///   Night, Day          - the AI believes it is night/day (Ice Queen, Pumpking, Eye of Cthulhu, Skeletron flee at dawn)
        ///   FrostMoon, PumpkinMoon - the AI believes the event is running
        /// </summary>
        public List<string> AiPatches { get; set; } = new List<string>();

        /// <summary>Boss ids or vanilla flags that must be downed before this boss can be summoned.</summary>
        public List<string> RequiresDowned { get; set; } = new List<string>();
        /// <summary>Required biome: Desert, Jungle, Corruption, Crimson, Mushroom, Snow, Dungeon, Hallow, Underworld, Ocean, Sky, Underground, Any.</summary>
        public string RequiredBiome { get; set; } = "Any";
        public bool RequireNight { get; set; } = false;
        public bool RequireDay { get; set; } = false;
        public bool RequireHardmode { get; set; } = false;
        /// <summary>Item the summoner must hold (consumed only when server-side characters are on). Empty = none.</summary>
        public string SummonItem { get; set; } = "";
        public int SummonItemStack { get; set; } = 1;

        public string SpawnMessage { get; set; } = "{name} has awoken!";
        public string DefeatMessage { get; set; } = "{name} has been defeated!";
        public string EnrageMessage { get; set; } = "{name} has become enraged!";

        public List<BossPhase> Phases { get; set; } = new List<BossPhase>();

        /// <summary>true = not listed and not summonable with /cs summon; used for event bosses.</summary>
        public bool Hidden { get; set; } = false;

        /// <summary>Items dropped when the boss dies.</summary>
        public List<LootEntry> Loot { get; set; } = new List<LootEntry>();
        /// <summary>true = the base NPC's own loot (and treasure bag) is suppressed and only Loot drops.</summary>
        public bool ReplaceVanillaLoot { get; set; } = false;

        // ---- resolved at load time ----
        [JsonIgnore] public int BaseNpcId = -1;
        [JsonIgnore] public HashSet<int> SegmentIds = new HashSet<int>();
        [JsonIgnore] public int SummonItemId = 0;
        [JsonIgnore] public bool IsVanillaAugment = false;

        public string NameOrVanilla => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Ids.NpcName(BaseNpcId);

        public string Format(string template) => (template ?? "").Replace("{name}", NameOrVanilla);

        /// <summary>Resolve names to ids and sort phases. Returns false if the definition is unusable.</summary>
        public bool Resolve(string context)
        {
            if (!Ids.TryNpc(BaseNpc, out BaseNpcId) || BaseNpcId <= 0)
            {
                Log.Warn($"{context}: unknown BaseNpc '{BaseNpc}' - boss disabled");
                return false;
            }
            SegmentIds = Ids.ResolveNpcSet(SegmentNpcs, context);
            SegmentIds.Add(BaseNpcId);
            if (!string.IsNullOrWhiteSpace(SummonItem) && !Ids.TryItem(SummonItem, out SummonItemId))
            {
                Log.Warn($"{context}: unknown SummonItem '{SummonItem}' - requirement ignored");
                SummonItemId = 0;
            }
            Phases ??= new List<BossPhase>();
            Phases.RemoveAll(ph => ph == null);
            Phases.Sort((a, b) => b.HealthBelowPercent.CompareTo(a.HealthBelowPercent));
            foreach (var phase in Phases)
            {
                phase.Attacks ??= new List<AttackPattern>();
                phase.Attacks.RemoveAll(a => a == null);
                phase.Minions ??= new List<MinionSpawn>();
                phase.Minions.RemoveAll(m => m == null);
                phase.Attacks.RemoveAll(a =>
                {
                    if (!Ids.TryProjectile(a.Projectile, out a.ProjectileId) || a.ProjectileId <= 0)
                    {
                        Log.Warn($"{context}: unknown projectile '{a.Projectile}' - attack removed");
                        return true;
                    }
                    if (!Net.IsHostileProjectile(a.ProjectileId))
                        Log.Warn($"{context}: projectile '{a.Projectile}' is not hostile by default and will not hurt players");
                    if (a.IntervalTicks < 5) a.IntervalTicks = 5;
                    if (a.Count < 1) a.Count = 1;
                    if (a.BurstCount < 1) a.BurstCount = 1;
                    return false;
                });
                phase.Minions.RemoveAll(m =>
                {
                    if (!Ids.TryNpc(m.Npc, out m.NpcId) || m.NpcId == 0)
                    {
                        Log.Warn($"{context}: unknown minion NPC '{m.Npc}' - removed");
                        return true;
                    }
                    if (m.Count < 1) m.Count = 1;
                    return false;
                });
            }
            Loot ??= new List<LootEntry>();
            Loot.RemoveAll(l => l == null);
            Loot.RemoveAll(l =>
            {
                if (!Ids.TryItem(l.Item, out l.ItemId) || l.ItemId <= 0)
                {
                    Log.Warn($"{context}: unknown loot item '{l.Item}' - removed");
                    return true;
                }
                return false;
            });
            RequiresDowned ??= new List<string>();
            RequiresDowned.RemoveAll(r => r == null);
            AiPatches ??= new List<string>();
            AiPatches.RemoveAll(a => a == null);
            foreach (string patch in AiPatches)
                if (!AiPatch.IsKnown(patch)) Log.Warn($"{context}: unknown AiPatch '{patch}' (ignored)");
            return true;
        }
    }

    /// <summary>Applies/undoes the AiPatches of a boss around a vanilla AI call. See BossDefinition.AiPatches.</summary>
    public sealed class AiPatch : IDisposable
    {
        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "InEvil", "InJungle", "InSnow", "InHallow", "InDesert", "InUndergroundDesert", "InBeach", "InDungeon", "InMushroom", "InSky",
            "Night", "Day", "FrostMoon", "PumpkinMoon"
        };

        public static bool IsKnown(string name) => !string.IsNullOrWhiteSpace(name) && Known.Contains(name.Trim());

        private readonly List<string> _patches;
        private readonly List<(Player player, byte z1, byte z2, byte z3, byte z4, byte z5)> _zones = new List<(Player, byte, byte, byte, byte, byte)>();
        private readonly bool _dayTime = Main.dayTime, _snowMoon = Main.snowMoon, _pumpkinMoon = Main.pumpkinMoon;

        public AiPatch(List<string> patches)
        {
            _patches = patches;
            if (patches == null || patches.Count == 0) return;
            bool anyZone = false;
            foreach (string p in patches)
            {
                switch (p.Trim().ToLowerInvariant())
                {
                    case "night": Main.dayTime = false; break;
                    case "day": Main.dayTime = true; break;
                    case "frostmoon": Main.snowMoon = true; break;
                    case "pumpkinmoon": Main.pumpkinMoon = true; break;
                    default: anyZone = true; break;
                }
            }
            if (!anyZone) return;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player pl = Main.player[i];
                if (pl == null || !pl.active) continue;
                _zones.Add((pl, pl.zone1, pl.zone2, pl.zone3, pl.zone4, pl.zone5));
                foreach (string p in patches)
                {
                    switch (p.Trim().ToLowerInvariant())
                    {
                        case "inevil": pl.ZoneCorrupt = true; pl.ZoneCrimson = true; break;
                        case "injungle": pl.ZoneJungle = true; break;
                        case "insnow": pl.ZoneSnow = true; break;
                        case "inhallow": pl.ZoneHallow = true; break;
                        case "indesert": pl.ZoneDesert = true; break;
                        case "inundergrounddesert": pl.ZoneUndergroundDesert = true; break;
                        case "inbeach": pl.ZoneBeach = true; break;
                        case "indungeon": pl.ZoneDungeon = true; break;
                        case "inmushroom": pl.ZoneGlowshroom = true; break;
                        case "insky": pl.ZoneSkyHeight = true; break;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_patches == null || _patches.Count == 0) return;
            Main.dayTime = _dayTime;
            Main.snowMoon = _snowMoon;
            Main.pumpkinMoon = _pumpkinMoon;
            foreach (var z in _zones)
            {
                z.player.zone1 = z.z1;
                z.player.zone2 = z.z2;
                z.player.zone3 = z.z3;
                z.player.zone4 = z.z4;
                z.player.zone5 = z.z5;
            }
        }
    }

    /// <summary>bosses.json</summary>
    public class BossCatalog
    {
        /// <summary>Custom bosses summoned with /cs summon &lt;id&gt;.</summary>
        public List<BossDefinition> Bosses { get; set; } = new List<BossDefinition>();

        /// <summary>
        /// Extra phases/attacks for vanilla bosses, keyed by NPC name. Only used when the active mode has VanillaBossAugments = true.
        /// Worm bosses need SegmentNpcs so the whole worm counts as one boss.
        /// </summary>
        public Dictionary<string, BossDefinition> VanillaAugments { get; set; } = new Dictionary<string, BossDefinition>(StringComparer.OrdinalIgnoreCase);

        public static BossCatalog CreateDefault()
        {
            var cat = new BossCatalog();

            // ------------------------------------------------------------------ custom bosses
            cat.Bosses.Add(new BossDefinition
            {
                Id = "desertscourge",
                DisplayName = "Desert Scourge",
                BaseNpc = "EaterofWorldsHead",
                AiPatches = new List<string> { "InEvil" },
                SegmentNpcs = new List<string> { "EaterofWorldsHead", "EaterofWorldsBody", "EaterofWorldsTail" },
                DifficultyBonus = 0.25f,
                PlayerCountOverride = 0,
                MovementSpeedMultiplier = 1.25f,
                RequiredBiome = "Desert",
                RequiresDowned = new List<string>(),
                SpawnMessage = "The Desert Scourge bursts out of the sand!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "SandBallFalling", Pattern = "rain", Count = 6, Speed = 9, Damage = 18, IntervalTicks = 150, RainWidth = 700, RainHeight = 600 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 60,
                        Message = "The Desert Scourge whips up a sandstorm!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "SandBallFalling", Pattern = "rain", Count = 10, Speed = 11, Damage = 20, IntervalTicks = 120, RainWidth = 900, RainHeight = 650 },
                            new AttackPattern { Projectile = "SandnadoHostile", Pattern = "aimed", Count = 1, Speed = 0.1f, Damage = 25, IntervalTicks = 600, StartDelayTicks = 120 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "Antlion", Count = 2, IntervalTicks = 900, MaxAlive = 4 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 25,
                        Message = "The Desert Scourge is frenzied!",
                        MovementSpeedMultiplier = 1.5f,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "SandBallFalling", Pattern = "rain", Count = 14, Speed = 13, Damage = 22, IntervalTicks = 90, RainWidth = 1000, RainHeight = 700 },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "AntlionMandible", MinStack = 8, MaxStack = 14 },
                    new LootEntry { Item = "FossilOre", MinStack = 15, MaxStack = 25 },
                    new LootEntry { Item = "SandstorminaBottle", Chance = 0.2f },
                    new LootEntry { Item = "AncientChisel", Chance = 0.33f },
                    new LootEntry { Item = "GoldCoin", MinStack = 4, MaxStack = 8 },
                    new LootEntry { Item = "LesserHealingPotion", MinStack = 5, MaxStack = 10 },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "crabulon",
                DisplayName = "Crabulon",
                BaseNpc = "KingSlime",
                DifficultyBonus = 0.25f,
                AiSpeed = 1.25f,
                RequiredBiome = "Mushroom",
                RequiresDowned = new List<string> { "desertscourge" },
                SpawnMessage = "Crabulon crawls out of the glowing mushrooms!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "SpikedSlimeSpike", Pattern = "ring", Count = 8, Speed = 7, Damage = 20, IntervalTicks = 180 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "AnomuraFungus", Count = 2, IntervalTicks = 600, MaxAlive = 4 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 50,
                        Message = "Crabulon releases a cloud of spores!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "SpikedSlimeSpike", Pattern = "ring", Count = 12, Speed = 8, Damage = 22, IntervalTicks = 150 },
                            new AttackPattern { Projectile = "PoisonDartShotFromSlimes", Pattern = "aimed", Count = 3, Speed = 9, Damage = 18, SpreadDegrees = 25, IntervalTicks = 100, StartDelayTicks = 40 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "MushiLadybug", Count = 3, IntervalTicks = 480, MaxAlive = 6 },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "GlowingMushroom", MinStack = 40, MaxStack = 60 },
                    new LootEntry { Item = "MushroomGrassSeeds", MinStack = 5, MaxStack = 10 },
                    new LootEntry { Item = "Shroomerang", Chance = 0.5f },
                    new LootEntry { Item = "MushroomHat", Chance = 0.5f },
                    new LootEntry { Item = "GoldCoin", MinStack = 5, MaxStack = 9 },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "hivemind",
                DisplayName = "The Hive Mind",
                BaseNpc = "BrainofCthulhu",
                AiPatches = new List<string> { "InEvil" },
                SegmentNpcs = new List<string> { "Creeper" },
                DifficultyBonus = 0.35f,
                MovementSpeedMultiplier = 1.2f,
                RequiredBiome = "Corruption",
                RequiresDowned = new List<string> { "EyeOfCthulhu" },
                SpawnMessage = "The Hive Mind rises from the rot!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "aimed", Count = 3, Speed = 8, Damage = 22, SpreadDegrees = 35, IntervalTicks = 150 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "EaterofSouls", Count = 3, IntervalTicks = 720, MaxAlive = 6 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 50,
                        Message = "The Hive Mind convulses!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "ring", Count = 10, Speed = 7, Damage = 24, IntervalTicks = 160 },
                            new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "aimed", Count = 4, Speed = 10, Damage = 24, SpreadDegrees = 40, IntervalTicks = 110, StartDelayTicks = 60 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "DevourerHead", Count = 1, IntervalTicks = 900, MaxAlive = 2, Message = "A Devourer tunnels toward you!" },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "DemoniteBar", MinStack = 10, MaxStack = 16 },
                    new LootEntry { Item = "RottenChunk", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "ShadowScale", MinStack = 15, MaxStack = 30 },
                    new LootEntry { Item = "BallOHurt", Chance = 0.33f },
                    new LootEntry { Item = "GoldCoin", MinStack = 8, MaxStack = 14 },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "perforators",
                DisplayName = "The Perforator Hive",
                BaseNpc = "BrainofCthulhu",
                AiPatches = new List<string> { "InEvil" },
                SegmentNpcs = new List<string> { "Creeper" },
                DifficultyBonus = 0.35f,
                MovementSpeedMultiplier = 1.2f,
                RequiredBiome = "Crimson",
                RequiresDowned = new List<string> { "EyeOfCthulhu" },
                SpawnMessage = "The Perforator Hive tears itself free of the flesh!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "BloodShot", Pattern = "aimed", Count = 3, Speed = 9, Damage = 22, SpreadDegrees = 30, IntervalTicks = 140 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "Crimera", Count = 3, IntervalTicks = 720, MaxAlive = 6 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 50,
                        Message = "Perforators burst from the hive!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "BloodShot", Pattern = "ring", Count = 10, Speed = 8, Damage = 24, IntervalTicks = 150 },
                            new AttackPattern { Projectile = "GoldenShowerHostile", Pattern = "aimed", Count = 1, Speed = 12, Damage = 20, IntervalTicks = 90, StartDelayTicks = 45 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "LeechHead", Count = 2, IntervalTicks = 600, MaxAlive = 3 },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "CrimtaneBar", MinStack = 10, MaxStack = 16 },
                    new LootEntry { Item = "Vertebrae", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "TissueSample", MinStack = 15, MaxStack = 30 },
                    new LootEntry { Item = "TheRottedFork", Chance = 0.33f },
                    new LootEntry { Item = "GoldCoin", MinStack = 8, MaxStack = 14 },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "slimegod",
                DisplayName = "The Slime God",
                BaseNpc = "QueenSlimeBoss",
                Difficulty = null,
                // Queen Slime is a hardmode boss (18000 HP / 60 contact at Classic); this is fought after Skeletron.
                DifficultyBonus = -1.0f,
                PlayerCountOverride = 0,
                RequiredBiome = "Any",
                RequiresDowned = new List<string> { "Skeletron" },
                SpawnMessage = "The Slime God descends!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "QueenSlimeGelAttack", Pattern = "aimed", Count = 3, Speed = 9, Damage = 24, SpreadDegrees = 30, IntervalTicks = 120 },
                        },
                        Minions = new List<MinionSpawn>
                        {
                            new MinionSpawn { Npc = "CorruptSlime", Count = 2, IntervalTicks = 600, MaxAlive = 4 },
                            new MinionSpawn { Npc = "Crimslime", Count = 2, IntervalTicks = 600, MaxAlive = 4 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 50,
                        Message = "The Slime God splits!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "QueenSlimeMinionBlueSpike", Pattern = "ring", Count = 12, Speed = 8, Damage = 26, IntervalTicks = 140 },
                            new AttackPattern { Projectile = "QueenSlimeGelAttack", Pattern = "rain", Count = 6, Speed = 10, Damage = 26, IntervalTicks = 110, StartDelayTicks = 50 },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "Gel", MinStack = 60, MaxStack = 99 },
                    new LootEntry { Item = "SlimeStaff", Chance = 0.5f },
                    new LootEntry { Item = "SlimeGun", Chance = 1f },
                    new LootEntry { Item = "RoyalGel", Chance = 1f },
                    new LootEntry { Item = "GoldCoin", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "LifeCrystal", MinStack = 1, MaxStack = 1, Chance = 1f, MinMode = "revengeance" },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "cryogen",
                DisplayName = "Cryogen",
                BaseNpc = "IceQueen",
                AiPatches = new List<string> { "Night" },
                // Ice Queen is a Frost Moon boss (34000 HP / 120 contact at Classic); pull her down to early-hardmode numbers.
                DifficultyBonus = -1.0f,
                MovementSpeedMultiplier = 1.15f,
                RequiredBiome = "Snow",
                RequireHardmode = true,
                RequiresDowned = new List<string> { "WallOfFlesh" },
                SpawnMessage = "Cryogen shatters free of its icy prison!",
                Phases = new List<BossPhase>
                {
                    new BossPhase
                    {
                        HealthBelowPercent = 100,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "FrostShard", Pattern = "aimed", Count = 4, Speed = 10, Damage = 40, SpreadDegrees = 40, IntervalTicks = 120 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 66,
                        Message = "Cryogen's shield cracks!",
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "IceSpike", Pattern = "ring", Count = 12, Speed = 9, Damage = 44, IntervalTicks = 150 },
                            new AttackPattern { Projectile = "FrostShard", Pattern = "aimed", Count = 5, Speed = 11, Damage = 42, SpreadDegrees = 45, IntervalTicks = 100 },
                        }
                    },
                    new BossPhase
                    {
                        HealthBelowPercent = 33,
                        Message = "Cryogen unleashes a blizzard!",
                        MovementSpeedMultiplier = 1.4f,
                        Attacks = new List<AttackPattern>
                        {
                            new AttackPattern { Projectile = "FrostBlastHostile", Pattern = "rain", Count = 8, Speed = 12, Damage = 46, IntervalTicks = 90 },
                            new AttackPattern { Projectile = "IceSpike", Pattern = "ring", Count = 16, Speed = 10, Damage = 46, IntervalTicks = 120, StartDelayTicks = 45 },
                        }
                    }
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "FrostCore", MinStack = 3, MaxStack = 5 },
                    new LootEntry { Item = "IceFeather", MinStack = 2, MaxStack = 4 },
                    new LootEntry { Item = "FrostStaff", Chance = 0.33f },
                    new LootEntry { Item = "IceBow", Chance = 0.33f },
                    new LootEntry { Item = "FrozenTurtleShell", Chance = 0.25f },
                    new LootEntry { Item = "GoldCoin", MinStack = 15, MaxStack = 25 },
                    new LootEntry { Item = "PlatinumCoin", MinStack = 1, MaxStack = 1, MinMode = "death" },
                },
                ReplaceVanillaLoot = true,
            });

            cat.Bosses.Add(new BossDefinition
            {
                Id = "mauler",
                DisplayName = "The Mauler",
                BaseNpc = "DukeFishron",
                // Duke Fishron has 60000 HP / 100 contact at Classic; the lowest difficulty the game accepts (0.5, Journey)
                // makes a ~30000 HP, ~50-damage ocean brute.
                Difficulty = 0.5f,
                AiPatches = new List<string> { "InBeach" },
                Hidden = true,
                RequiredBiome = "Ocean",
                SpawnMessage = "The Mauler surfaces!",
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "SharknadoBolt", Pattern = "aimed", Count = 3, Speed = 10, Damage = 22, SpreadDegrees = 30, IntervalTicks = 150 } } },
                    new BossPhase { HealthBelowPercent = 50, Message = "The Mauler thrashes!", Attacks = { new AttackPattern { Projectile = "SharknadoBolt", Pattern = "ring", Count = 8, Speed = 8, Damage = 24, IntervalTicks = 140 } }, Minions = { new MinionSpawn { Npc = "Shark", Count = 2, IntervalTicks = 480, MaxAlive = 4 } } },
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "SharkFin", MinStack = 5, MaxStack = 8 },
                    new LootEntry { Item = "GoldCoin", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "Trident", Chance = 0.5f },
                    new LootEntry { Item = "WaterWalkingBoots", Chance = 0.33f },
                },
                ReplaceVanillaLoot = true,
            });

            // ------------------------------------------------------------------ vanilla augments
            void Aug(string npc, BossDefinition d)
            {
                d.Id = "vanilla:" + npc.ToLowerInvariant();
                d.BaseNpc = npc;
                d.IsVanillaAugment = true;
                cat.VanillaAugments[npc] = d;
            }

            Aug("KingSlime", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "SpikedSlimeSpike", Pattern = "ring", Count = 6, Speed = 6, Damage = 16, IntervalTicks = 200 } } },
                    new BossPhase { HealthBelowPercent = 50, Message = "King Slime is furious!", Attacks = { new AttackPattern { Projectile = "SpikedSlimeSpike", Pattern = "ring", Count = 10, Speed = 7, Damage = 18, IntervalTicks = 150 } } },
                }
            });
            Aug("EyeofCthulhu", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Minions = { new MinionSpawn { Npc = "ServantofCthulhu", Count = 2, IntervalTicks = 600, MaxAlive = 6 } } },
                    new BossPhase { HealthBelowPercent = 50, Message = "The Eye of Cthulhu's gaze sharpens!", Attacks = { new AttackPattern { Projectile = "DemonSickle", Pattern = "aimed", Count = 2, Speed = 8, Damage = 18, SpreadDegrees = 20, IntervalTicks = 180 } } },
                }
            });
            Aug("EaterofWorldsHead", new BossDefinition
            {
                SegmentNpcs = new List<string> { "EaterofWorldsHead", "EaterofWorldsBody", "EaterofWorldsTail" },
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "aimed", Count = 1, Speed = 8, Damage = 18, IntervalTicks = 180 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "aimed", Count = 3, Speed = 9, Damage = 20, SpreadDegrees = 30, IntervalTicks = 150 } } },
                }
            });
            Aug("BrainofCthulhu", new BossDefinition
            {
                SegmentNpcs = new List<string> { "Creeper" },
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "BloodShot", Pattern = "aimed", Count = 2, Speed = 8, Damage = 18, SpreadDegrees = 20, IntervalTicks = 180 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "BloodShot", Pattern = "ring", Count = 8, Speed = 7, Damage = 20, IntervalTicks = 160 } } },
                }
            });
            Aug("QueenBee", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "QueenBeeStinger", Pattern = "aimed", Count = 3, Speed = 9, Damage = 22, SpreadDegrees = 30, IntervalTicks = 160 } } },
                    new BossPhase { HealthBelowPercent = 50, Message = "The hive swarms!", Minions = { new MinionSpawn { Npc = "Bee", Count = 4, IntervalTicks = 300, MaxAlive = 10 } }, Attacks = { new AttackPattern { Projectile = "QueenBeeStinger", Pattern = "ring", Count = 10, Speed = 8, Damage = 24, IntervalTicks = 150 } } },
                }
            });
            Aug("SkeletronHead", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "Skull", Pattern = "aimed", Count = 2, Speed = 6, Damage = 26, SpreadDegrees = 25, IntervalTicks = 200 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "Skull", Pattern = "aimed", Count = 4, Speed = 7, Damage = 28, SpreadDegrees = 45, IntervalTicks = 150 } } },
                }
            });
            Aug("Deerclops", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "FrostShard", Pattern = "aimed", Count = 3, Speed = 9, Damage = 24, SpreadDegrees = 30, IntervalTicks = 180 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "IceSpike", Pattern = "rain", Count = 6, Speed = 10, Damage = 26, IntervalTicks = 150 } } },
                }
            });
            Aug("QueenSlimeBoss", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "QueenSlimeGelAttack", Pattern = "aimed", Count = 3, Speed = 9, Damage = 40, SpreadDegrees = 30, IntervalTicks = 150 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "QueenSlimeMinionPinkBall", Pattern = "ring", Count = 10, Speed = 8, Damage = 42, IntervalTicks = 140 } } },
                }
            });
            Aug("TheDestroyer", new BossDefinition
            {
                SegmentNpcs = new List<string> { "TheDestroyer", "TheDestroyerBody", "TheDestroyerTail" },
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "DeathLaser", Pattern = "aimed", Count = 3, Speed = 11, Damage = 50, SpreadDegrees = 30, IntervalTicks = 150 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "DeathLaser", Pattern = "rain", Count = 8, Speed = 12, Damage = 52, IntervalTicks = 120 } } },
                }
            });
            Aug("Retinazer", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "EyeLaser", Pattern = "aimed", Count = 3, Speed = 12, Damage = 46, SpreadDegrees = 25, IntervalTicks = 140 } } },
                    new BossPhase { HealthBelowPercent = 40, Attacks = { new AttackPattern { Projectile = "DeathLaser", Pattern = "aimed", Count = 5, Speed = 13, Damage = 50, SpreadDegrees = 40, IntervalTicks = 120 } } },
                }
            });
            Aug("Spazmatism", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "aimed", Count = 3, Speed = 9, Damage = 48, SpreadDegrees = 30, IntervalTicks = 140 } } },
                    new BossPhase { HealthBelowPercent = 40, Attacks = { new AttackPattern { Projectile = "CursedFlameHostile", Pattern = "ring", Count = 10, Speed = 8, Damage = 50, IntervalTicks = 130 } } },
                }
            });
            Aug("SkeletronPrime", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "RocketSkeleton", Pattern = "aimed", Count = 2, Speed = 9, Damage = 50, SpreadDegrees = 20, IntervalTicks = 150 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "BombSkeletronPrime", Pattern = "rain", Count = 5, Speed = 8, Damage = 55, IntervalTicks = 140 } } },
                }
            });
            Aug("Plantera", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "SeedPlantera", Pattern = "aimed", Count = 4, Speed = 11, Damage = 54, SpreadDegrees = 35, IntervalTicks = 120 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "ThornBall", Pattern = "ring", Count = 6, Speed = 6, Damage = 60, IntervalTicks = 240 } } },
                }
            });
            Aug("Golem", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "Fireball", Pattern = "aimed", Count = 3, Speed = 9, Damage = 60, SpreadDegrees = 30, IntervalTicks = 140 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "RockGolemRock", Pattern = "rain", Count = 6, Speed = 9, Damage = 62, IntervalTicks = 130 } } },
                }
            });
            Aug("DukeFishron", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "SharknadoBolt", Pattern = "aimed", Count = 4, Speed = 12, Damage = 70, SpreadDegrees = 35, IntervalTicks = 120 } } },
                    new BossPhase { HealthBelowPercent = 40, Attacks = { new AttackPattern { Projectile = "SharknadoBolt", Pattern = "ring", Count = 12, Speed = 10, Damage = 74, IntervalTicks = 110 } } },
                }
            });
            Aug("CultistBoss", new BossDefinition
            {
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "CultistBossFireBall", Pattern = "aimed", Count = 3, Speed = 10, Damage = 64, SpreadDegrees = 30, IntervalTicks = 130 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "CultistBossIceMist", Pattern = "aimed", Count = 2, Speed = 7, Damage = 60, SpreadDegrees = 20, IntervalTicks = 170 } } },
                }
            });
            Aug("MoonLordCore", new BossDefinition
            {
                SegmentNpcs = new List<string> { "MoonLordCore", "MoonLordHead", "MoonLordHand" },
                Phases = new List<BossPhase>
                {
                    new BossPhase { HealthBelowPercent = 100, Attacks = { new AttackPattern { Projectile = "PhantasmalBolt", Pattern = "aimed", Count = 4, Speed = 12, Damage = 90, SpreadDegrees = 35, IntervalTicks = 120 } } },
                    new BossPhase { HealthBelowPercent = 50, Attacks = { new AttackPattern { Projectile = "PhantasmalEye", Pattern = "ring", Count = 10, Speed = 9, Damage = 95, IntervalTicks = 140 } } },
                }
            });

            return cat;
        }
    }
}
