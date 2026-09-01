using System.Text;
using System.Text.RegularExpressions;

namespace SocklessNpmManager.Core.Projects
{
    /// <summary>
    /// Minimal glob matcher for npm/Yarn/pnpm <c>workspaces</c> patterns (<c>packages/*</c>,
    /// <c>apps/**</c>, <c>libs/*/pkg</c>). Only the features those patterns use are supported.
    /// </summary>
    internal static class GlobMatcher
    {
        public static bool IsMatch(string pattern, string path)
        {
            var normalizedPath = path.Replace('\\', '/').Trim('/');
            var regex = "^" + Translate(pattern.Replace('\\', '/').Trim('/')) + "$";
            return Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase);
        }

        private static string Translate(string pattern)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*':
                        if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                        {
                            i++;
                            // "**/" or trailing "**"
                            if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                            {
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

            return sb.ToString();
        }
    }
}
