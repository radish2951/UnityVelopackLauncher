using System;
using System.IO;
using System.Xml.Linq;

namespace Configurator.Utils;

public static class ManifestPatcher
{
    // Minimal patch: only override assemblyIdentity/@name, leave everything else intact.
    public static string CreateIdentityPatchedManifest(string sourcePath, string identityName)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Manifest not found", sourcePath);

        var doc = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        XNamespace asmV1 = "urn:schemas-microsoft-com:asm.v1";

        var root = doc.Root ?? throw new InvalidOperationException("Manifest root missing");
        var asmId = root.Element(asmV1 + "assemblyIdentity");
        if (asmId != null)
        {
            asmId.SetAttributeValue("name", identityName);
        }

        string tmp = Path.Combine(Path.GetTempPath(), $"patched_manifest_{Guid.NewGuid():N}.manifest");
        doc.Save(tmp);
        return tmp;
    }

    public static string DeriveIdentity(string company, string product)
    {
        string c = SanitizeForIdentity(company);
        string p = SanitizeForIdentity(product);
        if (string.IsNullOrEmpty(c) && string.IsNullOrEmpty(p))
            return "Launcher";
        if (string.IsNullOrEmpty(c)) return p + ".Launcher";
        if (string.IsNullOrEmpty(p)) return c + ".Launcher";
        return c + "." + p + ".Launcher";
    }

    public static string SanitizeForIdentity(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var arr = s.Trim().ToCharArray();
        for (int i = 0; i < arr.Length; i++)
        {
            char ch = arr[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_'))
                arr[i] = '.'; // replace invalid with dot
        }
        var str = new string(arr);
        while (str.Contains("..")) str = str.Replace("..", ".");
        return str.Trim('.');
    }

    public static string EnsureValidIdentity(string? input, string fallback)
    {
        string s = SanitizeForIdentity(input);
        if (string.IsNullOrEmpty(s)) return fallback;
        return s;
    }
}
