using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: the rank maths, the lane costs and the team generator. Generate takes a seeded Random, so results are repeatable.
    public class TeamBalancerTests
    {
        private static BalancerPlayer Player(int id, LeagueRank cur = LeagueRank.Gold, int div = 2,
            LeagueRank? peak = null, int? peakDiv = null, Dictionary<LeagueLane, int>? levels = null, int? tier = null) =>
            new()
            {
                Id = id,
                Name = "P" + id,
                CurRank = cur,
                CurDiv = div,
                PeakRank = peak ?? cur,
                PeakDiv = peakDiv ?? div,
                LaneLevels = levels,
                TierIndex = tier,
            };

        // A player who plays exactly one lane at level 5 (every other lane is level 1).
        private static BalancerPlayer Specialist(int id, LeagueLane lane, LeagueRank rank = LeagueRank.Gold) =>
            Player(id, rank, levels: new Dictionary<LeagueLane, int> { [lane] = 5 });

        private static List<BalancerPlayer> Roster(int count) =>
            Enumerable.Range(1, count)
                .Select(i => Player(i, (LeagueRank)(i % 8), div: 1 + i % 4, levels: new Dictionary<LeagueLane, int> { [(LeagueLane)(i % 5)] = 5 }))
                .ToList();

        private static List<int> PlacedIds(BalancerResult res) =>
            res.Teams.SelectMany(t => t.Slots).Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).OrderBy(x => x).ToList();

        // ---- rank maths ----

        [Theory]
        [InlineData(LeagueRank.Iron, 4, 0)]
        [InlineData(LeagueRank.Iron, 1, 3)]
        [InlineData(LeagueRank.Gold, 2, 14)]        // 3 * 4 + (4 - 2)
        [InlineData(LeagueRank.Diamond, 1, 27)]     // 6 * 4 + 3
        [InlineData(LeagueRank.Master, 0, 28)]      // no divisions from Master up
        [InlineData(LeagueRank.Challenger, 0, 36)]
        public void Score_is_four_points_per_tier_plus_the_division(LeagueRank rank, int div, int expected)
        {
            Assert.Equal(expected, LeagueRankMath.Score(rank, div));
        }

        [Fact]
        public void Only_the_ranks_up_to_Diamond_have_divisions()
        {
            Assert.True(LeagueRankMath.HasDivision(LeagueRank.Diamond));
            Assert.False(LeagueRankMath.HasDivision(LeagueRank.Master));
        }

        [Fact]
        public void Blend_is_sixty_percent_current_and_forty_percent_peak()
        {
            var p = Player(1, LeagueRank.Gold, 2, LeagueRank.Platinum, 1);   // 14 now, 19 at peak
            Assert.Equal(14 * .6 + 19 * .4, TeamBalancer.Blend(p), 6);
        }

        [Fact]
        public void Value_uses_the_rank_blend_unless_tier_balance_has_a_tier_to_use()
        {
            var p = Player(1, tier: 0);
            var blend = TeamBalancer.Blend(p);
            Assert.Equal(blend, TeamBalancer.Value(p, LeagueBalance.Rank, 5), 6);
            Assert.Equal(blend, TeamBalancer.Value(p, LeagueBalance.Tier, 1), 6);                // fewer than 2 tiers
            Assert.Equal(blend, TeamBalancer.Value(Player(2), LeagueBalance.Tier, 5), 6);        // no tier: falls back to rank
            Assert.Equal(36.0, TeamBalancer.Value(Player(3, tier: 0), LeagueBalance.Tier, 5), 6); // top tier
            Assert.Equal(0.0, TeamBalancer.Value(Player(4, tier: 4), LeagueBalance.Tier, 5), 6);  // bottom tier
            Assert.Equal(0.0, TeamBalancer.Value(Player(5, tier: 99), LeagueBalance.Tier, 5), 6); // out of range is clamped
        }

        // ---- lane costs ----

        [Fact]
        public void A_lane_costs_more_the_lower_the_players_level_in_it()
        {
            var p = Player(1, levels: new Dictionary<LeagueLane, int>
            {
                [LeagueLane.Top] = 5, [LeagueLane.Jungle] = 4, [LeagueLane.Mid] = 3, [LeagueLane.Adc] = 2, [LeagueLane.Support] = 1,
            });
            Assert.Equal(0, TeamBalancer.LaneCostFor(p, LeagueLane.Top));
            Assert.Equal(1, TeamBalancer.LaneCostFor(p, LeagueLane.Jungle));
            Assert.Equal(4, TeamBalancer.LaneCostFor(p, LeagueLane.Mid));
            Assert.Equal(6, TeamBalancer.LaneCostFor(p, LeagueLane.Adc));
            Assert.Equal(8, TeamBalancer.LaneCostFor(p, LeagueLane.Support));
        }

        [Fact]
        public void A_lane_the_player_does_not_list_costs_the_most()
        {
            var p = Player(1, levels: new Dictionary<LeagueLane, int> { [LeagueLane.Top] = 5 });
            Assert.Equal(8, TeamBalancer.LaneCostFor(p, LeagueLane.Mid));
            Assert.False(p.Plays(LeagueLane.Mid));
            Assert.True(p.Plays(LeagueLane.Top));
        }

        [Fact]
        public void BestPerm_gives_every_player_their_own_lane_when_they_all_differ()
        {
            var team = new List<BalancerPlayer>
            {
                Specialist(1, LeagueLane.Top), Specialist(2, LeagueLane.Jungle), Specialist(3, LeagueLane.Mid),
                Specialist(4, LeagueLane.Adc), Specialist(5, LeagueLane.Support),
            };
            var (cost, asg) = TeamBalancer.BestPerm(team);
            Assert.Equal(0, cost);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, asg);
        }

        [Fact]
        public void BestPerm_charges_the_full_price_when_two_players_want_the_same_lane()
        {
            var team = new List<BalancerPlayer> { Specialist(1, LeagueLane.Top), Specialist(2, LeagueLane.Top) };
            var (cost, asg) = TeamBalancer.BestPerm(team);
            Assert.Equal(8, cost);                       // one gets Top (0), the other any lane he does not play (8)
            Assert.Equal(2, asg.Length);
            Assert.Equal(2, asg.Distinct().Count());     // never the same lane twice
        }

        [Fact]
        public void BestPerm_refuses_a_team_of_more_than_five()
        {
            Assert.Throws<ArgumentException>(() => TeamBalancer.BestPerm(Roster(6)));
        }

        // ---- Generate ----

        [Theory]
        [InlineData(LeagueBalance.Random)]
        [InlineData(LeagueBalance.Lanes)]
        [InlineData(LeagueBalance.Rank)]
        public void Ten_players_make_two_full_teams_with_everyone_placed_once(LeagueBalance balance)
        {
            var roster = Roster(10);
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Normal, Balance = balance, Players = roster }, new Random(7));

            Assert.True(res.Ok, res.Error);
            Assert.Equal(2, res.Teams.Count);
            Assert.All(res.Teams, t => Assert.Equal(5, t.Slots.Count));
            Assert.Equal(roster.Select(p => p.Id).OrderBy(x => x).ToList(), PlacedIds(res));
        }

        [Fact]
        public void The_same_seed_gives_the_same_teams()
        {
            var request = new BalancerRequest { Mode = LeagueMode.Normal, Balance = LeagueBalance.Rank, Players = Roster(10) };
            var a = new TeamBalancer().Generate(request, new Random(42));
            var b = new TeamBalancer().Generate(request, new Random(42));

            string Shape(BalancerResult r) => string.Join("|", r.Teams.Select(t => string.Join(",", t.Slots.Select(s => s.PlayerId?.ToString() ?? "-"))));
            Assert.Equal(Shape(a), Shape(b));
        }

        [Fact]
        public void Nine_players_make_a_team_of_five_and_a_team_of_four()
        {
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Normal, Balance = LeagueBalance.Lanes, Players = Roster(9) }, new Random(3));

            Assert.True(res.Ok, res.Error);
            var sizes = res.Teams.Select(t => t.Slots.Count(s => s.PlayerId != null)).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { 4, 5 }, sizes);
        }

        [Fact]
        public void Rank_balance_puts_the_two_strongest_players_on_different_teams()
        {
            var roster = Roster(10);
            roster[0] = Player(101, LeagueRank.Challenger, 0);
            roster[1] = Player(102, LeagueRank.Challenger, 0);
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Normal, Balance = LeagueBalance.Rank, Players = roster }, new Random(11));

            Assert.True(res.Ok, res.Error);
            bool OnTeam(BalancerTeam t, int id) => t.Slots.Any(s => s.PlayerId == id);
            Assert.NotEqual(OnTeam(res.Teams[0], 101), OnTeam(res.Teams[0], 102));
        }

        [Fact]
        public void One_player_is_refused()
        {
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Normal, Players = Roster(1) }, new Random(1));
            Assert.False(res.Ok);
            Assert.Contains("at least 2", res.Error!);
        }

        [Fact]
        public void More_players_than_the_mode_allows_is_refused_and_says_how_many_to_remove()
        {
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Normal, ModePlayerCount = 10, Players = Roster(11) }, new Random(1));
            Assert.False(res.Ok);
            Assert.Contains("allows 10", res.Error!);
            Assert.Contains("Remove 1", res.Error!);
        }

        [Fact]
        public void Aram_makes_two_teams_and_says_lanes_do_not_apply()
        {
            var roster = Roster(10);
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Aram, Balance = LeagueBalance.Lanes, ModePlayerCount = 10, Players = roster }, new Random(5));

            Assert.True(res.Ok, res.Error);
            Assert.Equal(2, res.Teams.Count);
            Assert.Contains("Lanes don't apply", res.Note!);
            Assert.Equal(roster.Select(p => p.Id).OrderBy(x => x).ToList(), res.Teams.SelectMany(t => t.Slots).Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).OrderBy(x => x).ToList());
        }

        [Fact]
        public void Arena_makes_the_number_of_teams_asked_for()
        {
            var roster = Roster(8);
            var res = new TeamBalancer().Generate(
                new BalancerRequest { Mode = LeagueMode.Arena, Balance = LeagueBalance.Random, ModePlayerCount = 8, ArenaTeamCount = 4, Players = roster }, new Random(9));

            Assert.True(res.Ok, res.Error);
            Assert.Equal(4, res.Teams.Count);
            Assert.Equal(roster.Select(p => p.Id).OrderBy(x => x).ToList(), res.Teams.SelectMany(t => t.Slots).Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).OrderBy(x => x).ToList());
        }
    }
}
