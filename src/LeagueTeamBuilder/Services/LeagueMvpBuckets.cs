using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // MVP redesign, Step 5a: the three groups the MVP counts and win records are split into. Pure rules (no EF, no clock).
    // Design: STEP-0-MVP-REDESIGN.md, "Counts (owner design, 22 Oct)".
    //
    // WIRED IN (Step 5b): LeagueOverviewStats.DraftMvpCounts takes the CustomDraft number from MvpCounts for the Overview, rank tie-break,
    // Leaderboard MVP column, Player page and MVP King badge. NOT YET USED: the Clash and CustomAram numbers, and Records (Steps 5c/5d).
    // Only FINISHED matches (status Done) are passed in.
    //
    //   CustomDraft = a custom game with ModeKey "normal"          -> the main number everywhere
    //   Clash       = a Clash game, draft or ARAM                  -> its own number, never added to the draft one
    //   CustomAram  = a custom game with ModeKey "aram" or "arena" -> recorded, shown on the Player page only
    //   Other       = a custom game with an unknown or empty ModeKey (cannot happen for games saved by the site): counted nowhere

    public enum LeagueMatchBucket { CustomDraft = 0, Clash = 1, CustomAram = 2, Other = 3 }

    // MVPs of one person in each group.
    public record BucketMvpCounts(int CustomDraft, int Clash, int CustomAram);

    // Games, wins and losses of one person in one group. A game with no recorded winner is a game but neither a win nor a loss.
    public record BucketRecord(int Games, int Wins, int Losses)
    {
        public int Decided => Wins + Losses;
    }

    public record BucketRecords(BucketRecord CustomDraft, BucketRecord Clash, BucketRecord CustomAram);

    public static class LeagueMvpBuckets
    {
        public static LeagueMatchBucket BucketOf(LeagueMatchKind kind, string? modeKey)
        {
            if (kind == LeagueMatchKind.Clash) return LeagueMatchBucket.Clash;
            return (modeKey ?? "").Trim().ToLowerInvariant() switch
            {
                "normal" => LeagueMatchBucket.CustomDraft,
                "aram" or "arena" => LeagueMatchBucket.CustomAram,
                _ => LeagueMatchBucket.Other,
            };
        }

        // MVP count per profile and group. One credit per match per person.
        //   Custom games (draft, ARAM, Arena): the counted match MVP (MvpPicks.CountedMvp: the all-stats pick, else the K/D/A pick).
        //     The losing team's Team MVP never counts, as before.
        //   Clash games: a Clash game has only our team, so the pick is the Match MVP when we won and the Team MVP when we lost.
        //     Both count, in the Clash number only.
        public static Dictionary<int, BucketMvpCounts> MvpCounts(IEnumerable<(LeagueMatchKind Kind, string? ModeKey, MvpPicks Picks)> matches)
        {
            var draft = new Dictionary<int, int>();
            var clash = new Dictionary<int, int>();
            var aram = new Dictionary<int, int>();

            static void Add(Dictionary<int, int> d, int? id)
            {
                if (id is int x) d[x] = d.GetValueOrDefault(x) + 1;
            }

            foreach (var (kind, modeKey, picks) in matches)
            {
                switch (BucketOf(kind, modeKey))
                {
                    case LeagueMatchBucket.CustomDraft: Add(draft, picks.CountedMvp); break;
                    case LeagueMatchBucket.CustomAram: Add(aram, picks.CountedMvp); break;
                    case LeagueMatchBucket.Clash: Add(clash, picks.CountedMvp ?? picks.CountedTeamMvp); break;
                }
            }

            return draft.Keys.Union(clash.Keys).Union(aram.Keys).ToDictionary(
                id => id,
                id => new BucketMvpCounts(draft.GetValueOrDefault(id), clash.GetValueOrDefault(id), aram.GetValueOrDefault(id)));
        }

        // Games, wins and losses per profile and group. Same counting as LeagueLeaderboardRules.Records (one row per person per
        // match, rows with no profile count for nobody), but split by group; a game in the Other group counts nowhere.
        public static Dictionary<int, BucketRecords> Records(IEnumerable<OvMatch> done, IEnumerable<OvRow> rows)
        {
            var info = done.ToDictionary(m => m.Id, m => (Winner: m.WinnerTeamIndex, Bucket: BucketOf(m.Kind, m.ModeKey)));
            var result = new Dictionary<int, BucketRecords>();

            foreach (var person in rows.Where(r => r.ProfileId != null && info.ContainsKey(r.MatchId)).GroupBy(r => r.ProfileId!.Value))
            {
                var games = new int[3];
                var wins = new int[3];
                var losses = new int[3];
                foreach (var match in person.GroupBy(r => r.MatchId))
                {
                    var (winner, bucket) = info[match.Key];
                    if (bucket == LeagueMatchBucket.Other) continue;
                    var b = (int)bucket;
                    games[b]++;
                    if (winner == null) continue;
                    if (match.First().TeamIndex == winner) wins[b]++; else losses[b]++;
                }
                result[person.Key] = new BucketRecords(
                    new BucketRecord(games[0], wins[0], losses[0]),
                    new BucketRecord(games[1], wins[1], losses[1]),
                    new BucketRecord(games[2], wins[2], losses[2]));
            }
            return result;
        }
    }
}
