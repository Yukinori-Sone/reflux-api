using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Reflux
{
    class ScoreMap
    {
        /// <summary>
        /// Representation of a node in INFINITAS score hashmap
        /// </summary>
        struct ListNode
        {
            public long next;
            public long prev;
            public int diff;
            public int song;
            public int playtype;
            public int uk2;
            public int score;
            public uint misscount;
            public int uk3;
            public int uk4;
            public int lamp;
        }

        public struct ScoreData
        {
            public string songID;
            public Lamp[] lamp;
            public int[] score;
            public uint[] misscount;
            public decimal[] DJPoints;
        }
        static Dictionary<string, ListNode> nodes = new Dictionary<string, ListNode>();
        public static Dictionary<string, ScoreData> Scores { get; private set; }
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(int hProcess,
            Int64 lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);
        /// <summary>
        /// Parser of the hashmap for player top scores
        /// </summary>
        public static void LoadMap()
        {
            nodes.Clear(); /* Drop nodes from a previous attach, otherwise updated scores are never re-read */
            var l = Api.Layout.ScoreMap; /* Field positions from layout.json or built-in defaults */
            long datamap = Offsets.DataMap;
            long nullobj = Utils.ReadInt64(datamap, l.NullNode);
            long startaddress = Utils.ReadInt64(datamap, l.BucketsStart);
            long endaddress = Utils.ReadInt64(datamap, l.BucketsEnd);
            byte[] buffer = new byte[endaddress - startaddress];

            int nRead = 0;
            ReadProcessMemory((int)Utils.handle, startaddress, buffer, buffer.Length, ref nRead);
            List<long> llist_startpoints = new List<long>();
            for (int i = 0; i < buffer.Length / 8; i++)
            {
                long addr = Utils.BytesToInt64(buffer, i * 8);
                if (addr != 0x494fdce0)
                {
                    llist_startpoints.Add(addr);
                }
            }

            buffer = new byte[l.NodeSize];
            foreach (var entrypoint in llist_startpoints)
            {
                if (entrypoint == nullobj) { continue; }
                ReadProcessMemory((int)Utils.handle, entrypoint, buffer, buffer.Length, ref nRead);
                ListNode entry = new ListNode()
                {
                    next = Utils.BytesToInt64(buffer, l.Next, 8),
                    prev = Utils.BytesToInt64(buffer, l.Prev, 8),
                    diff = Utils.BytesToInt32(buffer, l.Difficulty, 4),
                    song = Utils.BytesToInt32(buffer, l.Song, 4),
                    playtype = Utils.BytesToInt32(buffer, l.PlayType, 4),
                    uk2 = Utils.BytesToInt32(buffer, 28, 4),
                    score = Utils.BytesToInt32(buffer, l.Score, 4),
                    misscount = (uint)Utils.BytesToInt32(buffer, l.MissCount, 4),
                    uk3 = Utils.BytesToInt32(buffer, 40, 4),
                    uk4 = Utils.BytesToInt32(buffer, 44, 4),
                    lamp = Utils.BytesToInt32(buffer, l.Lamp, 4)
                };
                FollowLinkedList(entry);

            }

            Scores = new Dictionary<string, ScoreData>();
            /* Parse into more workable format */
            foreach (var node in nodes)
            {
                var sections = node.Key.Split('_');
                if (!Scores.ContainsKey(sections[0]))
                {
                    var scoredata = new ScoreData()
                    {
                        songID = sections[0],
                        score = new int[10],
                        misscount = new uint[10],
                        lamp = new Lamp[10],
                        DJPoints = new decimal[10]
                    };
                    Scores.Add(sections[0], scoredata);

                }
                var difficulty = int.Parse(sections[1]) + (int.Parse(sections[2]) * 5);
                var songScores = Scores[sections[0]];
                songScores.lamp[difficulty] = (Lamp)node.Value.lamp;
                songScores.score[difficulty] = node.Value.score;
                songScores.misscount[difficulty] = node.Value.misscount;
                songScores.DJPoints[difficulty] = Utils.CalculateDJP(sections[0], (Difficulty)difficulty, node.Value.score, (Lamp)node.Value.lamp);
            }
        }
        static void FollowLinkedList(ListNode entrypoint)
        {
            var l = Api.Layout.ScoreMap; /* Field positions from layout.json or built-in defaults */
            var buffer = new byte[l.NodeSize];
            int nRead = 0;

            var traveller = entrypoint;

            while (Utils.songDb.ContainsKey(traveller.song.ToString("D5")))
            {
                var addr = traveller.next;

                string key = $"{traveller.song:D5}_{traveller.diff}_{traveller.playtype}";
                if (nodes.ContainsKey(key))
                {
                    break;
                }
                else
                {
                    nodes.Add(key, traveller);
                }
                ReadProcessMemory((int)Utils.handle, addr, buffer, buffer.Length, ref nRead);
                traveller = new ListNode()
                {
                    next = Utils.BytesToInt64(buffer, l.Next, 8),
                    prev = Utils.BytesToInt64(buffer, l.Prev, 8),
                    diff = Utils.BytesToInt32(buffer, l.Difficulty, 4),
                    song = Utils.BytesToInt32(buffer, l.Song, 4),
                    playtype = Utils.BytesToInt32(buffer, l.PlayType, 4),
                    uk2 = Utils.BytesToInt32(buffer, 28, 4),
                    score = Utils.BytesToInt32(buffer, l.Score, 4),
                    misscount = (uint)Utils.BytesToInt32(buffer, l.MissCount, 4),
                    uk3 = Utils.BytesToInt32(buffer, 40, 4),
                    uk4 = Utils.BytesToInt32(buffer, 44, 4),
                    lamp = Utils.BytesToInt32(buffer, l.Lamp, 4)
                };
            }
        }
    }
}
