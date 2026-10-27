using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace CalamityShock
{
    /// <summary>
    /// Everything about *numbers*: how much tougher an NPC should be, and how to apply that in a way the
    /// vanilla client agrees with.
    ///
    /// Terraria 1.4.5 gives every NPC a float "difficulty" (Classic 1, Expert 2, Master 3, Legendary 4) that is
    /// sent in the NPC sync packet and re-applied by the client through NPC.SetDefaults(type, NPCSpawnParams).
    /// Max life scales 1:1 with that number (the curve keeps extrapolating past Legendary), damage scales a bit
    /// faster, and the player-count bonus for bosses is sent the same way. So if we re-run SetDefaults on the server
    /// with our own numbers *before the first sync*, every client shows the right health bar and deals/takes the right damage.
    ///
    /// Defense is not synced; the server is authoritative for damage dealt to NPCs, so a server-side defense change
    /// is real, the client just briefly predicts a slightly different number until the next life sync.
    /// </summary>
    public static class Difficulty
    {
        /// <summary>The world's own difficulty (1 Classic, 2 Expert, 3 Master, +1 on For The Worthy seeds).</summary>
        public static float World => Main.Difficulty;

        public static float ForEnemy(ModeSettings mode) => World + (mode?.EnemyDifficultyBonus ?? 0f);

        public static float ForBoss(ModeSettings mode, BossDefinition def = null)
        {
            if (def != null && def.Difficulty.HasValue) return Math.Max(0.5f, def.Difficulty.Value);
            return Math.Max(0.5f, World + (mode?.BossDifficultyBonus ?? 0f) + (def?.DifficultyBonus ?? 0f));
        }

        /// <summary>
        /// Re-runs SetDefaults on a freshly spawned NPC with the given parameters, restoring the spawn state that
        /// NPC.NewNPC had already set (position, ai, target...). Call this from the NpcSpawn hook or right after
        /// NPC.NewNPC, i.e. before the NPC's first sync packet goes out.
        /// </summary>
        // Which NPC slots (by generation) this plugin re-scaled, so Transform can tell "scaled by us" from "never scaled".
        private static readonly int[] _appliedGeneration = CreateGenerationTable();

        private static int[] CreateGenerationTable()
        {
            var t = new int[Main.maxNPCs + 1];
            for (int i = 0; i < t.Length; i++) t[i] = -1;
            return t;
        }

        /// <summary>True when Apply() ran on this very NPC instance (slot + generation).</summary>
        public static bool WasApplied(NPC npc)
        {
            if (npc == null || npc.whoAmI < 0 || npc.whoAmI >= _appliedGeneration.Length) return false;
            return _appliedGeneration[npc.whoAmI] == npc.generation;
        }

        public static void Apply(NPC npc, float difficulty, int playerCountOverride, float defenseMultiplier)
        {
            if (npc == null || !npc.active) return;
            if (npc.whoAmI >= 0 && npc.whoAmI < _appliedGeneration.Length) _appliedGeneration[npc.whoAmI] = npc.generation;

            Vector2 bottom = npc.Bottom;
            Vector2 velocity = npc.velocity;
            float[] ai = (float[])npc.ai.Clone();
            int target = npc.target;
            int timeLeft = npc.timeLeft;
            int direction = npc.direction;
            bool wet = npc.wet;
            string givenName = npc.GivenName;
            bool fromStatue = npc.SpawnedFromStatue;

            var spawnParams = new NPCSpawnParams
            {
                difficultyOverride = difficulty,
                playerCountForMultiplayerDifficultyOverride = playerCountOverride > 0 ? playerCountOverride : (int?)null,
            };

            npc.SetDefaults(npc.netID, spawnParams);

            npc.Bottom = bottom;
            npc.velocity = velocity;
            for (int i = 0; i < NPC.maxAI && i < ai.Length; i++) npc.ai[i] = ai[i];
            npc.target = target;
            npc.timeLeft = timeLeft;
            npc.direction = direction;
            npc.wet = wet;
            npc.active = true;
            npc.SpawnedFromStatue = fromStatue;
            if (!string.IsNullOrEmpty(givenName)) npc.GivenName = givenName;

            if (Math.Abs(defenseMultiplier - 1f) > 0.001f)
                npc.defense = Math.Max(0, (int)Math.Round(npc.defense * defenseMultiplier));

            npc.netUpdate = true;
        }

        /// <summary>Human readable summary used by /cs status.</summary>
        public static string Describe(NPC npc)
        {
            if (npc == null) return "";
            return $"difficulty {npc.difficulty:0.##}, {npc.life}/{npc.lifeMax} HP, {npc.damage} dmg, {npc.defense} def" +
                   (npc.statsAreScaledForThisManyPlayers > 1 ? $", scaled for {npc.statsAreScaledForThisManyPlayers} players" : "");
        }
    }
}
