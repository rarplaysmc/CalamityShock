using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace CalamityShock
{
    public static class Loot
    {
        /// <summary>Rolls every entry of a loot table and drops the results around a position.</summary>
        public static void DropTable(List<LootEntry> table, Vector2 where, CalamityConfig cfg)
        {
            if (table == null || table.Count == 0) return;
            int activeRank = cfg?.CurrentMode?.Rank ?? 0;
            foreach (LootEntry e in table)
            {
                if (e == null) continue;
                if (e.ItemId <= 0 && (!Ids.TryItem(e.Item, out e.ItemId) || e.ItemId <= 0))
                {
                    Log.Warn($"loot: unknown item '{e.Item}'");
                    continue;
                }
                if (!string.IsNullOrEmpty(e.MinMode))
                {
                    int required = cfg?.RankOf(e.MinMode) ?? -1;
                    if (required < 0) { Log.Warn($"loot: unknown MinMode '{e.MinMode}' for {e.Item}"); continue; }
                    if (activeRank < required) continue;
                }
                if (e.Chance < 1f && Main.rand.NextDouble() > e.Chance) continue;

                int min = Math.Max(1, Math.Min(e.MinStack, e.MaxStack));
                int max = Math.Max(min, Math.Max(e.MinStack, e.MaxStack));
                int stack = Main.rand.Next(min, max + 1);
                Vector2 pos = where + new Vector2((float)(Main.rand.NextDouble() - 0.5) * 48f, (float)(Main.rand.NextDouble() - 0.5) * 24f);

                // Respect max stack sizes by splitting big drops.
                var probe = new Item();
                probe.SetDefaults(e.ItemId);
                int perStack = Math.Max(1, probe.maxStack);
                while (stack > 0)
                {
                    int n = Math.Min(stack, perStack);
                    Net.DropItem(pos, e.ItemId, n, e.Prefix);
                    stack -= n;
                }
            }
        }
    }
}
