using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Configurator.Utils;

public static class ManifestReader
{
    private const ushort RT_MANIFEST = 24;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    public static string? TryReadAssemblyIdentityName(string exePath)
    {
        try
        {
            string? xml = TryExtractEmbeddedManifest(exePath);
            if (string.IsNullOrWhiteSpace(xml)) return null;
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            XNamespace asmV1 = "urn:schemas-microsoft-com:asm.v1";
            var asmId = doc.Root?.Element(asmV1 + "assemblyIdentity") ?? doc.Root?.Element("assemblyIdentity");
            var nameAttr = asmId?.Attribute("name");
            return nameAttr?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractEmbeddedManifest(string exePath)
    {
        IntPtr h = IntPtr.Zero;
        try
        {
            h = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (h == IntPtr.Zero) return null;

            // Common IDs for RT_MANIFEST are 1 or 2
            var hrsrc = FindResource(h, new IntPtr(1), new IntPtr(RT_MANIFEST));
            if (hrsrc == IntPtr.Zero)
                hrsrc = FindResource(h, new IntPtr(2), new IntPtr(RT_MANIFEST));
            if (hrsrc == IntPtr.Zero) return null;

            uint size = SizeofResource(h, hrsrc);
            if (size == 0) return null;
            IntPtr hData = LoadResource(h, hrsrc);
            if (hData == IntPtr.Zero) return null;
            IntPtr p = LockResource(hData);
            if (p == IntPtr.Zero) return null;

            byte[] bytes = new byte[size];
            Marshal.Copy(p, bytes, 0, (int)size);
            return DecodeText(bytes);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (h != IntPtr.Zero) FreeLibrary(h);
        }
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            // UTF-16 BOM LE/BE
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // Heuristic: if many NUL bytes, it's likely UTF-16LE
        int nulCount = 0;
        for (int i = 1; i < Math.Min(bytes.Length, 64); i += 2)
            if (bytes[i] == 0) nulCount++;
        if (nulCount > 8)
            return Encoding.Unicode.GetString(bytes);

        return Encoding.UTF8.GetString(bytes);
    }
}

