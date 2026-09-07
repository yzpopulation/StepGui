using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StepGui
{
    public class StepResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public bool Success { get { return ExitCode == 0; } }
    }

    public static class StepCli
    {
        private static readonly Regex AnsiRegex = new Regex("\u001B\\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
        public const int TimeoutMs = 180000;

        public static string Quote(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '"', '\t' }) < 0) return arg;
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        public static string StripAnsi(string s)
        {
            return s == null ? "" : AnsiRegex.Replace(s, string.Empty);
        }

        public static string WritePasswordFile(string password)
        {
            string p = Path.Combine(Path.GetTempPath(), "stepgui_pwd_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(p, password, new UTF8Encoding(false));
            return p;
        }

        public static void DeletePasswordFile(string p)
        {
            try { if (p != null && File.Exists(p)) File.Delete(p); } catch { }
        }

        public static Task<StepResult> RunAsync(string exePath, string args, string workDir, IProgress<string> log)
        {
            return Task.Run(delegate
            {
                if (log != null) log.Report(Environment.NewLine + "> step " + args);
                var sbOut = new StringBuilder();
                var sbErr = new StringBuilder();
                var psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = args;
                psi.WorkingDirectory = !string.IsNullOrEmpty(workDir) && Directory.Exists(workDir) ? workDir : Path.GetTempPath();
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                int exitCode;
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (sbOut) sbOut.AppendLine(e.Data); }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (sbErr) sbErr.AppendLine(e.Data); }
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    bool done = p.WaitForExit(TimeoutMs);
                    if (!done)
                    {
                        try { p.Kill(); } catch { }
                        if (log != null) log.Report("[执行超时，进程已终止]");
                        exitCode = -1;
                    }
                    else
                    {
                        p.WaitForExit();
                        exitCode = p.ExitCode;
                    }
                }
                string text = "";
                lock (sbOut) { if (sbOut.Length > 0) text = StripAnsi(sbOut.ToString()).TrimEnd(); }
                lock (sbErr)
                {
                    if (sbErr.Length > 0)
                    {
                        string err = StripAnsi(sbErr.ToString()).TrimEnd();
                        text = text.Length > 0 ? text + Environment.NewLine + err : err;
                    }
                }
                if (log != null) log.Report(text + Environment.NewLine + "[退出码 " + exitCode + "]");
                return new StepResult { ExitCode = exitCode, Output = text };
            });
        }
    }
}
