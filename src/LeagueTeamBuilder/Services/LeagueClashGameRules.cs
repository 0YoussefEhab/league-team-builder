using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 6e1: the pure rules for saving a game of a Clash series (docs/LEAGUE_BUILD_SPEC.md section 4). No database:
    // LeagueMatchService.SubmitAsync loads the event, its games and the profiles and calls these.
    public static class LeagueClashGameRules
    {
        // Spec: "cap at 4 games".
        public const int MaxGames = 4;
        public const int TeamSize = 5;
        public const int MaxLogLines = 30;
        public const int MaxLogLineLength = 200;
        public const string ClashTeamName = "Clash team";
        public const string OpponentsName = "Opponents";

        // Only an Open or InProgress clash takes a new game.
        public static string? CheckEvent(LeagueClashEvent ev)
        {
            if (ev.Status != LeagueClashStatus.Open && ev.Status != LeagueClashStatus.InProgress)
                return "That clash is over or cancelled.";
            if (!LeagueClashScheduleService.IsClashMode(ev.Mode)) return "A clash is Normal Draft or ARAM.";
            return null;
        }

        // Step 6e2: a game can only be started while its clash is Open or InProgress. A game of a cancelled or
        // finished clash stays where it is and can be deleted from History.
        public static string? CheckStart(LeagueClashStatus status) =>
            status == LeagueClashStatus.Open || status == LeagueClashStatus.InProgress
                ? null
                : "That clash is over or cancelled, so its game cannot be started.";

        // Starting the first game moves the clash from Open to InProgress (this is what locks the answers, 6c).
        // A later game finds it already InProgress and leaves it alone.
        public static bool StartMovesEvent(LeagueClashStatus status) => status == LeagueClashStatus.Open;

        // Step 6e4a: End clash. Only a clash that is InProgress can be ended, and only when every saved game is Done
        // (an unfinished game would be stranded: the Clash page lists Open and InProgress clashes only). The way out of
        // an abandoned game is Delete in History. An Open clash that never started is cancelled in Settings, not ended.
        public static string? CheckEnd(LeagueClashStatus status, IReadOnlyCollection<LeagueMatchStatus> gameStatuses)
        {
            switch (status)
            {
                case LeagueClashStatus.Open:
                    return "That clash has not started. Cancel it in Settings > Clash schedule instead.";
                case LeagueClashStatus.Done:
                    return "That clash has already ended.";
                case LeagueClashStatus.Cancelled:
                    return "That clash was cancelled.";
            }
            if (gameStatuses.Any(s => s != LeagueMatchStatus.Done))
                return "Finish the current game first (or delete it in History), then end the clash.";
            return null;
        }

        // The number of the next game: highest saved number + 1, so deleting game 2 of 3 never makes a duplicate.
        // Refused while a game of the event is still Submitted or Live, and past the cap.
        public static (int Number, string? Error) NextGameNumber(IReadOnlyCollection<(int? GameNumber, LeagueMatchStatus Status)> games)
        {
            if (games.Any(g => g.Status != LeagueMatchStatus.Done))
                return (0, "Finish the current game before saving the next one.");
            var next = games.Count == 0 ? 1 : games.Max(g => g.GameNumber ?? 0) + 1;
            if (next > MaxGames)
                return (0, $"A clash has at most {MaxGames} games. End the clash when you are done.");
            return (next, null);
        }

        // Step 6e4d: Next game. Checks the clash and its games, and picks the game to copy the team from (the highest
        // numbered one; every game is Done by then). Refused with no game saved yet, while a game is Submitted or Live,
        // and when the clash already has 4 games. The number is the one NextGameNumber gives, so Save as game N agrees.
        public static (LeagueMatch? Source, int Number, string? Error) CheckNext(LeagueClashEvent ev, IReadOnlyCollection<LeagueMatch> games)
        {
            var eventError = CheckEvent(ev);
            if (eventError != null) return (null, 0, eventError);
            if (games.Count == 0) return (null, 0, "Save and finish game 1 first. There is no finished game to copy the team from.");
            var (number, error) = NextGameNumber(games.Select(g => (g.GameNumber, g.Status)).ToList());
            if (error != null) return (null, 0, error);
            var source = games.OrderByDescending(g => g.GameNumber ?? 0).ThenByDescending(g => g.Id).First();
            return (source, number, null);
        }

        // Step 6e4d: the Clash team of a saved game as five slots. Slot i is lane i in Normal, no lane in ARAM (the mode
        // comes from the event). A Normal player goes back to the lane slot the game saved; anyone without a usable lane
        // takes the first empty slot. A player whose profile is gone (existingProfileIds does not have it) leaves the
        // slot empty and is reported by name. Nobody is marked Manual: the page rechecks the team anyway.
        public static (List<LeagueClashSlotDto> Slots, List<string> Missing) SlotsFromGame(
            LeagueMatch source, LeagueMode mode, IReadOnlySet<int> existingProfileIds)
        {
            var slots = Enumerable.Range(0, TeamSize)
                .Select(i => new LeagueClashSlotDto { Lane = mode == LeagueMode.Normal ? i : null })
                .ToList();
            var missing = new List<string>();
            var team = source.Teams.FirstOrDefault(t => t.Index == 0);
            if (team == null) return (slots, missing);

            var seen = new HashSet<int>();
            var loose = new List<int>();
            foreach (var p in team.Players.OrderBy(x => x.Id))
            {
                if (p.ProfileId is not int pid || !existingProfileIds.Contains(pid))
                {
                    missing.Add(string.IsNullOrWhiteSpace(p.NameSnapshot) ? "A player" : p.NameSnapshot);
                    continue;
                }
                if (!seen.Add(pid)) continue;
                if (mode == LeagueMode.Normal && p.Lane is LeagueLane lane
                    && (int)lane >= 0 && (int)lane < TeamSize && slots[(int)lane].PlayerId == null)
                {
                    slots[(int)lane].PlayerId = pid;
                    continue;
                }
                loose.Add(pid);
            }
            foreach (var pid in loose)
            {
                var free = slots.FirstOrDefault(s => s.PlayerId == null);
                if (free == null) break;
                free.PlayerId = pid;
            }
            return (slots, missing);
        }

        // The builder log lines the page posted: trimmed, one line each, at most 30 lines of 200 characters.
        // Null when nothing is left. Stored joined by newlines and shown later with textContent.
        public static string? CleanLog(IEnumerable<string?>? lines)
        {
            if (lines == null) return null;
            var kept = new List<string>();
            foreach (var raw in lines)
            {
                var t = (raw ?? "").Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
                if (t.Length == 0) continue;
                if (t.Length > MaxLogLineLength) t = t.Substring(0, MaxLogLineLength);
                kept.Add(t);
                if (kept.Count == MaxLogLines) break;
            }
            return kept.Count == 0 ? null : string.Join("\n", kept);
        }

        // Turns the posted team into the game to insert: team 0 "Clash team" with the players, team 1 "Opponents" with
        // nobody (a win is winner 0, a loss winner 1). Slot i is lane i in Normal, no lane in ARAM; the lane the browser
        // sent is ignored. Names, guest flag and ranks are copied now (snapshots, spec R4).
        public static (LeagueMatch? Match, string? Error) BuildGame(
            LeagueClashEvent ev, int gameNumber, IReadOnlyList<LeagueClashSlotDto?>? slots, IEnumerable<string?>? log,
            IReadOnlyDictionary<int, LeagueProfile> profiles, string? userId, DateTime nowUtc, int? clashTeamId = null)
        {
            var eventError = CheckEvent(ev);
            if (eventError != null) return (null, eventError);
            if (slots == null || slots.Count != TeamSize) return (null, "The team needs exactly 5 slots.");

            var team = new LeagueMatchTeam { Index = 0, Name = ClashTeamName };
            var seen = new HashSet<int>();
            for (var i = 0; i < TeamSize; i++)
            {
                if (slots[i]?.PlayerId is not int pid) continue;
                if (!seen.Add(pid)) return (null, "A player can only be on the team once.");
                if (!profiles.TryGetValue(pid, out var p)) return (null, "A player no longer exists. Reload the page.");
                LeagueLane? lane = ev.Mode == LeagueMode.Normal ? (LeagueLane)i : null;
                team.Players.Add(LeagueMatchService.SnapshotPlayer(p, lane));
            }
            if (team.Players.Count == 0) return (null, "Put at least one player on the team.");

            var match = new LeagueMatch
            {
                Kind = LeagueMatchKind.Clash,
                ModeKey = LeagueBuilderService.ModeKey(ev.Mode),
                ModeName = LeagueModeRules.Name(ev.Mode),
                Status = LeagueMatchStatus.Submitted,
                SubmittedUtc = nowUtc,
                PlayedUtc = nowUtc,
                CreatedByUserId = userId,
                ClashEventId = ev.Id,
                ClashTeamId = clashTeamId,
                GameNumber = gameNumber,
                BuilderLog = CleanLog(log),
            };
            match.Teams.Add(team);
            match.Teams.Add(new LeagueMatchTeam { Index = 1, Name = OpponentsName });
            return (match, null);
        }
    }
}
