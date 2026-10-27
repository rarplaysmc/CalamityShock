using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Terraria;
using Terraria.DataStructures;
using TShockAPI;
using TShockAPI.DB;

namespace CalamityShock
{
    public class RegionBuff
    {
        public string Buff { get; set; } = "";
        public int Seconds { get; set; } = 2;
        [JsonIgnore] public int BuffId;
    }

    public class WeightedNpc
    {
        public string Npc { get; set; } = "";
        public int Weight { get; set; } = 1;
        [JsonIgnore] public int NpcId;
    }

    /// <summary>What happens inside one TShock region (define the region itself with /region define ... as usual).</summary>
    public class RegionEffect
    {
        /// <summary>Buffs kept on players inside (re-applied every second).</summary>
        public List<RegionBuff> Buffs { get; set; } = new List<RegionBuff>();
        /// <summary>Added to the difficulty of enemies that spawn naturally inside.</summary>
        public float DifficultyBonus { get; set; } = 0f;
        /// <summary>Natural spawns inside are replaced by one of these with SpawnReplaceChance.</summary>
        public List<WeightedNpc> SpawnReplace { get; set; } = new List<WeightedNpc>();
        public float SpawnReplaceChance { get; set; } = 0f;
        public string EnterMessage { get; set; } = "";
        public string ExitMessage { get; set; } = "";
        /// <summary>Custom bosses cannot be summoned inside.</summary>
        public bool BlockBossSummons { get; set; } = false;

        public bool Resolve(string name)
        {
            Buffs ??= new List<RegionBuff>();
            SpawnReplace ??= new List<WeightedNpc>();
            Buffs.RemoveAll(b => b == null);
            SpawnReplace.RemoveAll(w => w == null);
            Buffs.RemoveAll(b => { if (!Ids.TryBuff(b.Buff, out b.BuffId) || b.BuffId <= 0) { Log.Warn($"regions.json '{name}': unknown buff '{b.Buff}'"); return true; } if (b.Seconds < 1) b.Seconds = 1; return false; });
            SpawnReplace.RemoveAll(w => { if (!Ids.TryNpc(w.Npc, out w.NpcId) || w.NpcId == 0) { Log.Warn($"regions.json '{name}': unknown NPC '{w.Npc}'"); return true; } if (w.Weight < 1) w.Weight = 1; return false; });
            return true;
        }

        public int PickReplacement()
        {
            int total = SpawnReplace.Sum(w => w.Weight);
            if (total <= 0) return 0;
            int roll = Main.rand.Next(total);
            foreach (WeightedNpc w in SpawnReplace)
            {
                roll -= w.Weight;
                if (roll < 0) return w.NpcId;
            }
            return SpawnReplace[0].NpcId;
        }
    }

    /// <summary>regions.json</summary>
    public class RegionCatalog
    {
        /// <summary>Keyed by TShock region name.</summary>
        public Dictionary<string, RegionEffect> Regions { get; set; } = new Dictionary<string, RegionEffect>();

        public static RegionCatalog CreateDefault()
        {
            var cat = new RegionCatalog();
            cat.Regions["brimstone-crag"] = new RegionEffect
            {
                Buffs = { new RegionBuff { Buff = "OnFire", Seconds = 2 } },
                DifficultyBonus = 1.0f,
                SpawnReplace = { new WeightedNpc { Npc = "RedDevil", Weight = 2 }, new WeightedNpc { Npc = "FireImp", Weight = 3 }, new WeightedNpc { Npc = "Lavabat", Weight = 2 }, new WeightedNpc { Npc = "BoneSerpentHead", Weight = 1 } },
                SpawnReplaceChance = 0.6f,
                EnterMessage = "The air burns. You have entered the Brimstone Crag.",
                ExitMessage = "You leave the Brimstone Crag.",
            };
            cat.Regions["abyss"] = new RegionEffect
            {
                Buffs = { new RegionBuff { Buff = "Darkness", Seconds = 2 }, new RegionBuff { Buff = "Slow", Seconds = 2 } },
                DifficultyBonus = 1.5f,
                SpawnReplace = { new WeightedNpc { Npc = "Shark", Weight = 3 }, new WeightedNpc { Npc = "AnglerFish", Weight = 3 }, new WeightedNpc { Npc = "Arapaima", Weight = 1 }, new WeightedNpc { Npc = "GiantFlyingFox", Weight = 1 } },
                SpawnReplaceChance = 0.8f,
                EnterMessage = "The pressure crushes you. Welcome to the Abyss.",
                ExitMessage = "You surface from the Abyss.",
                BlockBossSummons = true,
            };
            return cat;
        }
    }

    /// <summary>
    /// Turns TShock regions into Calamity-style areas: buffs while inside, tougher and different enemies spawning
    /// inside, entry/exit messages. Define the region with TShock (/region define, /region set 1, /region set 2),
    /// then give it an entry in regions.json under the same name.
    /// </summary>
    public class RegionEffects
    {
        private const string FileName = "regions.json";
        private readonly CalamityShockPlugin _plugin;
        private readonly Dictionary<string, RegionEffect> _effects = new Dictionary<string, RegionEffect>(StringComparer.OrdinalIgnoreCase);
        private readonly string[] _lastRegion = new string[Main.maxPlayers];
        private long _tick;
        private bool _replacing;

        public RegionCatalog Catalog { get; private set; } = new RegionCatalog();
        public IReadOnlyDictionary<string, RegionEffect> Effects => _effects;

        public RegionEffects(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        public void Load()
        {
            Catalog = JsonStore.LoadOrCreate(FileName, RegionCatalog.CreateDefault);
            Catalog.Regions ??= new Dictionary<string, RegionEffect>();
            _effects.Clear();
            foreach (var kv in Catalog.Regions)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                kv.Value.Resolve(kv.Key);
                _effects[kv.Key.Trim()] = kv.Value;
            }
            int defined = _effects.Keys.Count(k => TShock.Regions.GetRegionByName(k) != null);
            Log.Info($"Loaded {_effects.Count} region effects ({defined} match an existing TShock region).");
        }

        /// <summary>The effect for the highest-priority configured region at a tile position, or null.</summary>
        public RegionEffect EffectAt(int tileX, int tileY, out string regionName)
        {
            regionName = null;
            if (_effects.Count == 0) return null;
            Region best = null;
            foreach (Region r in TShock.Regions.InAreaRegion(tileX, tileY))
            {
                if (!_effects.ContainsKey(r.Name)) continue;
                if (best == null || r.Z > best.Z) best = r;
            }
            if (best == null) return null;
            regionName = best.Name;
            return _effects[best.Name];
        }

        public RegionEffect EffectAt(Player p, out string regionName)
        {
            regionName = null;
            if (p == null) return null;
            return EffectAt((int)(p.Center.X / 16f), (int)(p.Center.Y / 16f), out regionName);
        }

        /// <summary>Every tick; does the per-player work once a second.</summary>
        public void Tick()
        {
            _tick++;
            if (_effects.Count == 0 || _tick % 60 != 0) return;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p == null || !p.Active || p.TPlayer == null) { _lastRegion[i] = null; continue; }
                RegionEffect fx = EffectAt(p.TPlayer, out string name);
                string last = _lastRegion[i];
                if (!string.Equals(last, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (last != null && _effects.TryGetValue(last, out var old) && !string.IsNullOrEmpty(old.ExitMessage)) p.SendMessage(old.ExitMessage, Net.EventColor);
                    if (fx != null && !string.IsNullOrEmpty(fx.EnterMessage)) p.SendMessage(fx.EnterMessage, Net.WarnColor);
                    _lastRegion[i] = name;
                }
                if (fx == null || p.TPlayer.dead) continue;
                foreach (RegionBuff b in fx.Buffs)
                    p.SetBuff(b.BuffId, b.Seconds * 60, true);
            }
        }

        /// <summary>
        /// Called from the NpcSpawn hook for natural hostile spawns. Returns true when the spawn was handled here
        /// (replaced), false when the caller should go on with normal scaling using the returned bonus.
        /// </summary>
        public bool OnNaturalSpawn(NPC npc, int index, out float difficultyBonus)
        {
            difficultyBonus = 0f;
            if (_effects.Count == 0 || _replacing) return false;
            RegionEffect fx = EffectAt((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f), out _);
            if (fx == null) return false;
            difficultyBonus = fx.DifficultyBonus;
            if (fx.SpawnReplace.Count == 0 || fx.SpawnReplaceChance <= 0f || Main.rand.NextDouble() > fx.SpawnReplaceChance) return false;
            int replacement = fx.PickReplacement();
            if (replacement == 0 || replacement == npc.type) return false;

            Microsoft.Xna.Framework.Vector2 bottom = npc.Bottom;
            Net.KillNpcSilently(index);
            _replacing = true;
            try
            {
                float difficulty = Difficulty.ForEnemy(_plugin.Config.CurrentMode) + fx.DifficultyBonus;
                BossManager.PendingScaling = new BossManager.SpawnScaling { Difficulty = difficulty, DefenseMultiplier = _plugin.Config.CurrentMode.EnemyDefenseMultiplier };
                int idx = NPC.NewNPC(new EntitySource_DebugCommand(), (int)bottom.X, (int)bottom.Y, replacement);
                if (idx >= 0 && idx < Main.maxNPCs && Main.npc[idx].active) Net.SyncNpc(idx);
            }
            finally
            {
                BossManager.PendingScaling = null;
                _replacing = false;
            }
            return true;
        }
    }
}
