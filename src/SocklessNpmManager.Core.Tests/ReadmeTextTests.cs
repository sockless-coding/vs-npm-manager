using SocklessNpmManager.Core.Npm;
using Xunit;

namespace SocklessNpmManager.Core.Tests
{
    public class ReadmeTextTests
    {
        [Fact]
        public void ReturnsNullForEmpty()
        {
            Assert.Null(ReadmeText.ToPlainText(null));
            Assert.Null(ReadmeText.ToPlainText("   "));
        }

        [Fact]
        public void StripsMarkdownAndHtmlToReadableText()
        {
            var md = "# Title\n\nSome **bold** and _italic_ text with a [link](https://example.com).\n\n" +
                     "<img src=\"https://img.shields.io/badge/x.svg\" alt=\"badge\">\n\n" +
                     "```js\nconst x = 1;\n```\n\n- one\n- two\n";
            var text = ReadmeText.ToPlainText(md)!;

            Assert.Contains("Title", text);
            Assert.Contains("bold", text);
            Assert.Contains("link", text);
            Assert.Contains("const x = 1;", text);
            Assert.DoesNotContain("<img", text);
            Assert.DoesNotContain("**", text);
            Assert.DoesNotContain("shields.io", text);
        }
    }
}
