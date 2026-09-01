using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocklessNpmManager.Core.Model;

namespace SocklessNpmManager.Core.Projects
{
    /// <summary>
    /// One <c>via</c> entry: either a plain package name (this package is only affected because it
    /// depends on that also-listed package) or an actual advisory object.
    /// </summary>
    [JsonConverter(typeof(NpmAuditViaConverter))]
    public sealed class NpmAuditVia
    {
        /// <summary>Non-null when this entry is a bare package-name reference.</summary>
        public string? Package { get; set; }

        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Severity { get; set; }
        public string? Range { get; set; }

        public bool IsReference => Package != null;

        public static NpmAuditVia Reference(string package) => new NpmAuditVia { Package = package };

        public static NpmAuditVia Advisory(string title, string url, string severity, string? range = null) =>
            new NpmAuditVia { Title = title, Url = url, Severity = severity, Range = range };
    }

    public sealed class NpmAuditAdvisory
    {
        [JsonProperty("severity")]
        public string Severity { get; set; } = "info";

        [JsonProperty("via")]
        public List<NpmAuditVia> Via { get; set; } = new List<NpmAuditVia>();

        [JsonProperty("range")]
        public string? Range { get; set; }

        [JsonProperty("nodes")]
        public List<string>? Nodes { get; set; }

        /// <summary>Other packages that become vulnerable because they depend on this one.</summary>
        [JsonProperty("effects")]
        public List<string>? Effects { get; set; }
    }

    public sealed class NpmAuditOutput
    {
        [JsonProperty("vulnerabilities")]
        public Dictionary<string, NpmAuditAdvisory>? Vulnerabilities { get; set; }
    }

    /// <summary>
    /// Resolving <c>npm audit --json</c> advisory chains. Port of <c>src/projects/advisories.ts</c>.
    ///
    /// <c>npm audit</c> attaches the actual advisory object only to the package that carries the CVE.
    /// Every package that merely depends on it is listed with a bare package name in its own
    /// <c>via</c> array. <see cref="CollectAdvisories"/> walks those <c>via</c> chains recursively so
    /// every affected package — direct or transitive — ends up with the full, real set of advisories.
    /// </summary>
    public static class Advisories
    {
        public static readonly IReadOnlyDictionary<string, int> SeverityWords = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["info"] = 0,
            ["low"] = 0,
            ["moderate"] = 1,
            ["high"] = 2,
            ["critical"] = 3,
        };

        /// <summary>Resolve every real advisory reachable from <paramref name="name"/>'s <c>via</c> chain. Cycle-guarded.</summary>
        public static List<VulnerabilityInfo> CollectAdvisories(
            string name,
            IReadOnlyDictionary<string, NpmAuditAdvisory> entries,
            HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>(StringComparer.Ordinal);
            var results = new List<VulnerabilityInfo>();
            if (!seen.Add(name)) return results;
            if (!entries.TryGetValue(name, out var entry)) return results;

            foreach (var v in entry.Via)
            {
                if (v.IsReference)
                {
                    results.AddRange(CollectAdvisories(v.Package!, entries, seen));
                }
                else if (!string.IsNullOrEmpty(v.Url))
                {
                    var severityWord = v.Severity ?? entry.Severity;
                    results.Add(new VulnerabilityInfo
                    {
                        Severity = SeverityWords.TryGetValue(severityWord ?? "", out var s) ? s : 0,
                        AdvisoryUrl = v.Url!,
                        Title = v.Title,
                        Range = v.Range,
                    });
                }
            }

            return results;
        }
    }

    internal sealed class NpmAuditViaConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(NpmAuditVia);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                return NpmAuditVia.Reference((string)reader.Value!);
            }

            var obj = JObject.Load(reader);
            return new NpmAuditVia
            {
                Title = (string?)obj["title"],
                Url = (string?)obj["url"],
                Severity = (string?)obj["severity"],
                Range = (string?)obj["range"],
            };
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var via = (NpmAuditVia?)value;
            if (via == null)
            {
                writer.WriteNull();
                return;
            }

            if (via.IsReference)
            {
                writer.WriteValue(via.Package);
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("title");
            writer.WriteValue(via.Title);
            writer.WritePropertyName("url");
            writer.WriteValue(via.Url);
            writer.WritePropertyName("severity");
            writer.WriteValue(via.Severity);
            writer.WritePropertyName("range");
            writer.WriteValue(via.Range);
            writer.WriteEndObject();
        }
    }
}
