namespace Reflux
{
    class Judge
    {
        public PlayType playtype;
        public int pgreat;
        public int great;
        public int good;
        public int bad;
        public int poor;
        public int fast;
        public int slow;
        public int combobreak;
        public bool prematureEnd;
        public bool quickRetry;

        public bool PFC { get { return (good + bad + poor) == 0; } }
        /// <summary>
        /// Fetch all the judge information from the play
        /// </summary>
        public void Fetch()
        {
            var l = Api.Layout.Judge; /* Field positions from layout.json or built-in defaults */

            var style = PlayType.P1;
            var p1pgreat = Utils.ReadInt32(Offsets.JudgeData, l.P1Pgreat);
            var p1great = Utils.ReadInt32(Offsets.JudgeData, l.P1Great);
            var p1good = Utils.ReadInt32(Offsets.JudgeData, l.P1Good);
            var p1bad = Utils.ReadInt32(Offsets.JudgeData, l.P1Bad);
            var p1poor = Utils.ReadInt32(Offsets.JudgeData, l.P1Poor);
            var p2pgreat = Utils.ReadInt32(Offsets.JudgeData, l.P2Pgreat);
            var p2great = Utils.ReadInt32(Offsets.JudgeData, l.P2Great);
            var p2good = Utils.ReadInt32(Offsets.JudgeData, l.P2Good);
            var p2bad = Utils.ReadInt32(Offsets.JudgeData, l.P2Bad);
            var p2poor = Utils.ReadInt32(Offsets.JudgeData, l.P2Poor);
            var p1cb = Utils.ReadInt32(Offsets.JudgeData, l.P1ComboBreak);
            var p2cb = Utils.ReadInt32(Offsets.JudgeData, l.P2ComboBreak);
            var p1fast = Utils.ReadInt32(Offsets.JudgeData, l.P1Fast);
            var p2fast = Utils.ReadInt32(Offsets.JudgeData, l.P2Fast);
            var p1slow = Utils.ReadInt32(Offsets.JudgeData, l.P1Slow);
            var p2slow = Utils.ReadInt32(Offsets.JudgeData, l.P2Slow);
            var p1measure_end = Utils.ReadInt32(Offsets.JudgeData, l.P1MeasureEnd);
            var p2measure_end = Utils.ReadInt32(Offsets.JudgeData, l.P2MeasureEnd);

            if (p1pgreat + p1great + p1good + p1bad + p1poor == 0)
            {
                style = PlayType.P2;
            }
            else if (p2pgreat + p2great + p2good + p2bad + p2poor > 0)
            {
                style = PlayType.DP;
            }

            playtype = style;
            pgreat = p1pgreat + p2pgreat;
            great = p1great + p2great;
            good = p1good + p2good;
            bad = p1bad + p2bad;
            poor = p1poor + p2poor;
            fast = p1fast + p2fast;
            slow = p1slow + p2slow;
            combobreak = p1cb + p2cb;
            prematureEnd = p1measure_end + p2measure_end != 0;

            quickRetry = pgreat + great + good + bad + poor == 0 && !prematureEnd;
        }
    }
}
