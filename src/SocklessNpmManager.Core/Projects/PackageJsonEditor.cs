using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Projects
{
    /// <summary>
    /// Format-preserving-ish edits to <c>package.json</c>. Port of <c>src/projects/jsonEdit.ts</c>.
    ///
    /// JSON carries no comments and no semantically-meaningful whitespace, so a full
    /// parse/mutate/re-serialize round trip is safe. We only detect and reapply the file's
    /// indentation, line-ending style and trailing newline so a one-dependency change doesn't
    /// produce a whole-file diff. New dependency keys are inserted in alphabetical order within
    /// their section, matching what <c>npm install</c> itself does.
    /// </summary>
    public static class PackageJsonEditor
    {
        private static readonly DependencyType[] DependencyFields =
        {
            DependencyType.Dependencies,
            DependencyType.DevDependencies,
            DependencyType.PeerDependencies,
            DependencyType.OptionalDependencies,
        };

        private sealed class FileFormat
        {
            public string Indent = "  ";
            public bool TrailingNewline;
            public bool Crlf;
        }

        private static readonly Regex IndentRe = new Regex("\n([ \t]+)\\S", RegexOptions.Compiled);
        private static readonly Regex TrailingNewlineRe = new Regex("\r?\n\\s*$", RegexOptions.Compiled);

        private static FileFormat DetectFormat(string text)
        {
            var crlfCount = CountOccurrences(text, "\r\n");
            var lfCount = CountOccurrences(text, "\n");
            var crlf = crlfCount > 0 && crlfCount >= lfCount;

            var indentMatch = IndentRe.Match(text);
            return new FileFormat
            {
                Indent = indentMatch.Success ? indentMatch.Groups[1].Value : "  ",
                TrailingNewline = TrailingNewlineRe.IsMatch(text),
                Crlf = crlf,
            };
        }

        private static string Serialize(JToken doc, FileFormat format)
        {
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            using (var writer = new JsonTextWriter(sw))
            {
                ConfigureIndent(writer, format.Indent);
                writer.QuoteName = true;
                doc.WriteTo(writer);
            }

            var outText = sb.ToString();
            if (format.TrailingNewline) outText += "\n";
            if (format.Crlf) outText = outText.Replace("\n", "\r\n");
            return outText;
        }

        private static void ConfigureIndent(JsonTextWriter writer, string indent)
        {
            writer.Formatting = Formatting.Indented;
            if (indent.Length > 0 && indent.All(c => c == indent[0]))
            {
                writer.IndentChar = indent[0];
                writer.Indentation = indent.Length;
            }
            else
            {
                writer.IndentChar = ' ';
                writer.Indentation = 2;
            }
        }

        private static JObject SortedSection(JObject section)
        {
            var sorted = new JObject();
            foreach (var prop in section.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                sorted[prop.Name] = prop.Value.DeepClone();
            }

            return sorted;
        }

        /// <summary>Add or update a dependency in <paramref name="dependencyType"/>; the section is created if missing.</summary>
        public static string UpsertDependency(string text, string id, string version, DependencyType dependencyType)
        {
            var format = DetectFormat(text);
            var doc = JObject.Parse(text);
            var key = dependencyType.ToJsonKey();

            var section = doc[key] as JObject ?? new JObject();
            section[id] = version;
            doc[key] = SortedSection(section);

            return Serialize(doc, format);
        }

        /// <summary>Remove a dependency from whichever section(s) reference it. No-op (returns <paramref name="text"/>) if absent.</summary>
        public static string RemoveDependency(string text, string id)
        {
            var format = DetectFormat(text);
            var doc = JObject.Parse(text);
            var changed = false;

            foreach (var field in DependencyFields)
            {
                if (doc[field.ToJsonKey()] is JObject section && section.Remove(id))
                {
                    if (!section.HasValues) doc.Remove(field.ToJsonKey());
                    changed = true;
                }
            }

            return changed ? Serialize(doc, format) : text;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
