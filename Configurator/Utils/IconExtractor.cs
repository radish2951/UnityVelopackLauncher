using System.IO;
using System.Runtime.InteropServices;

namespace Configurator.Utils;

// Extracts the full multi-size, full-color ICO from an executable by reading
// RT_GROUP_ICON + RT_ICON resources and writing a correct .ico file.
public static class IconExtractor
{
    private const int RT_ICON = 3;
    private const int RT_GROUP_ICON = 14;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    // Returns path to saved .ico (in %TEMP%) or null on failure.
    public static string? TryExtractFullIconIco(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;

        IntPtr h = IntPtr.Zero;
        try
        {
            h = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (h == IntPtr.Zero) return null;

            // Pick the first RT_GROUP_ICON resource we can find.
            IntPtr? firstGroup = null;
            EnumResourceNames(h, new IntPtr(RT_GROUP_ICON), (mod, type, name, l) =>
            {
                if (firstGroup == null) firstGroup = name;
                return false; // stop after first
            }, IntPtr.Zero);

            if (firstGroup == null)
            {
                // Fallback: try common IDs 1 then 2
                firstGroup = new IntPtr(1);
                if (FindResource(h, firstGroup.Value, new IntPtr(RT_GROUP_ICON)) == IntPtr.Zero)
                {
                    firstGroup = new IntPtr(2);
                    if (FindResource(h, firstGroup.Value, new IntPtr(RT_GROUP_ICON)) == IntPtr.Zero)
                        return null;
                }
            }

            // Load group icon directory
            var groupBytes = LoadResBytes(h, firstGroup.Value, RT_GROUP_ICON);
            if (groupBytes == null || groupBytes.Length < 6) return null;

            using var msGroup = new MemoryStream(groupBytes);
            using var br = new BinaryReader(msGroup);

            ushort reserved = br.ReadUInt16(); // 0
            ushort type = br.ReadUInt16();     // 1
            ushort count = br.ReadUInt16();
            if (type != 1 || count == 0 || count > 256) return null;

            var entries = new List<GrpEntry>(count);
            for (int i = 0; i < count; i++)
            {
                byte width = br.ReadByte();
                byte height = br.ReadByte();
                byte colorCount = br.ReadByte();
                byte reserved2 = br.ReadByte();
                ushort planes = br.ReadUInt16();
                ushort bitCount = br.ReadUInt16();
                uint bytesInRes = br.ReadUInt32();
                ushort id = br.ReadUInt16(); // resource ID of RT_ICON
                entries.Add(new GrpEntry
                {
                    Width = width,
                    Height = height,
                    ColorCount = colorCount,
                    Reserved = reserved2,
                    Planes = planes,
                    BitCount = bitCount,
                    BytesInRes = bytesInRes,
                    ResourceId = id,
                });
            }

            // Fetch each RT_ICON payload
            var images = new List<byte[]>(entries.Count);
            foreach (var e in entries)
            {
                var data = LoadResBytes(h, new IntPtr(e.ResourceId), RT_ICON);
                if (data == null) return null;
                images.Add(data);
            }

            // Write .ico: ICONDIR + ICONDIRENTRY[count] + image blobs
            string outPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(exePath) + "_icon_full.ico");
            using var fs = File.Create(outPath);
            using var bw = new BinaryWriter(fs);

            bw.Write((ushort)0); // reserved
            bw.Write((ushort)1); // type = icon
            bw.Write((ushort)images.Count);

            int headerSize = 6 + (16 * images.Count);
            int offset = headerSize;

            // Precompute entries with correct sizes and offsets
            var fileEntries = new List<FileEntry>(images.Count);
            for (int i = 0; i < images.Count; i++)
            {
                var g = entries[i];
                var data = images[i];
                fileEntries.Add(new FileEntry
                {
                    Width = g.Width,
                    Height = g.Height,
                    ColorCount = g.ColorCount,
                    Reserved = g.Reserved,
                    Planes = g.Planes,
                    BitCount = g.BitCount,
                    BytesInRes = (uint)data.Length,
                    ImageOffset = (uint)offset,
                    Data = data,
                });
                offset += data.Length;
            }

            // Write directory entries
            foreach (var e in fileEntries)
            {
                bw.Write(e.Width);
                bw.Write(e.Height);
                bw.Write(e.ColorCount);
                bw.Write(e.Reserved);
                bw.Write(e.Planes);
                bw.Write(e.BitCount);
                bw.Write(e.BytesInRes);
                bw.Write(e.ImageOffset);
            }

            // Write image data
            foreach (var e in fileEntries)
            {
                bw.Write(e.Data);
            }

            return outPath;
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

    private static byte[]? LoadResBytes(IntPtr module, IntPtr name, int type)
    {
        IntPtr hRes = FindResource(module, name, new IntPtr(type));
        if (hRes == IntPtr.Zero) return null;
        uint size = SizeofResource(module, hRes);
        if (size == 0) return null;
        IntPtr hData = LoadResource(module, hRes);
        if (hData == IntPtr.Zero) return null;
        IntPtr p = LockResource(hData);
        if (p == IntPtr.Zero) return null;
        byte[] bytes = new byte[size];
        Marshal.Copy(p, bytes, 0, (int)size);
        return bytes;
    }

    private struct GrpEntry
    {
        public byte Width;
        public byte Height;
        public byte ColorCount;
        public byte Reserved;
        public ushort Planes;
        public ushort BitCount;
        public uint BytesInRes;
        public ushort ResourceId;
    }

    private struct FileEntry
    {
        public byte Width;
        public byte Height;
        public byte ColorCount;
        public byte Reserved;
        public ushort Planes;
        public ushort BitCount;
        public uint BytesInRes;
        public uint ImageOffset;
        public byte[] Data;
    }
}

