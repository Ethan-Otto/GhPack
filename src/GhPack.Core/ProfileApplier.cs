namespace GhPack.Core;

public sealed record ApplyResult(
    IReadOnlyList<string> EnabledNow,
    IReadOnlyList<string> DisabledNow,
    IReadOnlyList<string> MissingFromDisk,
    IReadOnlyList<string> Errors)
{
    public bool Changed => EnabledNow.Count > 0 || DisabledNow.Count > 0;
}

public static class ProfileApplier
{
    /// <summary>
    /// Makes the markers on disk match the profile. A null profile enables every plugin.
    /// </summary>
    public static ApplyResult Apply(PluginScanner scanner, IReadOnlyList<PluginEntry> plugins, Profile? profile)
    {
        var enabledNow = new List<string>();
        var disabledNow = new List<string>();
        var errors = new List<string>();

        foreach (var plugin in plugins)
        {
            var want = profile is null || profile.Includes(plugin.Name);
            if (want == plugin.Enabled) continue;
            try
            {
                scanner.SetEnabled(plugin.FilePath, want);
                (want ? enabledNow : disabledNow).Add(plugin.Name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{plugin.Name}: {e.Message}");
            }
        }

        var missing = profile is null
            ? new List<string>()
            : profile.Enabled
                .Where(name => !plugins.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        return new ApplyResult(enabledNow, disabledNow, missing, errors);
    }
}
