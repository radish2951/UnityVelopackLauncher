using System.Text;

/// Utility for building a Windows-compatible command line from argv tokens.
public static class CommandLineQuoter
{
  // Quote a single argument per Windows CommandLineToArgvW rules.
  public static string QuoteArg(string a)
  {
    if (string.IsNullOrEmpty(a)) return "\"\"";
    bool needsQuotes = false;
    foreach (char ch in a)
    {
      if (char.IsWhiteSpace(ch) || ch == '"') { needsQuotes = true; break; }
    }
    if (!needsQuotes) return a;

    var sb = new StringBuilder();
    sb.Append('"');
    int bsCount = 0;
    foreach (char c in a)
    {
      if (c == '\\') { bsCount++; continue; }
      if (c == '"') { sb.Append(new string('\\', bsCount * 2 + 1)); sb.Append('"'); bsCount = 0; continue; }
      if (bsCount > 0) { sb.Append(new string('\\', bsCount)); bsCount = 0; }
      sb.Append(c);
    }
    if (bsCount > 0) sb.Append(new string('\\', bsCount * 2));
    sb.Append('"');
    return sb.ToString();
  }
}

