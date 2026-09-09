using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArrivalMeetings;

// Kept independent of Unity so ambiguous names and model output can be tested.
internal static class MeetingIntent
{
    internal static bool Mentions(string text, string name, IReadOnlyList<string> allNames)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (ContainsName(text, name.Trim())) return true;
        string first = name.Trim().Split(' ')[0];
        return first.Length >= 3 && allNames.Count(n =>
            n.Trim().Split(' ')[0].Equals(first, StringComparison.OrdinalIgnoreCase)) == 1
            && ContainsName(text, first);
    }

    private static bool ContainsName(string text, string name) => Regex.IsMatch(text,
        @"(?<![\p{L}\p{N}_])" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static int[] ParseSelection(string? answer, int candidateCount, int maximum)
    {
        string text = (answer ?? "").Trim();
        if (text.Equals("NONE", StringComparison.OrdinalIgnoreCase)) return Array.Empty<int>();
        // No fuzzy matching, substring extraction, or random fallback for malformed answers.
        if (!Regex.IsMatch(text, @"\AMEET: *[1-9][0-9]*(?: *, *[1-9][0-9]*)*\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return Array.Empty<int>();
        var result = new List<int>();
        foreach (string part in text.Substring(5).Split(','))
        {
            if (!int.TryParse(part.Trim(), out int id) || id < 1 || id > candidateCount)
                return Array.Empty<int>();
            if (!result.Contains(id - 1)) result.Add(id - 1);
        }
        return result.Take(Math.Max(0, maximum)).ToArray();
    }

    internal static string Question(string destination, IReadOnlyList<string> candidates, int maximum) =>
        "The game's location-change action has just moved the conversation to " + destination + ". " +
        "From the recent conversation, which listed people did the group explicitly come HERE to meet, " +
        "visit, find, speak to, or join right now? Select only intended meeting targets at THIS destination. " +
        "Do not select people merely mentioned, building owners named in a location, people the group is " +
        "leaving, rejected/negated plans, hypothetical meetings, or meetings planned for later or elsewhere. " +
        "If intent is unclear, select nobody. At most " + maximum + " people.\n" +
        string.Join("\n", candidates.Select((name, i) => (i + 1) + ": " + name)) +
        "\nReply with exactly NONE or MEET: followed by comma-separated numeric IDs, for example MEET: 1, 2. " +
        "Do not include explanation, JSON, or markdown.";
}
