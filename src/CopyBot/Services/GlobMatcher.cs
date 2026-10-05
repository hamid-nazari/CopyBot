using System.Text;
using System.Text.RegularExpressions;

namespace CopyBot.Services;

/// <summary>
/// Minimal glob-to-regex matcher used for the copy <c>Included</c>/<c>Excluded</c>
/// patterns. Supports <c>*</c> (any chars within a path segment), <c>?</c> (one char in a
/// segment), and <c>**</c> (any number of path segments). Matching is case-insensitive.
/// A pattern with no path separator is also matched against the file name, so e.g.
/// <c>*.log</c> excludes <c>.log</c> files at any depth.
/// </summary>
public static class GlobMatcher
{
    public static bool IsMatch(string pattern, string relativePath)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(relativePath))
            return false;

        string normalizedPattern = Normalize(pattern);
        string normalizedPath = Normalize(relativePath);

        bool hasSeparator = pattern.Contains('/') || pattern.Contains('\\');
        if (!hasSeparator)
        {
            int idx = normalizedPath.LastIndexOf('/');
            string fileName = idx >= 0 ? normalizedPath[(idx + 1)..] : normalizedPath;
            if (BuildRegex(normalizedPattern).IsMatch(fileName))
                return true;
        }

        return BuildRegex(normalizedPattern).IsMatch(normalizedPath);
    }

    public static bool IsMatchAny(IEnumerable<string> patterns, string relativePath)
    {
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) && IsMatch(pattern, relativePath))
                return true;
        }

        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static Regex BuildRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        // "**" wildcard.
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            // "**/" matches zero or more directories.
                            i++;
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;

                case '?':
                    sb.Append("[^/]");
                    break;

                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}