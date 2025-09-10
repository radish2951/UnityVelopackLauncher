// Provides access to the raw Windows command line (minus the exe token)
// without re-quoting. This preserves the user's original quoting/spacing.
public static class RawCommandLine
{
  // Returns the portion of Environment.CommandLine after the exe name
  // and following whitespace. If no args, returns empty string.
  public static string GetAfterExeFromEnvironment()
  {
    return GetAfterExe(Environment.CommandLine ?? string.Empty);
  }

  // Pure helper: given a full command line (as returned by GetCommandLineW),
  // returns the substring after the program name token and any following spaces.
  public static string GetAfterExe(string full)
  {
    if (string.IsNullOrEmpty(full)) return string.Empty;
    int i = 0;
    int n = full.Length;

    // Skip leading spaces
    while (i < n && char.IsWhiteSpace(full[i])) i++;

    if (i >= n) return string.Empty;

    if (full[i] == '"')
    {
      // Quoted executable path: skip opening quote and read until the next quote
      i++; // skip opening quote
      while (i < n && full[i] != '"') i++;
      if (i < n && full[i] == '"') i++; // skip closing quote
    }
    else
    {
      // Unquoted: read until first whitespace
      while (i < n && !char.IsWhiteSpace(full[i])) i++;
    }

    // Skip whitespace after exe
    while (i < n && char.IsWhiteSpace(full[i])) i++;

    if (i >= n) return string.Empty;
    return full.Substring(i);
  }
}

