using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

public class CommandLineQuoterTests
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

  [Fact]
  public void Roundtrip_ProperQuoting_MatchesOriginal()
  {
    string[] tokens =
    {
      "-w","1280",
      "--path","C: \\ My Game \\ save file.txt".Replace(" \\ ", "\\"),
      "--name","Alice \"The Great\"",
      "--dir","C: \\ Temp \\".Replace(" \\ ", "\\"),
      "--empty",""
    };

    string cmd = string.Join(" ", tokens.Select(CommandLineQuoter.QuoteArg));
    var reparsed = ParseWithWindowsRules("demo.exe " + cmd).Skip(1).ToArray();
    Assert.Equal(tokens, reparsed);
  }

  [Fact]
  public void NaiveJoin_BreaksOnSpacesQuotesBackslashes()
  {
    string[] tokens =
    {
      "--path","C: \\ My Game \\ save file.txt".Replace(" \\ ", "\\"),
      "--name","Alice \"The Great\"",
      "--dir","C: \\ Temp \\".Replace(" \\ ", "\\"),
      "--empty",""
    };

    string cmd = string.Join(" ", tokens);
    var reparsed = ParseWithWindowsRules("demo.exe " + cmd).Skip(1).ToArray();
    Assert.NotEqual(tokens, reparsed);
  }

  [Fact]
  public void NoQuotesNeeded_StaysUnchanged()
  {
    string[] tokens = { "abc", "123", "--flag" };
    string cmd = string.Join(" ", tokens.Select(CommandLineQuoter.QuoteArg));
    var reparsed = ParseWithWindowsRules("demo.exe " + cmd).Skip(1).ToArray();
    Assert.Equal(tokens, reparsed);
  }
}
