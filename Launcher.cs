using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Linq;
using Velopack;

// 'partial' is required because LibraryImport generates partial methods.
// Removing this modifier will result in build errors.
public static partial class Launcher
{
  // --- Win32 API P/Invoke definitions ---
  [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  private static partial IntPtr LoadLibraryW(string lpLibFileName);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  private static partial IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool FreeLibrary(IntPtr hModule);

  [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  private static partial int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

  [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  private static partial IntPtr GetModuleHandleW(string? lpModuleName); // lpModuleName is string? because null is passed

  // MessageBoxW flags
  private const uint MB_OK = 0x00000000U;
  private const uint MB_ICONERROR = 0x00000010U;

  // nCmdShow value
  private const int SW_SHOWDEFAULT = 10;

  // --- UnityMain function delegate definition ---
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  private delegate int UnityMainDelegate(IntPtr hInstance, IntPtr hPrevInstance, [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, int nCmdShow);

  // --- Entry point ---
  [STAThread]
  static int Main(string[] args)
  {
    IntPtr hUnityPlayer = IntPtr.Zero;
    try
    {
      // 1. Initialize Velopack
      VelopackApp.Build().Run();

      // 2. Load UnityPlayer.dll
      string unityPlayerPath = Path.Combine(AppContext.BaseDirectory, "UnityPlayer.dll");
      hUnityPlayer = LoadLibraryW(unityPlayerPath);
      if (hUnityPlayer == IntPtr.Zero)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to load UnityPlayer.dll from '{unityPlayerPath}'. Please ensure it exists.");
      }

      // 3. Get address of UnityMain function
      IntPtr pUnityMain = GetProcAddress(hUnityPlayer, "UnityMain");
      if (pUnityMain == IntPtr.Zero)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to find the 'UnityMain' function in UnityPlayer.dll.");
      }

      // 4. Create UnityMain delegate
      var unityMain = Marshal.GetDelegateForFunctionPointer<UnityMainDelegate>(pUnityMain);

      // 5. Prepare arguments for UnityMain
      IntPtr hInstance = GetModuleHandleW(null); // Get instance handle of the executable
      if (hInstance == IntPtr.Zero)
      {
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get module handle.");
      }
      string commandLine = string.Join(" ", args.Select(a => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")); // Command line arguments

      // 6. Call UnityMain
      int exitCode = unityMain(hInstance, IntPtr.Zero, commandLine, SW_SHOWDEFAULT);
      return exitCode;
    }
    catch (Exception ex) when (ex is Win32Exception || ex is EntryPointNotFoundException || ex is MarshalDirectiveException)
    {
      // Specific errors related to Unity startup (DLL load failure, function not found, etc.)
      ShowError($"Failed to start the application (Unity error):\n{ex.Message}");
      return 1; // Exit with error
    }
    catch (Exception ex)
    {
      // Unexpected errors
      ShowError($"An unexpected error occurred:\n{ex.ToString()}"); // Include stack trace
      return 1; // Exit with error
    }
    finally
    {
      // 7. Ensure loaded library is freed
      if (hUnityPlayer != IntPtr.Zero)
      {
        bool freed = FreeLibrary(hUnityPlayer);
        if (!freed)
        {
          int error = Marshal.GetLastWin32Error();
          ShowError($"Failed to free UnityPlayer.dll: {new Win32Exception(error).Message}");
        }
      }
    }
  }

  // Helper method to show error messages (uses Win32 API MessageBoxW)
  private static void ShowError(string message)
  {
    string caption = AppDomain.CurrentDomain.FriendlyName ?? "Application Error";
    MessageBoxW(IntPtr.Zero, message, caption, MB_ICONERROR | MB_OK);
  }
}
