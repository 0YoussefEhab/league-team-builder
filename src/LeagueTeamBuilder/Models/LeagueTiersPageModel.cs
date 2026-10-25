using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    public class LeagueTiersPageModel
    {
        public List<LeagueTierService.TierRow> Tiers { get; set; } = new();
        public List<LeagueTierService.PlayerTierRow> Players { get; set; } = new();
    }
}
