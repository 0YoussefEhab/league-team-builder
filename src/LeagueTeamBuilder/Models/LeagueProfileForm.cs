using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Http;

namespace LeagueTeamBuilder.Models
{
    // Posted by Views/League/Profile.cshtml. Rank tiers/divisions bind from the rank select
    // (option value = enum number). Champion ids bind from repeated hidden inputs, one list per lane.
    public class LeagueProfileForm
    {
        public string? InGameName { get; set; }

        public LeagueRank CurTier { get; set; } = LeagueRank.Iron;
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; } = LeagueRank.Iron;
        public int? PeakDiv { get; set; }

        // Step 8f: one level per lane, 1-5 (the five sliders). 1 = the lane is not played. The main lane is worked out
        // by the service (the highest level), not posted.
        public int TopLevel { get; set; } = 1;
        public int JungleLevel { get; set; } = 1;
        public int MidLevel { get; set; } = 1;
        public int AdcLevel { get; set; } = 1;
        public int SupportLevel { get; set; } = 1;

        // Before 8f (tick + main radio + tick order). Still bound so old callers compile, but nothing reads them any more.
        public LeagueLane MainLane { get; set; } = LeagueLane.Top;
        public List<LeagueLane> Lanes { get; set; } = new();
        public string? LaneOrder { get; set; }

        public List<int> TopChamps { get; set; } = new();
        public List<int> JungleChamps { get; set; } = new();
        public List<int> MidChamps { get; set; } = new();
        public List<int> AdcChamps { get; set; } = new();
        public List<int> SupportChamps { get; set; } = new();

        public int? DoBetterWithId { get; set; }
        public string? RiotId { get; set; }

        public IFormFile? Avatar { get; set; }
        public bool RemoveAvatar { get; set; }
        // Set by "Copy name and picture": on save the main profile's picture URL is copied once.
        public bool UseMainAvatar { get; set; }

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

        public List<int> ChampsFor(LeagueLane lane) => lane switch
        {
            LeagueLane.Top => TopChamps,
            LeagueLane.Jungle => JungleChamps,
            LeagueLane.Mid => MidChamps,
            LeagueLane.Adc => AdcChamps,
            _ => SupportChamps,
        };
    }

    public record LeagueChampOption(int Id, string Name, bool Retired);

    public class LeagueProfilePageModel
    {
        public LeagueProfileForm Form { get; set; } = new();
        public bool IsNew { get; set; }
        public bool CopiedFromMain { get; set; }
        public string? AvatarPreviewUrl { get; set; }
        public bool HasMainAvatar { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new();
        // Active catalog champions (for the picker's search list).
        public List<LeagueChampOption> Catalog { get; set; } = new();
        // Champions already on this profile per lane, including retired ones.
        public Dictionary<LeagueLane, List<LeagueChampOption>> Selected { get; set; } = new();
        public List<(int Id, string Name)> OtherProfiles { get; set; } = new();
        // Step 5d: read-only "My league stats" block; null while the profile does not exist yet.
        public LeaguePlayerStats? Stats { get; set; }
    }


    // Step 8f: the five lane sliders (levels per lane, 1-5) and the lane pyramid (live = JS rebuilds it while a slider moves).
    public record LeagueLaneSlidersModel(Dictionary<LeagueLane, int> Levels);

    public record LeaguePyramidModel(Dictionary<LeagueLaneTier, List<LaneLevel>> Tiers, bool Live = false);

    // Step 8g: which badges to draw (the _LeagueBadges partial). Icon = icon only (tight slots).
    public record LeagueBadgeModel(bool Rank, bool Mvp, bool Icon = false, bool Clash = false);

    public record LeagueRankSelectModel(string Label, string TierName, LeagueRank Tier, string DivName, int? Div);

    public record LeagueChampionPickerModel(LeagueLane Lane, string FieldName, List<LeagueChampOption> Selected);
}
