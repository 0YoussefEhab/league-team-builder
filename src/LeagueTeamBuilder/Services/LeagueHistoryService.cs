using System.Linq.Expressions;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 5a: read-only match history. A list (filter + paging) and the data for the match popup.
    // Reads the snapshots on the match; nothing here writes. Editing a result and saving stats are in LeagueMatchService.
    public class LeagueHistoryService
    {
        public const int PageSize = 20;

        private readonly ApplicationDbContext _db;

        public LeagueHistoryService(ApplicationDbContext db) => _db = db;

        // "Matches missing stats": a finished match where at least one player has no K, D or A entered.
        // One definition for the database filter, the count badge and the row flag (tests run the compiled form).
        public static readonly Expression<Func<LeagueMatch, bool>> MissingStats = m =>
            m.Status == LeagueMatchStatus.Done
            && m.Teams.SelectMany(t => t.Players).Any(p => p.Kills == null || p.Deaths == null || p.Assists == null);

        private static readonly Func<LeagueMatch, bool> MissingStatsFn = MissingStats.Compile();

        // ── Pure logic (no database, covered by tests) ────────────────────────────────────────────────────

        public static LeagueHistoryFilter ParseFilter(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "done" => LeagueHistoryFilter.Done,
            "open" => LeagueHistoryFilter.Open,
            "missing" => LeagueHistoryFilter.Missing,
            _ => LeagueHistoryFilter.All,
        };

        public static string FilterKey(LeagueHistoryFilter f) => f.ToString().ToLowerInvariant();

        // ── Step 8h: kind, mode, search and player filters (pure, covered by tests) ───────────────────────

        public const int MaxSearchLength = 40;

        public static LeagueHistoryKind ParseKind(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "custom" => LeagueHistoryKind.Custom,
            "clash" => LeagueHistoryKind.Clash,
            _ => LeagueHistoryKind.All,
        };

        public static string? KindKey(LeagueHistoryKind k) => k == LeagueHistoryKind.All ? null : k.ToString().ToLowerInvariant();

        public static LeagueHistoryMode ParseMode(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "draft" => LeagueHistoryMode.Draft,
            "aram" => LeagueHistoryMode.Aram,
            "arena" => LeagueHistoryMode.Arena,
            _ => LeagueHistoryMode.All,
        };

        public static string? ModeUrlKey(LeagueHistoryMode m) => m == LeagueHistoryMode.All ? null : m.ToString().ToLowerInvariant();

        // The mode key stored on a match (LeagueBuilderService.ModeKey): Draft is stored as "normal". Null = every mode.
        public static string? StoredModeKey(LeagueHistoryMode m) => m switch
        {
            LeagueHistoryMode.Draft => LeagueBuilderService.ModeKey(LeagueMode.Normal),
            LeagueHistoryMode.Aram => LeagueBuilderService.ModeKey(LeagueMode.Aram),
            LeagueHistoryMode.Arena => LeagueBuilderService.ModeKey(LeagueMode.Arena),
            _ => null,
        };

        // Never throws. Search is trimmed and cut to MaxSearchLength; a player id below 1 means "everybody"; page below 1 is 1.
        public static LeagueHistoryQuery ParseQuery(string? filter, string? kind, string? mode, string? search, int? player, int page = 1)
        {
            var text = (search ?? "").Trim();
            if (text.Length > MaxSearchLength) text = text[..MaxSearchLength].TrimEnd();
            return new LeagueHistoryQuery
            {
                Filter = ParseFilter(filter),
                Kind = ParseKind(kind),
                Mode = ParseMode(mode),
                Search = text,
                PlayerId = player is int id && id > 0 ? id : null,
                Page = Math.Max(1, page),
            };
        }

        // The route values that rebuild this query as a link (defaults left out), with a different page and / or status pill.
        public static Dictionary<string, object?> RouteValues(LeagueHistoryQuery q, int page = 1, LeagueHistoryFilter? filter = null)
        {
            var f = filter ?? q.Filter;
            var d = new Dictionary<string, object?>();
            if (f != LeagueHistoryFilter.All) d["filter"] = FilterKey(f);
            if (KindKey(q.Kind) is string k) d["kind"] = k;
            if (ModeUrlKey(q.Mode) is string m) d["mode"] = m;
            if (q.Search.Length > 0) d["q"] = q.Search;
            if (q.PlayerId is int p) d["player"] = p;
            if (page > 1) d["page"] = page;
            return d;
        }

        // A match is listed when any player has a name containing the text (the name saved on the match or the live
        // league name) or played a champion whose name contains it. Case-insensitive; the same text is lower-cased
        // here, so it works on Postgres and SQLite alike.
        public static Expression<Func<LeagueMatch, bool>> MatchesSearch(string text)
        {
            var n = (text ?? "").Trim().ToLowerInvariant();
            return m => m.Teams.SelectMany(t => t.Players).Any(p =>
                p.NameSnapshot.ToLower().Contains(n)
                || (p.Profile != null && p.Profile.InGameName.ToLower().Contains(n))
                || (p.Champion != null && p.Champion.Name.ToLower().Contains(n)));
        }

        // The player took part in the match (through the live profile link).
        public static Expression<Func<LeagueMatch, bool>> HasPlayer(int profileId) =>
            m => m.Teams.SelectMany(t => t.Players).Any(p => p.ProfileId == profileId);

        private static IQueryable<LeagueMatch> ApplyExtra(IQueryable<LeagueMatch> q, LeagueHistoryQuery query)
        {
            if (query.Kind == LeagueHistoryKind.Custom) q = q.Where(m => m.Kind == LeagueMatchKind.Custom);
            else if (query.Kind == LeagueHistoryKind.Clash) q = q.Where(m => m.Kind == LeagueMatchKind.Clash);
            if (StoredModeKey(query.Mode) is string key) q = q.Where(m => m.ModeKey == key);
            if (query.PlayerId is int pid) q = q.Where(HasPlayer(pid));
            if (query.Search.Length > 0) q = q.Where(MatchesSearch(query.Search));
            return q;
        }

        public static string LaneLabel(LeagueLane lane) => lane == LeagueLane.Adc ? "ADC" : lane.ToString();

        // "Gold II", "Master". Division 1 is the highest (I), 4 the lowest (IV); Master and above have none.
        public static string? RankLabel(LeagueRank? rank, int? div)
        {
            if (rank is not LeagueRank r) return null;
            if (!LeagueRankMath.HasDivision(r)) return r.ToString();
            var d = div is >= 1 and <= 4 ? div.Value : 4;
            return r + " " + new[] { "I", "II", "III", "IV" }[d - 1];
        }

        public static string? Kda(int? k, int? d, int? a)
        {
            if (k == null && d == null && a == null) return null;
            static string P(int? v) => v?.ToString() ?? "-";
            return $"{P(k)} / {P(d)} / {P(a)}";
        }

        // ── Step 6e5: Clash games in History and the popup ────────────────────────────────────────────────

        // "Game 2 of 4". A number past the cap (cannot be saved today) still reads sensibly; no number, no label.
        public static string? GameLabel(int? number) =>
            number is not int n || n < 1 ? null
            : n <= LeagueClashGameRules.MaxGames ? $"Game {n} of {LeagueClashGameRules.MaxGames}"
            : $"Game {n}";

        // The series so far: "2W 1L · 3 games saved". Null when the event has no game (nothing to say).
        // A win is the Clash team (team 0) winning, as on the Clash page (LeagueClashBoardService.RecordOf).
        public static string? SeriesText(LeagueClashRecord record)
        {
            if (record.Games <= 0) return null;
            var saved = record.Games == 1 ? "1 game saved" : $"{record.Games} games saved";
            var result = record.Finished == 0 ? "no game finished yet" : record.Text;
            return $"{result} · {saved}";
        }

        // Step 11f: the clash team that played a Clash game (the caller must have loaded m.ClashTeam). Null for a custom match
        // and for an old game with no team.
        private static string? ClashTeamNameOf(LeagueMatch m) =>
            m.Kind == LeagueMatchKind.Clash && !string.IsNullOrWhiteSpace(m.ClashTeam?.Name) ? m.ClashTeam!.Name.Trim() : null;

        // In a Clash game team 0 is stored as "Clash team"; when the game has a real team name, that name is what is shown.
        private static string ShownTeamName(LeagueMatchTeam t, string? clashTeamName) =>
            clashTeamName != null && t.Index == 0 ? clashTeamName : t.Name;

        private static string? ClashTitleOf(LeagueMatch m) =>
            m.Kind == LeagueMatchKind.Clash && !string.IsNullOrWhiteSpace(m.ClashEvent?.Title) ? m.ClashEvent!.Title.Trim() : null;

        // The extra stats that exist for a player, as one line ("a stat is shown only when it exists").
        public static string? ExtraText(LeagueMatchPlayer p)
        {
            static string N(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            var parts = new List<string>();
            if (p.Cs is int cs) parts.Add("CS " + N(cs));
            if (p.DamageDealt is int dd) parts.Add("Damage " + N(dd));
            if (p.DamageTaken is int dt) parts.Add("Taken " + N(dt));
            if (p.Gold is int g) parts.Add("Gold " + N(g));
            if (p.VisionScore is int vs) parts.Add("Vision " + N(vs));
            if (p.ObjectiveDamage is int od) parts.Add("Objectives " + N(od));
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        // ── Step 5d: one player's numbers ─────────────────────────────────────────────────────────────────

        public const int MaxChampionsListed = 5;
        public const int RecentCount = 10;

        private static int? Pct(int wins, int decided) =>
            decided == 0 ? null : (int)Math.Round(100.0 * wins / decided, MidpointRounding.AwayFromZero);

        // rows = the player's LeagueMatchPlayer rows with Team, Team.Match and Champion loaded. Only finished matches
        // count. A match without a recorded winner is a game but neither a win nor a loss. Averages use only the
        // matches where K, D and A were all entered; nothing is guessed for a match that lacks a stat.
        public static LeaguePlayerStats ComputePlayerStats(IEnumerable<LeagueMatchPlayer> rows, int? profileId = null)
        {
            var list = rows
                .Where(p => p.Team?.Match is { Status: LeagueMatchStatus.Done })
                .OrderByDescending(p => p.Team!.Match!.PlayedUtc).ThenByDescending(p => p.Team!.Match!.Id)
                .ToList();

            static bool? Won(LeagueMatchPlayer p) =>
                p.Team!.Match!.WinnerTeamIndex is int w ? p.Team.Index == w : null;

            var stats = new LeaguePlayerStats { Games = list.Count };
            if (list.Count == 0) return stats;
            stats.Buckets = BucketStatsOf(profileId ?? list.Select(p => p.ProfileId).FirstOrDefault(x => x != null), list);

            stats.Wins = list.Count(p => Won(p) == true);
            stats.Losses = list.Count(p => Won(p) == false);
            stats.Decided = stats.Wins + stats.Losses;
            stats.WinRate = Pct(stats.Wins, stats.Decided);
            stats.Recent = list.Take(RecentCount).Select(p => Won(p) switch { true => "W", false => "L", _ => "-" }).ToList();

            var kda = list.Where(p => p.Kills != null && p.Deaths != null && p.Assists != null).ToList();
            stats.KdaMatches = kda.Count;
            if (kda.Count > 0)
            {
                var k = kda.Sum(p => p.Kills!.Value);
                var d = kda.Sum(p => p.Deaths!.Value);
                var a = kda.Sum(p => p.Assists!.Value);
                stats.AvgKills = (double)k / kda.Count;
                stats.AvgDeaths = (double)d / kda.Count;
                stats.AvgAssists = (double)a / kda.Count;
                stats.KdaRatio = d == 0 ? "Perfect"
                    : ((double)(k + a) / d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }

            var withChamp = list.Where(p => p.ChampionId != null).ToList();
            stats.ChampionMatches = withChamp.Count;
            stats.Champions = withChamp
                .GroupBy(p => p.ChampionId!.Value)
                .Select(g =>
                {
                    var first = g.First();
                    var wins = g.Count(p => Won(p) == true);
                    var decided = g.Count(p => Won(p) != null);
                    var name = string.IsNullOrWhiteSpace(first.Champion?.Name) ? "Unknown champion" : first.Champion!.Name;
                    var icon = string.IsNullOrWhiteSpace(first.Champion?.IconUrl) ? null : first.Champion!.IconUrl;
                    return new LeagueStatsChampionUse(name, icon, g.Count(), wins, Pct(wins, decided));
                })
                .OrderByDescending(c => c.Games).ThenByDescending(c => c.Wins).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxChampionsListed).ToList();

            var withLane = list.Where(p => (p.PlayedLane ?? p.Lane) != null).ToList();
            stats.LaneMatches = withLane.Count;
            stats.Lanes = withLane
                .GroupBy(p => (p.PlayedLane ?? p.Lane)!.Value)
                .OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key)
                .Select(g =>
                {
                    var wins = g.Count(p => Won(p) == true);
                    var decided = g.Count(p => Won(p) != null);
                    return new LeagueStatsLaneUse(LaneLabel(g.Key), g.Count(), wins, Pct(wins, decided));
                }).ToList();

            return stats;
        }

        // MVP redesign Step 5d: MVPs, games and wins of one profile per group (custom draft / Clash / custom ARAM+Arena), through the
        // same rules as the Leaderboard (LeagueMvpBuckets). list = the profile's rows of finished matches, Team.Match loaded.
        private static LeagueBucketStats BucketStatsOf(int? profileId, List<LeagueMatchPlayer> list)
        {
            var result = new LeagueBucketStats();
            if (profileId is not int id) return result;
            var matches = list.Select(p => p.Team!.Match!).GroupBy(m => m.Id).Select(g => g.First()).ToList();
            var mvps = LeagueMvpBuckets.MvpCounts(matches.Select(m =>
                (m.Kind, (string?)m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));
            var records = LeagueMvpBuckets.Records(
                matches.Select(m => new OvMatch(m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.DurationSeconds)),
                list.Select(p => new OvRow(p.Team!.MatchId, p.Team.Index, p.ProfileId, null, null)));
            var c = mvps.GetValueOrDefault(id) ?? new BucketMvpCounts(0, 0, 0);
            var r = records.GetValueOrDefault(id)
                ?? new BucketRecords(new BucketRecord(0, 0, 0), new BucketRecord(0, 0, 0), new BucketRecord(0, 0, 0));
            result.Draft = new LeagueBucketStat(c.CustomDraft, r.CustomDraft.Games, r.CustomDraft.Wins, r.CustomDraft.Losses);
            result.Clash = new LeagueBucketStat(c.Clash, r.Clash.Games, r.Clash.Wins, r.Clash.Losses);
            result.Aram = new LeagueBucketStat(c.CustomAram, r.CustomAram.Games, r.CustomAram.Wins, r.CustomAram.Losses);
            return result;
        }

        public static LeagueHistoryRow ToRow(LeagueMatch m)
        {
            var teams = m.Teams.OrderBy(t => t.Index).ToList();
            var twoTeams = teams.Count == 2;
            var clashTeam = ClashTeamNameOf(m);
            return new LeagueHistoryRow
            {
                Id = m.Id,
                IsClash = m.Kind == LeagueMatchKind.Clash,
                ModeName = m.ModeName,
                Status = LeagueMatchService.StatusKey(m.Status),
                PlayedUtc = m.PlayedUtc,
                AddedLater = m.Status == LeagueMatchStatus.Done && LeagueDayRules.IsAddedLater(m.PlayedUtc, m.FinishedUtc),
                WinnerName = m.Status == LeagueMatchStatus.Done && m.WinnerTeamIndex is int w
                    ? teams.Where(t => t.Index == w).Select(t => ShownTeamName(t, clashTeam)).FirstOrDefault() : null,
                Kills0 = twoTeams ? m.TeamKills0 : null,
                Kills1 = twoTeams ? m.TeamKills1 : null,
                TeamNames = teams.Select(t => ShownTeamName(t, clashTeam)).ToList(),
                PlayerCount = teams.Sum(t => t.Players.Count),
                GameNumber = m.Kind == LeagueMatchKind.Clash ? m.GameNumber : null,
                GameLabel = m.Kind == LeagueMatchKind.Clash ? GameLabel(m.GameNumber) : null,
                ClashTitle = ClashTitleOf(m),
                ClashTeamName = clashTeam,
                MissingStats = MissingStatsFn(m),
            };
        }

        // The popup. Only what exists is filled in; the view skips the rest (spec section 8: absent data is
        // simply not shown). CanDelete is set by the controller.
        // series: the Clash event's record so far (Step 6e5); the database method fills it for a Clash game.
        public static LeagueMatchDetailModel ToDetail(LeagueMatch m, LeagueClashRecord? series = null)
        {
            var isClash = m.Kind == LeagueMatchKind.Clash;
            var teams = m.Teams.OrderBy(t => t.Index).ToList();
            var twoTeams = teams.Count == 2;
            var done = m.Status == LeagueMatchStatus.Done;
            var clashTeam = ClashTeamNameOf(m);

            var timeline = new List<LeagueMatchTimelineStep> { new("Teams submitted", m.SubmittedUtc) };
            if (m.StartedUtc is DateTime st) timeline.Add(new("Match started", st));
            if (m.FinishedUtc is DateTime fi) timeline.Add(new("Finished", fi));

            var detail = new LeagueMatchDetailModel
            {
                Id = m.Id,
                IsClash = m.Kind == LeagueMatchKind.Clash,
                ModeName = m.ModeName,
                Status = LeagueMatchService.StatusKey(m.Status),
                PlayedUtc = m.PlayedUtc,
                AddedLater = done && LeagueDayRules.IsAddedLater(m.PlayedUtc, m.FinishedUtc),
                PlayedOnInput = LeagueDayRules.InputValue(m.PlayedUtc),
                WinnerName = done && m.WinnerTeamIndex is int w ? teams.Where(t => t.Index == w).Select(t => ShownTeamName(t, clashTeam)).FirstOrDefault() : null,
                WinnerIndex = done ? m.WinnerTeamIndex : null,
                EditedUtc = done ? m.StatsEditedUtc : null,
                DurationSeconds = m.DurationSeconds,
                GameNumber = isClash ? m.GameNumber : null,
                GameLabel = isClash ? GameLabel(m.GameNumber) : null,
                ClashEventId = isClash ? m.ClashEventId : null,
                ClashTeamId = isClash ? m.ClashTeamId : null,
                ClashTitle = ClashTitleOf(m),
                ClashTeamName = clashTeam,
                SeriesText = isClash && series != null ? SeriesText(series) : null,
                BuilderLog = string.IsNullOrWhiteSpace(m.BuilderLog) ? null : m.BuilderLog,
                Timeline = timeline,
                HasLanes = teams.SelectMany(t => t.Players).Any(p => p.Lane != null),
                Teams = teams.Select(t => ToDetailTeam(t, done && m.WinnerTeamIndex == t.Index,
                    twoTeams ? (t.Index == 0 ? m.TeamKills0 : t.Index == 1 ? m.TeamKills1 : null) : null)).ToList(),
            };
            foreach (var dt in detail.Teams) dt.Name = ShownTeamName(teams.First(t => t.Index == dt.Index), clashTeam);
            if (done) FillMvps(detail, m, teams);
            detail.ResultImage = LeagueResultImageRules.Build(detail);   // Results/export step 3
            return detail;
        }

        // Step 8a3: the MVP lines, the tags on the players and the options of the admin pick form. Names come from the
        // match's own snapshots, so a renamed or merged profile still shows the name it had in this match.
        private static void FillMvps(LeagueMatchDetailModel d, LeagueMatch m, List<LeagueMatchTeam> teams)
        {
            d.MvpIsManual = m.MvpIsManual;
            d.MvpId = m.MvpProfileId;
            d.StatsMvpId = m.StatsMvpProfileId;
            d.TeamMvpId = m.TeamMvpProfileId;
            d.StatsTeamMvpId = m.StatsTeamMvpProfileId;

            var names = new Dictionary<int, string>();
            foreach (var p in teams.SelectMany(t => t.Players))
                if (p.ProfileId is int id && !names.ContainsKey(id))
                    names[id] = string.IsNullOrWhiteSpace(p.NameSnapshot) ? "?" : p.NameSnapshot.Trim();

            // Step 7: "why" sentences are worked out live from the saved stats; a hand pick gets none.
            var mvpPlayers = LeagueMatchService.ToMvpPlayers(m);
            var mvpProfile = LeagueMatchService.MvpProfileOf(m);

            void Line(int? profileId, string label, string tag, bool explain = false)
            {
                if (profileId is not int id || !names.TryGetValue(id, out var name)) return;
                var why = explain && !m.MvpIsManual ? LeagueMvpBreakdown.Why(mvpPlayers, id, mvpProfile, names) : null;
                d.MvpLines.Add(new LeagueMvpLine(label, name, why));
                foreach (var p in d.Teams.SelectMany(t => t.Players).Where(p => p.ProfileId == id)) p.MvpTags.Add(tag);
            }
            // Owner, 2026-10-19: once the all-stats pick exists, it is THE MVP and the K/D/A line (and its tag) is not shown.
            // Only when there is no all-stats pick (some stat missing) does the K/D/A pick show, still labelled K/D/A.
            if (m.StatsMvpProfileId != null) Line(m.StatsMvpProfileId, "Match MVP", "MVP", true);
            else Line(m.MvpProfileId, "Match MVP (K/D/A)", "MVP");
            if (m.StatsTeamMvpProfileId != null) Line(m.StatsTeamMvpProfileId, "Team MVP", "Team MVP", true);
            else Line(m.TeamMvpProfileId, "Team MVP (K/D/A)", "Team MVP");
            foreach (var p in d.Teams.SelectMany(t => t.Players)) p.MvpTags = p.MvpTags.Distinct().ToList();

            List<LeagueMvpOption> Options(LeagueMatchTeam? t) => t == null ? new() :
                t.Players.Where(p => p.ProfileId != null).OrderBy(p => p.Id)
                    .Select(p => new LeagueMvpOption(p.ProfileId!.Value, string.IsNullOrWhiteSpace(p.NameSnapshot) ? "?" : p.NameSnapshot.Trim())).ToList();
            if (m.WinnerTeamIndex is int w)
            {
                d.WinnerOptions = Options(teams.FirstOrDefault(t => t.Index == w));
                if (teams.Count == 2) d.LoserOptions = Options(teams.FirstOrDefault(t => t.Index != w));
            }
        }

        private static LeagueMatchDetailTeam ToDetailTeam(LeagueMatchTeam t, bool winner, int? kills)
        {
            var players = t.Players.OrderBy(p => p.Id).ToList();
            var withStats = players.Where(p => p.Kills != null || p.Deaths != null || p.Assists != null).ToList();

            var parts = new List<string>();
            if (players.Any(p => p.Kills != null)) parts.Add("K " + players.Sum(p => p.Kills ?? 0));
            if (players.Any(p => p.Deaths != null)) parts.Add("D " + players.Sum(p => p.Deaths ?? 0));
            if (players.Any(p => p.Assists != null)) parts.Add("A " + players.Sum(p => p.Assists ?? 0));

            return new LeagueMatchDetailTeam
            {
                Index = t.Index,
                Name = t.Name,
                IsWinner = winner,
                Kills = kills,
                Players = players.Select(ToDetailPlayer).ToList(),
                TotalsText = parts.Count == 0 ? null : string.Join(" · ", parts),
                StatPlayers = withStats.Count,
            };
        }

        private static LeagueMatchDetailPlayer ToDetailPlayer(LeagueMatchPlayer p)
        {
            var name = string.IsNullOrWhiteSpace(p.NameSnapshot) ? "?" : p.NameSnapshot.Trim();
            var other = LeagueExtraStatRules.FromJson(p.ExtraJson);
            return new LeagueMatchDetailPlayer
            {
                Name = name,
                IsGuest = p.WasGuest,
                LaneLabel = p.Lane is LeagueLane l ? LaneLabel(l) : null,
                PlayedLaneLabel = p.PlayedLane is LeagueLane pl && pl != p.Lane ? LaneLabel(pl) : null,
                AvatarUrl = string.IsNullOrWhiteSpace(p.Profile?.AvatarUrl) ? null : p.Profile!.AvatarUrl,
                Initial = new System.Globalization.StringInfo(name).SubstringByTextElements(0, 1).ToUpperInvariant(),
                RankKey = p.CurRankSnapshot?.ToString().ToLowerInvariant(),
                RankLabel = RankLabel(p.CurRankSnapshot, p.CurDivSnapshot),
                PeakRankLabel = RankLabel(p.PeakRankSnapshot, p.PeakDivSnapshot),
                // A retired (disabled) champion keeps its name, like everywhere else.
                ChampionName = p.Champion?.Name,
                ChampionIconUrl = string.IsNullOrWhiteSpace(p.Champion?.IconUrl) ? null : p.Champion!.IconUrl,
                Kda = Kda(p.Kills, p.Deaths, p.Assists),
                PlayerId = p.Id,
                ProfileId = p.ProfileId,
                GameName = LeagueGameNameRules.FromRiotId(p.Profile?.RiotId),
                ChampionId = p.ChampionId,
                Lane = p.Lane is LeagueLane bl ? (int)bl : null,
                PlayedLane = p.PlayedLane is LeagueLane pl2 ? (int)pl2 : null,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                Cs = p.Cs,
                DamageDealt = p.DamageDealt,
                DamageTaken = p.DamageTaken,
                Gold = p.Gold,
                VisionScore = p.VisionScore,
                ObjectiveDamage = p.ObjectiveDamage,
                Notes = string.IsNullOrWhiteSpace(p.Notes) ? null : p.Notes.Trim(),
                ExtraText = ExtraText(p),
                OtherStats = other.Select(e => new LeagueOtherStat { Name = e.Key, Label = LeagueExtraStatRules.Label(e.Key), Value = e.Value }).ToList(),
                OtherText = LeagueExtraStatRules.Text(other),
            };
        }

        // ── Database ──────────────────────────────────────────────────────────────────────────────────────

        // Before 8h: only the status pill and the page. Kept so old callers and tests still compile.
        public Task<LeagueHistoryPageModel> GetPageAsync(string? filterKey, int page) =>
            GetPageAsync(ParseQuery(filterKey, null, null, null, null, page));

        public async Task<LeagueHistoryPageModel> GetPageAsync(LeagueHistoryQuery query)
        {
            var filter = query.Filter;
            var page = query.Page;
            IQueryable<LeagueMatch> q = ApplyExtra(_db.LeagueMatches.AsNoTracking(), query);
            q = filter switch
            {
                LeagueHistoryFilter.Done => q.Where(m => m.Status == LeagueMatchStatus.Done),
                LeagueHistoryFilter.Open => q.Where(m => m.Status != LeagueMatchStatus.Done),
                LeagueHistoryFilter.Missing => q.Where(MissingStats),
                _ => q,
            };

            var total = await q.CountAsync();
            var pages = Math.Max(1, (total + PageSize - 1) / PageSize);
            var current = Math.Clamp(page, 1, pages);

            var rows = await q
                .OrderByDescending(m => m.PlayedUtc).ThenByDescending(m => m.Id)
                .Skip((current - 1) * PageSize).Take(PageSize)
                .Include(m => m.Teams).ThenInclude(t => t.Players)
                .Include(m => m.ClashEvent)
                .Include(m => m.ClashTeam)
                .ToListAsync();

            return new LeagueHistoryPageModel
            {
                Rows = rows.Select(ToRow).ToList(),
                Filter = filter,
                Page = current,
                TotalPages = pages,
                TotalCount = total,
                MissingCount = await _db.LeagueMatches.CountAsync(MissingStats),
                Query = query,
                PlayerName = query.PlayerId is int pid
                    ? await _db.LeagueProfiles.AsNoTracking().Where(p => p.Id == pid).Select(p => p.InGameName).FirstOrDefaultAsync() ?? "that player"
                    : null,
            };
        }

        // withChampionOptions: fill the champion picker list (only the stats form needs it).
        // Step 5d: the numbers for one league profile (finished matches that are still linked to it).
        public async Task<LeaguePlayerStats> GetPlayerStatsAsync(int profileId)
        {
            var rows = await _db.LeagueMatchPlayers.AsNoTracking()
                .Include(p => p.Team).ThenInclude(t => t!.Match)
                .Include(p => p.Champion)
                .Where(p => p.ProfileId == profileId && p.Team!.Match!.Status == LeagueMatchStatus.Done)
                .ToListAsync();
            return ComputePlayerStats(rows, profileId);
        }

        public async Task<LeagueMatchDetailModel?> GetDetailAsync(int id, bool withChampionOptions = false)
        {
            var m = await _db.LeagueMatches.AsNoTracking()
                .Include(x => x.Teams).ThenInclude(t => t.Players).ThenInclude(p => p.Champion)
                .Include(x => x.Teams).ThenInclude(t => t.Players).ThenInclude(p => p.Profile)
                .Include(x => x.ClashEvent)
                .Include(x => x.ClashTeam)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (m == null) return null;
            var detail = ToDetail(m, await SeriesOfAsync(m));
            if (withChampionOptions) detail.Champions = await ChampionOptionsAsync(m);
            return detail;
        }

        // Step 12h2: the data of the Game result picture of one match (used by the auto-send after Finish). Read-only.
        // Only a finished custom game with two teams and players has one (LeagueResultImageRules).
        public async Task<LeagueGameImageResponse> GameImageAsync(int matchId)
        {
            var detail = await GetDetailAsync(matchId);
            if (detail == null) return new LeagueGameImageResponse { Ok = false, Error = "That match no longer exists." };
            return detail.ResultImage == null
                ? new LeagueGameImageResponse { Ok = false, Error = "This match has no result picture. It needs to be a finished game with two teams." }
                : new LeagueGameImageResponse { Ok = true, Spec = detail.ResultImage };
        }

        // Step 12e: the data of the Session collection picture for the block of games that holds this match: the finished custom
        // games of the same Cairo day in play order, four to a picture. Read-only. Which games belong together (70% rule) is 12f.
        public async Task<LeagueSessionImageResponse> SessionImageAsync(int matchId)
        {
            var played = await _db.LeagueMatches.AsNoTracking().Where(m => m.Id == matchId).Select(m => (DateTime?)m.PlayedUtc).FirstOrDefaultAsync();
            if (played == null) return SessionFail("That match no longer exists. Reload the page.");

            var (start, end) = LeagueDayRules.DayRangeUtc(LeagueDayRules.CairoDay(played.Value));
            var day = await _db.LeagueMatches.AsNoTracking()
                .Include(m => m.Teams).ThenInclude(t => t.Players)
                .Where(m => m.Kind == LeagueMatchKind.Custom && m.Status == LeagueMatchStatus.Done && m.PlayedUtc >= start && m.PlayedUtc < end)
                .ToListAsync();

            var block = LeagueSessionImageRules.BlockOf(matchId, day);
            if (block == null) return SessionFail("This match cannot be part of a session picture. It needs two teams with players and a result.");
            var spec = LeagueSessionImageRules.Build(matchId, block.Value.Block, block.Value.Offset, block.Value.Total);
            return spec == null
                ? SessionFail("The session picture could not be made from these games.")
                : new LeagueSessionImageResponse { Ok = true, Spec = spec };
        }

        // Step 12f: the finished custom games of one Cairo day, with the teams and players the rules need. Read-only.
        private async Task<List<LeagueMatch>> DayMatchesAsync(DateOnly day, bool withDetail = false)
        {
            var (start, end) = LeagueDayRules.DayRangeUtc(day);
            IQueryable<LeagueMatch> q = _db.LeagueMatches.AsNoTracking()
                .Include(m => m.Teams).ThenInclude(t => t.Players);
            if (withDetail)
                q = q.Include(m => m.Teams).ThenInclude(t => t.Players).ThenInclude(p => p.Champion)
                     .Include(m => m.Teams).ThenInclude(t => t.Players).ThenInclude(p => p.Profile);
            return await q
                .Where(m => m.Kind == LeagueMatchKind.Custom && m.Status == LeagueMatchStatus.Done && m.PlayedUtc >= start && m.PlayedUtc < end)
                .ToListAsync();
        }

        // How many games of that day can be drawn (greys the History buttons out at 0).
        public async Task<int> ExportableCountAsync(DateOnly day) =>
            LeagueSessionImageRules.Order(await DayMatchesAsync(day)).Count;

        // The export dialog's data. ids = the ticked match ids (empty = all of the day's games). mode: "none" = checklist and overlap only,
        // "session" = the session pictures' data (blocks of four), "games" = the Game result picture of every ticked game.
        public async Task<LeagueExportResponse> ExportDataAsync(DateOnly day, IReadOnlyList<int> ids, string mode)
        {
            var dayMatches = await DayMatchesAsync(day, withDetail: mode == "games");
            var ordered = LeagueSessionImageRules.Order(dayMatches);
            var rows = LeagueExportDayRules.Rows(dayMatches);

            // Day numbers by match id; ticked = the day's games whose id was sent, in play order (unknown ids are ignored).
            var numberOf = rows.ToDictionary(r => r.Id, r => r.Number);
            var ticked = ids.Count == 0 ? ordered : ordered.Where(m => ids.Contains(m.Id)).ToList();

            var overlap = LeagueExportDayRules.Overlap(ticked.Select(m => (ISet<string>)LeagueExportDayRules.PlayerKeys(m)).ToList());
            var one = ticked.Count >= 2 && LeagueExportDayRules.IsOneSession(overlap);
            var percent = LeagueExportDayRules.Percent(overlap);
            var res = new LeagueExportResponse
            {
                Ok = true,
                Day = LeagueExportDayRules.DayKey(day),
                DayText = day.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Rows = rows,
                Ticked = ticked.Count,
                Percent = percent,
                OneSession = one,
                Message = rows.Count == 0 ? "No matches on this day." : LeagueExportDayRules.Message(ticked.Count, percent, one),
                DefaultMode = LeagueExportDayRules.DefaultMode(ticked.Count, one),
            };
            if (mode != "session" && mode != "games") return res;

            if (ticked.Count == 0)
            {
                res.Ok = false;
                res.Error = rows.Count == 0 ? "No matches on this day." : "Tick at least one match.";
                return res;
            }
            if (ticked.Count > LeagueExportDayRules.MaxTicked)
            {
                res.Ok = false;
                res.Error = $"Tick at most {LeagueExportDayRules.MaxTicked} matches at a time.";
                return res;
            }

            if (mode == "session")
            {
                var numbers = ticked.Select(m => numberOf[m.Id]).ToList();
                foreach (var block in LeagueExportDayRules.Blocks(ticked, numbers))
                {
                    var spec = LeagueSessionImageRules.Build(block.Games[0].Id, block.Games, block.Numbers, rows.Count);
                    if (spec == null)
                    {
                        res.Ok = false;
                        res.Error = "The session picture could not be made from these games.";
                        res.Sessions.Clear();
                        return res;
                    }
                    res.Sessions.Add(spec);
                }
                return res;
            }

            foreach (var m in ticked)
            {
                var spec = ToDetail(m).ResultImage;
                if (spec == null)
                {
                    res.Ok = false;
                    res.Error = $"Game {numberOf[m.Id]} cannot be drawn. It needs two teams with players and a result.";
                    res.Games.Clear();
                    return res;
                }
                res.Games.Add(spec);
            }
            return res;
        }

        private static LeagueSessionImageResponse SessionFail(string error) => new() { Ok = false, Error = error };

        // Step 6e5: the record of the Clash event this game belongs to (every game saved for it, finished or not).
        // Null for a custom match. Reads only status and winner, nothing is written.
        // Step 11f: a game with a team gets that team's record (its own games only); an old game with no team keeps the whole clash's.
        private async Task<LeagueClashRecord?> SeriesOfAsync(LeagueMatch m)
        {
            if (m.Kind != LeagueMatchKind.Clash || m.ClashEventId is not int eventId) return null;
            var games = await _db.LeagueMatches.AsNoTracking()
                .Where(x => x.ClashEventId == eventId && (m.ClashTeamId == null || x.ClashTeamId == m.ClashTeamId))
                .Select(x => new { x.Status, x.WinnerTeamIndex })
                .ToListAsync();
            return LeagueClashBoardService.RecordOf(
                games.Where(g => g.Status == LeagueMatchStatus.Done).Select(g => g.WinnerTeamIndex), games.Count);
        }

        // Active champions by name, plus a retired one that a player of this match already has (so its name still
        // shows in the box and is not lost on the next save).
        private async Task<List<LeagueStatsChampion>> ChampionOptionsAsync(LeagueMatch m)
        {
            var used = m.Teams.SelectMany(t => t.Players).Where(p => p.ChampionId != null)
                .Select(p => p.ChampionId!.Value).Distinct().ToList();
            var rows = await _db.LeagueChampions.AsNoTracking()
                .Where(c => c.IsActive || used.Contains(c.Id))
                .OrderBy(c => c.Name).ToListAsync();
            return rows.Select(c => new LeagueStatsChampion(c.Id, c.Name,
                string.IsNullOrWhiteSpace(c.IconUrl) ? null : c.IconUrl, !c.IsActive)).ToList();
        }
    }
}
