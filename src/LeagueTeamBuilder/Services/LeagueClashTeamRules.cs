using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 11c: the rules for the teams of a clash (several teams can play the same clash). Pure rules, no database, so they are
    // unit-tested; LeagueClashTeamData below is the one small piece that touches the database and is shared by the services.
    public static class LeagueClashTeamRules
    {
        // Owner decisions (Step 11): at most 4 teams per clash, name 1-30 characters, unique per clash (ignoring case).
        public const int MaxTeams = 4;
        public const int MaxNameLength = 30;
        public const string FirstTeamName = "Team one";

        // Trims, folds any run of spaces / tabs / line breaks into one space and drops control characters.
        // Error is set for an empty name and for a name over 30 characters (the cleaned text is still returned).
        public static (string Name, string? Error) CleanName(string? raw)
        {
            var parts = (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new string(p.Where(c => !char.IsControl(c)).ToArray()))
                .Where(p => p.Length > 0);
            var text = string.Join(" ", parts);
            if (text.Length == 0) return ("", "Give the team a name.");
            if (text.Length > MaxNameLength) return (text, $"A team name has at most {MaxNameLength} characters.");
            return (text, null);
        }

        // Teams can only be added, renamed or deleted while the clash is Open or InProgress.
        public static string? CheckClashOpen(LeagueClashStatus status) =>
            status == LeagueClashStatus.Open || status == LeagueClashStatus.InProgress
                ? null
                : "That clash is over or cancelled, so its teams cannot be changed.";

        // existingNames = the names of the teams the clash has now. cleanName comes from CleanName.
        public static string? CheckCreate(IReadOnlyCollection<string> existingNames, string cleanName)
        {
            if (existingNames.Count >= MaxTeams) return $"A clash has at most {MaxTeams} teams.";
            if (existingNames.Any(n => string.Equals(n, cleanName, StringComparison.OrdinalIgnoreCase)))
                return $"There is already a team called {cleanName} in this clash.";
            return null;
        }

        // The other teams' names must not match; changing only the case of its own name is fine.
        public static string? CheckRename(IReadOnlyCollection<(int Id, string Name)> teams, int teamId, string cleanName)
        {
            if (teams.Any(t => t.Id != teamId && string.Equals(t.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
                return $"There is already a team called {cleanName} in this clash.";
            return null;
        }

        // A team with games is never deleted (the games point at it), and a clash keeps at least one team.
        public static string? CheckDelete(int gamesOfTeam, int teamCount)
        {
            if (gamesOfTeam > 0) return "That team has games, so it cannot be deleted.";
            if (teamCount <= 1) return "A clash needs at least one team.";
            return null;
        }

        // Step 11g: one team per player. Of the players asked for, the ones who are in an UNFINISHED game of another team of the same
        // clash (a game with no team counts as another team). A finished game frees its players. Pure, so it is unit-tested.
        public static List<int> PlayersInOtherTeamsGames(int myTeamId, IEnumerable<int> playerIds,
            IEnumerable<(int? TeamId, LeagueMatchStatus Status, IReadOnlyCollection<int> Players)> games)
        {
            var busy = new HashSet<int>();
            foreach (var g in games)
            {
                if (g.Status == LeagueMatchStatus.Done || g.TeamId == myTeamId) continue;
                foreach (var p in g.Players) busy.Add(p);
            }
            return playerIds.Where(busy.Contains).Distinct().ToList();
        }

        public static int NextSortOrder(IEnumerable<int> existingOrders)
        {
            var any = false;
            var max = 0;
            foreach (var o in existingOrders)
            {
                if (!any || o > max) max = o;
                any = true;
            }
            return any ? max + 1 : 0;
        }
    }

    // The part that needs the database. Static so LeagueMatchService, LeagueClashBoardService and LeagueClashScheduleService can
    // all use it without a new service to register (Process rule 9).
    public static class LeagueClashTeamData
    {
        public static Task<int?> FirstTeamIdAsync(ApplicationDbContext db, int eventId) =>
            db.LeagueClashTeams.AsNoTracking()
                .Where(t => t.EventId == eventId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .Select(t => (int?)t.Id)
                .FirstOrDefaultAsync();

        // The id of the clash's first team. A clash that has none yet gets "Team one" now. Two requests at the same moment end on
        // the unique (EventId, Name) index: the loser reads the winner's row.
        public static async Task<int> EnsureFirstTeamIdAsync(ApplicationDbContext db, int eventId)
        {
            var existing = await FirstTeamIdAsync(db, eventId);
            if (existing != null) return existing.Value;

            var team = new LeagueClashTeam
            {
                EventId = eventId,
                Name = LeagueClashTeamRules.FirstTeamName,
                SortOrder = 0,
                CreatedUtc = DateTime.UtcNow,
            };
            db.LeagueClashTeams.Add(team);
            try
            {
                await db.SaveChangesAsync();
                return team.Id;
            }
            catch (DbUpdateException)
            {
                db.Entry(team).State = EntityState.Detached;
                var again = await FirstTeamIdAsync(db, eventId);
                if (again != null) return again.Value;
                throw;
            }
        }
    }
}
