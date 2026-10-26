using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace AppFlow;

internal sealed class UpdateService
{
    private readonly UpdateManager? manager;
    private readonly string? configurationProblem;

    public UpdateService()
    {
        var repositoryUrl = typeof(UpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "GitHubRepositoryUrl")?.Value;

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Trim('/').Split('/').Length != 2)
        {
            configurationProblem = "GitHub updates are not configured for this build yet.";
            return;
        }

        manager = new UpdateManager(new GithubSource(uri.GetLeftPart(UriPartial.Path), accessToken: null, prerelease: false));
    }

    public bool IsConfigured => manager != null;
    public bool IsInstalled => manager?.IsInstalled == true;
    public string? CurrentVersion => manager?.CurrentVersion?.ToString();
    public string? ConfigurationProblem => configurationProblem;

    public Task<UpdateInfo?> CheckAsync() => manager!.CheckForUpdatesAsync();
    public Task DownloadAsync(UpdateInfo update) => manager!.DownloadUpdatesAsync(update);
    public void ApplyAndRestart(UpdateInfo update) => manager!.ApplyUpdatesAndRestart(update);
}
