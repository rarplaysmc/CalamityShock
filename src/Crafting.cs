using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace CalamityShock
{
    public class Ingredient
    {
        public string Item { get; set; } = "";
        public int Stack { get; set; } = 1;
        [JsonIgnore] public int ItemId;
    }

    /// <summary>
    /// A server-side recipe: /cs craft &lt;name&gt;. The vanilla client never learns about it, which is the point -
    /// it lets you hand out vanilla items at a point in progression where the game normally wouldn't.
    /// </summary>
    public class Recipe
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<Ingredient> Ingredients { get; set; } = new List<Ingredient>();
        public string ResultItem { get; set; } = "";
        public int ResultStack { get; set; } = 1;
        /// <summary>0 = none, -1 = random, or a prefix id.</summary>
        public int ResultPrefix { get; set; } = 0;
        /// <summary>Tile the player must stand near: Anvils, WorkBenches, Furnaces, MythrilAnvil, DemonAltar, Hellforge, AlchemyTable... Empty = anywhere.</summary>
        public string RequiresStation { get; set; } = "";
        /// <summary>Custom boss ids or vanilla flags, same as boss requirements.</summary>
        public List<string> RequiresDowned { get; set; } = new List<string>();
        public bool RequiresHardmode { get; set; } = false;
        /// <summary>Only craftable when the active mode's Rank is at least this mode's Rank. Empty = always.</summary>
        public string MinMode { get; set; } = "";

        [JsonIgnore] public int ResultItemId;
        [JsonIgnore] public int StationTileId = -1;

        public bool Resolve()
        {
            if (string.IsNullOrWhiteSpace(Name)) { Log.Warn("recipes.json: a recipe without a Name was skipped"); return false; }
            Name = Name.Trim().ToLowerInvariant();
            if (!Ids.TryItem(ResultItem, out ResultItemId) || ResultItemId <= 0)
            {
                Log.Warn($"recipe '{Name}': unknown ResultItem '{ResultItem}' - recipe disabled");
                return false;
            }
            Ingredients ??= new List<Ingredient>();
            Ingredients.RemoveAll(i => i == null);
            foreach (Ingredient ing in Ingredients)
            {
                if (!Ids.TryItem(ing.Item, out ing.ItemId) || ing.ItemId <= 0)
                {
                    Log.Warn($"recipe '{Name}': unknown ingredient '{ing.Item}' - recipe disabled");
                    return false;
                }
                if (ing.Stack < 1) ing.Stack = 1;
            }
            StationTileId = -1;
            if (!string.IsNullOrWhiteSpace(RequiresStation))
            {
                if (!Ids.TryTile(RequiresStation, out StationTileId))
                {
                    Log.Warn($"recipe '{Name}': unknown station tile '{RequiresStation}' - requirement ignored");
                    StationTileId = -1;
                }
            }
            RequiresDowned ??= new List<string>();
            if (ResultStack < 1) ResultStack = 1;
            return true;
        }

        public string IngredientText() => string.Join(", ", Ingredients.Select(i => $"{i.Stack}x {Ids.ItemName(i.ItemId)}"));
    }

    /// <summary>recipes.json</summary>
    public class RecipeBook
    {
        public List<Recipe> Recipes { get; set; } = new List<Recipe>();

        public static RecipeBook CreateDefault()
        {
            var book = new RecipeBook();
            book.Recipes.Add(new Recipe
            {
                Name = "ancient-chisel",
                Description = "Mine faster after taming the desert.",
                Ingredients = { new Ingredient { Item = "FossilOre", Stack = 10 }, new Ingredient { Item = "GoldBar", Stack = 3 } },
                ResultItem = "AncientChisel",
                RequiresStation = "Anvils",
                RequiresDowned = { "desertscourge" },
            });
            book.Recipes.Add(new Recipe
            {
                Name = "reaver-shark",
                Description = "A pickaxe from the deep, available once the Desert Scourge is down.",
                Ingredients = { new Ingredient { Item = "SharkFin", Stack = 2 }, new Ingredient { Item = "Coral", Stack = 8 }, new Ingredient { Item = "Seashell", Stack = 5 } },
                ResultItem = "ReaverShark",
                RequiresStation = "Anvils",
                RequiresDowned = { "desertscourge" },
            });
            book.Recipes.Add(new Recipe
            {
                Name = "life-crystal",
                Description = "Trade gold and potions for a Life Crystal.",
                Ingredients = { new Ingredient { Item = "GoldBar", Stack = 5 }, new Ingredient { Item = "LesserHealingPotion", Stack = 5 }, new Ingredient { Item = "FallenStar", Stack = 2 } },
                ResultItem = "LifeCrystal",
                RequiresStation = "Anvils",
                RequiresDowned = { "EyeOfCthulhu" },
            });
            book.Recipes.Add(new Recipe
            {
                Name = "hermes-boots",
                Description = "Crabulon's speed, bottled.",
                Ingredients = { new Ingredient { Item = "Silk", Stack = 10 }, new Ingredient { Item = "FallenStar", Stack = 3 }, new Ingredient { Item = "GoldBar", Stack = 5 } },
                ResultItem = "HermesBoots",
                RequiresStation = "WorkBenches",
                RequiresDowned = { "crabulon" },
            });
            book.Recipes.Add(new Recipe
            {
                Name = "frozen-turtle-shell",
                Description = "Cryogen's frost fused into a turtle shell.",
                Ingredients = { new Ingredient { Item = "FrostCore", Stack = 3 }, new Ingredient { Item = "TurtleShell", Stack = 1 }, new Ingredient { Item = "SoulofNight", Stack = 10 } },
                ResultItem = "FrozenTurtleShell",
                RequiresStation = "MythrilAnvil",
                RequiresDowned = { "cryogen" },
                RequiresHardmode = true,
            });
            return book;
        }
    }

    public class Crafting
    {
        private const string FileName = "recipes.json";
        private readonly CalamityShockPlugin _plugin;

        public RecipeBook Book { get; private set; } = new RecipeBook();
        public List<Recipe> Recipes { get; } = new List<Recipe>();

        public Crafting(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        public void Load()
        {
            Book = JsonStore.LoadOrCreate(FileName, RecipeBook.CreateDefault);
            Book.Recipes ??= new List<Recipe>();
            Recipes.Clear();
            foreach (Recipe r in Book.Recipes)
                if (r != null && r.Resolve()) Recipes.Add(r);
            Log.Info($"Loaded {Recipes.Count} recipes.");
        }

        public Recipe Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            return Recipes.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Requirements that are not about items: progression, hardmode, mode rank, station. Returns null when all good.</summary>
        public string CheckGates(TSPlayer player, Recipe r)
        {
            CalamityConfig cfg = _plugin.Config;
            var missing = _plugin.Progression.Unmet(r.RequiresDowned);
            if (missing.Count > 0)
                return "Requires defeating: " + string.Join(", ", missing.Select(m => ProgressionManager.Describe(m, _plugin.Bosses.Catalog)));
            if (r.RequiresHardmode && !Main.hardMode) return "Requires Hardmode.";
            if (!string.IsNullOrEmpty(r.MinMode))
            {
                int need = cfg.RankOf(r.MinMode);
                if (need >= 0 && cfg.CurrentMode.Rank < need) return $"Only available in {r.MinMode} mode or above.";
            }
            if (r.StationTileId >= 0 && !NearTile(player.TPlayer, r.StationTileId, cfg.StationRadiusTiles))
                return $"You must stand near a {r.RequiresStation}.";
            return null;
        }

        /// <summary>Attempts the craft. Returns null on success or the reason it failed.</summary>
        public string TryCraft(TSPlayer player, Recipe r)
        {
            if (player == null || !player.RealPlayer || player.TPlayer == null) return "Only an in-game player can craft.";
            string gate = CheckGates(player, r);
            if (gate != null) return gate;

            bool ssc = Main.ServerSideCharacter;
            int radius = _plugin.Config.GroundCraftRadiusTiles;
            var shortages = new List<string>();
            foreach (Ingredient ing in r.Ingredients)
            {
                int have = ssc ? CountInInventory(player.TPlayer, ing.ItemId) : CountOnGround(player.TPlayer, ing.ItemId, radius);
                if (have < ing.Stack) shortages.Add($"{ing.Stack - have} more {Ids.ItemName(ing.ItemId)}");
            }
            if (shortages.Count > 0)
            {
                string where = ssc ? "in your inventory" : $"dropped on the ground within {radius} tiles of you (server-side characters are off, so ingredients are taken from the ground)";
                return $"Missing {string.Join(", ", shortages)} {where}.";
            }

            foreach (Ingredient ing in r.Ingredients)
            {
                if (ssc) ConsumeFromInventory(player, ing.ItemId, ing.Stack);
                else ConsumeFromGround(player.TPlayer, ing.ItemId, ing.Stack, radius);
            }

            var probe = new Item();
            probe.SetDefaults(r.ResultItemId);
            int remaining = r.ResultStack;
            while (remaining > 0)
            {
                int n = Math.Min(remaining, Math.Max(1, probe.maxStack));
                player.GiveItem(r.ResultItemId, n, r.ResultPrefix);
                remaining -= n;
            }
            Log.Info($"{player.Name} crafted {r.Name}");
            return null;
        }

        // ------------------------------------------------------------------ inventory helpers (server-side characters)

        public static int CountInInventory(Player p, int itemType)
        {
            int n = 0;
            for (int i = 0; i < 58 && i < p.inventory.Length; i++)
            {
                Item it = p.inventory[i];
                if (it != null && it.type == itemType && it.stack > 0) n += it.stack;
            }
            return n;
        }

        /// <summary>Removes count items from the player's inventory and syncs the changed slots. Requires SSC to stick on the client.</summary>
        public static void ConsumeFromInventory(TSPlayer player, int itemType, int count)
        {
            Player p = player.TPlayer;
            for (int i = 0; i < 58 && i < p.inventory.Length && count > 0; i++)
            {
                Item it = p.inventory[i];
                if (it == null || it.type != itemType || it.stack <= 0) continue;
                int take = Math.Min(count, it.stack);
                it.stack -= take;
                count -= take;
                if (it.stack <= 0) it.TurnToAir();
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, i);
            }
        }

        // ------------------------------------------------------------------ ground helpers (no SSC)

        public static int CountOnGround(Player p, int itemType, int radiusTiles)
        {
            float r = radiusTiles * 16f;
            int n = 0;
            for (int i = 0; i < Main.maxItems; i++)
            {
                WorldItem it = Main.item[i];
                if (it == null || !it.active || it.type != itemType || it.stack <= 0) continue;
                if (Vector2.Distance(it.Center, p.Center) > r) continue;
                n += it.stack;
            }
            return n;
        }

        public static void ConsumeFromGround(Player p, int itemType, int count, int radiusTiles)
        {
            float r = radiusTiles * 16f;
            for (int i = 0; i < Main.maxItems && count > 0; i++)
            {
                WorldItem it = Main.item[i];
                if (it == null || !it.active || it.type != itemType || it.stack <= 0) continue;
                if (Vector2.Distance(it.Center, p.Center) > r) continue;
                int take = Math.Min(count, it.stack);
                it.stack -= take;
                count -= take;
                if (it.stack <= 0) Net.RemoveWorldItem(i);
                else it.SyncItem();
            }
        }

        // ------------------------------------------------------------------ stations

        public static bool NearTile(Player p, int tileType, int radiusTiles)
        {
            if (p == null || tileType < 0) return false;
            int cx = (int)(p.Center.X / 16f), cy = (int)(p.Center.Y / 16f);
            for (int x = cx - radiusTiles; x <= cx + radiusTiles; x++)
            {
                if (x < 0 || x >= Main.maxTilesX) continue;
                for (int y = cy - radiusTiles; y <= cy + radiusTiles; y++)
                {
                    if (y < 0 || y >= Main.maxTilesY) continue;
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && tile.type == tileType) return true;
                }
            }
            return false;
        }
    }
}
