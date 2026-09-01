using System.Collections.Generic;
using System.Linq;
using SocklessNpmManager.Core.Projects;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class AdvisoriesTests
    {
        // Trimmed from a real `npm audit --json` run against a project depending on the long-deprecated
        // `request@2.81.0`, which pulls in several vulnerable sub-dependencies.
        private static Dictionary<string, NpmAuditAdvisory> Entries() => new Dictionary<string, NpmAuditAdvisory>
        {
            ["ajv"] = new NpmAuditAdvisory
            {
                Severity = "moderate",
                Via = { NpmAuditVia.Advisory("Prototype Pollution in Ajv", "https://github.com/advisories/GHSA-v88g-cgmw-v5xw", "moderate") },
            },
            ["boom"] = new NpmAuditAdvisory { Severity = "high", Via = { NpmAuditVia.Reference("hoek") } },
            ["cryptiles"] = new NpmAuditAdvisory { Severity = "high", Via = { NpmAuditVia.Reference("boom") } },
            ["form-data"] = new NpmAuditAdvisory
            {
                Severity = "critical",
                Via = { NpmAuditVia.Advisory("form-data uses unsafe random function for choosing boundary", "https://github.com/advisories/GHSA-fjxv-7rqg-78g4", "critical") },
            },
            ["har-validator"] = new NpmAuditAdvisory { Severity = "moderate", Via = { NpmAuditVia.Reference("ajv") } },
            ["hawk"] = new NpmAuditAdvisory
            {
                Severity = "high",
                Via =
                {
                    NpmAuditVia.Advisory("Uncontrolled Resource Consumption in Hawk", "https://github.com/advisories/GHSA-44pw-h2cw-w3vq", "high"),
                    NpmAuditVia.Reference("boom"),
                    NpmAuditVia.Reference("cryptiles"),
                    NpmAuditVia.Reference("hoek"),
                },
            },
            ["hoek"] = new NpmAuditAdvisory
            {
                Severity = "high",
                Via = { NpmAuditVia.Advisory("hoek subject to prototype pollution via the clone function", "https://github.com/advisories/GHSA-c429-5p7v-vgjp", "high") },
            },
            ["request"] = new NpmAuditAdvisory
            {
                Severity = "critical",
                Via =
                {
                    NpmAuditVia.Advisory("Server-Side Request Forgery in Request", "https://github.com/advisories/GHSA-p8p7-x288-28g6", "critical"),
                    NpmAuditVia.Reference("form-data"),
                    NpmAuditVia.Reference("har-validator"),
                    NpmAuditVia.Reference("hawk"),
                },
            },
        };

        [Fact]
        public void ADirectDependencysOwnAdvisoryIsIncluded()
        {
            var result = Advisories.CollectAdvisories("request", Entries());
            Assert.Contains(result, v => v.Title == "Server-Side Request Forgery in Request");
        }

        [Fact]
        public void AdvisoriesAreResolvedThroughTheFullViaChain()
        {
            var titles = Advisories.CollectAdvisories("request", Entries()).Select(v => v.Title).ToList();
            Assert.Contains("form-data uses unsafe random function for choosing boundary", titles);
            Assert.Contains("Uncontrolled Resource Consumption in Hawk", titles);
            Assert.Contains("Prototype Pollution in Ajv", titles);
            Assert.Contains("hoek subject to prototype pollution via the clone function", titles);
        }

        [Fact]
        public void APackageWithNoAdvisoryOfItsOwnInheritsItsDependencys()
        {
            var result = Advisories.CollectAdvisories("boom", Entries());
            Assert.Single(result);
            Assert.Equal("hoek subject to prototype pollution via the clone function", result[0].Title);
            Assert.Equal(2, result[0].Severity);
        }

        [Fact]
        public void ADiamondReferenceIsNotDuplicated()
        {
            var result = Advisories.CollectAdvisories("hawk", Entries());
            Assert.Single(result, v => v.Title != null && v.Title.StartsWith("hoek"));
        }

        [Fact]
        public void AnIsolatedPackageWithSeveralOfItsOwnAdvisoriesReturnsAllOfThem()
        {
            Assert.Single(Advisories.CollectAdvisories("form-data", Entries()));
        }

        [Fact]
        public void AnUnknownPackageResolvesToNoAdvisories()
        {
            Assert.Empty(Advisories.CollectAdvisories("left-pad", Entries()));
        }

        [Fact]
        public void ACycleTerminates()
        {
            var cyclic = new Dictionary<string, NpmAuditAdvisory>
            {
                ["a"] = new NpmAuditAdvisory { Severity = "low", Via = { NpmAuditVia.Reference("b") } },
                ["b"] = new NpmAuditAdvisory { Severity = "low", Via = { NpmAuditVia.Reference("a") } },
            };
            Assert.Empty(Advisories.CollectAdvisories("a", cyclic));
        }
    }
}
