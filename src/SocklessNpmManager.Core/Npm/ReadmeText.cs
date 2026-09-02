using System.Text.RegularExpressions;
using Markdig;

namespace SocklessNpmManager.Core.Npm
{
    /// <summary>
    /// Renders a package readme (Markdown, often with embedded HTML) down to readable plain text for
    /// hosts that can't show rich Markdown. Images, badges and raw HTML are dropped.
    /// </summary>
    public static class ReadmeText
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static string? ToPlainText(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return null;

            try
            {
                var text = Markdown.ToPlainText(markdown!, Pipeline);

                // Markdig's plain-text renderer passes raw HTML blocks/inlines straight through.
                text = Regex.Replace(text, "<[^>]+>", "");
                // Drop lines that are now just leftover badge/link punctuation.
                text = Regex.Replace(text, @"(?m)^[ \t]*[\[\]()!|>*_`~-]+[ \t]*$", "");
                text = Regex.Replace(text, @"\r?\n[ \t]*(\r?\n[ \t]*){2,}", "\n\n");
                text = Regex.Replace(text, @"[ \t]+\r?\n", "\n");
                return text.Trim();
            }
            catch
            {
                return Regex.Replace(markdown, "<[^>]+>", "").Trim();
            }
        }
    }
}
