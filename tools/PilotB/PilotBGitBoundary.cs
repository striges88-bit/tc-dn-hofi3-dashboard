using System.Diagnostics;

namespace CryptoIndicatorApp.PilotB;

public static class PilotBGitBoundary
{
    public static bool IsExactRepositoryRoot(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = fullRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(fullRoot);
            process.StartInfo.ArgumentList.Add("rev-parse");
            process.StartInfo.ArgumentList.Add("--show-toplevel");
            if (!process.Start() || !process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            var reportedRoot = process.StandardOutput.ReadToEnd().Trim();
            _ = process.StandardError.ReadToEnd();
            return process.ExitCode == 0
                && string.Equals(Path.GetFullPath(reportedRoot), fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
