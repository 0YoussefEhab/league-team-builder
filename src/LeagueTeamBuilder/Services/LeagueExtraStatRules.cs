using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LeagueTeamBuilder.Services
{
    // Step 13d (PLAN-league.md): the "other stats" of a player (wards placed, turret damage, CC score...). They are kept in
    // LeagueMatchPlayer.ExtraJson as one flat object of whole numbers, for example {"turretDamage":3300,"wardsPlaced":12}.
    // No database here, so it is covered by tests. The page script has the same rules in league-stats-import.js
    // (normalizeExtraName, extraLabel); keep the two in step.
    public static class LeagueExtraStatRules
    {
        public const int MaxPerPlayer = 30;
        public const int MaxNameLength = 32;
        public const int MaxValue = 999999;

        // Names that already have a box of their own in the stats form (compared without case).
        private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        {
            "kills", "deaths", "assists", "cs", "damageDealt", "damageTaken", "gold", "visionScore", "objectiveDamage", "notes", "champion", "lane",
        };

        private static readonly Dictionary<string, string> Acronyms = new(StringComparer.Ordinal)
        {
            ["cc"] = "CC", ["kda"] = "KDA", ["hp"] = "HP", ["ap"] = "AP", ["ad"] = "AD", ["cs"] = "CS",
        };

        // camelCase letters and digits, first character a lower-case letter, at most 32 characters.
        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
            if (name[0] is < 'a' or > 'z') return false;
            foreach (var c in name)
                if (!(c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')) return false;
            return true;
        }

        // Checks what the form sent. null extras = "not sent" and is fine (an old page leaves the saved stats alone).
        // Returns an error text without the player's name (the caller adds it), or null.
        public static string? Validate(IReadOnlyDictionary<string, int>? extras)
        {
            if (extras == null || extras.Count == 0) return null;
            if (extras.Count > MaxPerPlayer) return $"at most {MaxPerPlayer} other stats.";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in extras)
            {
                if (!IsValidName(name)) return $"\"{Shorten(name)}\" is not a valid name for an other stat (letters and digits, starting with a letter).";
                if (Reserved.Contains(name)) return $"\"{name}\" already has its own box.";
                if (!seen.Add(name)) return $"\"{name}\" is listed twice.";
                if (value is < 0 or > MaxValue) return $"{Label(name)} must be between 0 and {MaxValue}.";
            }
            return null;
        }

        // The stored text: names in ordinal order so the same stats always give the same text (an identical save is then not a change).
        // null when there is nothing to keep.
        public static string? ToJson(IReadOnlyDictionary<string, int>? extras)
        {
            if (extras == null || extras.Count == 0) return null;
            var sorted = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (name, value) in extras) sorted[name] = value;
            return JsonSerializer.Serialize(sorted);
        }

        // Reads the stored text. Never throws: anything that is not a flat object of valid names and numbers is skipped.
        public static SortedDictionary<string, int> FromJson(string? json)
        {
            var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (result.Count >= MaxPerPlayer) break;
                    if (!IsValidName(prop.Name) || Reserved.Contains(prop.Name)) continue;
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var v)) continue;
                    if (v is < 0 or > MaxValue) continue;
                    result[prop.Name] = v;
                }
            }
            catch (JsonException) { /* not ours: show nothing */ }
            return result;
        }

        // "wardsPlaced" -> "Wards placed", "ccScore" -> "CC score".
        public static string Label(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var words = new List<string>();
            var cur = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                var prevUp = i > 0 && name[i - 1] is >= 'A' and <= 'Z';
                var up = c is >= 'A' and <= 'Z';
                var nextLow = i + 1 < name.Length && name[i + 1] is >= 'a' and <= 'z';
                if (cur.Length > 0 && up && (!prevUp || nextLow)) { words.Add(cur.ToString()); cur.Clear(); }
                cur.Append(c);
            }
            if (cur.Length > 0) words.Add(cur.ToString());

            for (var i = 0; i < words.Count; i++)
            {
                var low = words[i].ToLowerInvariant();
                if (Acronyms.TryGetValue(low, out var acr)) words[i] = acr;
                else words[i] = i == 0 ? char.ToUpperInvariant(low[0]) + low[1..] : low;
            }
            return string.Join(' ', words);
        }

        // "Wards placed 12 · Turret damage 3,300", or null when there is none.
        public static string? Text(IReadOnlyDictionary<string, int> extras)
        {
            if (extras.Count == 0) return null;
            return string.Join(" · ", extras.Select(e => Label(e.Key) + " " + e.Value.ToString("N0", CultureInfo.InvariantCulture)));
        }

        private static string Shorten(string? s)
        {
            s ??= string.Empty;
            return s.Length > 20 ? s[..20] + "..." : s;
        }
    }
}
