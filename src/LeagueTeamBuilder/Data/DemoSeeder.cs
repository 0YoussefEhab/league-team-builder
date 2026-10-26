using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Data
{
    // Step D1: fake League data for the demo and for the click-through. Everything here is invented.
    //
    // Switched on by configuration, never by default:
    //   Demo:Seed   true  = add the demo data at start-up when it is not there yet.
    //   Demo:Reset  true  = first remove the demo data (only the demo data), then add it again. Puts the demo data back to the same
    //                       picture after you have changed it by hand.
    //
    // What it adds: 16 guest players (names ending in the hidden Riot ID suffix "#DEMO"), 14 finished custom games over the last
    // three weeks (10 draft, 4 ARAM; the newest one has no stats on purpose, to try "Add stats"), one waiting game, one finished
    // Clash with three games, and one upcoming Clash with free / busy answers. Stats are random but fixed by a seed, so the numbers
    // are the same on every run. MVPs are worked out by the same rule the site uses (LeagueMatchService.ApplyMvp).
    //
    // What it never touches: real users, real guests, real matches, the champion list, badges, settings. The demo rows are found by
    // three marks: the "#DEMO" Riot ID suffix, a "[Demo]" start of the match's builder log, and a "[Demo]" start of the clash title.
    public static class DemoSeeder
    {
        public const string Tag = "[Demo]";
        public const string RiotSuffix = "#DEMO";

        private sealed record Person(string Name, LeagueRank Cur, int CurDiv, LeagueRank Peak, int PeakDiv, LeagueLane Main, int[] Levels);

        // Levels are listed Top, Jungle, Mid, Adc, Support (1..5, 5 = main). Three mains per lane, and one player who plays everything.
        private static readonly Person[] People =
        {
            new("Nova",    LeagueRank.Gold,     2, LeagueRank.Platinum, 4, LeagueLane.Top,     new[] { 5, 3, 2, 1, 1 }),
            new("Kestrel", LeagueRank.Silver,   1, LeagueRank.Gold,     4, LeagueLane.Top,     new[] { 5, 4, 1, 1, 2 }),
            new("Rook",    LeagueRank.Iron,     3, LeagueRank.Bronze,   4, LeagueLane.Top,     new[] { 4, 1, 1, 2, 3 }),
            new("Mira",    LeagueRank.Platinum, 3, LeagueRank.Emerald,  4, LeagueLane.Jungle,  new[] { 2, 5, 3, 1, 1 }),
            new("Orion",   LeagueRank.Emerald,  2, LeagueRank.Diamond,  4, LeagueLane.Jungle,  new[] { 1, 5, 4, 2, 1 }),
            new("Tavi",    LeagueRank.Bronze,   2, LeagueRank.Silver,   3, LeagueLane.Jungle,  new[] { 3, 4, 1, 1, 1 }),
            new("Juno",    LeagueRank.Diamond,  3, LeagueRank.Diamond,  1, LeagueLane.Mid,     new[] { 1, 2, 5, 4, 1 }),
            new("Brisk",   LeagueRank.Gold,     4, LeagueRank.Gold,     1, LeagueLane.Mid,     new[] { 2, 1, 5, 3, 1 }),
            new("Cobalt",  LeagueRank.Silver,   3, LeagueRank.Platinum, 4, LeagueLane.Mid,     new[] { 1, 1, 4, 2, 3 }),
            new("Wren",    LeagueRank.Emerald,  4, LeagueRank.Emerald,  1, LeagueLane.Adc,     new[] { 1, 1, 2, 5, 3 }),
            new("Pixel",   LeagueRank.Gold,     1, LeagueRank.Platinum, 3, LeagueLane.Adc,     new[] { 1, 2, 3, 5, 1 }),
            new("Ember",   LeagueRank.Bronze,   4, LeagueRank.Silver,   4, LeagueLane.Adc,     new[] { 2, 1, 1, 4, 4 }),
            new("Sable",   LeagueRank.Platinum, 1, LeagueRank.Emerald,  4, LeagueLane.Support, new[] { 1, 1, 1, 2, 5 }),
            new("Quill",   LeagueRank.Silver,   4, LeagueRank.Gold,     3, LeagueLane.Support, new[] { 1, 3, 1, 1, 5 }),
            new("Lumen",   LeagueRank.Gold,     3, LeagueRank.Gold,     1, LeagueLane.Support, new[] { 1, 1, 2, 3, 4 }),
            new("Dax",     LeagueRank.Master,   0, LeagueRank.Master,   0, LeagueLane.Mid,     new[] { 4, 4, 5, 4, 4 }),
        };

        // Champion names by lane (all are in the seeded catalog; a name the catalog does not have is skipped).
        private static readonly string[][] ChampsByLane =
        {
            new[] { "Garen", "Darius", "Fiora", "Ornn", "Sett", "Jax" },
            new[] { "Lee Sin", "Vi", "Hecarim", "Warwick", "Sejuani", "Viego" },
            new[] { "Ahri", "Syndra", "Zed", "Orianna", "Yone", "Annie" },
            new[] { "Jinx", "Caitlyn", "Ezreal", "Ashe", "Vayne", "Kai'Sa" },
            new[] { "Lulu", "Thresh", "Leona", "Soraka", "Nami", "Braum" },
        };

        public static async Task SeedAsync(ApplicationDbContext db, IConfiguration config, ILogger log)
        {
            if (!config.GetValue("Demo:Seed", false)) return;
            log.LogWarning("Demo:Seed is on: fake League data is added to this database. Do not use it for a real league.");
            await SeedAsync(db, config.GetValue("Demo:Reset", false), log, DateTime.UtcNow);
        }

        // Returns true when it added the data, false when the demo data was already there (and Reset was off).
        public static async Task<bool> SeedAsync(ApplicationDbContext db, bool reset, ILogger log, DateTime nowUtc)
        {
            var exists = await db.LeagueProfiles.AnyAsync(p => p.RiotId != null && p.RiotId.EndsWith(RiotSuffix));
            if (exists && !reset)
            {
                log.LogInformation("Demo data is already in the database; nothing added.");
                return false;
            }

            // All or nothing: if anything below throws, nothing stays in the database. Without this, the players were saved by the
            // first SaveChanges, a later failure left them behind, and every next start said "already there" with no games.
            await using var tx = await db.Database.BeginTransactionAsync();
            if (exists) await RemoveAsync(db);

            var champions = await db.LeagueChampions.Where(c => c.IsActive).ToListAsync();
            if (champions.Count == 0)
            {
                log.LogWarning("Demo data needs the champion list, and it is empty. Nothing added.");
                return false;
            }
            var byName = champions.GroupBy(c => c.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());

            var rng = new Random(20261027);
            var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

            // ---- players -------------------------------------------------------------------------------------------
            var profiles = new List<LeagueProfile>();
            foreach (var person in People)
            {
                var p = new LeagueProfile
                {
                    IsGuest = true,
                    InGameName = person.Name,
                    RiotId = person.Name + RiotSuffix,
                    CurTier = person.Cur,
                    CurDiv = LeagueRankMath.HasDivision(person.Cur) ? person.CurDiv : null,
                    PeakTier = person.Peak,
                    PeakDiv = LeagueRankMath.HasDivision(person.Peak) ? person.PeakDiv : null,
                    MainLane = person.Main,
                    CreatedUtc = now.AddDays(-30),
                };
                var priority = 1;
                foreach (var lane in LeagueLaneLevels.AllLanes.OrderByDescending(l => person.Levels[(int)l]).ThenBy(l => (int)l))
                {
                    var level = person.Levels[(int)lane];
                    if (level <= LeagueLaneLevels.Min) continue;   // a lane with no row counts as level 1
                    p.Lanes.Add(new LeagueProfileLane { Lane = lane, Level = level, Priority = priority++ });
                    if (level < 4) continue;
                    foreach (var name in ChampsByLane[(int)lane].OrderBy(_ => rng.Next()).Take(2))
                        if (byName.TryGetValue(name.ToLowerInvariant(), out var champ))
                            p.Champs.Add(new LeagueProfileChamp { Lane = lane, ChampionId = champ.Id });
                }
                profiles.Add(p);
            }
            db.LeagueProfiles.AddRange(profiles);
            await db.SaveChangesAsync();

            // Two players who do better together.
            var nova = profiles.First(p => p.InGameName == "Nova");
            var sable = profiles.First(p => p.InGameName == "Sable");
            nova.DoBetterWithId = sable.Id;
            sable.DoBetterWithId = nova.Id;

            // ---- finished custom games ------------------------------------------------------------------------------
            const int finishedCount = 14;
            var aramGames = new HashSet<int> { 3, 6, 9, 10 };   // four ARAM games, ten draft games
            for (var i = 0; i < finishedCount; i++)
            {
                var aram = aramGames.Contains(i);
                var withStats = i != finishedCount - 1;         // the newest game has no stats
                var played = now.Date.AddDays(-(int)Math.Round(21 - i * 1.5)).AddHours(17 + rng.Next(0, 4)).AddMinutes(rng.Next(0, 60));
                var match = aram
                    ? BuildAram(profiles, rng, byName, champions, played, withStats)
                    : BuildDraft(profiles, rng, byName, champions, played, withStats);
                db.LeagueMatches.Add(match);
            }

            // ---- one game that is waiting to be played -------------------------------------------------------------
            var waiting = BuildDraft(profiles, rng, byName, champions, now, withStats: false);
            waiting.Status = LeagueMatchStatus.Submitted;
            waiting.WinnerTeamIndex = null;
            waiting.StartedUtc = null;
            waiting.FinishedUtc = null;
            waiting.DurationSeconds = null;
            waiting.TeamKills0 = null;
            waiting.TeamKills1 = null;
            waiting.PlayedUtc = now;
            waiting.SubmittedUtc = now;
            db.LeagueMatches.Add(waiting);

            // ---- a finished Clash with three games -----------------------------------------------------------------
            var doneStart = now.Date.AddDays(-6).AddHours(18);
            var doneClash = new LeagueClashEvent
            {
                Title = Tag + " Friday Clash",
                StartsAtUtc = doneStart,
                Mode = LeagueMode.Normal,
                Status = LeagueClashStatus.Done,
                StartedUtc = doneStart.AddMinutes(5),
                EndedUtc = doneStart.AddHours(3),
                CreatedUtc = doneStart.AddDays(-5),
            };
            var doneTeam = new LeagueClashTeam { Name = "Team one", SortOrder = 0, CreatedUtc = doneClash.CreatedUtc };
            doneClash.Teams.Add(doneTeam);
            var clashLineup = PickLineup(profiles, rng, new HashSet<int>());
            var clashResults = new[] { true, true, false };
            for (var g = 0; g < clashResults.Length; g++)
            {
                var played = doneStart.AddMinutes(30 + g * 50);
                var game = BuildClashGame(clashLineup, rng, byName, champions, played, g + 1, clashResults[g]);
                game.ClashEvent = doneClash;
                game.ClashTeam = doneTeam;
                doneClash.Matches.Add(game);
            }
            db.LeagueClashEvents.Add(doneClash);

            // ---- an upcoming Clash with answers --------------------------------------------------------------------
            var nextStart = now.Date.AddDays(3).AddHours(18);
            var nextClash = new LeagueClashEvent
            {
                Title = Tag + " Sunday Clash",
                StartsAtUtc = nextStart,
                Mode = LeagueMode.Normal,
                Status = LeagueClashStatus.Open,
                CreatedUtc = now.AddDays(-2),
            };
            nextClash.Teams.Add(new LeagueClashTeam { Name = "Team one", SortOrder = 0, CreatedUtc = nextClash.CreatedUtc });
            var busy = new HashSet<string> { "Rook", "Ember", "Tavi" };
            foreach (var p in profiles.Where(p => busy.Contains(p.InGameName) || p.InGameName is "Nova" or "Mira" or "Juno" or "Wren"))
            {
                nextClash.Responses.Add(new LeagueClashResponse
                {
                    ProfileId = p.Id,
                    Status = busy.Contains(p.InGameName) ? LeagueClashAnswer.Busy : LeagueClashAnswer.Free,
                    RespondedUtc = now.AddHours(-rng.Next(2, 40)),
                });
            }
            db.LeagueClashEvents.Add(nextClash);

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            log.LogInformation("Demo data added: {Players} players, {Games} finished custom games, 1 waiting game, 2 clashes.", profiles.Count, finishedCount);
            return true;
        }

        // Removes only what the demo added. Matches first (a match points at its clash), then the clash rows, then the players.
        public static async Task RemoveAsync(ApplicationDbContext db)
        {
            await db.LeagueMatches.Where(m => m.BuilderLog != null && m.BuilderLog.StartsWith(Tag)).ExecuteDeleteAsync();
            var eventIds = await db.LeagueClashEvents.Where(e => e.Title.StartsWith(Tag)).Select(e => e.Id).ToListAsync();
            if (eventIds.Count > 0)
            {
                await db.LeagueClashResponses.Where(r => eventIds.Contains(r.EventId)).ExecuteDeleteAsync();
                await db.LeagueClashTeams.Where(t => eventIds.Contains(t.EventId)).ExecuteDeleteAsync();
                await db.LeagueClashEvents.Where(e => eventIds.Contains(e.Id)).ExecuteDeleteAsync();
            }
            await db.LeagueProfiles.Where(p => p.RiotId != null && p.RiotId.EndsWith(RiotSuffix)).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
        }

        // ---- building one game ---------------------------------------------------------------------------------------

        private static int LevelOf(LeagueProfile p, LeagueLane lane) => p.Lanes.FirstOrDefault(l => l.Lane == lane)?.Level ?? 1;

        // Two players for a lane: players who main it first (in random order), then whoever plays it best.
        private static List<LeagueProfile> PickLane(IReadOnlyList<LeagueProfile> all, HashSet<int> used, LeagueLane lane, int count, Random rng)
        {
            var free = all.Where(p => !used.Contains(p.Id)).ToList();
            var chosen = free.Where(p => LevelOf(p, lane) >= 4).OrderBy(_ => rng.Next()).ToList();
            if (chosen.Count < count)
                chosen.AddRange(free.Except(chosen).OrderByDescending(p => LevelOf(p, lane)).ThenBy(_ => rng.Next()));
            var take = chosen.Take(count).ToList();
            foreach (var p in take) used.Add(p.Id);
            return take;
        }

        // Five players, one per lane (Top to Support).
        private static List<LeagueProfile> PickLineup(IReadOnlyList<LeagueProfile> all, Random rng, HashSet<int> used)
            => LeagueLaneLevels.AllLanes.Select(lane => PickLane(all, used, lane, 1, rng)[0]).ToList();

        private static LeagueMatch BuildDraft(IReadOnlyList<LeagueProfile> all, Random rng, Dictionary<string, LeagueChampion> byName,
            List<LeagueChampion> champions, DateTime played, bool withStats)
        {
            var used = new HashSet<int>();
            var a = new List<LeagueProfile>();
            var b = new List<LeagueProfile>();
            foreach (var lane in LeagueLaneLevels.AllLanes)
            {
                var two = PickLane(all, used, lane, 2, rng);
                a.Add(two[0]);
                b.Add(two[1]);
            }
            var team0Wins = rng.Next(2) == 0;
            var match = NewMatch(LeagueMode.Normal, played);
            AddTeam(match, 0, "Team 1", a, lanes: true, rng, byName, champions, withStats, won: team0Wins);
            AddTeam(match, 1, "Team 2", b, lanes: true, rng, byName, champions, withStats, won: !team0Wins);
            Finish(match, rng, withStats, team0Wins ? 0 : 1);
            return match;
        }

        private static LeagueMatch BuildAram(IReadOnlyList<LeagueProfile> all, Random rng, Dictionary<string, LeagueChampion> byName,
            List<LeagueChampion> champions, DateTime played, bool withStats)
        {
            var ten = all.OrderBy(_ => rng.Next()).Take(10).ToList();
            var team0Wins = rng.Next(2) == 0;
            var match = NewMatch(LeagueMode.Aram, played);
            AddTeam(match, 0, "Team 1", ten.Take(5).ToList(), lanes: false, rng, byName, champions, withStats, won: team0Wins);
            AddTeam(match, 1, "Team 2", ten.Skip(5).ToList(), lanes: false, rng, byName, champions, withStats, won: !team0Wins);
            Finish(match, rng, withStats, team0Wins ? 0 : 1);
            return match;
        }

        // A Clash game: our team (index 0) and an empty "Opponents" team (index 1), like the real Clash board saves it.
        private static LeagueMatch BuildClashGame(List<LeagueProfile> lineup, Random rng, Dictionary<string, LeagueChampion> byName,
            List<LeagueChampion> champions, DateTime played, int gameNumber, bool won)
        {
            var match = NewMatch(LeagueMode.Normal, played);
            match.Kind = LeagueMatchKind.Clash;
            match.GameNumber = gameNumber;
            AddTeam(match, 0, LeagueClashGameRules.ClashTeamName, lineup, lanes: true, rng, byName, champions, withStats: true, won: won);
            match.Teams.Add(new LeagueMatchTeam { Index = 1, Name = LeagueClashGameRules.OpponentsName });
            var seconds = rng.Next(1500, 2500);
            match.StartedUtc = played.AddSeconds(-seconds);
            match.FinishedUtc = played;
            match.DurationSeconds = seconds;
            match.WinnerTeamIndex = won ? 0 : 1;
            match.TeamKills0 = match.Teams[0].Players.Sum(p => p.Kills ?? 0);
            LeagueMatchService.ApplyMvp(match);
            return match;
        }

        private static LeagueMatch NewMatch(LeagueMode mode, DateTime played) => new()
        {
            Kind = LeagueMatchKind.Custom,
            ModeKey = LeagueBuilderService.ModeKey(mode),
            ModeName = LeagueModeRules.Name(mode),
            Status = LeagueMatchStatus.Done,
            PlayedUtc = played,
            SubmittedUtc = played.AddMinutes(-45),
            BuilderLog = Tag + " Balance: Lanes.",
        };

        // The winning team's numbers are nudged up a little in FillStats, so the scoreboards look like real games.
        private static void AddTeam(LeagueMatch match, int index, string name, List<LeagueProfile> players, bool lanes, Random rng,
            Dictionary<string, LeagueChampion> byName, List<LeagueChampion> champions, bool withStats, bool won)
        {
            var team = new LeagueMatchTeam { Index = index, Name = name };
            for (var i = 0; i < players.Count; i++)
            {
                var lane = lanes ? (LeagueLane)i : (LeagueLane?)null;
                var row = LeagueMatchService.SnapshotPlayer(players[i], lane);
                if (withStats) FillStats(row, lane, rng, won, byName, champions);
                team.Players.Add(row);
            }
            match.Teams.Add(team);
        }

        private static void Finish(LeagueMatch match, Random rng, bool withStats, int winner)
        {
            var seconds = rng.Next(1200, 2400);
            match.StartedUtc = match.PlayedUtc.AddSeconds(-seconds);
            match.FinishedUtc = match.PlayedUtc;
            match.WinnerTeamIndex = winner;
            if (!withStats) return;
            match.DurationSeconds = seconds;
            match.TeamKills0 = match.Teams[0].Players.Sum(p => p.Kills ?? 0);
            match.TeamKills1 = match.Teams[1].Players.Sum(p => p.Kills ?? 0);
            LeagueMatchService.ApplyMvp(match);
        }

        // ---- one player's numbers ------------------------------------------------------------------------------------

        private static int R(Random rng, int lo, int hi) => rng.Next(lo, hi + 1);

        private static void FillStats(LeagueMatchPlayer row, LeagueLane? lane, Random rng, bool won,
            Dictionary<string, LeagueChampion> byName, List<LeagueChampion> champions)
        {
            var l = lane ?? (LeagueLane)rng.Next(5);   // ARAM has no lanes; the numbers still follow a role
            var support = l == LeagueLane.Support;
            row.Kills = R(rng, 0, support ? 5 : 9) + (won ? 2 : 0);
            row.Deaths = R(rng, 1, 7) + (won ? 0 : 3);
            row.Assists = R(rng, 2, 12) + (support ? 7 : 0) + (won ? 3 : 0);
            row.Cs = lane == null ? R(rng, 10, 60) : l switch
            {
                LeagueLane.Top => R(rng, 150, 260),
                LeagueLane.Jungle => R(rng, 120, 190),
                LeagueLane.Mid => R(rng, 160, 270),
                LeagueLane.Adc => R(rng, 180, 300),
                _ => R(rng, 15, 55),
            };
            row.DamageDealt = l switch
            {
                LeagueLane.Top => R(rng, 14000, 26000),
                LeagueLane.Jungle => R(rng, 9000, 18000),
                LeagueLane.Mid => R(rng, 16000, 30000),
                LeagueLane.Adc => R(rng, 18000, 34000),
                _ => R(rng, 5000, 11000),
            };
            row.DamageTaken = l switch
            {
                LeagueLane.Top => R(rng, 18000, 32000),
                LeagueLane.Jungle => R(rng, 15000, 28000),
                LeagueLane.Mid => R(rng, 9000, 17000),
                LeagueLane.Adc => R(rng, 8000, 16000),
                _ => R(rng, 11000, 22000),
            };
            row.Gold = support ? R(rng, 5500, 8500) : R(rng, 8000, 13500);
            row.VisionScore = lane == null ? R(rng, 1, 8) : support ? R(rng, 45, 95) : l == LeagueLane.Jungle ? R(rng, 20, 40) : R(rng, 12, 30);
            row.ObjectiveDamage = lane == null ? R(rng, 500, 3000) : l == LeagueLane.Jungle ? R(rng, 4000, 12000) : l == LeagueLane.Top ? R(rng, 2000, 7000) : R(rng, 500, 4000);
            row.PlayedLane = lane;

            // A champion from the player's lane (a random one from the catalog in ARAM).
            var names = ChampsByLane[(int)l];
            var champ = names.OrderBy(_ => rng.Next()).Select(n => byName.TryGetValue(n.ToLowerInvariant(), out var c) ? c : null).FirstOrDefault(c => c != null);
            row.ChampionId = (lane == null ? champions[rng.Next(champions.Count)] : champ ?? champions[rng.Next(champions.Count)]).Id;
        }
    }
}
