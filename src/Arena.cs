using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>config.json "Arena"</summary>
    public class ArenaSettings
    {
        public int Width { get; set; } = 120;
        public int Height { get; set; } = 50;
        /// <summary>Rows of platforms, this many tiles apart, from the floor up.</summary>
        public int PlatformSpacing { get; set; } = 9;
        public int TorchSpacing { get; set; } = 12;
        public string FloorTile { get; set; } = "Stone";
        public string PlatformTile { get; set; } = "Platforms";
        /// <summary>Also clear walls (background) inside the arena.</summary>
        public bool ClearWalls { get; set; } = true;
    }

    /// <summary>One saved arena: where it is and what the tiles looked like before it was built (for /cs arena undo).</summary>
    public class ArenaRecord
    {
        public string Name { get; set; } = "";
        public string Biome { get; set; } = "";
        /// <summary>The world the arena was built in (an undo record must never be applied to another world).</summary>
        public int WorldId { get; set; }
        public string WorldName { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>9 ints per tile, row-major: type, wall, liquid, sTileHeader, bTileHeader, bTileHeader2, bTileHeader3, frameX, frameY.</summary>
        public int[] Tiles { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Builds flat boss arenas out of vanilla tiles: a stone floor, rows of platforms, torches, and optionally a
    /// "biome pad" of enough biome blocks under the floor to make the game treat the arena as that biome
    /// (so Crabulon can be summoned in an arena that counts as a mushroom biome, etc.). Everything it overwrites
    /// is saved so the arena can be undone.
    /// </summary>
    public class ArenaBuilder
    {
        private readonly CalamityShockPlugin _plugin;
        private ArenaSettings S => _plugin.Config.Arena;

        private static string Dir => Path.Combine(JsonStore.Directory, "arenas");

        /// <summary>Biome pads: tile type and the number of tiles the game needs to see in its scan area.</summary>
        private static readonly Dictionary<string, (int tile, int needed)> Pads = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["desert"] = (TileID.Sand, 1500),
            ["snow"] = (TileID.IceBlock, 1500),
            ["corruption"] = (TileID.Ebonstone, 300),
            ["crimson"] = (TileID.Crimstone, 300),
            ["hallow"] = (TileID.Pearlstone, 125),
            ["jungle"] = (TileID.JungleGrass, 140),
            ["mushroom"] = (TileID.MushroomGrass, 100),
        };

        public static IEnumerable<string> PadNames => Pads.Keys;

        public ArenaBuilder(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        public List<string> ListSaved()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                return Directory.GetFiles(Dir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Builds an arena centred horizontally on the player, floor at the player's feet. Returns null on success.</summary>
        public string Build(TSPlayer p, string name, string biome)
        {
            if (p == null || !p.RealPlayer || p.TPlayer == null) return "Only an in-game player can build an arena.";
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) return "Give the arena a simple name (letters, digits, - or _).";
            if (File.Exists(Path.Combine(Dir, name + ".json"))) return $"An arena named '{name}' already exists. /cs arena undo {name} first.";
            biome = (biome ?? "").Trim();
            (int tile, int needed) pad = (0, 0);
            if (biome.Length > 0 && !biome.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                if (!Pads.TryGetValue(biome, out pad)) return "Unknown biome pad. Use one of: " + string.Join(", ", Pads.Keys) + " (or none).";
            }
            if (!Ids.TryTile(S.FloorTile, out int floorTile) || floorTile < 0 || floorTile >= TileID.Count || !Main.tileSolid[floorTile] || Main.tileSolidTop[floorTile])
                return $"config Arena.FloorTile '{S.FloorTile}' is not a solid block tile.";
            if (!Ids.TryTile(S.PlatformTile, out int platformTile) || platformTile < 0 || platformTile >= TileID.Count || !TileID.Sets.Platforms[platformTile])
                return $"config Arena.PlatformTile '{S.PlatformTile}' is not a platform tile.";

            int width = Math.Clamp(S.Width, 20, 300);
            int height = Math.Clamp(S.Height, 10, 150);
            int padRows = pad.needed > 0 ? (int)Math.Ceiling(pad.needed / (double)width) + 1 : 0;
            int x0 = (int)(p.TPlayer.Center.X / 16f) - width / 2;
            int floorY = (int)(p.TPlayer.Bottom.Y / 16f);           // tile row directly under the player's feet
            int y0 = floorY - height;                               // top of the arena
            int totalHeight = height + 1 + padRows;                 // arena + floor + pad
            if (x0 < 20 || x0 + width >= Main.maxTilesX - 20 || y0 < 20 || y0 + totalHeight >= Main.maxTilesY - 20)
                return "Too close to the edge of the world for an arena this size.";
            int containers = CountContainers(x0, y0, width, totalHeight);
            if (containers > 0) return $"The arena would overwrite {containers} chest/sign/entity tile(s) (their contents would be lost). Move them or build elsewhere.";

            // 1. snapshot, and save the undo record before touching anything
            var rec = new ArenaRecord
            {
                Name = name, Biome = pad.tile > 0 ? biome.ToLowerInvariant() : "",
                WorldId = Main.worldID, WorldName = Main.worldName ?? "",
                X = x0, Y = y0, Width = width, Height = totalHeight,
            };
            rec.Tiles = Snapshot(x0, y0, width, totalHeight);
            string recordPath = Path.Combine(Dir, name + ".json");
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(recordPath, System.Text.Json.JsonSerializer.Serialize(rec, JsonStore.Options));
            }
            catch (Exception ex)
            {
                return "Could not save the arena undo record, so nothing was built: " + ex.Message;
            }

            try
            {
                // 2. clear the arena volume
                for (int x = x0; x < x0 + width; x++)
                {
                    for (int y = y0; y < floorY; y++)
                    {
                        var t = Main.tile[x, y];
                        if (t == null) continue;
                        if (S.ClearWalls) t.ClearEverything();
                        else { t.ClearTile(); t.liquid = 0; }
                    }
                }
                // 3. floor + biome pad
                for (int x = x0; x < x0 + width; x++)
                {
                    SetBlock(x, floorY, floorTile);
                    for (int r = 1; r <= padRows; r++) SetBlock(x, floorY + r, pad.tile);
                }
                // 4. platforms and torches
                int spacing = Math.Max(4, S.PlatformSpacing);
                int torchEvery = Math.Max(3, S.TorchSpacing);
                for (int y = floorY - spacing; y > y0 + 2; y -= spacing)
                {
                    for (int x = x0 + 2; x < x0 + width - 2; x++)
                    {
                        if (!WorldGen.PlaceTile(x, y, platformTile, true, true)) SetBlock(x, y, platformTile);
                    }
                    for (int x = x0 + 4; x < x0 + width - 4; x += torchEvery)
                        PlaceTorch(x, y - 1);
                }
                for (int x = x0 + 4; x < x0 + width - 4; x += torchEvery) PlaceTorch(x, floorY - 1);
            }
            catch (Exception ex)
            {
                // Put the old tiles back rather than leave half an arena behind.
                Log.Error($"Building arena '{name}' failed ({ex.Message}); restoring the old tiles.");
                try { Restore(rec); } catch { }
                try { File.Delete(recordPath); } catch { }
                try { WorldGen.RangeFrame(x0 - 1, y0 - 1, x0 + width + 1, y0 + totalHeight + 1); } catch { }
                SyncRect(x0 - 1, y0 - 1, width + 2, totalHeight + 2);
                return "Building the arena failed: " + ex.Message;
            }

            // 5. frame and sync
            try { WorldGen.RangeFrame(x0 - 1, y0 - 1, x0 + width + 1, y0 + totalHeight + 1); } catch { }
            SyncRect(x0 - 1, y0 - 1, width + 2, totalHeight + 2);
            Log.Info($"{p.Name} built arena '{name}' ({width}x{height}{(pad.tile > 0 ? ", " + biome + " pad " + padRows + " rows" : "")}) at {x0},{y0}");
            return null;
        }

        /// <summary>Restores the tiles an arena replaced. Returns null on success.</summary>
        public string Undo(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) return "Give the arena's name (letters, digits, - or _). /cs arena list shows them.";
            string path = Path.Combine(Dir, name + ".json");
            if (!File.Exists(path)) return $"No saved arena named '{name}'. /cs arena list shows them.";
            ArenaRecord rec;
            try { rec = System.Text.Json.JsonSerializer.Deserialize<ArenaRecord>(File.ReadAllText(path), JsonStore.Options); }
            catch (Exception ex) { return "Could not read the arena record: " + ex.Message; }
            if (rec == null || rec.Tiles == null || rec.Width <= 0 || rec.Height <= 0 || rec.Tiles.Length != rec.Width * rec.Height * 9) return "The arena record is damaged.";
            if (rec.WorldId != Main.worldID) return $"Arena '{name}' was built in another world ('{rec.WorldName}'); it can only be undone there.";
            if (rec.X < 0 || rec.Y < 0 || rec.X + rec.Width >= Main.maxTilesX || rec.Y + rec.Height >= Main.maxTilesY) return "The arena record lies outside this world.";
            Restore(rec);
            try { WorldGen.RangeFrame(rec.X - 1, rec.Y - 1, rec.X + rec.Width + 1, rec.Y + rec.Height + 1); } catch { }
            SyncRect(rec.X - 1, rec.Y - 1, rec.Width + 2, rec.Height + 2);
            try { File.Delete(path); } catch { }
            Log.Info($"Arena '{name}' undone.");
            return null;
        }

        /// <summary>Chests, signs and tile entities inside a rectangle: overwriting their tiles would lose their contents.</summary>
        private static int CountContainers(int x0, int y0, int w, int h)
        {
            int n = 0;
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    var t = Main.tile[x, y];
                    if (t == null) continue;
                    if (t.active() && t.type < TileID.Count && (Main.tileContainer[t.type] || Main.tileSign[t.type])) n++;
                    else if (Terraria.DataStructures.TileEntity.ByPosition.ContainsKey(new Terraria.DataStructures.Point16(x, y))) n++;
                }
            }
            return n;
        }

        // ------------------------------------------------------------------ tiles

        private static void SetBlock(int x, int y, int type)
        {
            var t = Main.tile[x, y];
            if (t == null) return;
            t.ClearEverything();
            t.ResetToType((ushort)type);
        }

        private static void PlaceTorch(int x, int y)
        {
            var t = Main.tile[x, y];
            if (t == null) return;
            if (t.active()) return;
            if (!WorldGen.PlaceTile(x, y, TileID.Torches, true, true))
            {
                t.ClearTile();
                t.ResetToType(TileID.Torches);
                t.frameX = 0;
                t.frameY = 0;
            }
        }

        private static int[] Snapshot(int x0, int y0, int w, int h)
        {
            var data = new int[w * h * 9];
            int i = 0;
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    var t = Main.tile[x, y];
                    if (t == null) { i += 9; continue; }
                    data[i++] = t.type;
                    data[i++] = t.wall;
                    data[i++] = t.liquid;
                    data[i++] = t.sTileHeader;
                    data[i++] = t.bTileHeader;
                    data[i++] = t.bTileHeader2;
                    data[i++] = t.bTileHeader3;
                    data[i++] = t.frameX;
                    data[i++] = t.frameY;
                }
            }
            return data;
        }

        private static void Restore(ArenaRecord rec)
        {
            int i = 0;
            for (int y = rec.Y; y < rec.Y + rec.Height; y++)
            {
                for (int x = rec.X; x < rec.X + rec.Width; x++)
                {
                    var t = Main.tile[x, y];
                    if (t == null) { i += 9; continue; }
                    t.type = (ushort)rec.Tiles[i++];
                    t.wall = (ushort)rec.Tiles[i++];
                    t.liquid = (byte)rec.Tiles[i++];
                    t.sTileHeader = (ushort)rec.Tiles[i++];
                    t.bTileHeader = (byte)rec.Tiles[i++];
                    t.bTileHeader2 = (byte)rec.Tiles[i++];
                    t.bTileHeader3 = (byte)rec.Tiles[i++];
                    t.frameX = (short)rec.Tiles[i++];
                    t.frameY = (short)rec.Tiles[i++];
                }
            }
        }

        /// <summary>
        /// Sends a tile rectangle to everyone in chunks. The packet carries byte-sized dimensions and is limited to
        /// 64 KB; 50x50 stays under that even for the most expensive tiles (a 100x100 chunk of ordinary terrain does not).
        /// </summary>
        public static void SyncRect(int x0, int y0, int w, int h)
        {
            const int chunk = 50;
            for (int x = x0; x < x0 + w; x += chunk)
            {
                for (int y = y0; y < y0 + h; y += chunk)
                {
                    int cw = Math.Min(chunk, x0 + w - x), ch = Math.Min(chunk, y0 + h - y);
                    if (cw <= 0 || ch <= 0) continue;
                    TSPlayer.All.SendTileRect((short)x, (short)y, (byte)cw, (byte)ch);
                }
            }
        }
    }
}
