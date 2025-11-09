using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Configurator.Models;
using Configurator.Utils;

namespace Configurator.Services;

public static class LauncherBuilder
{
    public static bool IsAlreadyConverted(GameExeCandidate candidate)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(candidate.ExePath);
            string sp = VersionResource.GetStringValue(candidate.ExePath, "SpecialBuild") ?? vi.SpecialBuild ?? string.Empty;
            string lt = VersionResource.GetStringValue(candidate.ExePath, "LegalTrademarks") ?? vi.LegalTrademarks ?? string.Empty;
            bool hasFlag = sp.IndexOf("VelopackEnabled=1", StringComparison.OrdinalIgnoreCase) >= 0
                        || lt.IndexOf("VelopackEnabled=1", StringComparison.OrdinalIgnoreCase) >= 0;
            string original = Path.Combine(Path.GetDirectoryName(candidate.ExePath)!, candidate.BaseName + "_original.exe");
            return hasFlag && File.Exists(original);
        }
        catch
        {
            return false;
        }
    }

    public static string MakeSafeFileVersion(string? raw)
    {
        // Convert strings like "6000.0.23f1 (1c4764c07fb4)" into "6000.0.23.1" (4-part numeric).
        try
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var nums = new System.Collections.Generic.List<int>();
            long current = -1;
            foreach (char ch in raw!)
            {
                if (char.IsDigit(ch))
                {
                    if (current < 0) current = 0;
                    current = current * 10 + (ch - '0');
                    if (current > int.MaxValue) current = int.MaxValue;
                }
                else
                {
                    if (current >= 0)
                    {
                        nums.Add((int)current);
                        current = -1;
                    }
                }
            }
            if (current >= 0) nums.Add((int)current);

            while (nums.Count < 3) nums.Add(0);
            if (nums.Count == 3) nums.Add(0);
            if (nums.Count > 4) nums = nums.Take(4).ToList();
            return string.Join(".", nums);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsNumericVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        return Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$");
    }

    public class BuildRequest
    {
        public required string BuildPath { get; set; }
        public required string LauncherCsprojPath { get; set; }
        public string? ProductName { get; set; }
        public string? CompanyName { get; set; }
        public string? Version { get; set; }
        public string? Copyright { get; set; }
    }

    public class BuildResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? OutputExePath { get; set; }
        public string? BackupExePath { get; set; }
    }

    public static BuildResult Build(BuildRequest request, Action<string>? onProgress = null)
    {
        try
        {
            onProgress?.Invoke($"Analyzing Unity build: {request.BuildPath}");

            // Step 1: Detect Unity build
            var buildInfo = UnityBuildDetector.Analyze(request.BuildPath);
            if (buildInfo.Candidates.Count == 0)
            {
                return new BuildResult { Success = false, ErrorMessage = "No valid Unity build found" };
            }

            var candidate = buildInfo.Candidates[0];
            onProgress?.Invoke($"  Found: {Path.GetFileName(candidate.ExePath)}");
            onProgress?.Invoke($"  Architecture: {buildInfo.Architecture}");

            // Check if already converted
            if (IsAlreadyConverted(candidate))
            {
                onProgress?.Invoke("  Already converted to Velopack launcher (found VelopackEnabled=1 and _original.exe)");
                onProgress?.Invoke("  Skipping launcher build");
                return new BuildResult { Success = true, OutputExePath = candidate.ExePath };
            }

            // Step 2: Extract metadata from Unity exe (or use overrides)
            onProgress?.Invoke("Reading metadata...");

            string product = request.ProductName
                            ?? VersionResource.GetStringValue(candidate.ExePath, "ProductName")
                            ?? candidate.BaseName;
            string company = request.CompanyName
                            ?? VersionResource.GetStringValue(candidate.ExePath, "CompanyName")
                            ?? "";
            string version = request.Version
                            ?? VersionResource.GetStringValue(candidate.ExePath, "ProductVersion")
                            ?? "1.0.0";
            string description = VersionResource.GetStringValue(candidate.ExePath, "FileDescription")
                                ?? product;
            string copyright = request.Copyright
                              ?? VersionResource.GetStringValue(candidate.ExePath, "LegalCopyright")
                              ?? "";

            onProgress?.Invoke($"  Product: {product}");
            onProgress?.Invoke($"  Company: {company}");
            onProgress?.Invoke($"  Version: {version}");

            // Step 3: Extract icon from Unity exe
            onProgress?.Invoke("Extracting icon...");
            string? icoPath = IconExtractor.TryExtractFullIconIco(candidate.ExePath);

            if (string.IsNullOrEmpty(icoPath))
            {
                onProgress?.Invoke("  Warning: Could not extract icon, using default");
            }
            else
            {
                onProgress?.Invoke($"  Icon extracted: {icoPath}");
            }

            // Step 4: Patch manifest
            onProgress?.Invoke("Patching manifest...");

            string assemblyName = candidate.BaseName;
            string identity = ManifestPatcher.DeriveIdentity(company, product);
            onProgress?.Invoke($"  Assembly identity: {identity}");

            // Get the default manifest from Launcher project
            string launcherDir = Path.GetDirectoryName(request.LauncherCsprojPath)
                               ?? throw new InvalidOperationException("Invalid launcher project path");
            string defaultManifest = Path.Combine(launcherDir, "app.manifest");

            string? patchedManifest = null;
            if (File.Exists(defaultManifest))
            {
                patchedManifest = ManifestPatcher.CreateIdentityPatchedManifest(defaultManifest, identity);
                onProgress?.Invoke($"  Manifest patched: {patchedManifest}");
            }
            else
            {
                onProgress?.Invoke("  Warning: Default manifest not found, skipping");
            }

            // Step 5: Determine runtime ID
            string rid = buildInfo.Architecture switch
            {
                "x86" => "win-x86",
                "arm64" => "win-arm64",
                _ => "win-x64"
            };

            onProgress?.Invoke($"  Runtime ID: {rid}");

            // Step 6: Build MSBuild properties
            string safeFileVersion = MakeSafeFileVersion(version);
            onProgress?.Invoke($"  File version: {safeFileVersion}");

            var msbuildProps = new System.Text.StringBuilder();
            msbuildProps.Append($"/p:AssemblyName=\"{assemblyName}\" ");
            msbuildProps.Append($"/p:Company=\"{company}\" ");
            msbuildProps.Append($"/p:Product=\"{product}\" ");
            msbuildProps.Append($"/p:Description=\"{description}\" ");
            msbuildProps.Append($"/p:InformationalVersion=\"{version}\" ");
            msbuildProps.Append($"/p:FileVersion=\"{safeFileVersion}\" ");
            msbuildProps.Append("/p:IncludeSourceRevisionInInformationalVersion=false ");
            msbuildProps.Append("/p:EnableCompressionInSingleFile=true ");
            msbuildProps.Append("/p:SpecialBuild=\"VelopackEnabled=1\" ");
            msbuildProps.Append("/p:Trademark=\"VelopackEnabled=1\" ");

            if (!string.IsNullOrEmpty(patchedManifest))
            {
                msbuildProps.Append($"/p:ApplicationManifest=\"{patchedManifest}\" ");
            }

            if (!string.IsNullOrEmpty(icoPath))
            {
                msbuildProps.Append($"/p:ApplicationIcon=\"{icoPath}\" ");
            }

            if (!string.IsNullOrEmpty(copyright))
            {
                msbuildProps.Append($"/p:Copyright=\"{copyright}\" ");
            }

            // Add Version only if strictly numeric
            if (IsNumericVersion(version))
            {
                msbuildProps.Append($"/p:Version=\"{version}\" ");
            }

            // Step 7: Build launcher with dotnet publish
            onProgress?.Invoke("Building launcher with dotnet publish...");

            string args = $"publish \"{request.LauncherCsprojPath}\" " +
                         $"-c Release " +
                         $"-r {rid} " +
                         $"--self-contained true " +
                         $"/p:PublishSingleFile=true " +
                         msbuildProps.ToString();

            onProgress?.Invoke($"  dotnet {args}");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new BuildResult { Success = false, ErrorMessage = "Failed to start dotnet process" };
            }

            // Read output in real-time
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    onProgress?.Invoke($"  {e.Data}");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    onProgress?.Invoke($"  {e.Data}");
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return new BuildResult { Success = false, ErrorMessage = $"dotnet publish failed with exit code {process.ExitCode}" };
            }

            onProgress?.Invoke("  Build succeeded");

            // Step 8: Replace Unity exe with launcher
            onProgress?.Invoke("Replacing Unity exe...");

            string publishedLauncher = Path.Combine(launcherDir, "bin", "Release", "net8.0-windows", rid, "publish", $"{assemblyName}.exe");

            if (!File.Exists(publishedLauncher))
            {
                return new BuildResult { Success = false, ErrorMessage = $"Published launcher not found at: {publishedLauncher}" };
            }

            string originalExe = candidate.ExePath;
            string backupExe = Path.Combine(buildInfo.BuildDirectory, $"{assemblyName}_original.exe");

            onProgress?.Invoke($"  Backing up: {Path.GetFileName(backupExe)}");
            File.Move(originalExe, backupExe);

            onProgress?.Invoke($"  Copying launcher: {assemblyName}.exe");
            File.Copy(publishedLauncher, originalExe, overwrite: false);

            onProgress?.Invoke("");
            onProgress?.Invoke("✓ Launcher build completed successfully");
            onProgress?.Invoke($"  Output: {originalExe}");
            onProgress?.Invoke($"  Backup: {backupExe}");

            return new BuildResult
            {
                Success = true,
                OutputExePath = originalExe,
                BackupExePath = backupExe
            };
        }
        catch (Exception ex)
        {
            return new BuildResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
