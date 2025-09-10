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

namespace Configurator;

public partial class MainWindow : Window
{
    private UnityBuildInfo? _info;
    private GameExeCandidate? _selected;

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
        LblProductName.Text = LblCompanyName.Text = LblVersion.Text = LblArch.Text = string.Empty;
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
            var (product, company, version) = UnityBuildDetector.ReadFileVersion(cand.ExePath);
            LblProductName.Text = product;
            LblCompanyName.Text = company;
            LblVersion.Text = version;
            BtnReplace.IsEnabled = true;
            LoadIconPreview(cand.ExePath);
        }
        else
        {
            _selected = null;
            BtnReplace.IsEnabled = false;
            ImgIcon.Source = null;
        }
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
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
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

            var (product, company, version) = UnityBuildDetector.ReadFileVersion(_selected.ExePath);
            string assemblyName = _selected.BaseName;

            string icoPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_icon_{Guid.NewGuid():N}.ico");
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(_selected.ExePath);
                if (icon != null)
                {
                    using var fs = File.Create(icoPath);
                    icon.Save(fs);
                }
            }
            catch { }

            string rid = _info.Architecture switch
            {
                "x86" => "win-x86",
                "arm64" => "win-arm64",
                _ => "win-x64",
            };

            string safeFileVersion = MakeSafeFileVersion(version);
            string msbuildProps = $"/p:AssemblyName=\"{assemblyName}\" /p:Company=\"{company}\" /p:Product=\"{product}\" /p:Description=\"{product}\" /p:AssemblyInformationalVersion=\"{version}\" /p:IncludeSourceRevisionInInformationalVersion=false /p:EnableCompressionInSingleFile=true";
            if (!string.IsNullOrEmpty(safeFileVersion)) msbuildProps += $" /p:FileVersion=\"{safeFileVersion}\"";
            if (File.Exists(icoPath)) msbuildProps += $" /p:ApplicationIcon=\"{icoPath}\"";

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
            AnalyzeNow();
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
