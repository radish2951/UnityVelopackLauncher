using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Configurator.Utils;

public static class IconUtils
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon? ExtractBestIcon(string filePath)
    {
        try
        {
            IntPtr large;
            IntPtr small;
            uint count = ExtractIconEx(filePath, 0, out large, out small, 1);
            if (count == 0)
            {
                return null;
            }

            try
            {
                IntPtr h = large != IntPtr.Zero ? large : small;
                if (h == IntPtr.Zero) return null;
                using var tmp = Icon.FromHandle(h);
                return (Icon)tmp.Clone();
            }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        catch
        {
            return null;
        }
    }

    public static string? TrySaveIcoFromExe(string exePath)
    {
        try
        {
            var icon = ExtractBestIcon(exePath);
            if (icon == null) return null;
            string icoPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(exePath) + "_icon.ico");
            using (icon)
            using (var fs = File.Create(icoPath))
            {
                icon.Save(fs);
            }
            return icoPath;
        }
        catch
        {
            return null;
        }
    }
}

