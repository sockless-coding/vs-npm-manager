using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SocklessNpmManager.Core.Cli
{
    public sealed class RunResult
    {
        public int Code { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
    }

    /// <summary>Raised when the requested executable cannot be found on PATH.</summary>
    public sealed class ExecutableNotFoundException : Exception
    {
        public ExecutableNotFoundException(string exe, string message) : base(message) => Executable = exe;

        public string Executable { get; }
    }

    /// <summary>
    /// Runs an external command, resolving the executable against PATH (and PATHEXT on Windows so a
    /// bare <c>npm</c> finds <c>npm.cmd</c>). Replaces Node's <c>execFile</c> + <c>shell:true</c>
    /// behaviour from <c>src/node/cli.ts</c> without spawning a shell.
    /// </summary>
    public static class ProcessRunner
    {
        private const int MaxOutputChars = 32 * 1024 * 1024;

        public static bool IsWindows => Path.DirectorySeparatorChar == '\\';

        public static async Task<RunResult> RunAsync(
            string exe,
            IReadOnlyList<string> args,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            var resolved = ResolveExecutable(exe)
                ?? throw new ExecutableNotFoundException(exe, $"'{exe}' was not found on PATH.");

            var psi = new ProcessStartInfo
            {
                FileName = resolved,
                Arguments = BuildArguments(args),
                WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutDone = new TaskCompletionSource<bool>();
            var stderrDone = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) stdoutDone.TrySetResult(true);
                else if (stdout.Length < MaxOutputChars) stdout.Append(e.Data).Append('\n');
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) stderrDone.TrySetResult(true);
                else if (stderr.Length < MaxOutputChars) stderr.Append(e.Data).Append('\n');
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new ExecutableNotFoundException(exe, $"Failed to start '{exe}': {ex.Message}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() => TryKill(process)))
            {
                await Task.WhenAll(WaitForExitAsync(process), stdoutDone.Task, stderrDone.Task).ConfigureAwait(false);
            }

            return new RunResult
            {
                Code = process.HasExited ? process.ExitCode : 1,
                Stdout = stdout.ToString(),
                Stderr = stderr.ToString(),
            };
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch
            {
                // ignored
            }
        }

        private static Task WaitForExitAsync(Process process)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.Exited += (_, _) => tcs.TrySetResult(true);
            if (process.HasExited) tcs.TrySetResult(true);
            return tcs.Task;
        }

        /// <summary>Resolve <paramref name="exe"/> to a full path using PATH and (on Windows) PATHEXT.</summary>
        public static string? ResolveExecutable(string exe)
        {
            if (Path.IsPathRooted(exe))
            {
                return File.Exists(exe) ? exe : ResolveWithExtensions(exe);
            }

            if (exe.Contains(Path.DirectorySeparatorChar) || exe.Contains('/'))
            {
                var full = Path.GetFullPath(exe);
                return File.Exists(full) ? full : ResolveWithExtensions(full);
            }

            var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Where(d => d.Length > 0);

            foreach (var dir in pathDirs)
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
                var withExt = ResolveWithExtensions(candidate);
                if (withExt != null) return withExt;
            }

            return null;
        }

        private static string? ResolveWithExtensions(string basePath)
        {
            if (!IsWindows) return null;
            var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';')
                .Where(e => e.Length > 0);
            foreach (var ext in exts)
            {
                var candidate = basePath + ext;
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>Quote arguments per the Windows command-line convention (also fine on Unix for our args).</summary>
        internal static string BuildArguments(IReadOnlyList<string> args)
        {
            return string.Join(" ", args.Select(EscapeArgument));
        }

        private static string EscapeArgument(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return arg;
            }

            var sb = new StringBuilder();
            sb.Append('"');
            for (var i = 0; i < arg.Length; i++)
            {
                var backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }

                if (arg[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
