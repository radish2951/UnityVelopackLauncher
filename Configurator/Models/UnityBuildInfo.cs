using System.Collections.Generic;

namespace Configurator.Models;

public class UnityBuildInfo
{
    public required string BuildDirectory { get; init; }
    public required string UnityPlayerPath { get; init; }
    public required string Architecture { get; init; } // x64/x86/arm64/unknown
    public required List<GameExeCandidate> Candidates { get; init; }
}

public class GameExeCandidate
{
    public required string ExePath { get; init; }
    public required string BaseName { get; init; }
    public required string DataDirPath { get; init; }

    public override string ToString() => System.IO.Path.GetFileName(ExePath);
}

