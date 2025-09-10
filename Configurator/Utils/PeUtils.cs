using System;
using System.IO;

namespace Configurator.Utils;

public static class PeUtils
{
    // Returns machine string: x64, x86, arm64, arm, unknown
    public static string GetMachineString(string peFilePath)
    {
        try
        {
            using var fs = File.OpenRead(peFilePath);
            using var br = new BinaryReader(fs);

            // DOS header
            if (br.ReadUInt16() != 0x5A4D) // 'MZ'
                return "unknown";
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peHeaderOffset = br.ReadInt32();
            fs.Seek(peHeaderOffset, SeekOrigin.Begin);

            // PE signature 'PE\0\0'
            if (br.ReadUInt32() != 0x00004550)
                return "unknown";

            ushort machine = br.ReadUInt16();
            return machine switch
            {
                0x8664 => "x64",    // AMD64
                0x014c => "x86",    // I386
                0xAA64 => "arm64",  // ARM64
                0x01c4 => "arm",    // ARM Thumb-2
                _ => "unknown"
            };
        }
        catch
        {
            return "unknown";
        }
    }
}

