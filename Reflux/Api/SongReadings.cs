using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Reflux.Api
{
    /// <summary>
    /// Readings of the songs (sort / search table on the heap, found 2026-11-04 on 2026101400): bm2dx base + vector holds { begin, end }
    /// of an array in song list order, one entry per song (entries of songs without readings are empty, song ID 0).
    /// Strings are MSVC std::string: up to 15 bytes inside the entry, longer ones on the heap (first 8 bytes = pointer).
    /// The song select lists sort by the title reading (e.g. OTHERS: .59 = テンゴク, Ｘ↑Ｘ↓ = テンジョウテンゲ).
    /// </summary>
    public class ReadingLayout
    {
        /// <summary>Position of the vector { begin, end } relative to the bm2dx module base</summary>
        public int Vector = 0x2908798;
        public int EntrySize = 0x88;
        /// <summary>Song ID, int32 (0 for an empty entry)</summary>
        public int Id = 0x0;
        /// <summary>Artist reading, katakana (Shift-JIS)</summary>
        public int ArtistReading = 0x8;
        /// <summary>Title reading, katakana (Shift-JIS)</summary>
        public int TitleReading = 0x28;
        /// <summary>Search key of the title: upper case, only the low byte of each UTF-16 character (garbled for non-ASCII, not returned)</summary>
        public int SearchKey = 0x48;
        /// <summary>Search key of the artist, same encoding (not returned)</summary>
        public int ArtistKey = 0x68;
        /// <summary>std::string: length (int64) and capacity (int64) after the 16 byte buffer; capacity over 15 = heap</summary>
        public int StringLength = 0x10;
        public int StringCapacity = 0x18;
    }

    public class SongReading
    {
        public string Title;
        public string Artist;
    }

    static class SongReadings
    {
        const int MaxEntries = 20000;
        const int MaxString = 0x400;
        static int basePid;
        static long moduleBase;

        /// <summary>True when the last read found no readings</summary>
        public static bool Missing { get; private set; }
        static int retries;

        /// <summary>On song select: read again (republish the songs) when the last read found nothing, a few times per attach</summary>
        public static bool ShouldRetry()
        {
            if (!Missing) { retries = 0; return false; }
            return retries++ < 5;
        }

        /// <summary>Read the table with the main loop's handle. Song ID (D5) → readings, empty when not available. Never throws</summary>
        public static Dictionary<string, SongReading> Read()
        {
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { Missing = true; return new Dictionary<string, SongReading>(); }
                if (pid != basePid)
                {
                    moduleBase = (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress;
                    basePid = pid.Value;
                }
                var result = Read(Utils.ReadRaw, moduleBase, Layout.Current.Reading, out var error);
                Missing = result.Count == 0;
                ApiState.SetDataWarning("readings", Missing);
                if (Missing) { Console.WriteLine($"[API] Song readings not available ({error ?? "empty"}). Check GET /api/layout/preview?struct=reading"); }
                return result;
            }
            catch (Exception e)
            {
                Utils.Except(e, "SongReadings");
                Missing = true;
                return new Dictionary<string, SongReading>();
            }
        }

        /// <summary>Read the table with any memory reader (also used by the layout preview)</summary>
        public static Dictionary<string, SongReading> Read(Func<long, int, byte[]> read, long moduleBase, ReadingLayout l, out string error)
        {
            error = null;
            var result = new Dictionary<string, SongReading>();
            var vector = read(moduleBase + l.Vector, 16);
            long begin = BitConverter.ToInt64(vector, 0), end = BitConverter.ToInt64(vector, 8);
            if (begin == 0 || end <= begin) { error = "vector is empty"; return result; }
            long count = (end - begin) / l.EntrySize;
            if ((end - begin) % l.EntrySize != 0 || count > MaxEntries) { error = $"vector size 0x{end - begin:X} doesn't fit the entry size"; return result; }
            var data = read(begin, (int)(end - begin));
            var sjis = Sjis();
            string Str(int o)
            {
                long length = BitConverter.ToInt64(data, o + l.StringLength);
                long capacity = BitConverter.ToInt64(data, o + l.StringCapacity);
                if (length <= 0 || length > MaxString || capacity < length) { return length == 0 ? "" : null; }
                var bytes = capacity > 15 ? read(BitConverter.ToInt64(data, o), (int)length) : data.AsSpan(o, (int)length).ToArray();
                return sjis.GetString(bytes);
            }
            for (int i = 0; i < count; i++)
            {
                int o = i * l.EntrySize;
                int id = BitConverter.ToInt32(data, o + l.Id);
                if (id == 0) { continue; }
                if (id < 1000 || id > 99999) { error = $"entry {i} has song ID {id}"; result.Clear(); return result; }
                result[id.ToString("D5")] = new SongReading
                {
                    Title = Str(o + l.TitleReading),
                    Artist = Str(o + l.ArtistReading),
                };
            }
            return result;
        }

        static Encoding Sjis()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("shift_jis");
        }
    }
}
