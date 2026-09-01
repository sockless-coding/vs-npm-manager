using System;
using System.Diagnostics;
using SocklessNpmManager.Core.Hosting;

namespace SocklessNpmManager.Vs.Hosting
{
    /// <summary>
    /// Diagnostic sink for CLI output and JSON-fallback notes. Currently writes to the debugger /
    /// trace listeners; routing this to a real VS Output window pane via
    /// <c>VisualStudioExtensibility.Views().Output</c> is a follow-up.
    /// </summary>
    internal sealed class VsHostLogger : IHostLogger
    {
        public void Line(string message) => Trace.WriteLine("[npm] " + message);

        public void Append(string message) => Trace.Write(message);
    }
}
