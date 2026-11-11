using System.Collections;
using System.Reflection;
using GhPack.Core;
using Rhino;
using Rhino.PlugIns;
using Rhino.Runtime;

namespace GhPack;

/// <summary>
/// Rhino-side facts: where Grasshopper looks for .gha files and which marker it honours.
/// GhPack does not reference Grasshopper.dll. When Grasshopper's assembly is already in the
/// process it is asked by reflection (the exact answer); otherwise the same rules are
/// recomputed here, so the commands also work before Grasshopper is loaded.
/// </summary>
public sealed class GhPackService
{
    public const string AllPlugins = "All";
    private static readonly Guid GrasshopperId = new("b45a29b1-4343-4035-989e-044e8580d9cf");
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public PluginScanner Scanner { get; }
    public ProfileStore Store { get; }

    public GhPackService()
    {
        Scanner = new PluginScanner(MarkerExtensions());
        Store = new ProfileStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhPack", "profiles.json"));
    }

    /// <summary>
    /// True when Grasshopper has already loaded its .gha files in this Rhino session.
    /// The Grasshopper plug-in itself can load at Rhino start; the .gha files load later, when
    /// the component server is created.
    /// </summary>
    public static bool IsGrasshopperLoaded
    {
        get
        {
            try
            {
                var grasshopper = GrasshopperAssembly();
                if (grasshopper is null) return false;
                var serverField = grasshopper.GetType("Grasshopper.Instances")?.GetField("m_comServer", AnyStatic);
                if (serverField != null) return serverField.GetValue(null) != null;
            }
            catch (Exception)
            {
                // Fall through to the conservative answer below.
            }
            return PlugIn.GetPlugInInfo(GrasshopperId)?.IsLoaded ?? false;
        }
    }

    public IReadOnlyList<PluginEntry> ScanPlugins(ProfileFile file) => Scanner.Scan(Folders(file));

    /// <summary>Applies a profile by name ("All" enables every plugin) and records it as active.</summary>
    public ApplyResult Apply(ProfileFile file, string profileName)
    {
        Profile? profile = null;
        if (!IsAll(profileName))
            profile = file.Find(profileName) ?? throw new ArgumentException($"Profile '{profileName}' does not exist.");

        var result = ProfileApplier.Apply(Scanner, ScanPlugins(file), profile);
        file.ActiveProfile = profile?.Name ?? AllPlugins;
        Store.Save(file);
        return result;
    }

    public static bool IsAll(string? name) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name, AllPlugins, StringComparison.OrdinalIgnoreCase);

    public static void Report(string profileName, ApplyResult result)
    {
        RhinoApp.WriteLine($"GhPack: profile '{profileName}' applied. Enabled {result.EnabledNow.Count}, disabled {result.DisabledNow.Count}.");
        foreach (var name in result.MissingFromDisk) RhinoApp.WriteLine($"GhPack: '{name}' is in the profile but is not installed.");
        foreach (var error in result.Errors) RhinoApp.WriteLine($"GhPack: could not change {error}");
        if (result.Changed && IsGrasshopperLoaded)
            RhinoApp.WriteLine("GhPack: Grasshopper has already loaded its plugins. Restart Rhino for the change to take effect.");
    }

    private static Assembly? GrasshopperAssembly() =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Grasshopper");

    // ---- marker extension -------------------------------------------------------------------

    // Grasshopper skips "Foo.gha" when "Foo.no<RhinoVersion>" or GH_ComponentServer.NoLoadExtension
    // (".no<RhinoVersion>" plus "-Scheme" for a non-default scheme) exists beside it.
    private static IReadOnlyList<string> MarkerExtensions()
    {
        var plain = $".no{RhinoApp.ExeVersion}";
        var scoped = NoLoadExtensionFromGrasshopper() ?? ComputedNoLoadExtension(plain);
        return string.Equals(scoped, plain, StringComparison.Ordinal) ? new[] { plain } : new[] { scoped, plain };
    }

    private static string? NoLoadExtensionFromGrasshopper()
    {
        try
        {
            return GrasshopperAssembly()?.GetType("Grasshopper.Kernel.GH_ComponentServer")
                ?.GetProperty("NoLoadExtension", AnyStatic)?.GetValue(null) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ComputedNoLoadExtension(string plain)
    {
        var scheme = RhinoApp.SchemeName ?? string.Empty;
        const string prefix = "Scheme: ";
        if (!scheme.StartsWith(prefix, StringComparison.Ordinal)) return plain;
        scheme = scheme.Substring(prefix.Length);
        return scheme.Length == 0 || scheme == "Default" ? plain : $"{plain}-{scheme.Replace('.', '_')}";
    }

    // ---- folders ----------------------------------------------------------------------------

    private static IEnumerable<ScanFolder> Folders(ProfileFile file)
    {
        // Both sources are used; the scanner drops duplicates. Grasshopper's own list also has the
        // folders added in GrasshopperDeveloperSettings.
        var folders = ComputedFolders();
        folders.AddRange(FoldersFromGrasshopper());
        foreach (var extra in file.ExtraFolders)
            if (!string.IsNullOrWhiteSpace(extra)) folders.Add(new ScanFolder(extra, Recursive: true));
        return folders;
    }

    // Grasshopper.Folders.AssemblyFolders, minus the built-in ("Core") folder. Empty when
    // Grasshopper.dll is not in the process or its shape is not the expected one.
    private static List<ScanFolder> FoldersFromGrasshopper()
    {
        var result = new List<ScanFolder>();
        try
        {
            var list = GrasshopperAssembly()?.GetType("Grasshopper.Folders")
                ?.GetProperty("AssemblyFolders", AnyStatic)?.GetValue(null) as IEnumerable;
            if (list is null) return result;

            foreach (var info in list)
            {
                var folder = Member(info, "Folder") as string;
                if (string.IsNullOrEmpty(folder)) continue;
                if (string.Equals(Member(info, "Type")?.ToString(), "Core", StringComparison.Ordinal)) continue;
                if (IsInsideRhinoInstall(folder)) continue;
                result.Add(new ScanFolder(folder, Member(info, "SearchOption") is SearchOption.AllDirectories));
            }
        }
        catch (Exception)
        {
            // Keep what was read; the computed folders cover the standard locations.
        }
        return result;
    }

    // GH_AssemblyFolderInfo exposes some members as fields and some as properties.
    private static object? Member(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();
        return type.GetProperty(name, flags)?.GetValue(target) ?? type.GetField(name, flags)?.GetValue(target);
    }

    // The same rules as Grasshopper.Folders.AssemblyFolders, for when Grasshopper.dll is not loaded yet.
    private static List<ScanFolder> ComputedFolders()
    {
        var result = new List<ScanFolder>();
        var libraries = DefaultLibrariesFolder();
        result.Add(new ScanFolder(libraries, Recursive: true));

        foreach (var linked in GhLinkFolders(libraries)) result.Add(new ScanFolder(linked, Recursive: false));

        // Yak packages and other installed plug-in folders.
        foreach (var path in HostUtils.GetAssemblySearchPaths())
        {
            foreach (var candidate in new[] { path, Path.GetDirectoryName(path) })
            {
                if (string.IsNullOrEmpty(candidate) || IsInsideRhinoInstall(candidate)) continue;
                result.Add(new ScanFolder(candidate, Recursive: false));
            }
        }
        return result;
    }

    // Windows: %APPDATA%\Grasshopper\Libraries.
    // Mac: <Rhino data>/Plug-ins/Grasshopper (<id>)/Libraries.
    private static string DefaultLibrariesFolder()
    {
        if (HostUtils.RunningOnWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grasshopper", "Libraries");
        return Path.Combine(RhinoApp.GetDataDirectory(true, false), "Plug-ins", $"Grasshopper ({GrasshopperId})", "Libraries");
    }

    private static IEnumerable<string> GhLinkFolders(string libraries)
    {
        if (!Directory.Exists(libraries)) yield break;
        foreach (var link in Directory.GetFiles(libraries, "*.ghlink"))
        {
            string[] lines;
            try { lines = File.ReadAllLines(link); }
            catch (IOException) { continue; }
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
                if (Directory.Exists(line)) yield return line;
                else if (File.Exists(line)) yield return Path.GetDirectoryName(line)!;
            }
        }
    }

    // Plugins that ship inside Rhino (Grasshopper's own components) are not managed:
    // GhPack never writes into the Rhino install.
    private static bool IsInsideRhinoInstall(string path)
    {
        var root = RhinoInstallRoot();
        return root != null && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    // Mac: the ".app" bundle. Windows: the folder above "System" (e.g. C:\Program Files\Rhino 8).
    private static string? RhinoInstallRoot()
    {
        var location = typeof(RhinoApp).Assembly.Location;
        if (string.IsNullOrEmpty(location)) return null;
        var bundle = location.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (bundle >= 0) return location.Substring(0, bundle + 5);
        var system = Path.GetDirectoryName(location);
        var parent = system is null ? null : Path.GetDirectoryName(system);
        var root = parent ?? system;
        return root is null ? null : root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}
