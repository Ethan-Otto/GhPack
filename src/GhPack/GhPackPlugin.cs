using System.Runtime.InteropServices;
using Rhino;
using Rhino.PlugIns;

[assembly: Guid("7c1f0e52-3b6a-4d7e-9a41-5e2b8c0d9f13")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://github.com/Ethan-Otto/GhPack")]

namespace GhPack;

public sealed class GhPackPlugin : PlugIn
{
    private const string ToolbarShownKey = "ToolbarShown";

    public static GhPackPlugin? Instance { get; private set; }

    public GhPackPlugin() => Instance = this;

    // Load with Rhino so the toolbar button is there before Grasshopper opens.
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        // Toolbars are not ready while plug-ins load; wait for the first idle.
        RhinoApp.Idle += ShowToolbar;
        return LoadReturnCode.Success;
    }

    /// <summary>
    /// Opens GhPack.rui (it sits beside the .rhp) and shows its one-button toolbar.
    /// If Rhino already has the file open it keeps the user's own show/hide choice.
    /// </summary>
    private void ShowToolbar(object? sender, EventArgs e)
    {
        RhinoApp.Idle -= ShowToolbar;
        try
        {
            var path = Path.ChangeExtension(Assembly.Location, ".rui");
            if (!File.Exists(path)) return;

            var file = RhinoApp.ToolbarFiles.FindByPath(path);
            var openedNow = file is null;
            file ??= RhinoApp.ToolbarFiles.Open(path);
            if (file is null || file.GroupCount == 0) return;

            if (openedNow || !Settings.GetBool(ToolbarShownKey, false))
            {
                file.GetGroup(0).Visible = true;
                Settings.SetBool(ToolbarShownKey, true);
            }
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"GhPack: could not show the toolbar ({ex.Message}). Use the GhPackLaunch command.");
        }
    }
}
