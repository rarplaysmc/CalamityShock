using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace CalamityShock
{
    /// <summary>progress.json - which custom bosses are down, and how many times each was killed.</summary>
    public class ProgressState
    {
        public List<string> Downed { get; set; } = new List<string>();
        public Dictionary<string, int> Kills { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Progression gate. A requirement is either a custom boss id ("desertscourge") or one of the vanilla
    /// flag names below ("EyeOfCthulhu", "WallOfFlesh", ...). Both are case-insensitive.
    /// </summary>
    public class ProgressionManager
    {
        private const string FileName = "progress.json";
        private ProgressState _state = new ProgressState();
        private readonly HashSet<string> _downed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Vanilla progression flags readable through the same requirement strings.</summary>
        public static readonly Dictionary<string, Func<bool>> VanillaFlags = new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["KingSlime"] = () => NPC.downedSlimeKing,
            ["EyeOfCthulhu"] = () => NPC.downedBoss1,
            ["EvilBoss"] = () => NPC.downedBoss2,
            ["EaterOfWorlds"] = () => NPC.downedBoss2,
            ["BrainOfCthulhu"] = () => NPC.downedBoss2,
            ["QueenBee"] = () => NPC.downedQueenBee,
            ["Skeletron"] = () => NPC.downedBoss3,
            ["Deerclops"] = () => NPC.downedDeerclops,
            ["WallOfFlesh"] = () => Main.hardMode,
            ["Hardmode"] = () => Main.hardMode,
            ["QueenSlime"] = () => NPC.downedQueenSlime,
            ["TheDestroyer"] = () => NPC.downedMechBoss1,
            ["TheTwins"] = () => NPC.downedMechBoss2,
            ["SkeletronPrime"] = () => NPC.downedMechBoss3,
            ["AnyMechBoss"] = () => NPC.downedMechBossAny,
            ["AllMechBosses"] = () => NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3,
            ["Plantera"] = () => NPC.downedPlantBoss,
            ["Golem"] = () => NPC.downedGolemBoss,
            ["DukeFishron"] = () => NPC.downedFishron,
            ["EmpressOfLight"] = () => NPC.downedEmpressOfLight,
            ["LunaticCultist"] = () => NPC.downedAncientCultist,
            ["MoonLord"] = () => NPC.downedMoonlord,
            ["GoblinArmy"] = () => NPC.downedGoblins,
            ["PirateInvasion"] = () => NPC.downedPirates,
            ["FrostLegion"] = () => NPC.downedFrost,
            ["MartianMadness"] = () => NPC.downedMartians,
            ["Pumpking"] = () => NPC.downedHalloweenKing,
            ["MourningWood"] = () => NPC.downedHalloweenTree,
            ["IceQueen"] = () => NPC.downedChristmasIceQueen,
            ["SantaNK1"] = () => NPC.downedChristmasSantank,
            ["Everscream"] = () => NPC.downedChristmasTree,
        };

        /// <summary>
        /// Killing a custom boss runs the base NPC's vanilla death code, which flips that vanilla boss's "downed" flag
        /// (a Desert Scourge built on the Eater of Worlds would count as beating the Eater of Worlds).
        /// The snapshot remembers, for the base type only, which of its flags were false when the fight began, and
        /// Restore() clears those again after the kill. Other flags are never touched, so a boss someone else killed
        /// during the fight stays downed.
        /// </summary>
        public sealed class VanillaFlagSnapshot
        {
            private readonly List<(Func<bool> get, Action<bool> set, bool wasSet)> _flags = new List<(Func<bool>, Action<bool>, bool)>();

            private static IEnumerable<(Func<bool> get, Action<bool> set)> FlagsFor(int baseType)
            {
                switch (baseType)
                {
                    case 50: yield return (() => NPC.downedSlimeKing, v => NPC.downedSlimeKing = v); break;
                    case 4: yield return (() => NPC.downedBoss1, v => NPC.downedBoss1 = v); break;
                    case 13: case 14: case 15: case 266: yield return (() => NPC.downedBoss2, v => NPC.downedBoss2 = v); break;
                    case 35: yield return (() => NPC.downedBoss3, v => NPC.downedBoss3 = v); break;
                    case 222: yield return (() => NPC.downedQueenBee, v => NPC.downedQueenBee = v); break;
                    case 668: yield return (() => NPC.downedDeerclops, v => NPC.downedDeerclops = v); break;
                    case 657: yield return (() => NPC.downedQueenSlime, v => NPC.downedQueenSlime = v); break;
                    case 134: case 135: case 136:
                        yield return (() => NPC.downedMechBoss1, v => NPC.downedMechBoss1 = v);
                        yield return (() => NPC.downedMechBossAny, v => NPC.downedMechBossAny = v);
                        break;
                    case 125: case 126:
                        yield return (() => NPC.downedMechBoss2, v => NPC.downedMechBoss2 = v);
                        yield return (() => NPC.downedMechBossAny, v => NPC.downedMechBossAny = v);
                        break;
                    case 127:
                        yield return (() => NPC.downedMechBoss3, v => NPC.downedMechBoss3 = v);
                        yield return (() => NPC.downedMechBossAny, v => NPC.downedMechBossAny = v);
                        break;
                    case 262: yield return (() => NPC.downedPlantBoss, v => NPC.downedPlantBoss = v); break;
                    case 245: case 246: yield return (() => NPC.downedGolemBoss, v => NPC.downedGolemBoss = v); break;
                    case 370: yield return (() => NPC.downedFishron, v => NPC.downedFishron = v); break;
                    case 636: yield return (() => NPC.downedEmpressOfLight, v => NPC.downedEmpressOfLight = v); break;
                    case 439: yield return (() => NPC.downedAncientCultist, v => NPC.downedAncientCultist = v); break;
                    case 396: case 397: case 398: yield return (() => NPC.downedMoonlord, v => NPC.downedMoonlord = v); break;
                    case 345: yield return (() => NPC.downedChristmasIceQueen, v => NPC.downedChristmasIceQueen = v); break;
                    case 346: yield return (() => NPC.downedChristmasSantank, v => NPC.downedChristmasSantank = v); break;
                    case 344: yield return (() => NPC.downedChristmasTree, v => NPC.downedChristmasTree = v); break;
                    case 327: yield return (() => NPC.downedHalloweenKing, v => NPC.downedHalloweenKing = v); break;
                    case 325: yield return (() => NPC.downedHalloweenTree, v => NPC.downedHalloweenTree = v); break;
                    // Wall of Flesh (113) starts Hardmode; that is deliberately not undone.
                }
            }

            public VanillaFlagSnapshot(int baseNpcType)
            {
                foreach (var f in FlagsFor(baseNpcType))
                {
                    bool was;
                    try { was = f.get(); } catch { continue; }
                    _flags.Add((f.get, f.set, was));
                }
            }

            /// <summary>Clears the base boss's flags that were false when the snapshot was taken. Returns true if anything changed.</summary>
            public bool Restore()
            {
                bool changed = false;
                foreach (var f in _flags)
                {
                    if (!f.wasSet && f.get())
                    {
                        f.set(false);
                        changed = true;
                    }
                }
                return changed;
            }
        }

        /// <summary>Vanilla flags in the order the checklist prints them.</summary>
        public static readonly string[] ChecklistOrder =
        {
            "KingSlime", "EyeOfCthulhu", "EvilBoss", "QueenBee", "Skeletron", "Deerclops", "WallOfFlesh",
            "QueenSlime", "TheDestroyer", "TheTwins", "SkeletronPrime", "Plantera", "Golem", "DukeFishron",
            "EmpressOfLight", "LunaticCultist", "MoonLord"
        };

        public void Load()
        {
            _state = JsonStore.LoadOrCreate(FileName, () => new ProgressState());
            _state.Downed ??= new List<string>();
            _state.Kills ??= new Dictionary<string, int>();
            _downed.Clear();
            foreach (string id in _state.Downed) _downed.Add(id);
        }

        public void Save()
        {
            _state.Downed = _downed.OrderBy(s => s).ToList();
            JsonStore.Save(FileName, _state);
        }

        public bool IsCustomDowned(string bossId) => !string.IsNullOrEmpty(bossId) && _downed.Contains(bossId);

        public int KillCount(string bossId)
        {
            if (string.IsNullOrEmpty(bossId)) return 0;
            foreach (var kv in _state.Kills)
                if (string.Equals(kv.Key, bossId, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return 0;
        }

        public void RecordKill(string bossId)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            _downed.Add(bossId);
            string key = _state.Kills.Keys.FirstOrDefault(k => string.Equals(k, bossId, StringComparison.OrdinalIgnoreCase)) ?? bossId;
            _state.Kills[key] = KillCount(bossId) + 1;
            Save();
        }

        public void SetDowned(string bossId, bool downed)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            if (downed) _downed.Add(bossId); else _downed.Remove(bossId);
            Save();
        }

        public void ResetAll()
        {
            _downed.Clear();
            _state.Kills.Clear();
            Save();
        }

        /// <summary>True when the requirement (custom boss id or vanilla flag) is satisfied.</summary>
        public bool IsMet(string requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement)) return true;
            requirement = requirement.Trim();
            if (VanillaFlags.TryGetValue(requirement, out var flag))
            {
                try { return flag(); } catch { return false; }
            }
            return _downed.Contains(requirement);
        }

        /// <summary>Returns the requirements that are NOT met (empty = all good).</summary>
        public List<string> Unmet(IEnumerable<string> requirements)
        {
            var missing = new List<string>();
            if (requirements == null) return missing;
            foreach (string r in requirements)
                if (!IsMet(r)) missing.Add(r);
            return missing;
        }

        /// <summary>Human readable name for a requirement string.</summary>
        public static string Describe(string requirement, BossCatalog catalog)
        {
            if (string.IsNullOrEmpty(requirement)) return "?";
            var def = catalog?.Bosses?.FirstOrDefault(b => string.Equals(b.Id, requirement, StringComparison.OrdinalIgnoreCase));
            if (def != null) return def.NameOrVanilla;
            return requirement;
        }
    }
}
