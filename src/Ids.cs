using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;

namespace CalamityShock
{
    /// <summary>
    /// Resolves the names used in the JSON files ("KingSlime", "SandBallFalling", "GoldCoin", "Anvils")
    /// to the numeric IDs Terraria uses. Numeric strings are accepted everywhere a name is.
    /// Names are matched case-insensitively and ignore spaces, underscores and dashes,
    /// so "king slime", "king_slime" and "KingSlime" all resolve to NPCID.KingSlime.
    /// </summary>
    public static class Ids
    {
        private static readonly Dictionary<string, int> Npcs = Build(typeof(NPCID));
        private static readonly Dictionary<string, int> Projectiles = Build(typeof(ProjectileID));
        private static readonly Dictionary<string, int> Items = Build(typeof(ItemID));
        private static readonly Dictionary<string, int> Tiles = Build(typeof(TileID));
        private static readonly Dictionary<string, int> Buffs = Build(typeof(BuffID));

        private static Dictionary<string, int> Build(Type idClass)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (FieldInfo f in idClass.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.IsLiteral && !f.IsInitOnly) continue;
                object raw;
                try { raw = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null); }
                catch { continue; }
                int value;
                if (raw is short s) value = s;
                else if (raw is ushort us) value = us;
                else if (raw is int i) value = i;
                else if (raw is byte b) value = b;
                else continue;
                if (f.Name == "Count" || f.Name == "NegativeIDCount") continue;
                map[Normalize(f.Name)] = value;
            }
            return map;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var chars = new List<char>(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' || c == '_' || c == '-' || c == '\'') continue;
                chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }

        /// <summary>Names come from the id tables; a plain number is accepted only inside the table's valid range.</summary>
        private static bool TryResolve(Dictionary<string, int> map, string text, out int id, int minId, int maxExclusive)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (int.TryParse(text, out id))
            {
                if (id >= minId && id < maxExclusive) return true;
                id = 0;
                return false;
            }
            return map.TryGetValue(Normalize(text), out id);
        }

        // Negative NPC ids are the game's net ids for slime/zombie/skeleton variants.
        public static bool TryNpc(string text, out int id) => TryResolve(Npcs, text, out id, -NPCID.NegativeIDCount, NPCID.Count);
        public static bool TryProjectile(string text, out int id) => TryResolve(Projectiles, text, out id, 0, ProjectileID.Count);
        public static bool TryItem(string text, out int id) => TryResolve(Items, text, out id, 0, ItemID.Count);
        public static bool TryTile(string text, out int id) => TryResolve(Tiles, text, out id, 0, TileID.Count);
        public static bool TryBuff(string text, out int id) => TryResolve(Buffs, text, out id, 0, BuffID.Count);

        /// <summary>Display name of an NPC type/netID as the game shows it ("King Slime").</summary>
        public static string NpcName(int netId)
        {
            try { return Lang.GetNPCNameValue(netId); } catch { return "NPC#" + netId; }
        }

        /// <summary>Display name of an item type ("Gold Coin").</summary>
        public static string ItemName(int type)
        {
            try { return Lang.GetItemNameValue(type); } catch { return "Item#" + type; }
        }

        /// <summary>Resolve a list of names, logging and skipping the ones that don't exist.</summary>
        public static HashSet<int> ResolveNpcSet(IEnumerable<string> names, string context)
        {
            var set = new HashSet<int>();
            if (names == null) return set;
            foreach (string n in names)
            {
                if (TryNpc(n, out int id)) set.Add(id);
                else Log.Warn($"{context}: unknown NPC '{n}' (ignored)");
            }
            return set;
        }
    }
}
