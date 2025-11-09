using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Configurator.Services;
using Configurator.Utils;

namespace Configurator;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Configure UTF-8 output for Japanese characters at the very start
        // Use SetOut instead of OutputEncoding to avoid "handle is invalid" error when redirected
        try
        {
            Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8) { AutoFlush = true });
        }
        catch { /* Ignore if console redirection fails */ }

        // CLI mode: if arguments are provided
        if (args.Length > 0)
        {
            return RunCliMode(args);
        }

        // GUI mode: no arguments
        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }

    private static int RunCliMode(string[] args)
    {

        try
        {
            // Parse arguments
            string? buildPath = null;
            string? launcherCsproj = null;
            string? productName = null;
            string? companyName = null;
            string? version = null;
            string? copyright = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--build-path" && i + 1 < args.Length)
                {
                    buildPath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--launcher-csproj" && i + 1 < args.Length)
                {
                    launcherCsproj = args[i + 1];
                    i++;
                }
                else if (args[i] == "--product-name" && i + 1 < args.Length)
                {
                    productName = args[i + 1];
                    i++;
                }
                else if (args[i] == "--company-name" && i + 1 < args.Length)
                {
                    companyName = args[i + 1];
                    i++;
                }
                else if (args[i] == "--version" && i + 1 < args.Length)
                {
                    version = args[i + 1];
                    i++;
                }
                else if (args[i] == "--copyright" && i + 1 < args.Length)
                {
                    copyright = args[i + 1];
                    i++;
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    ShowHelp();
                    return 0;
                }
            }

            if (string.IsNullOrEmpty(buildPath) || string.IsNullOrEmpty(launcherCsproj))
            {
                Console.Error.WriteLine("Error: --build-path and --launcher-csproj are required");
                ShowHelp();
                return 1;
            }

            // Run the launcher build process
            return RunLauncherBuild(buildPath, launcherCsproj, productName, companyName, version, copyright);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"Inner: {ex.InnerException.Message}");
            }
            return 1;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("UnityVelopackConfigurator CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  UnityVelopackConfigurator.exe --build-path <path> --launcher-csproj <path> [options]");
        Console.WriteLine();
        Console.WriteLine("Required:");
        Console.WriteLine("  --build-path <path>       Path to Unity build directory");
        Console.WriteLine("  --launcher-csproj <path>  Path to Launcher.csproj");
        Console.WriteLine();
        Console.WriteLine("Optional (override auto-detected values):");
        Console.WriteLine("  --product-name <name>     Product name (overrides Unity exe metadata)");
        Console.WriteLine("  --company-name <name>     Company name (overrides Unity exe metadata)");
        Console.WriteLine("  --version <version>       Version string (overrides Unity exe metadata)");
        Console.WriteLine("  --copyright <text>        Copyright text (overrides Unity exe metadata)");
        Console.WriteLine("  --help, -h                Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  UnityVelopackConfigurator.exe --build-path \"C:\\Build\\v1.1.5\" --launcher-csproj \"..\\Launcher\\Launcher.csproj\"");
        Console.WriteLine("  UnityVelopackConfigurator.exe --build-path \"C:\\Build\\v1.1.5\" --launcher-csproj \"..\\Launcher\\Launcher.csproj\" --company-name \"MyCompany\"");
    }

    private static int RunLauncherBuild(string buildPath, string launcherCsproj,
                                        string? overrideProductName, string? overrideCompanyName,
                                        string? overrideVersion, string? overrideCopyright)
    {
        var request = new Services.LauncherBuilder.BuildRequest
        {
            BuildPath = buildPath,
            LauncherCsprojPath = launcherCsproj,
            ProductName = overrideProductName,
            CompanyName = overrideCompanyName,
            Version = overrideVersion,
            Copyright = overrideCopyright
        };

        var result = Services.LauncherBuilder.Build(request, Console.WriteLine);

        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.ErrorMessage}");
            return 1;
        }

        return 0;
    }
}
