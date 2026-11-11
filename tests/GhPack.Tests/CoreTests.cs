using GhPack.Core;
using Xunit;

namespace GhPack.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghpack-" + Guid.NewGuid().ToString("N"));
    private readonly PluginScanner _scanner = new(new[] { ".no9", ".no9-Custom" });

    public CoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "Alpha.gha"), "");
        File.WriteAllText(Path.Combine(_root, "Beta.gha"), "");
        File.WriteAllText(Path.Combine(_root, "sub", "Gamma.gha"), "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private IReadOnlyList<PluginEntry> Scan(bool recursive = true) =>
        _scanner.Scan(new[] { new ScanFolder(_root, recursive) });

    [Fact]
    public void Scan_finds_gha_files_and_honours_recursion()
    {
        Assert.Equal(new[] { "Alpha.gha", "Beta.gha", "Gamma.gha" }, Scan().Select(p => p.Name));
        Assert.Equal(new[] { "Alpha.gha", "Beta.gha" }, Scan(recursive: false).Select(p => p.Name));
    }

    [Fact]
    public void Scan_skips_missing_folders_and_duplicate_folders()
    {
        var plugins = _scanner.Scan(new[]
        {
            new ScanFolder(Path.Combine(_root, "nope"), true),
            new ScanFolder(_root, true),
            new ScanFolder(_root, false),
        });
        Assert.Equal(3, plugins.Count);
    }

    [Fact]
    public void Marker_file_beside_gha_means_disabled()
    {
        File.WriteAllText(Path.Combine(_root, "Alpha.no9"), "");
        File.WriteAllText(Path.Combine(_root, "Beta.no9-Custom"), "");
        var plugins = Scan();
        Assert.False(plugins.Single(p => p.Name == "Alpha.gha").Enabled);
        Assert.False(plugins.Single(p => p.Name == "Beta.gha").Enabled);
        Assert.True(plugins.Single(p => p.Name == "Gamma.gha").Enabled);
    }

    [Fact]
    public void SetEnabled_writes_first_marker_and_removes_all_markers()
    {
        var alpha = Path.Combine(_root, "Alpha.gha");
        _scanner.SetEnabled(alpha, false);
        Assert.True(File.Exists(Path.Combine(_root, "Alpha.no9")));
        Assert.True(File.Exists(alpha)); // the .gha itself is never touched

        File.WriteAllText(Path.Combine(_root, "Alpha.no9-Custom"), "");
        _scanner.SetEnabled(alpha, true);
        Assert.False(File.Exists(Path.Combine(_root, "Alpha.no9")));
        Assert.False(File.Exists(Path.Combine(_root, "Alpha.no9-Custom")));
    }

    [Fact]
    public void Apply_profile_enables_only_listed_plugins()
    {
        var profile = new Profile { Name = "P", Enabled = { "beta.GHA", "NotInstalled.gha" } };
        var result = ProfileApplier.Apply(_scanner, Scan(), profile);

        Assert.Equal(new[] { "Alpha.gha", "Gamma.gha" }, result.DisabledNow);
        Assert.Empty(result.EnabledNow);
        Assert.Equal(new[] { "NotInstalled.gha" }, result.MissingFromDisk);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { false, true, false }, Scan().Select(p => p.Enabled));
    }

    [Fact]
    public void Apply_is_idempotent_and_null_profile_enables_everything()
    {
        var profile = new Profile { Name = "P", Enabled = { "Alpha.gha" } };
        ProfileApplier.Apply(_scanner, Scan(), profile);
        Assert.False(ProfileApplier.Apply(_scanner, Scan(), profile).Changed);

        var result = ProfileApplier.Apply(_scanner, Scan(), null);
        Assert.Equal(new[] { "Beta.gha", "Gamma.gha" }, result.EnabledNow);
        Assert.All(Scan(), p => Assert.True(p.Enabled));
    }

    [Fact]
    public void Store_round_trips_and_missing_file_gives_empty()
    {
        var store = new ProfileStore(Path.Combine(_root, "cfg", "profiles.json"));
        Assert.Empty(store.Load().Profiles);

        var file = new ProfileFile { ActiveProfile = "Structural" };
        file.Profiles.Add(new Profile { Name = "Structural", Enabled = { "Alpha.gha" } });
        file.ExtraFolders.Add("/x");
        store.Save(file);

        var loaded = store.Load();
        Assert.Equal("Structural", loaded.ActiveProfile);
        Assert.Equal(new[] { "Alpha.gha" }, loaded.Find("structural")!.Enabled);
        Assert.Equal(new[] { "/x" }, loaded.ExtraFolders);
    }
}
