namespace Reflux.Api
{
    /// <summary>
    /// Plays the game doesn't keep: DOUBLE BATTLE, H-RANDOM, ALL SCRATCH and REGUL-SPEED (SpecialOptions). For them the game writes no play data (song, difficulty, lamp and
    /// EX score stay 0) and the result screen shows no best score, so Reflux builds the play from the chart being played, the
    /// judges, the gauge and the options instead (checked on 2026-11-04 with INFINITAS 2026101400). Such plays are recorded in
    /// the play history only: not in tracker.db, the game's bests (PlayCounts) or bestdetail.json.
    /// </summary>
    static class UnsavedPlay
    {
        public static bool Applies(Settings settings) => settings.battle || settings.Hran || settings.allScratch || settings.regulSpeed;

        /// <summary>
        /// The chart played. currentSong holds the difficulty within the play style (0..4, ANOTHER is 3 in SP and DP alike):
        /// a play judged on both sides is DP, except DOUBLE BATTLE, which plays the SP chart on both sides
        /// </summary>
        public static (string songId, Difficulty difficulty) Chart(Judge judges, Settings settings)
        {
            var current = Utils.FetchCurrentChart();
            int tier = (int)current.difficulty % 5;
            bool dp = judges.playtype == PlayType.DP && !settings.battle;
            return (current.songID, (Difficulty)(tier + (dp ? 5 : 0)));
        }

        /// <summary>
        /// The clear lamp from the gauge, as the result screen shows it: ended prematurely = FAILED, no combo break = FULL COMBO
        /// (PERFECT without GOOD), else cleared when the gauge is at the border at the end (ASSIST EASY 60%, EASY and normal 80%,
        /// HARD / EX HARD survived), ASSIST CLEAR with an assist option. gauge is the percent at the end (P1 + P2 side)
        /// </summary>
        public static Lamp Lamp(Judge judges, Settings settings, int gauge)
        {
            if (judges.prematureEnd) { return Reflux.Lamp.F; }
            if (judges.combobreak == 0 && judges.pgreat + judges.great > 0) { return judges.PFC ? Reflux.Lamp.PFC : Reflux.Lamp.FC; }
            bool cleared;
            switch (settings.gauge)
            {
                case "ASSIST EASY": cleared = gauge >= 60; break;
                case "EASY":
                case "OFF": cleared = gauge >= 80; break;
                case "HARD":
                case "EX HARD": cleared = gauge > 0; break;
                default: return Reflux.Lamp.NP; /* A gauge Reflux doesn't know (DAN gauge ...): the game keeps no lamp for it */
            }
            if (!cleared) { return Reflux.Lamp.F; }
            if (settings.assist != "OFF") { return Reflux.Lamp.AC; }
            switch (settings.gauge)
            {
                case "ASSIST EASY": return Reflux.Lamp.AC;
                case "EASY": return Reflux.Lamp.EC;
                case "HARD": return Reflux.Lamp.HC;
                case "EX HARD": return Reflux.Lamp.EX;
                default: return Reflux.Lamp.NC;
            }
        }

        /// <summary>DJ LEVEL by the notes shown on the result screen (DOUBLE BATTLE: twice the SP chart, with AUTO SCRATCH too)</summary>
        public static Grade Grade(int exScore, int notes)
        {
            if (notes <= 0) { return Reflux.Grade.NP; }
            double part = notes * 2 / 9.0;
            if (exScore >= part * 8) { return Reflux.Grade.AAA; }
            if (exScore >= part * 7) { return Reflux.Grade.AA; }
            if (exScore >= part * 6) { return Reflux.Grade.A; }
            if (exScore >= part * 5) { return Reflux.Grade.B; }
            if (exScore >= part * 4) { return Reflux.Grade.C; }
            if (exScore >= part * 3) { return Reflux.Grade.D; }
            if (exScore >= part * 2) { return Reflux.Grade.E; }
            return Reflux.Grade.F;
        }
    }
}
