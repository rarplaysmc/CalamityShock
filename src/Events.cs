using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using TShockAPI;

namespace CalamityShock
{
    public class EventSpawn
    {
        /// <summary>Vanilla NPC name/id to spawn, or empty when Boss is set.</summary>
        public string Npc { get; set; } = "";
        /// <summary>Custom boss id from bosses.json instead of a plain NPC (one per entry; Count is ignored).</summary>
        public string Boss { get; set; } = "";
        public int Count { get; set; } = 4;
        /// <summary>Added to the mode's enemy difficulty for these spawns.</summary>
        public float DifficultyBonus { get; set; } = 0f;

        [JsonIgnore] public int NpcId;
        [JsonIgnore] public BossDefinition BossDef;
    }

    public class EventWave
    {
        public string Name { get; set; } = "";
        public string Message { get; set; } = "";
        public List<EventSpawn> Spawns { get; set; } = new List<EventSpawn>();
        /// <summary>Never more than this many event enemies alive at once.</summary>
        public int MaxAlive { get; set; } = 10;
        public int SpawnIntervalTicks { get; set; } = 60;
    }

    public class EventDefinition
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        /// <summary>"waves" or "bossrush"</summary>
        public string Type { get; set; } = "waves";
        /// <summary>Waves only spawn near players standing in this biome (see RequiredBiome on bosses). "Any" = everywhere.</summary>
        public string RequiredBiome { get; set; } = "Any";
        public List<string> RequiresDowned { get; set; } = new List<string>();
        public List<EventWave> Waves { get; set; } = new List<EventWave>();
        /// <summary>Boss rush: custom boss ids and/or vanilla NPC names, fought in order. "Retinazer+Spazmatism" fights two at once.</summary>
        public List<string> Bosses { get; set; } = new List<string>();
        public int BossRushDelayTicks { get; set; } = 240;
        /// <summary>Boss rush: bosses drop nothing until the rush is complete.</summary>
        public bool SuppressBossLoot { get; set; } = true;
        /// <summary>Boss rush: the rush fails when a boss despawns (players died / left).</summary>
        public bool FailOnDespawn { get; set; } = true;
        /// <summary>Boss rush: AI patches applied to every boss in the rush so daytime/biome rules don't make them leave.</summary>
        public List<string> BossAiPatches { get; set; } = new List<string> { "Night", "InEvil" };
        /// <summary>0 = no time limit.</summary>
        public int TimeLimitSeconds { get; set; } = 0;
        /// <summary>Dropped at every participating player when the event completes.</summary>
        public List<LootEntry> Loot { get; set; } = new List<LootEntry>();
        public string StartMessage { get; set; } = "{name} has begun!";
        public string CompleteMessage { get; set; } = "{name} is over - the rewards are yours.";
        public string FailMessage { get; set; } = "{name} has failed.";

        /// <summary>One entry per rush stage; a stage holds one or more bosses fought together.</summary>
        [JsonIgnore] public List<List<(bool custom, BossDefinition def, int vanillaType)>> ResolvedBosses = new List<List<(bool, BossDefinition, int)>>();

        public string Format(string t) => (t ?? "").Replace("{name}", string.IsNullOrEmpty(DisplayName) ? Id : DisplayName);
        public bool IsBossRush => string.Equals(Type, "bossrush", StringComparison.OrdinalIgnoreCase);

        public bool Resolve(BossManager bosses)
        {
            if (string.IsNullOrWhiteSpace(Id)) { Log.Warn("events.json: an event without an Id was skipped"); return false; }
            Id = Id.Trim().ToLowerInvariant();
            DisplayName ??= ""; StartMessage ??= ""; CompleteMessage ??= ""; FailMessage ??= ""; RequiredBiome ??= "Any"; Type ??= "waves";
            string ctx = "event '" + Id + "'";
            Waves ??= new List<EventWave>();
            Bosses ??= new List<string>();
            Loot ??= new List<LootEntry>();
            RequiresDowned ??= new List<string>();
            // a stray "null" in any of the arrays must not take the whole plugin down
            Waves.RemoveAll(w => w == null);
            Bosses.RemoveAll(b => b == null);
            Loot.RemoveAll(l => l == null);
            RequiresDowned.RemoveAll(r => r == null);
            BossAiPatches ??= new List<string>();
            BossAiPatches.RemoveAll(ptch => ptch == null);
            foreach (EventWave w in Waves)
            {
                w.Name ??= ""; w.Message ??= "";
                w.Spawns ??= new List<EventSpawn>();
                w.Spawns.RemoveAll(sp => sp == null);
                w.Spawns.RemoveAll(sp =>
                {
                    if (!string.IsNullOrWhiteSpace(sp.Boss))
                    {
                        sp.BossDef = bosses.Find(sp.Boss);
                        if (sp.BossDef == null) { Log.Warn($"{ctx}: unknown boss '{sp.Boss}' in wave '{w.Name}' - removed"); return true; }
                        return false;
                    }
                    if (!Ids.TryNpc(sp.Npc, out sp.NpcId) || sp.NpcId == 0) { Log.Warn($"{ctx}: unknown NPC '{sp.Npc}' in wave '{w.Name}' - removed"); return true; }
                    if (sp.Count < 1) sp.Count = 1;
                    return false;
                });
                if (w.MaxAlive < 1) w.MaxAlive = 1;
                if (w.SpawnIntervalTicks < 5) w.SpawnIntervalTicks = 5;
            }
            ResolvedBosses.Clear();
            foreach (string stage in Bosses)
            {
                var entries = new List<(bool, BossDefinition, int)>();
                foreach (string b in (stage ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries))
                {
                    BossDefinition def = bosses.Find(b.Trim());
                    if (def != null) { entries.Add((true, def, def.BaseNpcId)); continue; }
                    if (Ids.TryNpc(b.Trim(), out int type) && type > 0) { entries.Add((false, null, type)); continue; }
                    Log.Warn($"{ctx}: unknown boss '{b}' - removed from the rush");
                }
                if (entries.Count > 0) ResolvedBosses.Add(entries);
            }
            Loot.RemoveAll(l => { if (!Ids.TryItem(l.Item, out l.ItemId) || l.ItemId <= 0) { Log.Warn($"{ctx}: unknown loot item '{l.Item}'"); return true; } return false; });
            BossAiPatches.RemoveAll(ptch => { if (!AiPatch.IsKnown(ptch)) { Log.Warn($"{ctx}: unknown AiPatch '{ptch}'"); return true; } return false; });
            if (IsBossRush && ResolvedBosses.Count == 0) { Log.Warn($"{ctx}: boss rush with no bosses - disabled"); return false; }
            if (!IsBossRush && Waves.Count == 0) { Log.Warn($"{ctx}: no waves - disabled"); return false; }
            return true;
        }
    }

    /// <summary>events.json</summary>
    public class EventCatalog
    {
        public List<EventDefinition> Events { get; set; } = new List<EventDefinition>();

        public static EventCatalog CreateDefault()
        {
            var cat = new EventCatalog();
            cat.Events.Add(new EventDefinition
            {
                Id = "acidrain",
                DisplayName = "Acid Rain",
                Type = "waves",
                RequiredBiome = "Ocean",
                StartMessage = "Acid rain starts falling on the ocean! Stand on the beach to fight the waves.",
                CompleteMessage = "The acid rain clears. Collect your rewards!",
                Waves = new List<EventWave>
                {
                    new EventWave { Name = "Wave 1", Message = "Wave 1: things crawl out of the water.", MaxAlive = 8, SpawnIntervalTicks = 60,
                        Spawns = { new EventSpawn { Npc = "Crab", Count = 6 }, new EventSpawn { Npc = "PinkJellyfish", Count = 3 } } },
                    new EventWave { Name = "Wave 2", Message = "Wave 2: the sea rises.", MaxAlive = 10, SpawnIntervalTicks = 50,
                        Spawns = { new EventSpawn { Npc = "Crab", Count = 6, DifficultyBonus = 0.25f }, new EventSpawn { Npc = "Squid", Count = 4 }, new EventSpawn { Npc = "Shark", Count = 2 } } },
                    new EventWave { Name = "Wave 3", Message = "Wave 3: sharks!", MaxAlive = 10, SpawnIntervalTicks = 45,
                        Spawns = { new EventSpawn { Npc = "Shark", Count = 5, DifficultyBonus = 0.5f }, new EventSpawn { Npc = "Piranha", Count = 6 }, new EventSpawn { Npc = "Antlion", Count = 2 } } },
                    new EventWave { Name = "Wave 4", Message = "Wave 4: the deep stirs.", MaxAlive = 12, SpawnIntervalTicks = 40,
                        Spawns = { new EventSpawn { Npc = "Shark", Count = 6, DifficultyBonus = 0.75f }, new EventSpawn { Npc = "GiantFlyingFox", Count = 3 }, new EventSpawn { Npc = "Squid", Count = 6, DifficultyBonus = 0.5f } } },
                    new EventWave { Name = "The Mauler", Message = "Something enormous breaches the surface...", MaxAlive = 6, SpawnIntervalTicks = 60,
                        Spawns = { new EventSpawn { Boss = "mauler" }, new EventSpawn { Npc = "Shark", Count = 4, DifficultyBonus = 0.5f } } },
                },
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "GoldCoin", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "SharkFin", MinStack = 3, MaxStack = 6 },
                    new LootEntry { Item = "Coral", MinStack = 10, MaxStack = 20 },
                    new LootEntry { Item = "Trident", Chance = 0.5f },
                    new LootEntry { Item = "BreathingReed", Chance = 0.5f },
                    new LootEntry { Item = "LifeCrystal", MinMode = "revengeance" },
                },
            });
            cat.Events.Add(new EventDefinition
            {
                Id = "bossrush",
                DisplayName = "Boss Rush",
                Type = "bossrush",
                RequiresDowned = new List<string> { "MoonLord" },
                Bosses = new List<string> { "KingSlime", "desertscourge", "EyeofCthulhu", "crabulon", "hivemind", "QueenBee", "SkeletronHead", "slimegod", "QueenSlimeBoss", "cryogen", "TheDestroyer", "Retinazer+Spazmatism", "SkeletronPrime", "Plantera", "Golem", "DukeFishron", "CultistBoss", "MoonLordCore" },
                SuppressBossLoot = true,
                FailOnDespawn = true,
                StartMessage = "BOSS RUSH! Every boss, back to back, no loot until the end. Good luck.",
                CompleteMessage = "Boss Rush complete. Legends.",
                FailMessage = "Boss Rush failed.",
                Loot = new List<LootEntry>
                {
                    new LootEntry { Item = "PlatinumCoin", MinStack = 5, MaxStack = 10 },
                    new LootEntry { Item = "LunarBar", MinStack = 30, MaxStack = 60 },
                    new LootEntry { Item = "CelestialSigil", MinStack = 1, MaxStack = 1 },
                    new LootEntry { Item = "DemonHeart", MinStack = 1, MaxStack = 1, Chance = 1f },
                },
            });
            return cat;
        }
    }

    /// <summary>Runs one event at a time: wave events (Acid-Rain style) and boss rushes.</summary>
    public class EventManager
    {
        private const string FileName = "events.json";
        private readonly CalamityShockPlugin _plugin;
        private readonly Dictionary<int, byte> _owned = new Dictionary<int, byte>();
        /// <summary>Which wave entry each owned NPC came from, so a despawned one can be re-spawned.</summary>
        private readonly Dictionary<int, int> _ownedEntry = new Dictionary<int, int>();
        private readonly HashSet<string> _participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long _tick;
        private long _startTick;
        private int _waveIndex = -1;
        private int[] _spawnedPerEntry = Array.Empty<int>();
        private long _nextSpawnTick;
        private int _rushIndex = -1;
        private long _rushNextSpawnTick;
        private readonly List<BossController> _rushBosses = new List<BossController>();

        public EventCatalog Catalog { get; private set; } = new EventCatalog();
        public List<EventDefinition> Definitions { get; } = new List<EventDefinition>();
        public EventDefinition Active { get; private set; }
        public bool SuppressBossLoot => Active != null && Active.IsBossRush && Active.SuppressBossLoot;

        public EventManager(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        public void Load()
        {
            Catalog = JsonStore.LoadOrCreate(FileName, EventCatalog.CreateDefault);
            Catalog.Events ??= new List<EventDefinition>();
            Definitions.Clear();
            foreach (EventDefinition e in Catalog.Events)
                if (e != null && e.Resolve(_plugin.Bosses)) Definitions.Add(e);
            Log.Info($"Loaded {Definitions.Count} events.");
        }

        public EventDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            id = id.Trim();
            return Definitions.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Definitions.FirstOrDefault(e => string.Equals((e.DisplayName ?? "").Replace(" ", ""), id.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ start / stop

        public string TryStart(EventDefinition def, TSPlayer starter, bool force)
        {
            if (def == null) return "Unknown event.";
            if (Active != null) return $"{Active.Format("{name}")} is already running. /cs event stop first.";
            if (!force)
            {
                var missing = _plugin.Progression.Unmet(def.RequiresDowned);
                if (missing.Count > 0) return "Requires: " + string.Join(", ", missing.Select(m => ProgressionManager.Describe(m, _plugin.Bosses.Catalog)));
                if (Net.CountLivingPlayers() == 0) return "No living players.";
            }
            Active = def;
            _owned.Clear();
            _ownedEntry.Clear();
            _participants.Clear();
            _startTick = _tick;
            _waveIndex = -1;
            _rushIndex = -1;
            _rushBosses.Clear();
            _rushNextSpawnTick = _tick + 60;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p != null && p.Active) _participants.Add(p.Name);
            }
            Net.Broadcast(def.Format(def.StartMessage), Net.EventColor);
            Log.Info($"Event {def.Id} started by {starter?.Name ?? "console"}");
            if (!def.IsBossRush) NextWave();
            return null;
        }

        public void Stop(bool failed, string reason = null)
        {
            EventDefinition def = Active;
            if (def == null) return;
            Active = null;
            foreach (var kv in _owned)
            {
                NPC n = Main.npc[kv.Key];
                if (n.active && n.generation == kv.Value) Net.KillNpcSilently(kv.Key);
            }
            _owned.Clear();
            _ownedEntry.Clear();
            // Rush bosses still standing would keep fighting with loot suppression now off; remove them.
            foreach (BossController c in _rushBosses)
                if (!c.Finished && !c.Killed) for (int i = 0; i < Main.maxNPCs; i++) if (c.Owns(i)) Net.KillNpcSilently(i);
            _rushBosses.Clear();
            if (failed) Net.Broadcast(def.Format(def.FailMessage) + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"), Net.WarnColor);
            Log.Info($"Event {def.Id} stopped{(failed ? " (failed)" : "")}.");
        }

        private void Complete()
        {
            EventDefinition def = Active;
            if (def == null) return;
            Active = null;
            _owned.Clear();
            _ownedEntry.Clear();
            _rushBosses.Clear();
            Net.Broadcast(def.Format(def.CompleteMessage), Net.EventColor);
            _plugin.Progression.RecordKill("event:" + def.Id);
            int rewarded = 0;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p == null || !p.Active || p.TPlayer == null) continue;
                if (!_participants.Contains(p.Name)) continue;
                Loot.DropTable(def.Loot, p.TPlayer.Center, _plugin.Config);
                rewarded++;
            }
            Log.Info($"Event {def.Id} completed; {rewarded} player(s) rewarded.");
        }

        // ------------------------------------------------------------------ ticking

        public void Tick()
        {
            _tick++;
            EventDefinition def = Active;
            if (def == null) return;
            if (def.TimeLimitSeconds > 0 && _tick - _startTick > (long)def.TimeLimitSeconds * 60)
            {
                Stop(true, "time limit reached");
                return;
            }
            if (def.IsBossRush) TickBossRush(def);
            else TickWaves(def);
        }

        private int AliveOwned()
        {
            int n = 0;
            foreach (var kv in _owned)
            {
                NPC npc = Main.npc[kv.Key];
                if (npc.active && npc.generation == kv.Value && npc.life > 0) n++;
            }
            return n;
        }

        /// <summary>
        /// Owned NPCs that are gone without having been killed (they despawned when every player walked away, or
        /// were removed by something else) give their quota back, so the wave cannot be "cleared" by leaving.
        /// </summary>
        private void ReclaimDespawned()
        {
            List<int> gone = null;
            foreach (var kv in _owned)
            {
                NPC npc = Main.npc[kv.Key];
                if (npc.active && npc.generation == kv.Value) continue;
                (gone ??= new List<int>()).Add(kv.Key);
            }
            if (gone == null) return;
            foreach (int slot in gone)
            {
                _owned.Remove(slot);
                if (_ownedEntry.TryGetValue(slot, out int entry))
                {
                    _ownedEntry.Remove(slot);
                    if (entry >= 0 && entry < _spawnedPerEntry.Length && _spawnedPerEntry[entry] > 0) _spawnedPerEntry[entry]--;
                }
            }
        }

        private void TickWaves(EventDefinition def)
        {
            if (_waveIndex < 0 || _waveIndex >= def.Waves.Count) { Complete(); return; }
            EventWave wave = def.Waves[_waveIndex];
            ReclaimDespawned();
            int alive = AliveOwned();

            bool allSpawned = true;
            for (int i = 0; i < wave.Spawns.Count; i++)
            {
                int quota = wave.Spawns[i].BossDef != null ? 1 : wave.Spawns[i].Count;
                if (_spawnedPerEntry[i] < quota) { allSpawned = false; break; }
            }
            if (allSpawned && alive == 0)
            {
                NextWave();
                return;
            }
            if (allSpawned || alive >= wave.MaxAlive || _tick < _nextSpawnTick) return;

            TSPlayer target = PickTargetPlayer(def.RequiredBiome);
            if (target == null) return; // nobody in the right place: the wave waits
            _nextSpawnTick = _tick + wave.SpawnIntervalTicks;

            // Pick the first entry that still has quota (bosses last so the wave's minions come first).
            int entry = -1;
            for (int i = 0; i < wave.Spawns.Count; i++)
            {
                int quota = wave.Spawns[i].BossDef != null ? 1 : wave.Spawns[i].Count;
                if (_spawnedPerEntry[i] < quota && wave.Spawns[i].BossDef == null) { entry = i; break; }
            }
            if (entry < 0)
                for (int i = 0; i < wave.Spawns.Count; i++)
                    if (_spawnedPerEntry[i] < 1 && wave.Spawns[i].BossDef != null) { entry = i; break; }
            if (entry < 0) return;

            EventSpawn sp = wave.Spawns[entry];
            if (sp.BossDef != null)
            {
                BossController c = _plugin.Bosses.SpawnCustomNearPlayer(sp.BossDef, target);
                if (c != null) { _owned[c.NpcIndex] = Main.npc[c.NpcIndex].generation; _ownedEntry[c.NpcIndex] = entry; _spawnedPerEntry[entry]++; }
                else _spawnedPerEntry[entry]++; // could not spawn (no slot); don't block the wave forever
                return;
            }

            if (!FindSpawnSpot(target.TPlayer, sp.NpcId, out Vector2 spot)) return;
            float difficulty = Difficulty.ForEnemy(_plugin.Config.CurrentMode) + sp.DifficultyBonus;
            BossManager.PendingScaling = new BossManager.SpawnScaling { Difficulty = difficulty, DefenseMultiplier = _plugin.Config.CurrentMode.EnemyDefenseMultiplier };
            try
            {
                int idx = NPC.NewNPC(new EntitySource_DebugCommand(), (int)spot.X, (int)spot.Y, sp.NpcId);
                if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].active)
                {
                    _owned[idx] = Main.npc[idx].generation;
                    _ownedEntry[idx] = entry;
                    _spawnedPerEntry[entry]++;
                    Net.SyncNpc(idx);
                }
            }
            finally
            {
                BossManager.PendingScaling = null;
            }
        }

        private void NextWave()
        {
            EventDefinition def = Active;
            _waveIndex++;
            if (_waveIndex >= def.Waves.Count) { Complete(); return; }
            EventWave wave = def.Waves[_waveIndex];
            _spawnedPerEntry = new int[wave.Spawns.Count];
            _nextSpawnTick = _tick + 60;
            string msg = string.IsNullOrEmpty(wave.Message) ? $"{def.Format("{name}")}: {wave.Name} ({_waveIndex + 1}/{def.Waves.Count})" : def.Format(wave.Message);
            Net.Broadcast(msg, Net.EventColor);
        }

        private void TickBossRush(EventDefinition def)
        {
            if (_rushBosses.Count > 0)
            {
                bool allKilled = _rushBosses.All(c => c.Killed);
                // Finished without a kill, or dropped by the boss manager (/cs despawn, an error) = despawned.
                bool despawned = _rushBosses.Any(c => (c.Finished || !_plugin.Bosses.Controllers.Contains(c)) && !c.Killed);
                if (allKilled)
                {
                    _rushBosses.Clear();
                    _rushNextSpawnTick = _tick + Math.Max(0, def.BossRushDelayTicks);
                }
                else if (despawned)
                {
                    foreach (BossController c in _rushBosses)
                        if (!c.Finished) for (int i = 0; i < Main.maxNPCs; i++) if (c.Owns(i)) Net.KillNpcSilently(i);
                    _rushBosses.Clear();
                    if (def.FailOnDespawn) { Stop(true, "the boss despawned"); return; }
                    _rushIndex--; // fight it again
                    _rushNextSpawnTick = _tick + Math.Max(0, def.BossRushDelayTicks);
                }
                return;
            }
            if (_tick < _rushNextSpawnTick) return;
            _rushIndex++;
            if (_rushIndex >= def.ResolvedBosses.Count) { Complete(); return; }

            TSPlayer target = PickTargetPlayer("Any");
            if (target == null) { Stop(true, "no living players"); return; }
            var stage = def.ResolvedBosses[_rushIndex];
            string name = string.Join(" & ", stage.Select(e => e.custom ? e.def.NameOrVanilla : Ids.NpcName(e.vanillaType)));
            Net.Broadcast($"Boss Rush {_rushIndex + 1}/{def.ResolvedBosses.Count}: {name}!", Net.EventColor);
            foreach (var (custom, bdef, vanillaType) in stage)
            {
                if (custom)
                {
                    BossController c = _plugin.Bosses.SpawnCustomNearPlayer(bdef, target);
                    if (c != null) _rushBosses.Add(c);
                }
                else
                {
                    _rushBosses.AddRange(_plugin.Bosses.SpawnVanillaOnPlayer(vanillaType, target));
                }
            }
            foreach (BossController c in _rushBosses) c.ExtraAiPatches = def.BossAiPatches;
            if (_rushBosses.Count == 0)
            {
                Log.Warn($"Boss rush: could not spawn {name}; skipping it.");
                _rushNextSpawnTick = _tick + 60;
            }
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>NpcKilled: drop dead event NPCs from the owned set (counts are recomputed from the set).</summary>
        public void OnNpcKilled(NPC npc)
        {
            if (Active == null || npc == null) return;
            if (_owned.TryGetValue(npc.whoAmI, out byte gen) && gen == npc.generation) { _owned.Remove(npc.whoAmI); _ownedEntry.Remove(npc.whoAmI); }
        }

        public bool OwnsNpc(int index) => _owned.TryGetValue(index, out byte gen) && Main.npc[index].active && Main.npc[index].generation == gen;

        /// <summary>HUD line for the player system.</summary>
        public string HudLine(TSPlayer p)
        {
            EventDefinition def = Active;
            if (def == null) return null;
            if (def.IsBossRush)
            {
                BossController cur = _rushBosses.FirstOrDefault(c => !c.Finished && !c.Killed);
                string boss = cur != null ? cur.Def.NameOrVanilla + $" {cur.HealthFraction() * 100f:0}%" : "next boss incoming";
                return $"[c/50FF82:Boss Rush] {Math.Min(_rushIndex + 1, def.ResolvedBosses.Count)}/{def.ResolvedBosses.Count} {boss}";
            }
            if (_waveIndex < 0 || _waveIndex >= def.Waves.Count) return null;
            EventWave wave = def.Waves[_waveIndex];
            int remaining = 0;
            for (int i = 0; i < wave.Spawns.Count; i++)
            {
                int quota = wave.Spawns[i].BossDef != null ? 1 : wave.Spawns[i].Count;
                remaining += Math.Max(0, quota - _spawnedPerEntry[i]);
            }
            remaining += AliveOwned();
            return $"[c/50FF82:{def.Format("{name}")}] {wave.Name} ({_waveIndex + 1}/{def.Waves.Count}) - {remaining} left";
        }

        public string StatusLine()
        {
            EventDefinition def = Active;
            if (def == null) return "No event is running.";
            long seconds = (_tick - _startTick) / 60;
            if (def.IsBossRush)
            {
                BossController cur = _rushBosses.FirstOrDefault(c => !c.Finished && !c.Killed);
                return $"{def.Format("{name}")}: boss {Math.Min(_rushIndex + 1, def.ResolvedBosses.Count)}/{def.ResolvedBosses.Count}, {seconds}s elapsed, {(cur != null ? cur.Def.NameOrVanilla + " active" : "between bosses")}";
            }
            return $"{def.Format("{name}")}: wave {_waveIndex + 1}/{def.Waves.Count}, {AliveOwned()} alive, {seconds}s elapsed";
        }

        // ------------------------------------------------------------------ helpers

        private static TSPlayer PickTargetPlayer(string biome)
        {
            var candidates = new List<TSPlayer>();
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p == null || !p.Active || p.TPlayer == null || p.TPlayer.dead) continue;
                if (!BossManager.InBiome(p.TPlayer, biome)) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return null;
            return candidates[Main.rand.Next(candidates.Count)];
        }

        private static readonly Dictionary<int, bool> FlyingCache = new Dictionary<int, bool>();

        private static bool IsFlying(int npcType)
        {
            if (FlyingCache.TryGetValue(npcType, out bool f)) return f;
            bool flying = false;
            try
            {
                var probe = new NPC();
                probe.SetDefaults(npcType);
                flying = probe.noGravity || probe.noTileCollide;
            }
            catch { }
            FlyingCache[npcType] = flying;
            return flying;
        }

        /// <summary>Finds an air tile 35-60 tiles to the side of the player (ground enemies land on solid ground).</summary>
        public static bool FindSpawnSpot(Player player, int npcType, out Vector2 spot)
        {
            bool flying = IsFlying(npcType);
            int px = (int)(player.Center.X / 16f), py = (int)(player.Center.Y / 16f);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int dx = Main.rand.Next(35, 60) * (Main.rand.Next(2) == 0 ? -1 : 1);
                int x = px + dx;
                if (x < 10 || x >= Main.maxTilesX - 10) continue;
                if (flying)
                {
                    int y = py - Main.rand.Next(5, 25);
                    if (y < 10) continue;
                    if (!WorldGen.SolidTile(x, y) && !WorldGen.SolidTile(x, y + 1)) { spot = new Vector2(x * 16 + 8, y * 16 + 8); return true; }
                    continue;
                }
                // ground: scan down from above the player for the first solid tile with air above it
                int top = Math.Max(10, py - 30);
                for (int y = top; y < Math.Min(Main.maxTilesY - 10, py + 40); y++)
                {
                    if (WorldGen.SolidTile(x, y) && !WorldGen.SolidTile(x, y - 1) && !WorldGen.SolidTile(x, y - 2) && !WorldGen.SolidTile(x, y - 3))
                    {
                        spot = new Vector2(x * 16 + 8, y * 16 - 4);
                        return true;
                    }
                }
            }
            spot = Vector2.Zero;
            return false;
        }
    }
}
