using System.Globalization;
using System.Text;

namespace LeagueTeamBuilder.Services
{
    // Step 13a (PLAN-league.md): the name a player has in the game. It is the part of the League profile's Riot ID before
    // the "#" ("Suguru#EG1" -> "Suguru"; a Riot ID typed without a tag is the name as it is). No database here, covered by tests.
    public static class LeagueGameNameRules
    {
        // The game name of a Riot ID, or null when there is none (empty, blank, or only a "#tag").
        public static string? FromRiotId(string? riotId)
        {
            if (string.IsNullOrWhiteSpace(riotId)) return null;
            var hash = riotId.IndexOf('#');
            var name = (hash >= 0 ? riotId[..hash] : riotId).Trim();
            return name.Length == 0 ? null : name;
        }

        // Checks a name typed in the "Fill from screenshots" box. Returns an error text (no player name in it; the caller adds it)
        // or null, and gives the trimmed text to store as the Riot ID in clean (stored as typed, so a "#tag" is kept).
        public static string? ValidateTyped(string? typed, out string clean)
        {
            clean = (typed ?? string.Empty).Trim();
            if (clean.Length == 0) return "type the name as it shows in the game.";
            if (clean.Any(char.IsControl)) return "the name must be on one line.";
            if (clean.Length > LeagueProfileService.MaxRiotIdLength)
                return $"the name can be at most {LeagueProfileService.MaxRiotIdLength} characters.";
            var game = FromRiotId(clean);
            if (game == null) return "type the name before the # (for example Suguru or Suguru#EG1).";
            if (game.Length > LeagueProfileService.MaxNameLength)
                return $"the game name can be at most {LeagueProfileService.MaxNameLength} characters.";
            return null;
        }

        // The form two game names are compared in: Unicode-normalised, no invisible marks (zero-width, left-to-right and similar),
        // spaces collapsed, upper and lower case the same. Used to find two players with the same name (and, in 13b, to match
        // the names the AI read from the screenshot).
        public static string Key(string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return string.Empty;
            string text;
            try { text = gameName.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { text = gameName; }   // text with a broken character: compare it as it is
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.Format) continue;
                sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
            }
            var collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return collapsed.ToLowerInvariant();
        }

        // The first game name that two different players share (as the user typed it), or null. Rows with no name are ignored.
        public static string? FindDuplicate(IEnumerable<string?> gameNames)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in gameNames)
            {
                var key = Key(n);
                if (key.Length == 0) continue;
                if (!seen.Add(key)) return n!.Trim();
            }
            return null;
        }
    }
}
