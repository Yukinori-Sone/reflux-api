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
    ///  - dead: ended before the end with HARD / EX HARD / DAN gauge (the gauge ran out)
    ///  - quit: ended before the end with another gauge (the player closed it)
    /// TODO: a play closed by the player on HARD / EX HARD / DAN gauge is "dead" for now; nothing found yet tells them apart
    /// (the gauge reads 0 in both). DOUBLE BATTLE / H-RANDOM plays (not kept by the game) and plays whose previous best is
    /// unknown count as pb when cleared.
    /// </summary>
    static class ResultKind
    {
        public static string Of(PlayData play, (Lamp lamp, int exScore, int? missCount)? prev, int? missCount)
        {
            if (play.PrematureEnd)
            {
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
