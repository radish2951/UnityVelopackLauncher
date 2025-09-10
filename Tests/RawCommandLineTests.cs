using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

public class RawCommandLineTests
{
  [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern IntPtr LocalFree(IntPtr hMem);

  private static string[] ParseWithWindowsRules(string cmdLine)
  {
    IntPtr argv = CommandLineToArgvW(cmdLine, out int argc);
    if (argv == IntPtr.Zero)
      throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
      var result = new string[argc];
      for (int i = 0; i < argc; i++)
      {
        IntPtr pStr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
        result[i] = Marshal.PtrToStringUni(pStr) ?? string.Empty;
      }
      return result;
    }
    finally
    {
      LocalFree(argv);
    }
  }

  [Theory]
  [InlineData("C\\Path\\Launcher.exe")]
  [InlineData("C:/Path/Launcher.exe")]
  public void Extract_AfterExe_UnquotedExe(string exePath)
  {
    string[] tokens =
    {
      "--path","C: \\ My Game \\ save file.txt".Replace(" \\ ", "\\"),
      "--name","Alice \"The Great\"",
      "--dir","C: \\ Temp \\".Replace(" \\ ", "\\"),
      "--empty",""
    };
    string remainder = string.Join(" ", tokens.Select(CommandLineQuoter.QuoteArg));
    string full = exePath + "  " + remainder; // multiple spaces before args are allowed

    string after = RawCommandLine.GetAfterExe(full);
    Assert.Equal(remainder, after); // exact, because we controlled input

    var reparsed = ParseWithWindowsRules("demo.exe " + after).Skip(1).ToArray();
    Assert.Equal(tokens, reparsed);
  }

  [Fact]
  public void Extract_AfterExe_QuotedExeWithSpaces()
  {
    string exe = "\"C: \\ Program Files \\ MyApp \\ Launcher.exe\"".Replace(" \\ ", "\\");
    string[] tokens =
    {
      "-w","1280",
      "--path","C: \\ My Game \\ save file.txt".Replace(" \\ ", "\\"),
      "--name","Alice \"The Great\"",
      "--dir","C: \\ Temp \\".Replace(" \\ ", "\\"),
      "--empty",""
    };
    string remainder = string.Join(" ", tokens.Select(CommandLineQuoter.QuoteArg));
    string full = exe + "\t" + remainder; // tab after exe

    string after = RawCommandLine.GetAfterExe(full);
    Assert.Equal(remainder, after);

    var reparsed = ParseWithWindowsRules("demo.exe " + after).Skip(1).ToArray();
    Assert.Equal(tokens, reparsed);
  }

  [Fact]
  public void Extract_AfterExe_NoArgs_ReturnsEmpty()
  {
    string full1 = "C:/Path/Launcher.exe";
    string full2 = "\"C:/Path With Space/Launcher.exe\"   ";
    Assert.Equal(string.Empty, RawCommandLine.GetAfterExe(full1));
    Assert.Equal(string.Empty, RawCommandLine.GetAfterExe(full2));
  }
}

