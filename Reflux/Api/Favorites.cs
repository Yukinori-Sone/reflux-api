using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// The player's favorite songs (MY FAVORITE folder, registered with the 6 key on song select), found 2026-11-27 on 2026111100 in the
    /// bm2dx image near the profile (Player.Profile): SP and DP each { int32 header; int32 song IDs[100] }, the DP block right after the SP
    /// one. Unused slots are -1, the game keeps the IDs sorted as decimal strings. Per song, for every chart type of the play side.
    /// This is the copy written when the game saves the play data (pdata /favorite, sent with SavePlayData when a mode ends): favorites
    /// registered on song select show here only after that. The live state is a std::list on the heap (not read)
    /// </summary>
    public class FavoriteLayout
    {
        /// <summary>First song ID slot of SP, relative to the bm2dx module base</summary>
        public int Sp = 0x69B4B4; /* 2026111100 */
        /// <summary>First song ID slot of DP</summary>
        public int Dp = 0x69B648; /* 2026111100: Sp + 0x194 (the header int32 of DP in between) */
        /// <summary>Slots per play side (the folder holds up to this many songs)</summary>
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

        /// <summary>Read both sides with the main loop's handle and publish them when they changed. Cheap (2 × 400 bytes), call every song select poll</summary>
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
                var dto = Read(Utils.ReadRaw, moduleBase, Layout.Current.Favorite, out var error);
                ApiState.SetDataWarning("favorites", dto == null);
                if (dto == null) { Utils.Debug($"Favorites not available ({error})"); return; }
                ApiState.PublishFavorites(dto);
            }
            catch (Exception e)
            {
                Utils.Except(e, "Favorites");
            }
        }

        /// <summary>Read both sides with any memory reader (also used by the layout preview). Null when a slot holds something that isn't a song ID</summary>
        public static FavoritesDto Read(Func<long, int, byte[]> read, long moduleBase, FavoriteLayout l, out string error)
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
