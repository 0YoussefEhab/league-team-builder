using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    // Posted by Views/LeagueSettings/Guests.cshtml (create) and EditGuest.cshtml (edit). A guest is a
    // lightweight profile an officer manages: name, ranks and lanes. No picture or champions in v1.
    public class LeagueGuestForm
    {
        public int Id { get; set; }
        public string? InGameName { get; set; }

        public LeagueRank CurTier { get; set; } = LeagueRank.Silver;
        public int? CurDiv { get; set; } = 4;
        public LeagueRank PeakTier { get; set; } = LeagueRank.Silver;
        public int? PeakDiv { get; set; } = 4;
        // Checked by default: the peak is the same as the current rank, so the peak fields are ignored.
        public bool PeakSameAsCur { get; set; } = true;

        // Step 8f: one level per lane, 1-5 (the five sliders). 1 = the lane is not played. A new guest starts as Mid 5.
        public int TopLevel { get; set; } = 1;
        public int JungleLevel { get; set; } = 1;
        public int MidLevel { get; set; } = 5;
        public int AdcLevel { get; set; } = 1;
        public int SupportLevel { get; set; } = 1;

        // Before 8f (main lane + other lanes). Still bound so old callers compile, but nothing reads them any more.
        public LeagueLane MainLane { get; set; } = LeagueLane.Mid;
        public List<LeagueLane> Lanes { get; set; } = new();

        public int LevelFor(LeagueLane lane) => lane switch
        {
            LeagueLane.Top => TopLevel,
            LeagueLane.Jungle => JungleLevel,
            LeagueLane.Mid => MidLevel,
            LeagueLane.Adc => AdcLevel,
            _ => SupportLevel,
        };

        public void SetLevel(LeagueLane lane, int level)
        {
            switch (lane)
            {
                case LeagueLane.Top: TopLevel = level; break;
                case LeagueLane.Jungle: JungleLevel = level; break;
                case LeagueLane.Mid: MidLevel = level; break;
                case LeagueLane.Adc: AdcLevel = level; break;
                default: SupportLevel = level; break;
            }
        }

        public Dictionary<LeagueLane, int> Levels() =>
            LeagueLaneLevels.AllLanes.ToDictionary(l => l, l => LevelFor(l));
    }

    public class LeagueGuestRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }
        public LeagueLane MainLane { get; set; }
        public List<LeagueLane> OtherLanes { get; set; } = new();

        // Admin fixes Step 4d: rows of the Linked filter (a member profile that was a guest).
        public bool IsLinked { get; set; }
        public string? UserLabel { get; set; }
        // The active link-log entry. Null on a linked row = an older link with no log.
        public int? LogId { get; set; }
        public DateTime? LinkedUtc { get; set; }
        public string? LinkedBy { get; set; }
        public bool HasLog => LogId.HasValue;
    }

    public class LeagueGuestsPageModel
    {
        // The rows of the chosen filter (guests, linked profiles, or both).
        public List<LeagueGuestRow> Guests { get; set; } = new();
        public string Filter { get; set; } = LeagueGuestFilter.Unlinked;
        public int UnlinkedCount { get; set; }
        public int LinkedCount { get; set; }
        public LeagueGuestForm Form { get; set; } = new();
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class LeagueGuestEditPageModel
    {
        public LeagueGuestForm Form { get; set; } = new();
        public Dictionary<string, string> Errors { get; set; } = new();
    }
}
