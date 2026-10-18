namespace Shop.Infrastructure.Scraping;

/// <summary>
/// Minimal robots.txt support: honours Allow/Disallow rules in the "*" group or a group naming our user agent.
/// The longest matching rule wins, as in the robots.txt standard (RFC 9309). Wildcards are not supported.
/// </summary>
public sealed class RobotsPolicy
{
    private readonly List<(string Path, bool Allow)> _rules;

    private RobotsPolicy(List<(string Path, bool Allow)> rules) => _rules = rules;

    public static RobotsPolicy AllowAll { get; } = new([]);

    public static RobotsPolicy Parse(string content, string userAgentToken)
    {
        var specific = new List<(string, bool)>();
        var wildcard = new List<(string, bool)>();
        List<List<(string, bool)>> currentTargets = [];
        var lastWasAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "user-agent")
            {
                if (!lastWasAgent)
                    currentTargets = [];
                if (value == "*")
                    currentTargets.Add(wildcard);
                else if (userAgentToken.Contains(value, StringComparison.OrdinalIgnoreCase))
                    currentTargets.Add(specific);
                lastWasAgent = true;
                continue;
            }

            lastWasAgent = false;
            if (key is not ("allow" or "disallow"))
                continue;
            if (key == "disallow" && value.Length == 0)
                continue; // "Disallow:" with no path allows everything
            foreach (var target in currentTargets)
                target.Add((value, key == "allow"));
        }

        return new RobotsPolicy(specific.Count > 0 ? specific : wildcard);
    }

    public bool IsAllowed(string pathAndQuery)
    {
        (string Path, bool Allow)? best = null;
        foreach (var rule in _rules)
        {
            if (!pathAndQuery.StartsWith(rule.Path.TrimEnd('*'), StringComparison.Ordinal))
                continue;
            if (best is null || rule.Path.Length > best.Value.Path.Length || (rule.Path.Length == best.Value.Path.Length && rule.Allow))
                best = rule;
        }
        return best?.Allow ?? true;
    }
}
