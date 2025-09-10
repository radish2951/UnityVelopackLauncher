using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Configurator.Utils;

public static class VersionResource
{
    [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileVersionInfoSizeW(string lptstrFilename, out uint lpdwHandle);

    [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileVersionInfoW(string lptstrFilename, uint dwHandle, uint dwLen, IntPtr lpData);

    [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VerQueryValueW(IntPtr pBlock, string lpSubBlock, out IntPtr lplpBuffer, out uint puLen);

    public static string? GetStringValue(string filePath, string key)
    {
        try
        {
            var (block, _) = LoadVersionBlock(filePath);
            if (block == IntPtr.Zero) return null;

            foreach (var (lang, codepage) in EnumerateTranslations(block))
            {
                string sub = $"\\StringFileInfo\\{lang:X4}{codepage:X4}\\{key}";
                if (VerQueryValueW(block, sub, out IntPtr ptr, out uint len) && ptr != IntPtr.Zero && len > 0)
                {
                    string? s = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }

            // Fallback to a common English block if translations are missing
            if (VerQueryValueW(block, "\\StringFileInfo\\040904B0\\" + key, out IntPtr ptrEn, out uint lenEn) && ptrEn != IntPtr.Zero && lenEn > 0)
            {
                string? s = Marshal.PtrToStringUni(ptrEn);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static (IntPtr block, IntPtr hMem) LoadVersionBlock(string filePath)
    {
        uint handle;
        uint size = GetFileVersionInfoSizeW(filePath, out handle);
        if (size == 0) return (IntPtr.Zero, IntPtr.Zero);
        IntPtr mem = Marshal.AllocHGlobal((int)size);
        bool ok = GetFileVersionInfoW(filePath, handle, size, mem);
        if (!ok)
        {
            Marshal.FreeHGlobal(mem);
            return (IntPtr.Zero, IntPtr.Zero);
        }
        return (mem, mem);
    }

    private static IEnumerable<(ushort lang, ushort codepage)> EnumerateTranslations(IntPtr block)
    {
        if (VerQueryValueW(block, "\\VarFileInfo\\Translation", out IntPtr ptr, out uint len) && ptr != IntPtr.Zero && len >= 4)
        {
            int count = (int)len / 4;
            for (int i = 0; i < count; i++)
            {
                ushort lang = (ushort)Marshal.ReadInt16(ptr, i * 4);
                ushort codepage = (ushort)Marshal.ReadInt16(ptr, i * 4 + 2);
                yield return (lang, codepage);
            }
        }
    }
}

