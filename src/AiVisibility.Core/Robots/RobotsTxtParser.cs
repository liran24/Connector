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
            if (ReadDirective(rawLine) is not var (field, value))
            {
                continue;
            }

            switch (field)
            {
                case "user-agent":
                    current = GroupFor(current, groups, startNew: !lastLineWasUserAgent);
                    current.UserAgents.Add(value.ToLowerInvariant());
                    lastLineWasUserAgent = true;
                    continue;

                case "allow" or "disallow":
                    current?.Rules.Add((field == "allow", value));
                    break;

                case "sitemap":
                    sitemaps.Add(value);
                    break;
            }

            lastLineWasUserAgent = false;
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

    /// <summary>
    /// Splits one line into its lower-cased field name and value, ignoring comments, blank
    /// lines and anything without a colon.
    /// </summary>
    private static (string Field, string Value)? ReadDirective(string rawLine)
    {
        var hash = rawLine.IndexOf('#');
        var line = (hash >= 0 ? rawLine[..hash] : rawLine).Trim();

        var separator = line.IndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        return (line[..separator].Trim().ToLowerInvariant(), line[(separator + 1)..].Trim());
    }

    /// <summary>
    /// Consecutive <c>User-agent</c> lines share one rule block; a <c>User-agent</c> line that
    /// follows rules starts a new group.
    /// </summary>
    private static RobotsGroup GroupFor(RobotsGroup? current, List<RobotsGroup> groups, bool startNew)
    {
        if (current is not null && !startNew)
        {
            return current;
        }

        var group = new RobotsGroup();
        groups.Add(group);
        return group;
    }
}
