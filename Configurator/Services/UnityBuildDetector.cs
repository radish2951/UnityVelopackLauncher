using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Configurator.Models;
using Configurator.Utils;

namespace Configurator.Services;

public static class UnityBuildDetector
{
    public static UnityBuildInfo Analyze(string buildDir)
    {
        if (string.IsNullOrWhiteSpace(buildDir) || !Directory.Exists(buildDir))
            throw new DirectoryNotFoundException("Build directory not found.");

        string unityPlayer = Path.Combine(buildDir, "UnityPlayer.dll");
        if (!File.Exists(unityPlayer))
            throw new FileNotFoundException("UnityPlayer.dll not found in selected directory.");

        var exes = Directory.GetFiles(buildDir, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(e => !IsCrashHandler(e))
                            .ToArray();
        var candidates = new List<GameExeCandidate>();
        foreach (var exe in exes)
        {
            string baseName = Path.GetFileNameWithoutExtension(exe);
            string dataDir = Path.Combine(buildDir, baseName + "_Data");
            if (Directory.Exists(dataDir))
            {
                candidates.Add(new GameExeCandidate
                {
                    ExePath = exe,
                    BaseName = baseName,
                    DataDirPath = dataDir,
                });
            }
        }

        if (candidates.Count == 0)
        {
            // Fallback: if no *\_Data folder matches, still include top-level exes
            candidates.AddRange(exes.Select(e => new GameExeCandidate
            {
                ExePath = e,
                BaseName = Path.GetFileNameWithoutExtension(e),
                DataDirPath = string.Empty,
            }));
        }

        string arch = PeUtils.GetMachineString(unityPlayer);

        return new UnityBuildInfo
        {
            BuildDirectory = buildDir,
            UnityPlayerPath = unityPlayer,
            Architecture = arch,
            Candidates = candidates.OrderByDescending(c => !string.IsNullOrEmpty(c.DataDirPath)).ThenBy(c => c.ExePath).ToList(),
        };
    }

    private static bool IsCrashHandler(string path)
    {
        var name = Path.GetFileName(path) ?? string.Empty;
        return name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase);
    }

    public static (string ProductName, string Company, string Version) ReadFileVersion(string exePath)
    {
        var vi = FileVersionInfo.GetVersionInfo(exePath);
        string product = FirstNonEmpty(vi.ProductName ?? string.Empty, vi.FileDescription ?? string.Empty, Path.GetFileNameWithoutExtension(exePath));
        string company = FirstNonEmpty(vi.CompanyName ?? string.Empty, string.Empty);
        string version = FirstNonEmpty(vi.ProductVersion ?? string.Empty, vi.FileVersion ?? string.Empty, string.Empty);
        return (product, company, version);
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
