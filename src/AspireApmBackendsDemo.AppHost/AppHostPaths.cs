internal sealed record AppHostPaths(string SourceDir, string ObservabilityDir)
{
    public static AppHostPaths FromAppHost()
    {
        var appHostDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)
            ?? throw new InvalidOperationException("Could not determine app host directory");

        var sourceDir = Path.GetFullPath(Path.Combine(appHostDir, "..", "..", "..", ".."));
        return new AppHostPaths(sourceDir, Path.Combine(sourceDir, "observability"));
    }
}
