using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FactorialFun.Core.Editor
{
    /// <summary>
    /// Automatic time tracker. Logs a line to time_tracker.txt (project root) every session:
    ///   open_datetime -> close_datetime // commit1, commit2, ...
    ///
    /// Menu: FactorialFun / Tools / TimeTracker / Show Report
    /// </summary>
    [InitializeOnLoad]
    public static class TimeTracker
    {
        const string SessionActiveKey = "FFTimeTracker.Active";
        const string SessionOpenKey   = "FFTimeTracker.OpenTime";
        const string FileName         = "time_tracker.txt";

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string FilePath    => Path.Combine(ProjectRoot, FileName);

        // -----------------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------------

        static TimeTracker()
        {
            // SessionState survives domain reloads (compile, play mode) but resets
            // when the editor process starts fresh — exactly what we want.
            if (!SessionState.GetBool(SessionActiveKey, false))
            {
                SessionState.SetBool(SessionActiveKey, true);
                SessionState.SetString(SessionOpenKey, DateTime.Now.ToString("o"));
                EnsureFileExists();
                UnityEngine.Debug.Log($"[TimeTracker] Session started — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            // Must re-subscribe after every domain reload.
            EditorApplication.quitting += OnQuit;
        }

        static void OnQuit()
        {
            var raw = SessionState.GetString(SessionOpenKey, "");
            if (string.IsNullOrEmpty(raw)) return;

            var openTime  = DateTime.Parse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var closeTime = DateTime.Now;
            var commits   = GetCommitsBetween(openTime, closeTime);
            var duration  = closeTime - openTime;

            var line = $"{openTime:yyyy-MM-dd HH:mm:ss} -> {closeTime:yyyy-MM-dd HH:mm:ss}";
            if (commits.Count > 0)
                line += " // " + string.Join(", ", commits);

            File.AppendAllText(FilePath, line + Environment.NewLine);
            UnityEngine.Debug.Log($"[TimeTracker] Session logged ({FormatDuration(duration)}).");
        }

        // -----------------------------------------------------------------------
        // Menu — Show Report
        // -----------------------------------------------------------------------

        [MenuItem("FactorialFun/Tools/TimeTracker/Show Report")]
        static void ShowReport()
        {
            if (!File.Exists(FilePath))
            {
                UnityEngine.Debug.Log("[TimeTracker] time_tracker.txt not found yet.");
                return;
            }

            var lines = File.ReadAllLines(FilePath)
                .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length == 0)
            {
                UnityEngine.Debug.Log("[TimeTracker] No sessions recorded yet.");
                return;
            }

            var total = TimeSpan.Zero;
            var sb    = new StringBuilder();
            sb.AppendLine("\n=== TimeTracker Report ===");

            foreach (var raw in lines)
            {
                // Format: "yyyy-MM-dd HH:mm:ss -> yyyy-MM-dd HH:mm:ss [// comment]"
                var arrowIdx = raw.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIdx < 0) { sb.AppendLine("  [unparseable] " + raw); continue; }

                var openStr  = raw.Substring(0, arrowIdx).Trim();
                var rest     = raw.Substring(arrowIdx + 4);

                var commentIdx = rest.IndexOf(" // ", StringComparison.Ordinal);
                var closeStr   = commentIdx >= 0 ? rest.Substring(0, commentIdx).Trim() : rest.Trim();
                var comment    = commentIdx >= 0 ? rest.Substring(commentIdx + 4).Trim() : "";

                if (!DateTime.TryParse(openStr,  out var open)  ||
                    !DateTime.TryParse(closeStr, out var close))
                {
                    sb.AppendLine("  [unparseable] " + raw);
                    continue;
                }

                var duration = close - open;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                total += duration;

                sb.Append($"  {open:yyyy-MM-dd}  {open:HH:mm} -> {close:HH:mm}  ({FormatDuration(duration)})");
                if (!string.IsNullOrEmpty(comment))
                    sb.Append("  // " + comment);
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine($"  Total : {FormatDuration(total)}  ({lines.Length} session{(lines.Length == 1 ? "" : "s")})");
            sb.AppendLine("==========================");

            UnityEngine.Debug.Log(sb.ToString());
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static void EnsureFileExists()
        {
            if (File.Exists(FilePath)) return;
            File.WriteAllText(FilePath,
                "# FactorialFun Time Tracker" + Environment.NewLine +
                "# Format: open -> close // commits" + Environment.NewLine +
                Environment.NewLine);
        }

        static List<string> GetCommitsBetween(DateTime since, DateTime until)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "git",
                    Arguments              = $"log --oneline " +
                                             $"--after=\"{since:yyyy-MM-dd HH:mm:ss}\" " +
                                             $"--before=\"{until:yyyy-MM-dd HH:mm:ss}\"",
                    WorkingDirectory       = ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };

                using var proc = Process.Start(psi);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                return output
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Select(l => { var sp = l.IndexOf(' '); return sp >= 0 ? l.Substring(sp + 1).Trim() : l; })
                    .Where(l => l.Length > 0)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        static string FormatDuration(TimeSpan t)
        {
            var h = (int)t.TotalHours;
            var m = t.Minutes;
            var s = t.Seconds;
            if (h > 0)  return $"{h}h {m:D2}m";
            if (m > 0)  return $"{m}m {s:D2}s";
            return $"{s}s";
        }
    }
}
