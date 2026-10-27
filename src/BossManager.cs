using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>
    /// Owns the boss catalog and every active <see cref="BossController"/>, and implements the NPC-side hooks:
    /// spawn scaling, part adoption, gating, AI speed, death, loot and chat-message rewriting.
    /// </summary>
    public class BossManager
    {
        private const string FileName = "bosses.json";

        private class SummonContext
        {
            public BossDefinition Def;
            public float Difficulty;
            public int PlayerCount;
            public float DefenseMultiplier;
            public int SpawnedIndex = -1;
            public int Summoner = -1;
        }

        private class TransformOverride
        {
            public NPC Npc;
            public float Difficulty;
            public int PlayerCount;
        }

        private readonly CalamityShockPlugin _plugin;
        private readonly List<BossController> _controllers = new List<BossController>();
        private readonly Dictionary<int, BossDefinition> _augmentsByType = new Dictionary<int, BossDefinition>();
        private readonly Dictionary<int, List<string>> _gatesByType = new Dictionary<int, List<string>>();
        private HashSet<int> _movementBoostTypes = new HashSet<int>();
        private SummonContext _pendingSummon;
        private TransformOverride _transformOverride;
        private string _suppressAwokenName;
        private long _suppressAwokenTick = -1;
        private int _lastCreateSlot = -1;
        private int _lastCreateParent = -1;
        private byte _lastCreateParentGen;
        private bool _lastCreateNatural;
        private bool _inExtraAiStep;
        private long _tick;

        /// <summary>Set by a controller while it spawns a minion that should inherit the boss's difficulty.</summary>
        public static BossController PendingMinion;

        /// <summary>Explicit scaling for the next NPC.NewNPC (events, region spawn replacement).</summary>
        public class SpawnScaling
        {
            public float Difficulty = 1f;
            public int PlayerCount = 0;
            public float DefenseMultiplier = 1f;
        }

        public static SpawnScaling PendingScaling;

        /// <summary>Raised when a boss (custom or vanilla) is killed, before its controller is removed.</summary>
        public event Action<BossController> BossDefeated;

        private bool _bypassGates;
        private List<BossController> _captureControllers;

        public BossCatalog Catalog { get; private set; } = new BossCatalog();
        public List<BossDefinition> Definitions { get; } = new List<BossDefinition>();
        public IReadOnlyList<BossController> Controllers => _controllers;

        private CalamityConfig Config => _plugin.Config;
        private ModeSettings Mode => _plugin.Config.CurrentMode;

        public BossManager(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        // ------------------------------------------------------------------ loading

        public void Load()
        {
            Catalog = JsonStore.LoadOrCreate(FileName, BossCatalog.CreateDefault);
            Catalog.Bosses ??= new List<BossDefinition>();
            Catalog.VanillaAugments = new Dictionary<string, BossDefinition>(Catalog.VanillaAugments ?? new Dictionary<string, BossDefinition>(), StringComparer.OrdinalIgnoreCase);

            Definitions.Clear();
            Catalog.Bosses.RemoveAll(d => d == null);
            foreach (BossDefinition def in Catalog.Bosses)
            {
                if (string.IsNullOrWhiteSpace(def.Id)) { Log.Warn("bosses.json: a boss without an Id was skipped"); continue; }
                def.Id = def.Id.Trim().ToLowerInvariant();
                def.IsVanillaAugment = false;
                if (def.Resolve("boss '" + def.Id + "'")) Definitions.Add(def);
            }

            _augmentsByType.Clear();
            foreach (var kv in Catalog.VanillaAugments)
            {
                BossDefinition def = kv.Value;
                if (def == null) continue;
                if (string.IsNullOrWhiteSpace(def.BaseNpc)) def.BaseNpc = kv.Key;
                def.Id = "vanilla:" + kv.Key.ToLowerInvariant();
                def.DisplayName = "";
                def.IsVanillaAugment = true;
                if (def.Resolve("augment '" + kv.Key + "'")) _augmentsByType[def.BaseNpcId] = def;
            }

            ReloadConfigDerived();
            Log.Info($"Loaded {Definitions.Count} custom bosses and {_augmentsByType.Count} vanilla augments.");
        }

        /// <summary>Re-resolves the parts of config.json this manager uses (call after config reload).</summary>
        public void ReloadConfigDerived()
        {
            _movementBoostTypes = Ids.ResolveNpcSet(Config.MovementBoostNpcs, "config MovementBoostNpcs");
            _gatesByType.Clear();
            foreach (var kv in Config.VanillaBossGates)
            {
                if (!Ids.TryNpc(kv.Key, out int type)) { Log.Warn($"config VanillaBossGates: unknown NPC '{kv.Key}'"); continue; }
                if (kv.Value != null && kv.Value.Count > 0) _gatesByType[type] = kv.Value;
            }
        }

        public void Save() => JsonStore.Save(FileName, Catalog);

        public BossDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            id = id.Trim();
            return Definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Definitions.FirstOrDefault(d => string.Equals((d.DisplayName ?? "").Replace(" ", ""), id.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        }

        public void ClearControllers() => _controllers.Clear();

        /// <summary>Removes the custom-boss controllers only (vanilla bosses keep theirs).</summary>
        public void RemoveCustomControllers() => _controllers.RemoveAll(c => c.IsCustom);

        // ------------------------------------------------------------------ summoning

        /// <summary>Checks requirements and spawns the boss on the player. Returns null on success or the reason it was refused.</summary>
        public string TrySummon(TSPlayer player, BossDefinition def, bool force)
        {
            if (player == null || !player.RealPlayer || player.TPlayer == null) return "Only an in-game player can summon a boss.";
            if (def == null) return "Unknown boss.";
            Player tp = player.TPlayer;

            if (!force)
            {
                if (def.Hidden) return "That boss only appears during events.";
                RegionEffect fx = _plugin.Regions.EffectAt(tp, out string regionName);
                if (fx != null && fx.BlockBossSummons) return $"Bosses cannot be summoned inside {regionName}.";
                var missing = _plugin.Progression.Unmet(def.RequiresDowned);
                if (missing.Count > 0)
                    return "You must first defeat: " + string.Join(", ", missing.Select(m => ProgressionManager.Describe(m, Catalog)));
                if (def.RequireHardmode && !Main.hardMode) return "This boss can only be summoned in Hardmode.";
                if (def.RequireNight && Main.dayTime) return "This boss can only be summoned at night.";
                if (def.RequireDay && !Main.dayTime) return "This boss can only be summoned during the day.";
                if (!InBiome(tp, def.RequiredBiome)) return $"You must be in the {def.RequiredBiome} to summon {def.NameOrVanilla}.";
                if (def.SummonItemId > 0)
                {
                    int have = tp.CountItem(def.SummonItemId);
                    if (have < Math.Max(1, def.SummonItemStack))
                        return $"You need {Math.Max(1, def.SummonItemStack)}x {Ids.ItemName(def.SummonItemId)} to summon {def.NameOrVanilla}.";
                }
                foreach (BossController c in _controllers)
                    if (!c.Finished && c.IsCustom) return $"{c.Def.NameOrVanilla} is already active.";
                if (NPC.AnyNPCs(def.BaseNpcId)) return $"{Ids.NpcName(def.BaseNpcId)} is already in the world.";
            }

            ModeSettings mode = Mode;
            var ctx = new SummonContext
            {
                Def = def,
                Difficulty = Difficulty.ForBoss(mode, def),
                PlayerCount = def.PlayerCountOverride,
                DefenseMultiplier = Math.Max(0.1f, mode.BossDefenseMultiplier * def.DefenseMultiplier),
                Summoner = player.Index,
            };

            _pendingSummon = ctx;
            try
            {
                NPC.SpawnOnPlayer(player.Index, def.BaseNpcId);
            }
            catch (Exception ex)
            {
                _pendingSummon = null;
                Log.Error($"SpawnOnPlayer failed for {def.Id}: {ex}");
                return "The boss could not be spawned (see server log).";
            }
            _pendingSummon = null;

            if (ctx.SpawnedIndex < 0)
            {
                // SpawnOnPlayer refused (no free slot, or the NPC spawn was cancelled by another plugin).
                return "The boss could not be spawned - no free NPC slot or the spawn was blocked.";
            }
            // Only now is the summon item spent (with server-side characters; otherwise the client's inventory is its own).
            if (def.SummonItemId > 0 && Main.ServerSideCharacter)
                Crafting.ConsumeFromInventory(player, def.SummonItemId, Math.Max(1, def.SummonItemStack));

            NPC npc = Main.npc[ctx.SpawnedIndex];
            BossLifetime(npc);
            // The first full sync (spawn bit set) makes clients re-create the NPC, which resets its name - so sync first, name second.
            Net.SyncNpc(ctx.SpawnedIndex);
            if (!string.IsNullOrEmpty(def.DisplayName))
            {
                npc.GivenName = def.DisplayName;
                Net.SyncNpcName(ctx.SpawnedIndex);
            }

            var controller = new BossController(def, mode, ctx.SpawnedIndex, ctx.Difficulty, ctx.PlayerCount, ctx.DefenseMultiplier)
            {
                SummonerIndex = player.Index,
                FlagSnapshot = Config.CustomBossesSetVanillaFlags ? null : new ProgressionManager.VanillaFlagSnapshot(def.BaseNpcId),
            };
            _controllers.Add(controller);

            Net.Broadcast(def.Format(def.SpawnMessage), Net.BossColor);
            Log.Info($"{player.Name} summoned {def.Id} (npc #{ctx.SpawnedIndex}, {Difficulty.Describe(npc)})");
            return null;
        }

        /// <summary>
        /// Spawns a custom boss at a world position without any player checks (admin /cs test, console).
        /// Returns the NPC index or -1.
        /// </summary>
        public BossController SpawnAt(BossDefinition def, int x, int y)
        {
            if (def == null) return null;
            ModeSettings mode = Mode;
            var ctx = new SummonContext
            {
                Def = def,
                Difficulty = Difficulty.ForBoss(mode, def),
                PlayerCount = def.PlayerCountOverride,
                DefenseMultiplier = Math.Max(0.1f, mode.BossDefenseMultiplier * def.DefenseMultiplier),
            };
            _pendingSummon = ctx;
            try
            {
                NPC.NewNPC(new Terraria.DataStructures.EntitySource_DebugCommand(), x, y, def.BaseNpcId);
            }
            catch (Exception ex)
            {
                Log.Error($"NewNPC failed for {def.Id}: {ex}");
                return null;
            }
            finally
            {
                _pendingSummon = null;
            }
            if (ctx.SpawnedIndex < 0) return null;

            NPC npc = Main.npc[ctx.SpawnedIndex];
            BossLifetime(npc);
            Net.SyncNpc(ctx.SpawnedIndex);
            if (!string.IsNullOrEmpty(def.DisplayName))
            {
                npc.GivenName = def.DisplayName;
                Net.SyncNpcName(ctx.SpawnedIndex);
            }
            var controller = new BossController(def, mode, ctx.SpawnedIndex, ctx.Difficulty, ctx.PlayerCount, ctx.DefenseMultiplier)
            {
                FlagSnapshot = Config.CustomBossesSetVanillaFlags ? null : new ProgressionManager.VanillaFlagSnapshot(def.BaseNpcId),
            };
            _controllers.Add(controller);
            Net.Broadcast(def.Format(def.SpawnMessage), Net.BossColor);
            Log.Info($"Spawned {def.Id} (npc #{ctx.SpawnedIndex}, {Difficulty.Describe(npc)})");
            return controller;
        }

        /// <summary>
        /// NPC.NewNPC gives an NPC the ordinary ~12 s inactivity timer; the game's own SpawnBoss multiplies it by 20
        /// so a boss survives everyone being off-screen for a while (dead players respawning at spawn, for one).
        /// </summary>
        private static void BossLifetime(NPC npc)
        {
            int bossTime = (int)(NPC.activeTime * 1.25) * 20;   // what SpawnBoss ends up with
            if (npc != null && npc.active && npc.timeLeft < bossTime) npc.timeLeft = bossTime;
        }

        /// <summary>Spawns a custom boss a screen away from a player, no checks (events).</summary>
        public BossController SpawnCustomNearPlayer(BossDefinition def, TSPlayer p)
        {
            if (def == null || p == null || p.TPlayer == null) return null;
            Vector2 c = p.TPlayer.Center;
            int x = (int)c.X + (Main.rand.Next(2) == 0 ? -700 : 700);
            int y = (int)c.Y - 300;
            return SpawnAt(def, x, y);
        }

        /// <summary>
        /// Spawns a vanilla boss on a player through the game's own SpawnOnPlayer (right positions, "has awoken"),
        /// ignoring progression gates, and returns the controllers created for it (the Twins give two).
        /// </summary>
        public List<BossController> SpawnVanillaOnPlayer(int npcType, TSPlayer p)
        {
            var result = new List<BossController>();
            if (p == null || !p.RealPlayer || npcType <= 0) return result;
            _bypassGates = true;
            _captureControllers = result;
            try
            {
                // SpawnOnPlayer refuses Golem without a Lihzahrd altar nearby and Duke Fishron without a bobber in the
                // water; for those, create the NPC above the player directly and announce it the vanilla way.
                if (npcType == NPCID.Golem || npcType == NPCID.DukeFishron)
                {
                    Player tp = p.TPlayer;
                    int spawnY = (int)Math.Max(16f * 40f, tp.Center.Y - 300f);   // stay inside the world when the player is near the top
                    int idx = NPC.NewNPC(new EntitySource_BossSpawn(tp), (int)tp.Center.X, spawnY, npcType);
                    if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].active)
                    {
                        BossLifetime(Main.npc[idx]);
                        Net.SyncNpc(idx);
                        Net.Broadcast($"{Ids.NpcName(npcType)} has awoken!", new Microsoft.Xna.Framework.Color(175, 75, 255));
                    }
                }
                else NPC.SpawnOnPlayer(p.Index, npcType);
            }
            catch (Exception ex) { Log.Error($"SpawnOnPlayer({npcType}) failed: {ex.Message}"); }
            finally
            {
                _bypassGates = false;
                _captureControllers = null;
            }
            return result;
        }

        public static bool InBiome(Player p, string biome)
        {
            if (string.IsNullOrWhiteSpace(biome)) return true;
            switch (biome.Trim().ToLowerInvariant())
            {
                case "any": case "": return true;
                case "desert": return p.ZoneDesert || p.ZoneUndergroundDesert;
                case "undergrounddesert": return p.ZoneUndergroundDesert;
                case "jungle": return p.ZoneJungle;
                case "corruption": case "corrupt": return p.ZoneCorrupt;
                case "crimson": return p.ZoneCrimson;
                case "evil": return p.ZoneCorrupt || p.ZoneCrimson;
                case "mushroom": case "glowshroom": case "glowingmushroom": return p.ZoneGlowshroom;
                case "snow": case "ice": case "tundra": return p.ZoneSnow;
                case "dungeon": return p.ZoneDungeon;
                case "hallow": return p.ZoneHallow;
                case "underworld": case "hell": return p.ZoneUnderworldHeight;
                case "ocean": case "beach": return p.ZoneBeach;
                case "sky": case "space": return p.ZoneSkyHeight;
                case "underground": case "caverns": return p.ZoneRockLayerHeight || p.ZoneDirtLayerHeight;
                case "surface": case "overworld": return p.ZoneOverworldHeight;
                case "graveyard": return p.ZoneGraveyard;
                case "meteor": return p.ZoneMeteor;
                case "temple": case "lihzahrd": return p.ZoneLihzhardTemple;
                case "hive": return p.ZoneHive;
                default:
                    Log.Warn($"Unknown RequiredBiome '{biome}' - treated as Any");
                    return true;
            }
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>
        /// OTAPI NPC.Create: fires inside NPC.NewNPC just before the NPC object is made and carries the spawn source.
        /// When the source is a parent NPC (worm segments, Creepers, Moon Lord parts, boss minions) remember it so the
        /// NpcSpawn hook can attach the new NPC to exactly the boss that spawned it.
        /// </summary>
        public void OnNpcCreate(object sender, OTAPI.Hooks.NPC.CreateEventArgs e)
        {
            _lastCreateSlot = e.Slot;
            _lastCreateParent = -1;
            // The game's natural spawner uses EntitySource_SpawnNPC; statues (EntitySource_Wiring), commands and
            // plugins use other sources, and those must not be replaced by region spawn tables.
            _lastCreateNatural = e.Source is EntitySource_SpawnNPC;
            if (e.Source is Terraria.DataStructures.EntitySource_Parent p && p.Entity is NPC parent && parent.whoAmI >= 0 && parent.whoAmI < Main.maxNPCs)
            {
                _lastCreateParent = parent.whoAmI;
                _lastCreateParentGen = parent.generation;
            }
        }

        /// <summary>ServerApi.Hooks.NpcSpawn: every NPC.NewNPC on the server passes through here before its first sync.</summary>
        public void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled) return;
            int index = args.NpcId;
            if (index < 0 || index >= Main.maxNPCs) return;
            NPC npc = Main.npc[index];
            if (npc == null || !npc.active) return;
            CalamityConfig cfg = Config;
            ModeSettings mode = Mode;

            // 1. A custom boss being summoned right now.
            SummonContext ctx = _pendingSummon;
            if (ctx != null && ctx.SpawnedIndex < 0 && npc.type == ctx.Def.BaseNpcId)
            {
                Difficulty.Apply(npc, ctx.Difficulty, ctx.PlayerCount, ctx.DefenseMultiplier);
                ctx.SpawnedIndex = index;
                return;
            }

            // 2. A minion spawned by one of our controllers, or a spawn with explicit scaling (events, regions).
            BossController minionOwner = PendingMinion;
            if (minionOwner != null)
            {
                Difficulty.Apply(npc, minionOwner.AppliedDifficulty, 0, minionOwner.AppliedDefenseMultiplier);
                return;
            }
            SpawnScaling scaling = PendingScaling;
            if (scaling != null)
            {
                Difficulty.Apply(npc, scaling.Difficulty, scaling.PlayerCount, scaling.DefenseMultiplier);
                return;
            }

            // 3. A part of an active boss (worm segment, creeper, Moon Lord part...). Prefer the exact parent link from
            //    the spawn source; fall back to "same part type, spawned next to the boss" when there is no parent.
            int parent = _lastCreateSlot == index ? _lastCreateParent : -1;
            bool parentValid = parent >= 0 && Main.npc[parent].generation == _lastCreateParentGen;
            bool natural = _lastCreateSlot == index && _lastCreateNatural;
            _lastCreateSlot = -1;
            foreach (BossController c in _controllers)
            {
                if (c.Finished) continue;
                bool adopt = parentValid ? (c.Owns(parent) && c.IsPartType(npc.type)) : (parent < 0 && c.ShouldAdopt(npc));
                if (!adopt) continue;
                Difficulty.Apply(npc, c.AppliedDifficulty, c.AppliedPlayerCount, c.AppliedDefenseMultiplier);
                c.Adopt(index);
                return;
            }

            bool bossLike = IsBossLike(npc);

            // 4. Vanilla boss gating.
            if (bossLike && !_bypassGates && _gatesByType.TryGetValue(npc.type, out var gate))
            {
                var missing = _plugin.Progression.Unmet(gate);
                if (missing.Count > 0)
                {
                    args.Handled = true;
                    Net.KillNpcSilently(index);
                    // Vanilla's SpawnBoss still announces "X has awoken!" after NewNPC returns; drop that one.
                    _suppressAwokenName = Ids.NpcName(npc.netID);
                    _suppressAwokenTick = _tick;
                    Net.Broadcast($"{Ids.NpcName(npc.netID)} cannot be fought yet - defeat {string.Join(", ", missing.Select(m => ProgressionManager.Describe(m, Catalog)))} first.", Net.WarnColor);
                    return;
                }
            }

            // 5. Vanilla boss: scale it and give it a controller (augments, enrage, speed).
            if (bossLike && !npc.friendly && !npc.townNPC)
            {
                // The controller is always created (events and augments need it); scaling is what the switch turns off.
                BossDefinition def = BareDefinitionFor(npc.type, mode.VanillaBossAugments);
                float diff = npc.difficulty;
                float defMult = 1f;
                if (cfg.ScaleVanillaBosses)
                {
                    diff = Difficulty.ForBoss(mode, null);
                    defMult = Math.Max(0.1f, mode.BossDefenseMultiplier);
                    Difficulty.Apply(npc, diff, 0, defMult);
                }
                var vc = new BossController(def, mode, index, diff, 0, defMult);
                _controllers.Add(vc);
                _captureControllers?.Add(vc);
                return;
            }

            // 6. Everything else hostile: region effects first (natural spawns only), then the mode's enemy scaling.
            if (npc.friendly || npc.townNPC || npc.lifeMax <= 5) return;
            float regionBonus = 0f;
            if (natural && _plugin.Regions.OnNaturalSpawn(npc, index, out regionBonus))
            {
                args.Handled = true; // replaced by the region's own spawn; the original is already gone
                return;
            }
            if (!cfg.ScaleEnemies) return;
            if (Math.Abs(mode.EnemyDifficultyBonus) < 0.001f && Math.Abs(mode.EnemyDefenseMultiplier - 1f) < 0.001f && Math.Abs(regionBonus) < 0.001f) return;
            Difficulty.Apply(npc, Difficulty.ForEnemy(mode) + regionBonus, 0, Math.Max(0.1f, mode.EnemyDefenseMultiplier));
        }

        /// <summary>
        /// "boss" as players think of it: the boss flag, or a boss-head icon (the Eater of Worlds head, event bosses and
        /// pillars don't carry the flag). The Dungeon Guardian is excluded - it is unkillable anyway.
        /// </summary>
        public static bool IsBossLike(NPC npc)
        {
            if (npc == null) return false;
            if (npc.boss) return true;
            int t = npc.type;
            if (t < 0 || t >= NPCID.Count) return false;
            // Map icons without the boss flag that are not bosses: the Dungeon Guardian (unkillable), the Lunatic
            // Cultist's clones, and Golem's head (a part of Golem, killed before the body).
            if (t == NPCID.DungeonGuardian || t == NPCID.CultistBossClone || t == NPCID.GolemHead || t == NPCID.GolemHeadFree) return false;
            return NPCID.Sets.BossHeadTextures[t] >= 0;
        }

        /// <summary>Definition used for a vanilla boss: the augment from bosses.json (if enabled) or an empty one that still knows the boss's parts.</summary>
        private BossDefinition BareDefinitionFor(int type, bool withAugments)
        {
            if (_augmentsByType.TryGetValue(type, out var aug))
            {
                if (withAugments) return aug;
                var bare = new BossDefinition { Id = aug.Id, BaseNpc = aug.BaseNpc, BaseNpcId = aug.BaseNpcId, IsVanillaAugment = true, SegmentIds = aug.SegmentIds };
                return bare;
            }
            var def = new BossDefinition { Id = "vanilla:" + type, BaseNpc = type.ToString(), BaseNpcId = type, IsVanillaAugment = true };
            def.SegmentIds.Add(type);
            return def;
        }

        /// <summary>OTAPI NPC.Transform pre-hook: remember our scaling so the SetDefaults inside Transform keeps it.</summary>
        public void OnTransform(NPC npc, HookEvents.Terraria.NPC.TransformEventArgs args)
        {
            if (npc == null || !args.ContinueExecution || Main.netMode != 2) return;
            // Only NPCs this plugin actually re-scaled keep their numbers through a Transform; anything else (critters
            // the game never scales, so their difficulty stays 1 even in Expert) must get the vanilla treatment.
            bool ours = Difficulty.WasApplied(npc) || _controllers.Any(c => !c.Finished && c.Owns(npc.whoAmI));
            if (!ours) return;
            _transformOverride = new TransformOverride { Npc = npc, Difficulty = npc.difficulty, PlayerCount = npc.statsAreScaledForThisManyPlayers };
        }

        /// <summary>OTAPI NPC.SetDefaults pre-hook: inject our spawn params into the SetDefaults call made by Transform.</summary>
        public void OnSetDefaults(NPC npc, HookEvents.Terraria.NPC.SetDefaultsEventArgs args)
        {
            TransformOverride ov = _transformOverride;
            if (ov == null || !ReferenceEquals(ov.Npc, npc)) return;
            _transformOverride = null;
            if (!args.ContinueExecution) return;
            NPCSpawnParams p = args.spawnparams;
            p.difficultyOverride = ov.Difficulty;
            p.playerCountForMultiplayerDifficultyOverride = ov.PlayerCount >= 1 ? ov.PlayerCount : (int?)null;
            args.spawnparams = p;
        }

        /// <summary>ServerApi.Hooks.GameUpdate</summary>
        public void OnGameUpdate()
        {
            _tick++;
            if (_controllers.Count == 0) return;
            bool announce = Config.AnnouncePhases;
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                BossController c = _controllers[i];
                try
                {
                    c.Tick(_tick, announce);
                }
                catch (Exception ex)
                {
                    Log.Error($"Boss controller {c.Def.Id} crashed and was removed: {ex}");
                    _controllers.RemoveAt(i);
                    continue;
                }
                if (c.Finished)
                {
                    if (!c.Killed && c.IsCustom) Log.Info($"{c.Def.Id} despawned.");
                    _controllers.RemoveAt(i);
                }
            }
        }

        /// <summary>ServerApi.Hooks.GamePostUpdate</summary>
        public void OnGamePostUpdate()
        {
            if (_controllers.Count == 0) return;
            foreach (BossController c in _controllers)
            {
                try { c.PostTick(_movementBoostTypes); }
                catch (Exception ex) { Log.Error($"PostTick failed for {c.Def.Id}: {ex.Message}"); }
            }
        }

        /// <summary>ServerApi.Hooks.NpcAIUpdate: runs the vanilla AI an extra time for sped-up bosses.</summary>
        public void OnNpcAiUpdate(NpcAiUpdateEventArgs args)
        {
            if (args.Handled || _inExtraAiStep || _controllers.Count == 0) return;
            NPC npc = args.Npc;
            if (npc == null || !npc.active) return;
            BossController owner = null;
            foreach (BossController c in _controllers)
            {
                if (!c.Finished && c.Owns(npc.whoAmI)) { owner = c; break; }
            }
            if (owner == null) return;
            int extra = owner.ExtraAiStepsThisTick;
            List<string> patches = owner.EffectiveAiPatches;
            bool patched = patches != null;
            if (extra <= 0 && !patched) return;

            // Take over this tick's AI: run it ourselves (1 + extra) times with the AI patches applied.
            // The hook re-enters for each call, which _inExtraAiStep lets straight through to vanilla.
            args.Handled = true;
            _inExtraAiStep = true;
            try
            {
                using (patched ? new AiPatch(patches) : null)
                {
                    for (int i = 0; i <= extra; i++)
                    {
                        if (!npc.active) break;
                        npc.AI();
                    }
                }
            }
            finally
            {
                _inExtraAiStep = false;
            }
        }

        /// <summary>ServerApi.Hooks.NpcKilled: fires after loot and the vanilla defeat message, before the NPC is deactivated.</summary>
        public void OnNpcKilled(NpcKilledEventArgs args)
        {
            NPC npc = args.npc;
            if (npc == null) return;
            for (int i = _controllers.Count - 1; i >= 0; i--)
            {
                BossController c = _controllers[i];
                if (c.Finished || !c.Owns(npc.whoAmI)) continue;
                // The dying NPC already has life <= 0, so PartsAlive() no longer counts it; the boss is dead when no part is left alive.
                if (c.PartsAlive() > 0) continue;
                c.Killed = true;
                HandleBossDefeated(c, npc);
                try { BossDefeated?.Invoke(c); } catch (Exception ex) { Log.Error("BossDefeated handler failed: " + ex.Message); }
                _controllers.RemoveAt(i);
            }
        }

        private void HandleBossDefeated(BossController c, NPC npc)
        {
            BossDefinition def = c.Def;
            ModeSettings mode = c.Mode ?? Mode;
            Vector2 where = npc.Center;
            long seconds = c.TicksAlive / 60;

            bool suppressLoot = _plugin.Events.SuppressBossLoot;
            if (c.IsCustom)
            {
                _plugin.Progression.RecordKill(def.Id);
                Net.Broadcast(def.Format(def.DefeatMessage) + $" ({seconds / 60}m {seconds % 60}s)", Net.BossColor);
                if (!suppressLoot) Loot.DropTable(def.Loot, where, Config);
                if (c.FlagSnapshot != null && c.FlagSnapshot.Restore())
                {
                    // The base NPC's death code flipped a vanilla "downed" flag; undo it and resend world info.
                    TSPlayer.All.SendData(PacketTypes.WorldInfo);
                    Log.Info($"Restored vanilla boss flags changed by the death of {def.Id}.");
                }
            }
            // Mode-wide extra boss loot (vanilla and custom bosses alike). The Twins are two controllers but one
            // fight: the loot drops with whichever twin dies last.
            bool twinStillUp = (npc.type == NPCID.Retinazer && NPC.AnyNPCs(NPCID.Spazmatism)) || (npc.type == NPCID.Spazmatism && NPC.AnyNPCs(NPCID.Retinazer));
            if (!suppressLoot && !twinStillUp) Loot.DropTable(mode.ExtraBossLoot, where, Config);
            Log.Info($"Boss {def.Id} defeated after {seconds}s.");
        }

        /// <summary>ServerApi.Hooks.NpcLootDrop: suppress vanilla loot for custom bosses that replace it.</summary>
        public void OnLootDrop(NpcLootDropEventArgs args)
        {
            if (args.Handled) return;
            bool rush = _plugin.Events.SuppressBossLoot;
            foreach (BossController c in _controllers)
            {
                if (c.Finished) continue;
                if (!rush && (!c.IsCustom || !c.Def.ReplaceVanillaLoot)) continue;
                if (c.Owns(args.NpcArrayIndex)) { args.Handled = true; return; }
            }
        }

        /// <summary>ServerApi.Hooks.DropBossBag: same for treasure bags.</summary>
        public void OnBossBag(DropBossBagEventArgs args)
        {
            if (args.Handled) return;
            bool rush = _plugin.Events.SuppressBossLoot;
            foreach (BossController c in _controllers)
            {
                if (c.Finished) continue;
                if (!rush && (!c.IsCustom || !c.Def.ReplaceVanillaLoot)) continue;
                if (c.Owns(args.NpcArrayIndex)) { args.Handled = true; return; }
            }
        }

        /// <summary>
        /// ServerApi.Hooks.ServerBroadcast: vanilla announces "King Slime has awoken!/has been defeated!" using the base
        /// NPC's name. Rewrite those to the custom boss name while a custom boss built on that NPC is around.
        /// </summary>
        public void OnBroadcast(ServerBroadcastEventArgs args)
        {
            if (args.Handled || args.Message == null) return;
            string text;
            try { text = args.Message.ToString(); } catch { return; }
            if (string.IsNullOrEmpty(text)) return;

            // "X has awoken!" for a vanilla boss we just refused to spawn (progression gate). Checked before anything
            // else: a gate usually fires when no controller exists at all.
            if (_suppressAwokenName != null)
            {
                bool fresh = _tick - _suppressAwokenTick <= 1;
                if (fresh && IsAwokenMessage(text, _suppressAwokenName))
                {
                    _suppressAwokenName = null;
                    args.Handled = true;
                    return;
                }
                if (!fresh) _suppressAwokenName = null;
            }
            if (_controllers.Count == 0 && _pendingSummon == null) return;

            // "X has awoken!" while we are summoning a custom boss built on X: we announce it ourselves.
            if (_pendingSummon != null && !string.IsNullOrEmpty(_pendingSummon.Def.DisplayName))
            {
                string vanilla = Ids.NpcName(_pendingSummon.Def.BaseNpcId);
                if (!string.IsNullOrEmpty(vanilla) && IsAwokenMessage(text, vanilla))
                {
                    args.Handled = true;
                    return;
                }
            }

            // "X has been defeated!" for the base NPC of a custom boss that just died: we announce that ourselves too.
            // Any other broadcast is left alone (a vanilla X fighting alongside, gate messages, other plugins).
            foreach (BossController c in _controllers)
            {
                if (!c.IsCustom || string.IsNullOrEmpty(c.Def.DisplayName)) continue;
                string vanilla = Ids.NpcName(c.Def.BaseNpcId);
                if (string.IsNullOrEmpty(vanilla) || !IsDefeatedMessage(text, vanilla)) continue;
                if (c.PartsAlive() == 0) { args.Handled = true; return; }
            }
        }

        /// <summary>The game's own announcement for this NPC name, in the server's language (with an English fallback).</summary>
        private static bool IsAwokenMessage(string text, string npcName)
        {
            if (string.IsNullOrEmpty(npcName) || !text.Contains(npcName)) return false;
            string expected = null;
            try { expected = Language.GetTextValue("Announcement.HasAwoken", npcName); } catch { }
            return text == expected || text.Contains("has awoken");
        }

        private static bool IsDefeatedMessage(string text, string npcName)
        {
            if (string.IsNullOrEmpty(npcName) || !text.Contains(npcName)) return false;
            string expected = null;
            try { expected = Language.GetTextValue("Announcement.HasBeenDefeated_Single", npcName); } catch { }
            return text == expected || text.Contains("has been defeated");
        }

        /// <summary>Status lines for /cs status.</summary>
        public List<string> StatusLines()
        {
            var lines = new List<string>();
            foreach (BossController c in _controllers)
            {
                if (c.Finished) continue;
                NPC a = c.Anchor;
                string name = c.Def.NameOrVanilla;
                string phase = c.PhaseIndex >= 0 ? $"phase {c.PhaseIndex + 1}/{Math.Max(1, c.Def.Phases.Count)}" : "no phases";
                string enrage = c.EnrageSeconds > 0 ? (c.Enraged ? "ENRAGED" : $"enrage in {Math.Max(0, c.EnrageSeconds - c.TicksAlive / 60)}s") : "no enrage";
                lines.Add($"{name}: {c.HealthFraction() * 100f:0}% HP, {phase}, {enrage}, {c.TicksAlive / 60}s, {(a != null ? Difficulty.Describe(a) : "")}");
            }
            return lines;
        }
    }
}
