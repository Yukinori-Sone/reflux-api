namespace Reflux.Api
{
    /// <summary>
    /// What kind of result a recorded play is, sent as "resultKind" with /api/latest and plays.jsonl. The monitor decides from it
    /// whether to keep the screenshot of the result screen (its settings, one check per kind).
    ///  - first: played to the end and cleared, the chart had no record before (NO PLAY and EX 0)
    ///  - pb / nopb: played to the end and cleared, the game's best (clear lamp, EX score, miss count) went up / didn't.
    ///    A higher DJ LEVEL always comes with a higher EX score. A DAN gauge play to the end (lamp NO PLAY, with a miss count)
    ///    counts as cleared, only its EX score and miss count are compared. PERFECT counts as FULL COMBO (the game keeps no PERFECT)
    ///  - failed: played to the end, FAILED
    ///  - dead: ended before the end because the gauge ran out (HARD / EX HARD / DAN)
    ///  - quit: ended before the end, closed by the player
    ///  - idle: ended before the end because 50 or more notes in a row were left unhit (any gauge, seen with EASY on 2026111100)
    /// dead, quit and idle come from the end state of each side in the judge data (JudgeLayout.P1EndState: 2 gauge ran out,
    /// 3 left unhit, 5 closed);
    /// when neither is there (layout moved), by the gauge: HARD / EX HARD / DAN = dead, others = quit.
    /// DOUBLE BATTLE / H-RANDOM plays (not kept by the game) and plays whose previous best is unknown count as pb when cleared.
    /// </summary>
    static class ResultKind
    {
        const int EndGaugeOut = 2, EndIdle = 3, EndClosed = 5;

        /// <summary>Main loop thread, on the result screen (reads the judge data)</summary>
        public static string Of(PlayData play, (Lamp lamp, int exScore, int? missCount)? prev, int? missCount)
        {
            if (play.PrematureEnd)
            {
                var l = Layout.Judge;
                int p1 = Utils.ReadInt32(Offsets.JudgeData, l.P1EndState), p2 = Utils.ReadInt32(Offsets.JudgeData, l.P2EndState);
                if (p1 == EndClosed || p2 == EndClosed) { return "quit"; }
                if (p1 == EndIdle || p2 == EndIdle) { return "idle"; }
                if (p1 == EndGaugeOut || p2 == EndGaugeOut) { return "dead"; }
                var g = play.Gauge;
                return g == "OFF" || g == "ASSIST EASY" || g == "EASY" ? "quit" : "dead";
            }
            var lamp = play.Lamp == Lamp.PFC ? Lamp.FC : play.Lamp;
            if (lamp == Lamp.F) { return "failed"; }
            if (play.Unsaved || !prev.HasValue) { return "pb"; }
            var (prevLamp, prevEx, prevMiss) = prev.Value;
            if (prevLamp == Lamp.NP && prevEx == 0) { return "first"; }
            bool dan = lamp == Lamp.NP;
            bool up = (!dan && lamp > prevLamp) || play.ExScore > prevEx ||
                      (missCount.HasValue && (!prevMiss.HasValue || missCount < prevMiss));
            return up ? "pb" : "nopb";
        }
    }
}
