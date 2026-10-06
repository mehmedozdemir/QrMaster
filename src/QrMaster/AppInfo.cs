using System.Reflection;

namespace QrMaster;

internal static class AppInfo
{
    public const string Name = "QR Master";

    public static string Version { get; } = ResolveVersion();

    public static string Repository { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitHubRepository")?.Value ?? "OWNER/QrMaster";

    public static bool IsRepositoryConfigured => !Repository.StartsWith("OWNER/", StringComparison.OrdinalIgnoreCase);

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QrMaster");

    private static string ResolveVersion()
    {
        var v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
