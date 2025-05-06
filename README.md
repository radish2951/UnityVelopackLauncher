# Unity Velopack Launcher

A template project to create a custom launcher for Unity applications, enabling self-updating capabilities through [Velopack](https://velopack.io/). This launcher dynamically loads `UnityPlayer.dll` and calls the `UnityMain` function. Crucially, it initializes Velopack **before** the Unity engine starts, which is a necessary step for Velopack to function correctly with Unity.

This project provides the **minimum C# launcher code** required to get Velopack initialized in a way that's compatible with a standard Unity build. The actual update checking, downloading, and applying logic within your Unity application needs to be implemented by you, leveraging the Velopack C# API.

**🚀 Super detailed explanations are available at [DeepWiki](https://deepwiki.com/radish2951/UnityVelopackLauncher)! It's fantastic! 🚀**

## Features

*   Integrates Velopack initialization into a standalone C# launcher, executed before Unity starts.
*   Dynamically loads and runs your Unity application.
*   Provides the foundation for your Unity application to use Velopack APIs.
*   Acts as a template for building a custom launcher tailored to your Unity project.

## Prerequisites

*   **.NET SDK:** You need the .NET SDK (version 8.0 or later recommended) installed on your development machine to build the launcher. You can download it from [here](https://dotnet.microsoft.com/download).
*   **Unity Project:** A built Unity application (Windows x64, using the Mono scripting backend is generally recommended for easier `System.Diagnostics.Process` API usage if needed by Velopack or your update logic. See Velopack's Unity documentation for IL2CPP considerations).

## How to Use

1.  **Clone or Download:**
    *   Clone this repository or download it as a ZIP.
    *   `git clone https://github.com/radish2951/UnityVelopackLauncher.git`

2.  **Customize Project Settings:**
    *   Open `Launcher.csproj` in a text editor or IDE.
    *   Update the following placeholder properties with your application's information:
        *   `<AssemblyName>YourProductName</AssemblyName>` (e.g., `MyAwesomeGame`)
        *   `<Company>YourCompanyName</Company>`
        *   `<Product>YourProductName</Product>` (e.g., `My Awesome Game`)
        *   `<Description>Your product description here</Description>`
        *   `<Copyright>Copyright (C) 2025 YourCompanyName</Copyright>`
    *   Open `Launcher.manifest`.
    *   Update the following placeholders:
        *   `<assemblyIdentity name="YourCompany.YourAwesomeGame.Launcher" ... />` (e.g., `MyCompany.MyAwesomeGame.Launcher`)
        *   `<description>Your application description here</description>`
    *   Replace `Launcher.ico` with your application's icon file. Ensure it's named `Launcher.ico` or update the `<ApplicationIcon>` tag in `Launcher.csproj` accordingly.

3.  **Install Velopack Package:**
    *   Open a terminal or command prompt in the `UnityVelopackLauncher` directory (where `Launcher.csproj` is located).
    *   Run the following command to add the Velopack NuGet package to the project:
        ```bash
        dotnet add package Velopack
        ```
        This ensures the necessary Velopack libraries are included for the launcher.

4.  **Build the Launcher:**
    *   Open a terminal or command prompt in the `UnityVelopackLauncher` directory.
    *   Run the publish command to create a single, self-contained executable:
        ```bash
        dotnet publish Launcher.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
        ```
        *   `-c Release`: Builds in Release configuration.
        *   `-r win-x64`: Targets Windows x64.
        *   `--self-contained true`: Includes the .NET runtime with the executable.
        *   `/p:PublishSingleFile=true`: Creates a single executable file.

    *   The published executable will be located in a subfolder within `bin/Release/net8.0-windows/win-x64/publish/`. For example:
        `UnityVelopackLauncher/bin/Release/net8.0-windows/win-x64/publish/YourProductName.exe` (The name will be what you set in `<AssemblyName>`).

5.  **Integrate with Your Unity Build:**
    *   Build your Unity project for Windows x64 as usual.
    *   Navigate to your Unity build's output directory (e.g., `Build/`).
    *   You will find your original Unity executable (e.g., `MyUnityGame.exe`) and a `*_Data` folder (e.g., `MyUnityGame_Data/`).
    *   **Replace the original Unity executable** (e.g., `MyUnityGame.exe`) with the launcher executable you built in step 4 (e.g., `YourProductName.exe`). Make sure to rename your built launcher to match the original Unity executable's name.
    *   Ensure `UnityPlayer.dll` is in the same directory as your new launcher executable. The launcher expects `UnityPlayer.dll` to be in its immediate vicinity.

6.  **Leverage Velopack in Your Unity Application (optionally):**
    *   This launcher (`Launcher.cs`) performs the crucial step of initializing Velopack via `VelopackApp.Build().Run()` **before** your Unity application starts. This is the primary function of this template.
    *   **With Velopack initialized by this launcher, your Unity application is now ready to utilize any of Velopack's features.**
    *   For example, you can:
        *   Use the `vpk` command-line tool to create installers and releases for your application.
        *   Implement in-app update checking and application logic using Velopack's C# API from your Unity scripts.
    *   How you choose to use Velopack beyond this initial setup is up to your project's needs.
    *   **For detailed guidance on using Velopack's C# API, creating packages, implementing update logic, and other features, please refer to the official Velopack documentation and their Unity sample project.** See the [References](#references) section below for direct links.

## Project Structure

```
UnityVelopackLauncher/
├── Launcher.cs # Main launcher code (C#) - Handles Velopack init and Unity launch
├── Launcher.csproj # C# project file
├── Launcher.manifest # Application manifest for Windows
├── Launcher.ico # Application icon
├── LICENSE # Project license
└── README.md # This file
```

## How it Works

1.  The `Main` method in `Launcher.cs` is the entry point.
2.  `VelopackApp.Build().Run()`: Initializes Velopack. This is the **key step** provided by this launcher. It handles Velopack's startup tasks, including checking for pending updates from a previous run and applying them if necessary. If an update was just applied and a restart is needed, Velopack handles it before your custom Unity launch code is executed.
3.  If no update-related restart by Velopack occurs, the launcher proceeds to:
    *   Load `UnityPlayer.dll` from the same directory.
    *   Get the address of the `UnityMain` function within `UnityPlayer.dll`.
    *   Call `UnityMain` to start the Unity engine, passing necessary arguments.
4.  Your Unity application then runs. It is now **your responsibility** to implement the logic using Velopack's C# API (e.g., `UpdateManager`) to check for new updates, download them, and trigger the update process (e.g., via `updateManager.WaitExitThenApplyUpdates(...)` followed by `UnityEngine.Application.Quit()`).
5.  Error handling is in place in the launcher to display messages if `UnityPlayer.dll` cannot be loaded or `UnityMain` cannot be found.

## References

For more detailed information on Velopack, Unity integration, and the Windows build process, please refer to the following resources:

*   **Velopack Documentation:**
    *   [Getting Started with .NET (Velopack C# API)](https://docs.velopack.io/getting-started/csharp) - Official guide for using Velopack with .NET applications.
    *   [Velopack App Hooks](https://docs.velopack.io/integrating/hooks) - Understanding how Velopack integrates with application startup.
*   **Velopack Unity Sample:**
    *   [CSharpUnityMono Sample (GitHub)](https://github.com/velopack/velopack/tree/develop/samples/CSharpUnityMono) - A sample project demonstrating Velopack integration with a Unity (Mono backend) application. This is a key resource for understanding how to implement update logic within Unity.
*   **Unity Documentation:**
    *   [Windows Player build binaries (Unity Manual)](https://docs.unity3d.com/Manual/WindowsStandaloneBinaries.html) - Official Unity documentation explaining the files generated during a Windows build and how `UnityPlayer.dll` is structured. This also touches upon rebuilding the executable.
    *   [Unity Standalone Player command line arguments](https://docs.unity3d.com/Manual/PlayerCommandLineArguments.html) - Useful if you need to pass custom arguments to `UnityMain` via this launcher.
    *   [Scripting restrictions in IL2CPP (Unity Manual)](https://docs.unity3d.com/Manual/scripting-restrictions.html) - Important considerations if you are targeting IL2CPP, especially regarding `System.Diagnostics.Process`.

## License

This project is licensed under the [MIT License](LICENSE).

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue.
