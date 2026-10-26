using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8b: the numbers behind the League Overview, as pure static rules (no EF, no clock), so they are
    // unit-testable without a database. LeagueOverviewService loads the rows and calls these.
    // Decisions: PLAN-league.md, "Step 8 - owner answers" and "Step 8b - decisions".
    // Only FINISHED matches (status Done) are ever passed in.

    // One finished match. WhenUtc = PlayedUtc (the editable "Played on" date; results/export step 1 changed it from FinishedUtc).
    public record OvMatch(int Id, LeagueMatchKind Kind, string ModeKey, int? ClashEventId, int? WinnerTeamIndex,
        DateTime WhenUtc, int? DurationSeconds);

    // One player row of a finished match. Lane = the lane actually played (PlayedLane, else the builder lane).
    // ProfileId is null when the profile was deleted or merged away; such rows count for no person.
    public record OvRow(int MatchId, int TeamIndex, int? ProfileId, LeagueLane? Lane, int? ChampionId);

    // A finished Clash event (status Done).
    public record OvClashEvent(int Id, string Title, DateTime StartsAtUtc, DateTime? EndedUtc);

    public record OvGameCounts(int Customs, int Draft, int Aram, int Arena, int ClashGames);

    // Wins per side in the two-side custom games (balance scoreboard). SideA = team index 0 (Team 1), SideB = index 1 (Team 2).
    public record OvSideWins(int SideA, int SideB);

    // The Clash series result: Wins = games the Clash team (team index 0) won.
    public record OvClashRecord(int Wins, int Games);

    // The chosen "best Clash team": the event and the game whose team is shown (the event's most recent won game).
    public record OvBestClash(OvClashEvent Event, int Wins, int Games, int GameMatchId);

    public static class LeagueOverviewStats
    {
        // A finished Clash counts as "won" when the Clash team won this many of its games. Owner decision 2026-10-18:
        // 3 wins (a team that has won 3 does not play a 4th); it was 4 on 2026-10-14. One number to change
        // (PLAN-league.md, "Step 8b - decisions").
        public const int MinWinsForWonClash = 3;

        // MVP count per profile: matches where the player is the COUNTED match MVP = the all-stats pick, else the K/D/A pick when
        // the game has no all-stats pick (owner decision 2026-10-19, it replaced "either score counts"). One credit per
        // match per player. Losing-team picks do not count. Same rule as
        // LeagueMvpRules.CountMvpMatches, in one pass for everybody.
        public static Dictionary<int, int> MvpCounts(IEnumerable<MvpPicks> matches)
        {
            var counts = new Dictionary<int, int>();
            foreach (var m in matches)
            {
                if (m.CountedMvp is int a) counts[a] = counts.GetValueOrDefault(a) + 1;
            }
            return counts;
        }

        // OLD (before Step 5b of the MVP redesign; nothing calls it any more, DraftMvpCounts replaced it).
        // The ONE place that applied "MVPs count from CUSTOM matches only" (owner decision 2026-10-14): Clash games are
        // dropped here, then MvpCounts runs. The Overview (8b) and the Leaderboard (8d) both call this; the profile (8e)
        // and the MVP badge (8g) must call it too instead of filtering on their own.
        public static Dictionary<int, int> CustomMvpCounts(IEnumerable<(LeagueMatchKind Kind, MvpPicks Picks)> matches) =>
            MvpCounts(matches.Where(m => m.Kind == LeagueMatchKind.Custom).Select(m => m.Picks));

        // MVP redesign, Step 5b: the MVP number the ranking uses = CUSTOM DRAFT matches only (ModeKey "normal"). Clash games and
        // custom ARAM/Arena games are not in it (they have their own numbers, shown in Steps 5c/5d). The ONE place the Overview
        // (Top 3 MVPs, rank tie-break), the Leaderboard, the Player page and the MVP King badge take their MVP number from, so
        // they always agree. Rule itself: LeagueMvpBuckets.MvpCounts.
        public static Dictionary<int, int> DraftMvpCounts(IEnumerable<(LeagueMatchKind Kind, string? ModeKey, MvpPicks Picks)> matches)
        {
            var all = LeagueMvpBuckets.MvpCounts(matches);
            var draft = new Dictionary<int, int>();
            foreach (var (id, c) in all)
                if (c.CustomDraft > 0) draft[id] = c.CustomDraft;
            return draft;
        }

        // Custom = every custom game; Draft / Aram / Arena = the custom games of that mode. Clash games count apart.
        public static OvGameCounts GameCounts(IEnumerable<OvMatch> done)
        {
            int customs = 0, draft = 0, aram = 0, arena = 0, clash = 0;
            foreach (var m in done)
            {
                if (m.Kind == LeagueMatchKind.Clash) { clash++; continue; }
                customs++;
                switch ((m.ModeKey ?? "").ToLowerInvariant())
                {
                    case "normal": draft++; break;
                    case "aram": aram++; break;
                    case "arena": arena++; break;
                }
            }
            return new OvGameCounts(customs, draft, aram, arena, clash);
        }

        // Balance scoreboard (C3): wins per side. Only finished CUSTOM games with two sides (ModeKey "normal" = draft, "aram");
        // arena has many teams and Clash is our team against an opponent, so neither says anything about Side A vs Side B.
        // A game with no recorded winner counts for neither side.
        public static OvSideWins SideWins(IEnumerable<OvMatch> done)
        {
            int a = 0, b = 0;
            foreach (var m in done)
            {
                if (m.Kind != LeagueMatchKind.Custom) continue;
                var mode = (m.ModeKey ?? "").ToLowerInvariant();
                if (mode != "normal" && mode != "aram") continue;
                if (m.WinnerTeamIndex == 0) a++;
                else if (m.WinnerTeamIndex == 1) b++;
            }
            return new OvSideWins(a, b);
        }

        // Per Clash event: games finished and games the Clash team (index 0) won.
        public static Dictionary<int, OvClashRecord> ClashRecords(IEnumerable<OvMatch> done)
        {
            var wins = new Dictionary<int, int>();
            var games = new Dictionary<int, int>();
            foreach (var m in done)
            {
                if (m.Kind != LeagueMatchKind.Clash || m.ClashEventId is not int ev) continue;
                games[ev] = games.GetValueOrDefault(ev) + 1;
                if (m.WinnerTeamIndex == 0) wins[ev] = wins.GetValueOrDefault(ev) + 1;
            }
            return games.ToDictionary(kv => kv.Key, kv => new OvClashRecord(wins.GetValueOrDefault(kv.Key), kv.Value));
        }

        public static int ClashesWon(IEnumerable<OvClashEvent> doneEvents, IReadOnlyDictionary<int, OvClashRecord> records) =>
            doneEvents.Count(e => records.TryGetValue(e.Id, out var r) && r.Wins >= MinWinsForWonClash);

        // The most recent finished Clash we won (ended last; then the later id), with the team of its most recent won game.
        // Null when no Clash was won yet.
        public static OvBestClash? BestClash(IEnumerable<OvClashEvent> doneEvents, IEnumerable<OvMatch> done)
        {
            var matches = done.Where(m => m.Kind == LeagueMatchKind.Clash && m.ClashEventId != null).ToList();
            var records = ClashRecords(matches);
            foreach (var ev in doneEvents
                .OrderByDescending(e => e.EndedUtc ?? e.StartsAtUtc).ThenByDescending(e => e.Id))
            {
                if (!records.TryGetValue(ev.Id, out var rec) || rec.Wins < MinWinsForWonClash) continue;
                var ofEvent = matches.Where(m => m.ClashEventId == ev.Id).ToList();
                var game = ofEvent.Where(m => m.WinnerTeamIndex == 0)
                    .OrderByDescending(m => m.WhenUtc).ThenByDescending(m => m.Id).FirstOrDefault()
                    ?? ofEvent.OrderByDescending(m => m.WhenUtc).ThenByDescending(m => m.Id).First();
                return new OvBestClash(ev, rec.Wins, rec.Games, game.Id);
            }
            return null;
        }

        // Most played champion: the champion entered on the most player rows. Ties: the name A to Z.
        public static (int ChampionId, int Games)? TopChampion(IEnumerable<OvRow> rows, IReadOnlyDictionary<int, string> championNames)
        {
            var best = rows.Where(r => r.ChampionId != null).GroupBy(r => r.ChampionId!.Value)
                .Select(g => (Id: g.Key, Games: g.Count()))
                .OrderByDescending(x => x.Games)
                .ThenBy(x => championNames.TryGetValue(x.Id, out var n) ? n : "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            return best.Games == 0 ? null : (best.Id, best.Games);
        }

        // Most picked lane and its share (whole percent) of every row that has a lane. Ties: lane order.
        public static (LeagueLane Lane, int Percent, int Picks)? TopLane(IEnumerable<OvRow> rows)
        {
            var withLane = rows.Where(r => r.Lane != null).Select(r => r.Lane!.Value).ToList();
            if (withLane.Count == 0) return null;
            var best = withLane.GroupBy(l => l).OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key).First();
            var percent = (int)Math.Round(best.Count() * 100.0 / withLane.Count, MidpointRounding.AwayFromZero);
            return (best.Key, percent, best.Count());
        }

        // Most finished matches played by one person. Ties: the name A to Z, then the id.
        public static (int ProfileId, int Games)? MostActive(IEnumerable<OvRow> rows, IReadOnlyDictionary<int, string> names)
        {
            var best = rows.Where(r => r.ProfileId != null).GroupBy(r => r.ProfileId!.Value)
                .Select(g => (Id: g.Key, Games: g.Select(r => r.MatchId).Distinct().Count()))
                .OrderByDescending(x => x.Games)
                .ThenBy(x => names.TryGetValue(x.Id, out var n) ? n : "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            return best.Games == 0 ? null : (best.Id, best.Games);
        }

        // Longest run of won games in a row for one person (custom and Clash together, oldest to newest, a match with
        // no recorded winner is skipped and does not break the run). Ties: the name A to Z, then the id.
        public static (int ProfileId, int Streak)? LongestWinStreak(IEnumerable<OvMatch> done, IEnumerable<OvRow> rows,
            IReadOnlyDictionary<int, string> names)
        {
            var decided = done.Where(m => m.WinnerTeamIndex != null).ToDictionary(m => m.Id);
            (int Id, int Streak) best = (0, 0);
            var bestName = "";
            foreach (var g in rows.Where(r => r.ProfileId != null && decided.ContainsKey(r.MatchId)).GroupBy(r => r.ProfileId!.Value))
            {
                var run = 0; var longest = 0;
                foreach (var r in g.OrderBy(r => decided[r.MatchId].WhenUtc).ThenBy(r => r.MatchId))
                {
                    if (r.TeamIndex == decided[r.MatchId].WinnerTeamIndex) { run++; if (run > longest) longest = run; }
                    else run = 0;
                }
                if (longest == 0) continue;
                var name = names.TryGetValue(g.Key, out var n) ? n : "";
                var better = longest > best.Streak
                    || (longest == best.Streak && (string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) < 0
                        || (string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) == 0 && g.Key < best.Id)));
                if (better) { best = (g.Key, longest); bestName = name; }
            }
            return best.Streak == 0 ? null : best;
        }

        // Average length in whole seconds over the matches that have a length; null when none has one.
        public static int? AverageLengthSeconds(IEnumerable<OvMatch> done)
        {
            var lengths = done.Where(m => m.DurationSeconds is > 0).Select(m => m.DurationSeconds!.Value).ToList();
            return lengths.Count == 0 ? null : (int)Math.Round(lengths.Average(), MidpointRounding.AwayFromZero);
        }

        // 1660 -> "27:40" (minutes can pass 59: 3725 -> "62:05").
        public static string FormatDuration(int seconds)
        {
            var s = Math.Max(0, seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
