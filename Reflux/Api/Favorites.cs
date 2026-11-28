using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// The player's favorite songs (MY FAVORITE folder, registered with the 6 key on song select; per song, for every chart type of the
    /// play side; DOUBLE BATTLE uses the DP ones). Found 2026-11-27 on 2026111100. Two places:
    /// - Live (what song select shows): bm2dx base + Object holds a pointer to an object (vtable, then an MSVC
    ///   std::unordered_map of int play style (0 SP, 1 DP) to std::unordered_set of int song IDs). Its element list { head, size } is at
    ///   MapList; each map node has the play style at MapKey and the set's list { head, size } at SetList; each set node the song ID at SetId.
    ///   Nodes start with { next, prev }, the lists are circular through the head (sentinel) node.
    /// - Saved copy: the play data (pdata /favorite) the game writes when it saves (a mode ends, sent with SavePlayData), in the bm2dx
    ///   image near the profile: SP and DP each { int32 has songs; int32 song IDs[100] }, unused slots -1. Used when the live one can't be read
    /// </summary>
    public class FavoriteLayout
    {
        /// <summary>Pointer to the favorites object, relative to the bm2dx module base</summary>
        public int Object = 0x25DB3E8; /* 2026111100 */
        /// <summary>unordered_map's element list { head node pointer, size } in the object</summary>
        public int MapList = 0x10;
        /// <summary>Play style (int32, 0 SP / 1 DP) in a map node</summary>
        public int MapKey = 0x10;
        /// <summary>unordered_set's element list { head node pointer, size } in a map node</summary>
        public int SetList = 0x20;
        /// <summary>Song ID (int32) in a set node</summary>
        public int SetId = 0x10;
        /// <summary>Saved copy: first song ID slot of SP, relative to the bm2dx module base</summary>
        public int Sp = 0x69B4B4; /* 2026111100 */
        /// <summary>Saved copy: first song ID slot of DP</summary>
        public int Dp = 0x69B648; /* 2026111100: Sp + 0x194 (the header int32 of DP in between) */
        /// <summary>Saved copy: slots per play side (the folder holds up to this many songs)</summary>
        public int Slots = 100;
    }

    public class FavoritesDto
    {
        public List<string> Sp { get; set; } = new List<string>();
        public List<string> Dp { get; set; } = new List<string>();
    }

    static class Favorites
    {
        static int basePid;
        static long moduleBase;
        const int MaxNodes = 1000;

        /// <summary>Read both sides with the main loop's handle and publish them when they changed. Cheap (a few hundred small reads), call every song select poll</summary>
        public static void Refresh()
        {
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { return; }
                if (pid != basePid)
                {
                    moduleBase = (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress;
                    basePid = pid.Value;
                }
                var l = Layout.Current.Favorite;
                var dto = ReadLive(Utils.ReadRaw, moduleBase, l, out var error);
                ApiState.SetDataWarning("favorites", dto == null); /* The saved copy still works, but changes show only after a mode ends */
                if (dto == null)
                {
                    Utils.Debug($"Live favorites not available ({error}), reading the saved copy");
                    dto = ReadSaved(Utils.ReadRaw, moduleBase, l, out error);
                    if (dto == null) { Utils.Debug($"Saved favorites not available ({error})"); return; }
                }
                ApiState.PublishFavorites(dto);
            }
            catch (Exception e)
            {
                Utils.Except(e, "Favorites");
            }
        }

        /// <summary>Walk the live lists with any memory reader (also used by the layout preview). Null when something doesn't look right</summary>
        public static FavoritesDto ReadLive(Func<long, int, byte[]> read, long moduleBase, FavoriteLayout l, out string error)
        {
            error = null;
            long Ptr(long a) => BitConverter.ToInt64(read(a, 8), 0);
            static bool Sane(long p) => p > 0x10000 && p < 0x7FFFFFFFFFFF;
            /* Nodes of a circular list { head, size } (head excluded), null when the walk doesn't come back to the head after size nodes */
            List<long> Nodes(long list, string what, out string err)
            {
                err = null;
                long head = Ptr(list), size = Ptr(list + 8);
                if (!Sane(head) || size < 0 || size > MaxNodes) { err = $"{what}: list at 0x{list:X} has head 0x{head:X}, size {size}"; return null; }
                var nodes = new List<long>();
                for (long n = Ptr(head); n != head; n = Ptr(n))
                {
                    if (!Sane(n) || nodes.Count >= size) { err = $"{what}: list at 0x{list:X} doesn't end after {size} nodes"; return null; }
                    nodes.Add(n);
                }
                if (nodes.Count != size) { err = $"{what}: list at 0x{list:X} has {nodes.Count} nodes, size {size}"; return null; }
                return nodes;
            }
            long obj = Ptr(moduleBase + l.Object);
            if (!Sane(obj)) { error = $"object pointer at 0x{moduleBase + l.Object:X} is 0x{obj:X}"; return null; }
            var map = Nodes(obj + l.MapList, "map", out error);
            if (map == null) { return null; }
            var dto = new FavoritesDto();
            foreach (var node in map)
            {
                int style = BitConverter.ToInt32(read(node + l.MapKey, 4), 0);
                if (style != 0 && style != 1) { error = $"map node 0x{node:X} has play style {style}"; return null; }
                var set = Nodes(node + l.SetList, style == 0 ? "sp" : "dp", out error);
                if (set == null) { return null; }
                var ids = new List<string>();
                foreach (var n in set)
                {
                    int id = BitConverter.ToInt32(read(n + l.SetId, 4), 0);
                    if (id < 1000 || id > 99999) { error = $"set node 0x{n:X} holds {id}"; return null; }
                    ids.Add(id.ToString("D5"));
                }
                ids.Sort(StringComparer.Ordinal);
                if (style == 0) { dto.Sp = ids.Distinct().ToList(); } else { dto.Dp = ids.Distinct().ToList(); }
            }
            return dto;
        }

        /// <summary>Read the saved copy of both sides with any memory reader. Null when a slot holds something that isn't a song ID</summary>
        public static FavoritesDto ReadSaved(Func<long, int, byte[]> read, long moduleBase, FavoriteLayout l, out string error)
        {
            error = null;
            var sp = Side(read, moduleBase + l.Sp, l.Slots, out error);
            if (sp == null) { error = "sp: " + error; return null; }
            var dp = Side(read, moduleBase + l.Dp, l.Slots, out error);
            if (dp == null) { error = "dp: " + error; return null; }
            return new FavoritesDto { Sp = sp, Dp = dp };
        }

        static List<string> Side(Func<long, int, byte[]> read, long address, int slots, out string error)
        {
            error = null;
            var data = read(address, slots * 4);
            var ids = new List<string>();
            for (int i = 0; i < slots; i++)
            {
                int id = BitConverter.ToInt32(data, i * 4);
                if (id == -1) { continue; }
                if (id < 1000 || id > 99999) { error = $"slot {i} at 0x{address + i * 4:X} holds {id}"; return null; }
                ids.Add(id.ToString("D5"));
            }
            ids.Sort(StringComparer.Ordinal);
            return ids.Distinct().ToList();
        }
    }
}
