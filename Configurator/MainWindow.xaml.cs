using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Configurator.Models;
using Configurator.Services;
using Configurator.Utils;

namespace Configurator;

public partial class MainWindow : Window
{
    private UnityBuildInfo? _info;
    private GameExeCandidate? _selected;
    // no marker file; rely on version resource metadata

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "Select the Unity build directory (contains UnityPlayer.dll)";
        dlg.UseDescriptionForTitle = true;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtBuildDir.Text = dlg.SelectedPath;
            AnalyzeNow();
        }
    }

    private void AnalyzeNow()
    {
        TxtStatus.Text = string.Empty;
        TxtProductName.Text = TxtCompanyName.Text = TxtVersion.Text = LblArch.Text = string.Empty;
        CmbExe.ItemsSource = null;
        BtnReplace.IsEnabled = false;
        ImgIcon.Source = null;

        try
        {
            string dir = TxtBuildDir.Text.Trim();
            _info = UnityBuildDetector.Analyze(dir);
            LblArch.Text = _info.Architecture;
            CmbExe.ItemsSource = _info.Candidates;
            if (_info.Candidates.Count > 0)
            {
                var preferred = _info.Candidates.Find(c => !string.IsNullOrEmpty(c.DataDirPath) && !IsCrashHandler(c.ExePath))
                                ?? _info.Candidates.Find(c => !IsCrashHandler(c.ExePath))
                                ?? _info.Candidates[0];
                CmbExe.SelectedItem = preferred;
                TxtStatus.Text = $"Found {_info.Candidates.Count} candidate(s).";
            }
            else
            {
                TxtStatus.Text = "No EXE found. Pick a different folder.";
            }
        }
        catch (Exception ex)
        {
            _info = null;
            TxtStatus.Text = ex.ToString();
        }
    }

    private void CmbExe_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbExe.SelectedItem is GameExeCandidate cand)
        {
            _selected = cand;
            var vi = FileVersionInfo.GetVersionInfo(cand.ExePath);
            string product = VersionResource.GetStringValue(cand.ExePath, "ProductName")
                                ?? (string.IsNullOrWhiteSpace(vi.ProductName) ? Path.GetFileNameWithoutExtension(cand.ExePath) : vi.ProductName!);
            string company = VersionResource.GetStringValue(cand.ExePath, "CompanyName") ?? vi.CompanyName ?? string.Empty;
            string version = VersionResource.GetStringValue(cand.ExePath, "ProductVersion") ?? vi.ProductVersion ?? string.Empty;
            string description = VersionResource.GetStringValue(cand.ExePath, "FileDescription") ?? vi.FileDescription ?? product;
            string? identityFromExe = ManifestReader.TryReadAssemblyIdentityName(cand.ExePath);
            string identity = !string.IsNullOrWhiteSpace(identityFromExe)
                ? ManifestPatcher.EnsureValidIdentity(identityFromExe, ManifestPatcher.DeriveIdentity(company, product))
                : ManifestPatcher.DeriveIdentity(company, product);

            TxtProductName.Text = product;
            TxtCompanyName.Text = company;
            TxtVersion.Text = version;
            TxtDescription.Text = description;
            TxtManifestName.Text = identity;
            string copy = VersionResource.GetStringValue(cand.ExePath, "LegalCopyright") ?? vi.LegalCopyright ?? string.Empty;
            TxtCopyright.Text = copy;

            BtnReplace.IsEnabled = true;
            LoadIconPreview(cand.ExePath);

            // Detect if already converted (flag + _original exists)
            if (IsAlreadyConverted(cand))
            {
                var result = System.Windows.MessageBox.Show(
                    "This folder already contains a Velopack-enabled launcher (flag + _original.exe).\nRestore the original and load it?",
                    "Already Converted",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.OK)
                {
                    try
                    {
                        RestoreOriginal(cand);
                        AnalyzeNow();
                        return;
                    }
                    catch (Exception ex)
                    {
                        TxtStatus.Text = ex.ToString();
                    }
                }
                else
                {
                    ResetFormButKeepFolder();
                    return;
                }
            }
            // no suppression needed; app exits after successful replace
        }
        else
        {
            _selected = null;
            BtnReplace.IsEnabled = false;
            ImgIcon.Source = null;
        }
    }

    private bool IsAlreadyConverted(GameExeCandidate cand)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(cand.ExePath);
            string sp = VersionResource.GetStringValue(cand.ExePath, "SpecialBuild") ?? vi.SpecialBuild ?? string.Empty;
            string lt = VersionResource.GetStringValue(cand.ExePath, "LegalTrademarks") ?? vi.LegalTrademarks ?? string.Empty;
            bool hasFlag = sp.IndexOf("VelopackEnabled=1", StringComparison.OrdinalIgnoreCase) >= 0 || lt.IndexOf("VelopackEnabled=1", StringComparison.OrdinalIgnoreCase) >= 0;
            string original = Path.Combine(Path.GetDirectoryName(cand.ExePath)!, cand.BaseName + "_original.exe");
            return hasFlag && File.Exists(original);
        } catch { return false; }
    }

    private void RestoreOriginal(GameExeCandidate cand)
    {
        string dir = Path.GetDirectoryName(cand.ExePath)!;
        string current = cand.ExePath;
        string orig = Path.Combine(dir, cand.BaseName + "_original.exe");
        if (!File.Exists(orig)) throw new FileNotFoundException("_original.exe not found", orig);

        // Delete current converted launcher and restore original filename
        try { File.Delete(current); } catch (Exception ex) { throw new IOException($"Failed to remove current launcher: {current}", ex); }
        File.Move(orig, current);
                
        TxtStatus.Text = $"Restored original: {MakeShortPath(current)}";
    }

    private void ResetFormButKeepFolder()
    {
        _selected = null;
        CmbExe.ItemsSource = null;
        TxtProductName.Text = string.Empty;
        TxtCompanyName.Text = string.Empty;
        TxtVersion.Text = string.Empty;
        TxtDescription.Text = string.Empty;
        TxtManifestName.Text = string.Empty;
        ImgIcon.Source = null;
        BtnReplace.IsEnabled = false;
        TxtStatus.Text = "Cancelled. No file loaded.";
    }

    private static string MakeShortPath(string path)
    {
        try
        {
            return Path.GetRelativePath(Environment.CurrentDirectory, path);
        }
        catch
        {
            return path;
        }
    }

    private static bool IsCrashHandler(string exePath)
    {
        var name = System.IO.Path.GetFileName(exePath) ?? string.Empty;
        return name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase);
    }

    private void TxtBuildDir_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AnalyzeNow();
        }
    }

    private void TxtBuildDir_LostFocus(object sender, RoutedEventArgs e)
    {
        AnalyzeNow();
    }

    private void LoadIconPreview(string exePath)
    {
        try
        {
            using var icon = IconUtils.ExtractBestIcon(exePath);
            if (icon != null)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    new System.Windows.Int32Rect(0, 0, icon.Width, icon.Height),
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));
                ImgIcon.Source = src;
            }
            else
            {
                ImgIcon.Source = null;
            }
        }
        catch
        {
            ImgIcon.Source = null;
        }
    }

    private async void BtnReplace_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null || _selected == null) return;
        BtnReplace.IsEnabled = false;
        TxtStatus.Text = "Building launcher...";
        try
        {
            string exeDir = AppContext.BaseDirectory;
            string repoRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", ".."));
            string launcherCsproj = Path.Combine(repoRoot, "Launcher.csproj");
            if (!File.Exists(launcherCsproj)) throw new FileNotFoundException("Launcher.csproj not found.");
            string launcherManifest = Path.Combine(repoRoot, "Launcher.manifest");
            if (!File.Exists(launcherManifest)) throw new FileNotFoundException("Launcher.manifest not found.");

            // Read values from editable fields
            string product = TxtProductName.Text?.Trim() ?? string.Empty;
            string company = TxtCompanyName.Text?.Trim() ?? string.Empty;
            string version = TxtVersion.Text?.Trim() ?? string.Empty;
            string description = TxtDescription.Text?.Trim() ?? product;
            string identityInput = TxtManifestName.Text?.Trim();
            string identity = ManifestPatcher.EnsureValidIdentity(identityInput, ManifestPatcher.DeriveIdentity(company, product));
            TxtManifestName.Text = identity;
            string copyright = TxtCopyright.Text?.Trim() ?? string.Empty;
            string assemblyName = _selected.BaseName;

            string icoPath = IconUtils.TrySaveIcoFromExe(_selected.ExePath) ?? string.Empty;

            string rid = _info.Architecture switch
            {
                "x86" => "win-x86",
                "arm64" => "win-arm64",
                _ => "win-x64",
            };

            string safeFileVersion = MakeSafeFileVersion(version);
            string msbuildProps = $"/p:AssemblyName=\"{assemblyName}\" /p:Company=\"{company}\" /p:Product=\"{product}\" /p:Description=\"{description}\" /p:InformationalVersion=\"{version}\" /p:IncludeSourceRevisionInInformationalVersion=false /p:EnableCompressionInSingleFile=true";
            string patchedManifest = ManifestPatcher.CreateIdentityPatchedManifest(launcherManifest, identity);
            msbuildProps += $" /p:ApplicationManifest=\"{patchedManifest}\"";
            if (!string.IsNullOrEmpty(safeFileVersion)) msbuildProps += $" /p:FileVersion=\"{safeFileVersion}\"";
            if (IsNumericVersion(version)) msbuildProps += $" /p:Version=\"{version}\""; // only when strictly numeric
            if (File.Exists(icoPath)) msbuildProps += $" /p:ApplicationIcon=\"{icoPath}\"";
            // Use SpecialBuild field as a technical marker
            msbuildProps += " /p:SpecialBuild=\"VelopackEnabled=1\""; msbuildProps += " /p:Trademark=\"VelopackEnabled=1\"";
            if (!string.IsNullOrEmpty(copyright)) msbuildProps += $" /p:Copyright=\"{copyright}\"";

            string args = $"publish \"{launcherCsproj}\" -c Release -r {rid} --self-contained true /p:PublishSingleFile=true {msbuildProps}";

            var (ok, output) = await RunProcessAsync("dotnet", args, repoRoot);
            if (!ok) throw new Exception("dotnet publish failed:\n" + output);

            string tfm = "net8.0-windows";
            string publishedExe = Path.Combine(repoRoot, "bin", "Release", tfm, rid, "publish", assemblyName + ".exe");
            if (!File.Exists(publishedExe)) throw new FileNotFoundException("Published launcher not found:", publishedExe);

            string orig = _selected.ExePath;
            string backup = Path.Combine(_info.BuildDirectory, assemblyName + "_original.exe");
            if (File.Exists(backup))
            {
                string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                backup = Path.Combine(_info.BuildDirectory, assemblyName + $"_original-{ts}.exe");
            }
            File.Move(orig, backup);

            File.Copy(publishedExe, orig, overwrite: false);

            TxtStatus.Text = $"Replaced. Backup: {MakeShortPath(backup)}";

            // Notify, open Explorer to the build directory, then exit the app
            try
            {
                System.Windows.MessageBox.Show(
                    "変換が完了しました。フォルダを開き、アプリを終了します。",
                    "完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = _info.BuildDirectory,
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            catch { }

            try { System.Windows.Application.Current.Shutdown(); } catch { }
            return;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = ex.ToString();
        }
        finally
        {
            BtnReplace.IsEnabled = true;
        }
    }

    private static async Task<(bool ok, string output)> RunProcessAsync(string fileName, string arguments, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;

        var proc = new Process { StartInfo = psi };
        proc.Start();
        string stdOut = await proc.StandardOutput.ReadToEndAsync();
        string stdErr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        bool ok = proc.ExitCode == 0;
        return (ok, stdOut + stdErr);
    }

    private static string MakeSafeFileVersion(string? raw)
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

            if (nums.Count == 0) return string.Empty;
            while (nums.Count < 3) nums.Add(0);
            if (nums.Count > 4) nums = nums.GetRange(0, 4);
            if (nums.Count == 3) nums.Add(0);
            return string.Join('.', nums);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsNumericVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        // 1 to 4 numeric parts
        return System.Text.RegularExpressions.Regex.IsMatch(v, "^\\d+(?:\\.\\d+){1,3}$");
    }

    private void BtnCopyStatus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = TxtStatus.Text;
            if (!string.IsNullOrEmpty(text))
            {
                System.Windows.Clipboard.SetText(text);
                TxtStatus.ToolTip = "Copied to clipboard";
            }
        }
        catch { }
    }
}


