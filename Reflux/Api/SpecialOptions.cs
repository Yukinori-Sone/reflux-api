using System;

namespace Reflux.Api
{
    /// <summary>
    /// Options of the premium course (checked on 2026-11-28 with INFINITAS 2026111100):
    ///  - ALL SCRATCH (shown ALL-SCR) and REGUL-SPEED: int32 switches in the play options (SettingsLayout.AllScratch / RegulSpeed, P1 side;
    ///    the P2 side at + p2Offset like the other options, not checked). The game keeps no play data and no best for them, so such a play
    ///    is built by Reflux like DOUBLE BATTLE / H-RANDOM (UnsavedPlay).
    ///  - SPECIAL GAUGE OPTION (DAN gauge with a start percent, or EROSION gauge with a level 1..5): a separate option item. Where the game
    ///    keeps it was not found (not in bm2dx's image, the option screen's working copy is on the heap). When it is on, the normal gauge
    ///    option reads OFF (0) once the song is decided. It is told from the gauge when the play starts: the normal (groove) gauge starts at
    ///    22 %, the DAN gauge at the chosen start percent, the EROSION gauge at 100 %. So a play with the gauge OFF that started at another
    ///    percent is a SPECIAL GAUGE play: DAN under 100 %, EROSION at 100 %. The EROSION level is not known.
    /// </summary>
    static class SpecialOptions
    {
        const int GrooveStart = 22;
        static readonly object sync = new object();
        static int? startGauge;

        /// <summary>Main loop: the game state became playing. Reads the gauge before the first notes (the play has a lead-in)</summary>
        public static void OnPlayStart()
        {
            int? value = null;
            try
            {
                var l = Layout.Judge;
                value = Utils.ReadInt32(Offsets.JudgeData, l.P1Gauge) + Utils.ReadInt32(Offsets.JudgeData, l.P2Gauge);
            }
            catch (Exception e)
            {
                Utils.Except(e, "SpecialGaugeStart");
            }
            lock (sync) { startGauge = value; }
        }

        /// <summary>The gauge percent read when the play started, null when unknown</summary>
        public static int? StartGauge { get { lock (sync) { return startGauge; } } }

        /// <summary>Settings.Fetch: read ALL SCRATCH / REGUL-SPEED and name a SPECIAL GAUGE play's gauge ("DAN 70%" / "EROSION")</summary>
        public static void Apply(Settings settings, PlayType playstyle)
        {
            var l = Layout.Settings;
            int side = playstyle == PlayType.P2 ? l.P2Offset : 0;
            settings.allScratch = Utils.ReadInt32(Offsets.PlaySettings, side + l.AllScratch) == 1;
            settings.regulSpeed = Utils.ReadInt32(Offsets.PlaySettings, side + l.RegulSpeed) == 1;
            var start = StartGauge;
            if (settings.gauge == "OFF" && start.HasValue && start.Value != GrooveStart && start.Value > 0)
            {
                /* A groove gauge play whose first notes were judged before the read could start a little over 22: the DAN start
                   percents seen are steps of 10 */
                if (start.Value >= 100) { settings.gauge = "EROSION"; }
                else if (start.Value % 10 == 0) { settings.gauge = $"DAN {start.Value}%"; }
            }
        }

        /// <summary>The gauge names Reflux gives a SPECIAL GAUGE play</summary>
        public static bool IsDan(string gauge) => gauge != null && gauge.StartsWith("DAN");
        public static bool IsErosion(string gauge) => gauge == "EROSION";
    }
}
