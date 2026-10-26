using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Phase B1: the picture address of every rank tier, kept in memory so views and scripts do not query the database for
    // each badge. Registered as a SINGLETON. Rebuilt from the database at start-up (Program.cs) and after every change by
    // LeagueRankBadgeService. Known limit: with several app instances behind one database, a change shows on the other
    // instances after they restart (fine for the demo; plan, Phase B).
    public class RankBadgeCatalog
    {
        public const string DefaultFolder = "/img/ranks/default/";

        // Replaced as a whole (never edited in place), so a reader never sees a half-updated list.
        private volatile IReadOnlyDictionary<LeagueRank, string> _uploaded = new Dictionary<LeagueRank, string>();

        // The placeholder picture for a tier (a neutral SVG per tier, added in B2).
        public static string DefaultUrl(LeagueRank rank) => DefaultFolder + rank.ToString().ToLowerInvariant() + ".svg";

        // What an <img> should show for this tier: the uploaded picture, else the placeholder. Never null.
        public string Url(LeagueRank rank) => _uploaded.TryGetValue(rank, out var url) ? url : DefaultUrl(rank);

        // The uploaded picture, or null when the tier uses the placeholder.
        public string? UploadedUrl(LeagueRank rank) => _uploaded.TryGetValue(rank, out var url) ? url : null;

        // Every tier, weakest first, with the address to show. For the page and for the scripts' config (B2: cfg.icons).
        public IReadOnlyList<(LeagueRank Tier, string Url)> All() =>
            Enum.GetValues<LeagueRank>().OrderBy(r => (int)r).Select(r => (r, Url(r))).ToList();

        // Same, keyed by the lower-case tier name ("iron", "gold"), the way the scripts look icons up.
        public IReadOnlyDictionary<string, string> ByKey() =>
            All().ToDictionary(x => x.Tier.ToString().ToLowerInvariant(), x => x.Url);

        public void Replace(IEnumerable<LeagueRankBadge> rows) =>
            _uploaded = rows.Where(r => !string.IsNullOrWhiteSpace(r.ImageUrl) && Enum.IsDefined(r.Tier))
                .ToDictionary(r => r.Tier, r => r.ImageUrl!);
    }
}
