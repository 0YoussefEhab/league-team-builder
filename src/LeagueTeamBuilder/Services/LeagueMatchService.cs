using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 4d: match lifecycle. Submit (snapshot the teams on screen) -> Start -> Finish (winner required,
    // team kills optional) -> Delete. The server re-checks everything the page posts (same rules as the
    // builder endpoint) and never trusts a status the browser sends: Start and Finish are single conditional
    // UPDATEs (WHERE Status = ...), so a double click or two admins at once cannot move a match twice.
    // Player stats (K/D/A, champions) are NOT entered here: that is the staged "Add stats" flow of Step 5.
    public class LeagueMatchService
    {
        public const int MaxOpenListed = 20;
        public const int MaxScore = 999;
        // Largest K, D or A a player can be given (same ceiling as the team kills).
        public const int MaxStat = 999;
        // A match never has more than this many player rows; refuses a silly request before it loads anything.
        public const int MaxStatRows = 100;
        // Extra stats (spec section 14).
        public const int MaxCs = 9999;
        public const int MaxBigStat = 999999;     // damage dealt / taken, gold, objective damage
        public const int MaxVision = 999;
        public const int MaxNoteLength = 200;
        public const int MaxDurationSeconds = 86400;   // 24 hours

        public static readonly string[] BalanceNames =
        {
            "", "Full random", "Random, respecting lanes", "Two teams, rank + lanes", "Two teams, custom tiers + lanes",
        };

        private readonly ApplicationDbContext _db;
        private readonly LeagueSettingsService _settings;

        public LeagueMatchService(ApplicationDbContext db, LeagueSettingsService settings)
        {
            _db = db;
            _settings = settings;
        }

        public static string StatusKey(LeagueMatchStatus s) => s.ToString().ToLowerInvariant();

        // ── Pure logic (no database, covered by tests) ────────────────────────────────────────────────────

        // Turns the teams on the builder page into a match to insert. Players come from the profiles the caller
        // loaded; their name, guest flag and ranks are copied now (snapshots, spec R4), so later profile edits,
        // guest linking or deletion never change history. Teams with nobody on them are left out.
        public static (LeagueMatch? Match, string? Error) BuildMatch(
            LeagueMatchSubmitRequest req, IReadOnlyDictionary<int, LeagueProfile> profiles,
            int modeCap, string? userId, DateTime nowUtc)
        {
            if (!LeagueBuilderService.TryParseMode(req.Mode, out var mode)) return (null, "Unknown game mode.");
            if (req.Balance < 1 || req.Balance > 4) return (null, "Unknown balance mode.");

            var (res, error) = LeagueBuilderService.ParseTeams(req.Teams, mode, (LeagueBalance)req.Balance);
            if (res == null) return (null, error);
            if (mode == LeagueMode.Aram && res.Teams.Count != 2) return (null, "ARAM has exactly two teams.");

            var filled = res.Teams.Where(t => t.Slots.Any(s => s.PlayerId != null)).ToList();
            if (filled.Count < 2) return (null, "A match needs players on at least two teams.");

            var total = filled.Sum(t => t.Slots.Count(s => s.PlayerId != null));
            if (total > modeCap) return (null, $"This mode allows at most {modeCap} players.");

            var match = new LeagueMatch
            {
                Kind = LeagueMatchKind.Custom,
                ModeKey = LeagueBuilderService.ModeKey(mode),
                ModeName = LeagueModeRules.Name(mode),
                Status = LeagueMatchStatus.Submitted,
                SubmittedUtc = nowUtc,
                PlayedUtc = nowUtc,
                CreatedByUserId = userId,
                BuilderLog = "Balance: " + BalanceNames[req.Balance] + ".",
            };

            for (var ti = 0; ti < filled.Count; ti++)
            {
                var team = new LeagueMatchTeam { Index = ti, Name = filled[ti].Name };
                foreach (var slot in filled[ti].Slots)
                {
                    if (slot.PlayerId is not int pid) continue;
                    if (!profiles.TryGetValue(pid, out var p))
                        return (null, "A player no longer exists. Reload the page.");
                    team.Players.Add(SnapshotPlayer(p, slot.Lane));
                }
                match.Teams.Add(team);
            }
            return (match, null);
        }

        // One player row with the name, guest flag and ranks copied from the profile (spec R4). Used by the
        // builder page's Submit and by the Clash game (6e1).
        public static LeagueMatchPlayer SnapshotPlayer(LeagueProfile p, LeagueLane? lane) => new()
        {
            Lane = lane,
            ProfileId = p.Id,
            NameSnapshot = p.InGameName,
            WasGuest = p.IsGuest,
            CurRankSnapshot = p.CurTier,
            CurDivSnapshot = LeagueRankMath.HasDivision(p.CurTier) ? (p.CurDiv ?? 4) : null,
            PeakRankSnapshot = p.PeakTier,
            PeakDivSnapshot = LeagueRankMath.HasDivision(p.PeakTier) ? (p.PeakDiv ?? 4) : null,
        };

        // Only the winner is required (spec section 14); the team kills are optional and only exist for
        // two-team matches. teamIndexes = the Index of every team the match has.
        public static string? ValidateFinish(IReadOnlyCollection<int> teamIndexes, int? winner, int? kills0, int? kills1)
        {
            if (winner is not int w) return "Choose which team won.";
            if (!teamIndexes.Contains(w)) return "That team is not in this match.";
            if (kills0 is < 0 or > MaxScore || kills1 is < 0 or > MaxScore)
                return $"Team kills must be between 0 and {MaxScore}.";
            if (teamIndexes.Count != 2 && (kills0 != null || kills1 != null))
                return "Team kills only apply to matches with two teams.";
            return null;
        }

        // Step 8a2: the match's players as the MVP rules see them. Lane = the lane actually played, else the lane the builder assigned.
        public static List<MvpPlayer> ToMvpPlayers(LeagueMatch m) =>
            m.Teams.SelectMany(t => t.Players.Select(p => new MvpPlayer(
                p.ProfileId, t.Index, p.PlayedLane ?? p.Lane,
                p.Kills, p.Deaths, p.Assists, p.Cs, p.DamageDealt, p.DamageTaken, p.Gold, p.VisionScore, p.ObjectiveDamage))).ToList();

        // MVP redesign Steps 2 and 4: the scoring profile of a match, from its saved ModeKey. Normal = Draft (weights by lane);
        // ARAM and Arena = Aram (same weights for everybody, no CS, vision or objectives). Clash games follow their own mode.
        // An unknown or empty ModeKey (cannot happen for matches saved by the site) returns null = the old formula.
        public static MvpScoringProfile? MvpProfileOf(LeagueMatch m)
        {
            bool Is(LeagueMode mode) => string.Equals(m.ModeKey, LeagueBuilderService.ModeKey(mode), StringComparison.OrdinalIgnoreCase);
            if (Is(LeagueMode.Normal)) return MvpScoringProfile.Draft;
            if (Is(LeagueMode.Aram) || Is(LeagueMode.Arena)) return MvpScoringProfile.Aram;
            return null;
        }

        // Step 12c.1: a Clash game (it has only our team; see LeagueMvpRules.Compute).
        public static bool IsClashGame(LeagueMatch m) => m.Kind == LeagueMatchKind.Clash || m.ClashEventId != null;

        // Step 12c.1: what to tell the person after "Back to automatic" when a team the rule looks at got no K/D/A pick.
        // Null when every team it looks at has one (or the match has no winner yet). Clash: only our team. Otherwise the winning
        // team, and the losing team of a two-team match.
        public static string? AutoMvpNotice(LeagueMatch m)
        {
            if (m.Status != LeagueMatchStatus.Done || m.WinnerTeamIndex is not int w) return null;
            var players = ToMvpPlayers(m);
            var reasons = new List<string>();
            void Check(LeagueMatchTeam? team)
            {
                if (team == null) return;
                var why = LeagueMvpRules.WhyNoKdaPick(string.IsNullOrWhiteSpace(team.Name) ? $"Team {team.Index + 1}" : team.Name,
                    players.Where(p => p.TeamIndex == team.Index).ToList());
                if (why != null) reasons.Add(why);
            }
            if (IsClashGame(m)) Check(m.Teams.FirstOrDefault(t => t.Index == LeagueSeriesMvpRules.ClashTeamIndex));
            else
            {
                Check(m.Teams.FirstOrDefault(t => t.Index == w));
                if (m.Teams.Count == 2) Check(m.Teams.FirstOrDefault(t => t.Index != w));
            }
            return reasons.Count == 0 ? null : "No automatic MVP set. " + string.Join(" ", reasons);
        }

        // Step 8a2: writes the four automatic picks into the match. A manual pick (MvpIsManual) is never touched.
        // A match that is not Done has no MVP. Returns whether any column changed.
        public static bool ApplyMvp(LeagueMatch m)
        {
            if (m.MvpIsManual) return false;
            var picks = m.Status == LeagueMatchStatus.Done ? LeagueMvpRules.Compute(m.WinnerTeamIndex, ToMvpPlayers(m), IsClashGame(m), MvpProfileOf(m)) : MvpPicks.None;
            var changed = m.MvpProfileId != picks.Mvp || m.TeamMvpProfileId != picks.TeamMvp
                || m.StatsMvpProfileId != picks.StatsMvp || m.StatsTeamMvpProfileId != picks.StatsTeamMvp;
            m.MvpProfileId = picks.Mvp;
            m.TeamMvpProfileId = picks.TeamMvp;
            m.StatsMvpProfileId = picks.StatsMvp;
            m.StatsTeamMvpProfileId = picks.StatsTeamMvp;
            return changed;
        }

        // Step 8a2: checks an admin's hand-picked MVPs. Rules: the match is finished; a pick must be a player of this
        // match; match picks come from the winning team and team picks from the losing team (two-team matches only);
        // a K/D/A pick needs K/D/A filled for that whole team and an all-stats pick needs every stat filled for it
        // (an MVP is never set without the stats it is based on).
        public static string? ValidateMvpPicks(LeagueMatch m, LeagueMatchMvpRequest req)
        {
            if (m.Status != LeagueMatchStatus.Done) return "Only a finished match can have an MVP.";
            if (m.WinnerTeamIndex is not int w) return "This match has no winner yet.";
            var winners = m.Teams.FirstOrDefault(t => t.Index == w);
            if (winners == null) return "This match has no winning team.";
            var loserTeams = m.Teams.Where(t => t.Index != w).ToList();
            var losers = m.Teams.Count == 2 ? loserTeams[0] : null;

            string? Check(int? profileId, LeagueMatchTeam? team, string what, bool allStats, string teamLabel)
            {
                if (profileId == null) return null;
                if (team == null) return $"{what}: this match has no {teamLabel} team.";
                if (!team.Players.Any(p => p.ProfileId == profileId)) return $"{what}: that player is not on the {teamLabel} team of this match.";
                var mine = ToMvpPlayers(m).Where(x => x.TeamIndex == team.Index).ToList();
                if (allStats ? !mine.All(LeagueMvpRules.HasAllStats) : !mine.All(LeagueMvpRules.HasKda))
                    return allStats
                        ? $"{what}: fill in every stat for all players of the {teamLabel} team first."
                        : $"{what}: fill in kills, deaths and assists for all players of the {teamLabel} team first.";
                return null;
            }

            return Check(req.Mvp, winners, "Match MVP", false, "winning")
                ?? Check(req.StatsMvp, winners, "All-stats match MVP", true, "winning")
                ?? Check(req.TeamMvp, losers, "Team MVP", false, "losing")
                ?? Check(req.StatsTeamMvp, losers, "All-stats team MVP", true, "losing");
        }

        // Step 5c: checks the whole stats form first (including the match length) and only then writes it into the match's players, so a
        // refused form changes nothing. selectableChampionIds = the champions the picker offers (active ones);
        // a retired champion already on a player stays allowed for that player. Players the form does not
        // mention are left alone. Returns whether anything actually changed (an identical save is not logged).
        public static (bool Changed, string? Error) ApplyStats(
            LeagueMatch m, LeagueMatchStatsRequest req, IReadOnlySet<int> selectableChampionIds)
        {
            var rows = (req.Players ?? new List<LeagueMatchStatsPlayerDto?>()).ToList();
            if (rows.Count == 0) return (false, "There are no player rows to save.");
            if (req.DurationSeconds is < 0 or > MaxDurationSeconds)
                return (false, "The match length must be between 0 seconds and 24 hours.");

            var players = m.Teams.SelectMany(t => t.Players).ToDictionary(p => p.Id);
            var seen = new HashSet<int>();

            foreach (var row in rows)
            {
                if (row == null || !players.TryGetValue(row.PlayerId, out var p) || !seen.Add(row.PlayerId))
                    return (false, "A row does not belong to this match. Reload the page and try again.");

                var who = string.IsNullOrWhiteSpace(p.NameSnapshot) ? "A player" : p.NameSnapshot.Trim();
                if (row.Kills is < 0 or > MaxStat) return (false, $"{who}: kills must be between 0 and {MaxStat}.");
                if (row.Deaths is < 0 or > MaxStat) return (false, $"{who}: deaths must be between 0 and {MaxStat}.");
                if (row.Assists is < 0 or > MaxStat) return (false, $"{who}: assists must be between 0 and {MaxStat}.");
                if (row.Cs is < 0 or > MaxCs) return (false, $"{who}: CS must be between 0 and {MaxCs}.");
                if (row.DamageDealt is < 0 or > MaxBigStat) return (false, $"{who}: damage dealt must be between 0 and {MaxBigStat}.");
                if (row.DamageTaken is < 0 or > MaxBigStat) return (false, $"{who}: damage taken must be between 0 and {MaxBigStat}.");
                if (row.Gold is < 0 or > MaxBigStat) return (false, $"{who}: gold must be between 0 and {MaxBigStat}.");
                if (row.VisionScore is < 0 or > MaxVision) return (false, $"{who}: vision score must be between 0 and {MaxVision}.");
                if (row.ObjectiveDamage is < 0 or > MaxBigStat) return (false, $"{who}: objective damage must be between 0 and {MaxBigStat}.");
                if (row.Notes != null && row.Notes.Trim().Length > MaxNoteLength)
                    return (false, $"{who}: the note can be at most {MaxNoteLength} characters.");
                var extraError = LeagueExtraStatRules.Validate(row.Extras);
                if (extraError != null) return (false, $"{who}: {extraError}");

                if (row.Lane is int lane)
                {
                    if (!Enum.IsDefined(typeof(LeagueLane), lane)) return (false, $"{who}: unknown lane.");
                    if (p.Lane == null) return (false, $"{who}: this match has no lanes.");
                }

                if (row.ChampionId is int champ && champ != p.ChampionId && !selectableChampionIds.Contains(champ))
                    return (false, $"{who}: that champion does not exist or has been retired.");
            }

            var changed = false;
            if (m.DurationSeconds != req.DurationSeconds) { m.DurationSeconds = req.DurationSeconds; changed = true; }
            foreach (var row in rows)
            {
                var p = players[row!.PlayerId];
                LeagueLane? lane = row.Lane is int l ? (LeagueLane)l : null;
                if (p.ChampionId != row.ChampionId) { p.ChampionId = row.ChampionId; p.Champion = null; changed = true; }
                if (p.PlayedLane != lane) { p.PlayedLane = lane; changed = true; }
                if (p.Kills != row.Kills) { p.Kills = row.Kills; changed = true; }
                if (p.Deaths != row.Deaths) { p.Deaths = row.Deaths; changed = true; }
                if (p.Assists != row.Assists) { p.Assists = row.Assists; changed = true; }
                if (p.Cs != row.Cs) { p.Cs = row.Cs; changed = true; }
                if (p.DamageDealt != row.DamageDealt) { p.DamageDealt = row.DamageDealt; changed = true; }
                if (p.DamageTaken != row.DamageTaken) { p.DamageTaken = row.DamageTaken; changed = true; }
                if (p.Gold != row.Gold) { p.Gold = row.Gold; changed = true; }
                if (p.VisionScore != row.VisionScore) { p.VisionScore = row.VisionScore; changed = true; }
                if (p.ObjectiveDamage != row.ObjectiveDamage) { p.ObjectiveDamage = row.ObjectiveDamage; changed = true; }
                var note = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim();
                if (p.Notes != note) { p.Notes = note; changed = true; }
                // Step 13d: not sent = leave what is saved; sent = replace it (empty clears it).
                if (row.Extras != null)
                {
                    var extraJson = LeagueExtraStatRules.ToJson(row.Extras);
                    if (p.ExtraJson != extraJson) { p.ExtraJson = extraJson; changed = true; }
                }
            }
            return (changed, null);
        }

        public static LeagueMatchDto ToDto(LeagueMatch m) => new()
        {
            Id = m.Id,
            ModeKey = m.ModeKey,
            ModeName = m.ModeName,
            Status = StatusKey(m.Status),
            SubmittedUtc = m.SubmittedUtc,
            StartedUtc = m.StartedUtc,
            FinishedUtc = m.FinishedUtc,
            ClashEventId = m.ClashEventId,
            GameNumber = m.GameNumber,
            Winner = m.WinnerTeamIndex,
            Kills0 = m.TeamKills0,
            Kills1 = m.TeamKills1,
            Teams = m.Teams.OrderBy(t => t.Index).Select(t => new LeagueMatchTeamDto
            {
                Index = t.Index,
                Name = t.Name,
                Players = t.Players.OrderBy(p => p.Id).Select(p => new LeagueMatchPlayerDto
                {
                    Name = p.NameSnapshot,
                    IsGuest = p.WasGuest,
                    Lane = p.Lane is LeagueLane l ? (int)l : null,
                }).ToList(),
            }).ToList(),
        };

        // ── Database ──────────────────────────────────────────────────────────────────────────────────────

        public async Task<List<LeagueMatchDto>> GetOpenAsync()
        {
            var rows = await _db.LeagueMatches.AsNoTracking()
                .Include(m => m.Teams).ThenInclude(t => t.Players)
                // Clash games are managed on the Clash page (6e3), not in the builder's list of open matches.
                .Where(m => m.Status != LeagueMatchStatus.Done && m.Kind != LeagueMatchKind.Clash)
                .OrderByDescending(m => m.SubmittedUtc)
                .Take(MaxOpenListed)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        private async Task<LeagueMatchDto?> GetAsync(int id)
        {
            var m = await _db.LeagueMatches.AsNoTracking()
                .Include(x => x.Teams).ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(x => x.Id == id);
            return m == null ? null : ToDto(m);
        }

        public async Task<LeagueMatchResponse> SubmitAsync(LeagueMatchSubmitRequest req, string? userId)
        {
            if (req.ClashEventId != null) return await SubmitClashGameAsync(req, userId);
            if (!LeagueBuilderService.TryParseMode(req.Mode, out var mode)) return Fail("Unknown game mode.");

            var ids = (req.Teams ?? new List<LeagueBuilderTeamDto>())
                .SelectMany(t => t?.Slots ?? new List<LeagueBuilderSlotDto>())
                .Where(s => s?.PlayerId != null).Select(s => s!.PlayerId!.Value).Distinct().ToList();
            if (ids.Count > LeagueBuilderService.MaxPlayersPerRequest) return Fail("Too many players.");

            var ms = (await _settings.GetAllAsync()).First(m => m.Mode == mode);
            var cap = LeagueBuilderService.EffectiveCap(mode, ms.PlayerCount);
            var profiles = await _db.LeagueProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            var (match, error) = BuildMatch(req, profiles, cap, userId, DateTime.UtcNow);
            if (match == null) return Fail(error!);

            _db.LeagueMatches.Add(match);
            await _db.SaveChangesAsync();
            return new LeagueMatchResponse { Ok = true, Match = ToDto(match) };
        }

        // Step 6e1: the next game of a Clash series. The event decides the mode; the game number is the next free one.
        // Step 11c: the game belongs to a team of the clash (none sent = the first team) and its number, the cap of 4 and the
        // one-unfinished-game rule all count that team's games only. The unique index (ClashEventId, ClashTeamId, GameNumber)
        // stops a double click from creating the same game twice.
        private async Task<LeagueMatchResponse> SubmitClashGameAsync(LeagueMatchSubmitRequest req, string? userId)
        {
            var slots = req.ClashSlots;
            if ((slots?.Count ?? 0) > 50) return Fail("The request was too large.");

            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == req.ClashEventId);
            if (ev == null) return Fail("That clash no longer exists. Reload the page.");
            var eventError = LeagueClashGameRules.CheckEvent(ev);
            if (eventError != null) return Fail(eventError);

            int teamId;
            if (req.ClashTeamId is int askedTeam)
            {
                var ok = await _db.LeagueClashTeams.AsNoTracking().AnyAsync(t => t.Id == askedTeam && t.EventId == ev.Id);
                if (!ok) return Fail("That team no longer exists in this clash. Reload the page.");
                teamId = askedTeam;
            }
            else
            {
                teamId = await LeagueClashTeamData.EnsureFirstTeamIdAsync(_db, ev.Id);
            }

            var games = (await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.ClashEventId == ev.Id && m.ClashTeamId == teamId)
                .Select(m => new { m.GameNumber, m.Status })
                .ToListAsync()).Select(g => (g.GameNumber, g.Status)).ToList();
            var (number, numberError) = LeagueClashGameRules.NextGameNumber(games);
            if (numberError != null) return Fail(numberError);

            var ids = (slots ?? new List<LeagueClashSlotDto?>()).Where(s => s?.PlayerId != null)
                .Select(s => s!.PlayerId!.Value).Distinct().ToList();
            var profiles = await _db.LeagueProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            // Step 11g: one team per player. Someone who is in another team's unfinished game cannot be put on this team's game.
            var openGames = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.ClashEventId == ev.Id && m.Status != LeagueMatchStatus.Done)
                .Select(m => new
                {
                    m.ClashTeamId, m.Status,
                    Players = m.Teams.Where(t => t.Index == 0).SelectMany(t => t.Players)
                        .Where(p => p.ProfileId != null).Select(p => p.ProfileId!.Value).ToList(),
                })
                .ToListAsync();
            var taken = LeagueClashTeamRules.PlayersInOtherTeamsGames(teamId, ids,
                openGames.Select(g => (g.ClashTeamId, g.Status, (IReadOnlyCollection<int>)g.Players)));
            if (taken.Count > 0)
            {
                var names = string.Join(", ", taken.Select(i => profiles.TryGetValue(i, out var pr) ? pr.InGameName : "A player"));
                return Fail($"{names} {(taken.Count == 1 ? "is" : "are")} already in another team's unfinished game. Finish that game first, or pick someone else.");
            }

            var (match, error) = LeagueClashGameRules.BuildGame(ev, number, slots, req.ClashLog, profiles, userId, DateTime.UtcNow, teamId);
            if (match == null) return Fail(error!);

            _db.LeagueMatches.Add(match);
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                return Fail($"Game {number} could not be saved. Someone may have saved it at the same moment. Reload the page and check.");
            }
            return new LeagueMatchResponse { Ok = true, Match = ToDto(match) };
        }

        public async Task<LeagueMatchResponse> StartAsync(int id)
        {
            var now = DateTime.UtcNow;

            // Step 6e2: a Clash game also moves its event to InProgress (which locks the members' answers).
            var info = await _db.LeagueMatches.AsNoTracking().Where(m => m.Id == id)
                .Select(m => new { m.Kind, m.ClashEventId }).FirstOrDefaultAsync();
            if (info != null && info.Kind == LeagueMatchKind.Clash && info.ClashEventId is int eventId)
                return await StartClashGameAsync(id, eventId, now);

            var n = await _db.LeagueMatches
                .Where(m => m.Id == id && m.Status == LeagueMatchStatus.Submitted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, LeagueMatchStatus.Live)
                    .SetProperty(m => m.StartedUtc, (DateTime?)now));
            if (n == 0) return await WhyNotAsync(id, "Only a submitted match can be started.");
            return await CurrentAsync(id);
        }

        // One transaction, two conditional UPDATEs: the game goes Submitted -> Live, and the event goes Open ->
        // InProgress (its StartedUtc is set by game 1 only). If the event was cancelled or ended in the meantime,
        // or the game was already moved, nothing is saved. The transaction is rolled back when it is disposed
        // without a commit.
        private async Task<LeagueMatchResponse> StartClashGameAsync(int id, int eventId, DateTime now)
        {
            var status = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == eventId)
                .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
            if (status == null) return Fail("That clash no longer exists. Reload the page.");
            var startError = LeagueClashGameRules.CheckStart(status.Value);
            if (startError != null) return Fail(startError);

            await using var tx = await _db.Database.BeginTransactionAsync();

            var n = await _db.LeagueMatches
                .Where(m => m.Id == id && m.Status == LeagueMatchStatus.Submitted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, LeagueMatchStatus.Live)
                    .SetProperty(m => m.StartedUtc, (DateTime?)now));
            if (n == 0) return await WhyNotAsync(id, "Only a submitted match can be started.");

            var moved = await _db.LeagueClashEvents
                .Where(e => e.Id == eventId && e.Status == LeagueClashStatus.Open)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, LeagueClashStatus.InProgress)
                    .SetProperty(e => e.StartedUtc, (DateTime?)now));
            if (moved == 0)
            {
                // Not Open any more: fine if it is already InProgress (a later game), a failure if it was cancelled or ended.
                var now2 = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == eventId)
                    .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
                if (now2 == null || LeagueClashGameRules.CheckStart(now2.Value) != null)
                    return Fail("That clash was cancelled or ended just now, so the game was not started. Reload the page.");
            }

            await tx.CommitAsync();
            return await CurrentAsync(id);
        }

        // canEditDate = the caller may use LeagueMatch > EditDate. A "Played on" sent without it is refused (not silently
        // ignored), so the form never says one date and saves another. No "Played on" = now, as before.
        public async Task<LeagueMatchResponse> FinishAsync(LeagueMatchFinishRequest req, bool canEditDate = false)
        {
            var m = await _db.LeagueMatches.AsNoTracking().Include(x => x.Teams).FirstOrDefaultAsync(x => x.Id == req.Id);
            if (m == null) return Fail("That match no longer exists.");
            if (m.Status != LeagueMatchStatus.Live) return Fail("Only a live match can be finished.");

            var problem = ValidateFinish(m.Teams.Select(t => t.Index).ToList(), req.Winner, req.Kills0, req.Kills1);
            if (problem != null) return Fail(problem);

            var now = DateTime.UtcNow;
            var playedUtc = now;
            if (!string.IsNullOrWhiteSpace(req.PlayedOn))
            {
                if (!canEditDate) return Fail("You are not allowed to change the played date.");
                var (parsed, dateError) = LeagueDayRules.ParsePlayedOn(req.PlayedOn, now);
                if (dateError != null) return Fail(dateError);
                playedUtc = parsed ?? now;
            }
            var winner = req.Winner;
            var k0 = req.Kills0;
            var k1 = req.Kills1;
            var n = await _db.LeagueMatches
                .Where(x => x.Id == req.Id && x.Status == LeagueMatchStatus.Live)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, LeagueMatchStatus.Done)
                    .SetProperty(x => x.FinishedUtc, (DateTime?)now)
                    .SetProperty(x => x.PlayedUtc, playedUtc)
                    .SetProperty(x => x.WinnerTeamIndex, winner)
                    .SetProperty(x => x.TeamKills0, k0)
                    .SetProperty(x => x.TeamKills1, k1));
            if (n == 0) return await WhyNotAsync(req.Id, "Only a live match can be finished.");
            return await CurrentAsync(req.Id);
        }

        // Step 5b: change the winner or the team kills of a FINISHED match. Same rules as Finish (winner required,
        // kills only for two teams). The lifecycle timestamps stay as they were. Who changed it and when goes into
        // StatsEditedByUserId / StatsEditedUtc (spec section 14: "log who edited and when"; one log for every later
        // edit of a finished match, stats included). The conditional UPDATE only touches a match that is still Done.
        public async Task<LeagueMatchResponse> EditResultAsync(LeagueMatchFinishRequest req, string? userId)
        {
            var m = await _db.LeagueMatches.AsNoTracking().Include(x => x.Teams).FirstOrDefaultAsync(x => x.Id == req.Id);
            if (m == null) return Fail("That match no longer exists.");
            if (m.Status != LeagueMatchStatus.Done) return Fail("Only a finished match has a result to edit.");
            var oldWinner = m.WinnerTeamIndex;

            var problem = ValidateFinish(m.Teams.Select(t => t.Index).ToList(), req.Winner, req.Kills0, req.Kills1);
            if (problem != null) return Fail(problem);

            var now = DateTime.UtcNow;
            var winner = req.Winner;
            var k0 = req.Kills0;
            var k1 = req.Kills1;
            var n = await _db.LeagueMatches
                .Where(x => x.Id == req.Id && x.Status == LeagueMatchStatus.Done)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.WinnerTeamIndex, winner)
                    .SetProperty(x => x.TeamKills0, k0)
                    .SetProperty(x => x.TeamKills1, k1)
                    .SetProperty(x => x.StatsEditedByUserId, userId)
                    .SetProperty(x => x.StatsEditedUtc, (DateTime?)now));
            if (n == 0) return await WhyNotAsync(req.Id, "Only a finished match has a result to edit.");
            await RecomputeMvpAfterResultAsync(req.Id, oldWinner != winner);   // Step 8a2
            return await CurrentAsync(req.Id);
        }

        // Results/export step 1: change the "Played on" date of a FINISHED match. Nothing else is touched: the winner,
        // the teams, the clash and its game number stay as they are (so a Clash game stays inside its clash). Who changed
        // it and when goes into the same StatsEditedByUserId / StatsEditedUtc log as every other later edit.
        public async Task<LeagueMatchResponse> EditDateAsync(LeagueMatchDateRequest req, string? userId)
        {
            var status = await _db.LeagueMatches.AsNoTracking().Where(x => x.Id == req.Id)
                .Select(x => (LeagueMatchStatus?)x.Status).FirstOrDefaultAsync();
            if (status == null) return Fail("That match no longer exists.");
            if (status != LeagueMatchStatus.Done)
                return Fail("Only a finished match has a played date to edit. Set it when you finish the match.");

            var now = DateTime.UtcNow;
            var (playedUtc, error) = LeagueDayRules.ParsePlayedOn(req.PlayedOn, now);
            if (error != null) return Fail(error);
            if (playedUtc is not DateTime played) return Fail("Enter the date and time the match was played.");

            var n = await _db.LeagueMatches
                .Where(x => x.Id == req.Id && x.Status == LeagueMatchStatus.Done)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.PlayedUtc, played)
                    .SetProperty(x => x.StatsEditedByUserId, userId)
                    .SetProperty(x => x.StatsEditedUtc, (DateTime?)now));
            if (n == 0) return await WhyNotAsync(req.Id, "Only a finished match has a played date to edit.");
            return await CurrentAsync(req.Id);
        }

        // Step 5c: champion, lane played, K/D/A and the optional extra stats (plus the match length) for the players of a FINISHED match, one save for the whole
        // match. Partial rows are fine; an empty part clears it. The result, the teams and the snapshots are
        // never touched here. Who saved and when goes into StatsEditedByUserId / StatsEditedUtc (same two columns
        // as the result edit), but only when something really changed.
        public async Task<LeagueMatchResponse> SaveStatsAsync(LeagueMatchStatsRequest req, string? userId)
        {
            if ((req.Players?.Count ?? 0) > MaxStatRows) return Fail("Too many rows.");

            var m = await _db.LeagueMatches
                .Include(x => x.Teams).ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(x => x.Id == req.Id);
            if (m == null) return Fail("That match no longer exists.");
            if (m.Status != LeagueMatchStatus.Done) return Fail("Stats can only be added to a finished match.");

            var wanted = (req.Players ?? new List<LeagueMatchStatsPlayerDto?>())
                .Where(r => r?.ChampionId != null).Select(r => r!.ChampionId!.Value).Distinct().ToList();
            var selectable = (await _db.LeagueChampions.AsNoTracking()
                .Where(c => c.IsActive && wanted.Contains(c.Id)).Select(c => c.Id).ToListAsync()).ToHashSet();

            var (changed, error) = ApplyStats(m, req, selectable);
            if (error != null) return Fail(error);

            if (changed)
            {
                m.StatsEditedByUserId = userId;
                m.StatsEditedUtc = DateTime.UtcNow;
                ApplyMvp(m);   // Step 8a2: automatic MVPs follow the stats (cleared again if a stat is removed); a manual pick stays
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException) { return Fail("That match was changed or deleted meanwhile. Reload the page."); }
            }
            return await CurrentAsync(req.Id);
        }

        // Step 8a2: an admin picks the MVPs by hand, or gives them back to the automatic rule (Auto = true).
        // Who and when goes into the same StatsEditedByUserId / StatsEditedUtc log as every other later edit.
        public async Task<LeagueMatchResponse> SetMvpAsync(LeagueMatchMvpRequest req, string? userId)
        {
            var m = await _db.LeagueMatches.Include(x => x.Teams).ThenInclude(t => t.Players).FirstOrDefaultAsync(x => x.Id == req.Id);
            if (m == null) return Fail("That match no longer exists.");
            if (m.Status != LeagueMatchStatus.Done) return Fail("Only a finished match can have an MVP.");

            if (req.Auto)
            {
                m.MvpIsManual = false;
                ApplyMvp(m);
            }
            else
            {
                var problem = ValidateMvpPicks(m, req);
                if (problem != null) return Fail(problem);
                m.MvpIsManual = true;
                m.MvpProfileId = req.Mvp;
                m.TeamMvpProfileId = req.TeamMvp;
                m.StatsMvpProfileId = req.StatsMvp;
                m.StatsTeamMvpProfileId = req.StatsTeamMvp;
            }
            m.StatsEditedByUserId = userId;
            m.StatsEditedUtc = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Fail("That match was changed or deleted meanwhile. Reload the page."); }
            var current = await CurrentAsync(req.Id);
            // Step 12c.1: "Back to automatic" no longer closes silently when the rule names nobody.
            if (req.Auto && current.Ok) current.Notice = AutoMvpNotice(m);
            return current;
        }

        // Step 8a2: one-time (safe to repeat) fill of the MVPs for matches that were finished and given stats before
        // this step existed. Matches with a manual pick are skipped. Works in small batches.
        public async Task<LeagueMvpBackfillResponse> BackfillMvpsAsync()
        {
            var ids = await _db.LeagueMatches.AsNoTracking()
                .Where(x => x.Status == LeagueMatchStatus.Done && !x.MvpIsManual)
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
            var updated = 0;
            foreach (var chunk in ids.Chunk(50))
            {
                var matches = await _db.LeagueMatches.Include(x => x.Teams).ThenInclude(t => t.Players)
                    .Where(x => chunk.Contains(x.Id)).ToListAsync();
                foreach (var m in matches) if (ApplyMvp(m)) updated++;
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException) { return new() { Ok = false, Error = "A match changed during the run. Run it again.", Checked = ids.Count, Updated = updated }; }
                _db.ChangeTracker.Clear();
            }
            return new() { Ok = true, Checked = ids.Count, Updated = updated };
        }

        // Step 8a2: after a result edit. The winner decides which team is "winning", so when the winner changed a hand-picked
        // MVP no longer fits and goes back to automatic; the admin can pick again.
        private async Task RecomputeMvpAfterResultAsync(int id, bool winnerChanged)
        {
            var m = await _db.LeagueMatches.Include(x => x.Teams).ThenInclude(t => t.Players).FirstOrDefaultAsync(x => x.Id == id);
            if (m == null || m.Status != LeagueMatchStatus.Done) return;
            if (winnerChanged) m.MvpIsManual = false;
            var changed = ApplyMvp(m) || winnerChanged;
            if (!changed) return;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { /* the match was changed meanwhile; the next stats save recomputes it */ }
        }

        // Any status. The teams and players go with it (database cascade).
        public async Task<LeagueMatchResponse> DeleteAsync(int id)
        {
            var n = await _db.LeagueMatches.Where(m => m.Id == id).ExecuteDeleteAsync();
            return n == 0 ? Fail("That match no longer exists.") : new LeagueMatchResponse { Ok = true, Deleted = true };
        }

        private async Task<LeagueMatchResponse> CurrentAsync(int id)
        {
            var dto = await GetAsync(id);
            return dto == null ? Fail("That match no longer exists.") : new LeagueMatchResponse { Ok = true, Match = dto };
        }

        // The conditional update touched nothing: the match is gone, or somebody else already moved it.
        private async Task<LeagueMatchResponse> WhyNotAsync(int id, string wrongState)
        {
            var exists = await _db.LeagueMatches.AnyAsync(m => m.Id == id);
            return Fail(exists ? wrongState + " Reload the page to see its current state." : "That match no longer exists.");
        }

        private static LeagueMatchResponse Fail(string message) => new() { Ok = false, Error = message };
    }
}
