using System.Text;
using System.Text.RegularExpressions;

namespace AiVisibility.Core.Robots;

/// <summary>
/// One <c>User-agent</c> block and the rules that follow it.
/// </summary>
internal sealed class RobotsGroup
{
    public List<string> UserAgents { get; } = new();
    public List<(bool Allow, string Pattern)> Rules { get; } = new();
}

/// <summary>
/// A parsed robots.txt, able to answer whether a given crawler may fetch a given path.
/// </summary>
/// <remarks>
/// Implements the parts of RFC 9309 that decide agent visibility: group matching by
/// user-agent, <c>Allow</c>/<c>Disallow</c> prefix rules with <c>*</c> and <c>$</c>
/// wildcards, and longest-match-wins with <c>Allow</c> breaking ties.
/// </remarks>
public sealed class RobotsTxtParser
{
    private readonly List<RobotsGroup> _groups;

    /// <summary>Sitemap URLs declared in the file, in the order they appeared.</summary>
    public IReadOnlyList<string> Sitemaps { get; }

    private RobotsTxtParser(List<RobotsGroup> groups, List<string> sitemaps)
    {
        _groups = groups;
        Sitemaps = sitemaps;
    }

    /// <summary>
    /// A robots.txt that permits everything — the correct interpretation when the file is
    /// absent or returns 404.
    /// </summary>
    public static RobotsTxtParser AllowAll() => new(new List<RobotsGroup>(), new List<string>());

    public static RobotsTxtParser Parse(string content)
    {
        var groups = new List<RobotsGroup>();
        var sitemaps = new List<string>();

        RobotsGroup? current = null;
        var lastLineWasUserAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var field = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();

            switch (field)
            {
                case "user-agent":
                    // Consecutive User-agent lines share one rule block; a User-agent line
                    // after rules starts a new group.
                    if (current is null || !lastLineWasUserAgent)
                    {
                        current = new RobotsGroup();
                        groups.Add(current);
                    }

                    current.UserAgents.Add(value.ToLowerInvariant());
                    lastLineWasUserAgent = true;
                    continue;

                case "allow" or "disallow":
                    current?.Rules.Add((field == "allow", value));
                    lastLineWasUserAgent = false;
                    continue;

                case "sitemap":
                    sitemaps.Add(value);
                    lastLineWasUserAgent = false;
                    continue;

                default:
                    lastLineWasUserAgent = false;
                    continue;
            }
        }

        return new RobotsTxtParser(groups, sitemaps);
    }

    /// <summary>
    /// Whether <paramref name="userAgent"/> is permitted to fetch <paramref name="path"/>.
    /// </summary>
    public bool IsAllowed(string userAgent, string path)
    {
        var group = FindGroup(userAgent);
        if (group is null)
        {
            return true;
        }

        var decision = true;
        var winningLength = -1;

        foreach (var (allow, pattern) in group.Rules)
        {
            // An empty Disallow value means "nothing is disallowed" and carries no weight.
            if (pattern.Length == 0)
            {
                continue;
            }

            if (!Matches(pattern, path))
            {
                continue;
            }

            // Longest matching rule wins; Allow beats Disallow at equal length.
            if (pattern.Length > winningLength || (pattern.Length == winningLength && allow))
            {
                winningLength = pattern.Length;
                decision = allow;
            }
        }

        return decision;
    }

    /// <summary>
    /// Finds the rule block that applies to a crawler: an exact user-agent match if the file
    /// names it, otherwise the wildcard group.
    /// </summary>
    private RobotsGroup? FindGroup(string userAgent)
    {
        var needle = userAgent.ToLowerInvariant();

        return _groups.FirstOrDefault(g => g.UserAgents.Contains(needle))
            ?? _groups.FirstOrDefault(g => g.UserAgents.Contains("*"));
    }

    /// <summary>Whether the store names this crawler explicitly rather than relying on <c>*</c>.</summary>
    public bool HasExplicitRuleFor(string userAgent)
    {
        var needle = userAgent.ToLowerInvariant();
        return _groups.Any(g => g.UserAgents.Contains(needle));
    }

    private static bool Matches(string pattern, string path)
    {
        if (!pattern.Contains('*') && !pattern.EndsWith('$'))
        {
            return path.StartsWith(pattern, StringComparison.Ordinal);
        }

        return BuildMatcher(pattern).IsMatch(path);
    }

    private static Regex BuildMatcher(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;

        var regex = new StringBuilder("^");
        foreach (var c in body)
        {
            regex.Append(c == '*' ? ".*" : Regex.Escape(c.ToString()));
        }

        if (anchored)
        {
            regex.Append('$');
        }

        return new Regex(regex.ToString(), RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash >= 0 ? line[..hash] : line;
    }
}
