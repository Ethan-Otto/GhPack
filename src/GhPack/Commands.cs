using GhPack.Core;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace GhPack;

/// <summary>GhPack: opens the profile editor. Scripted ("-GhPack"): applies a profile by name.</summary>
[CommandStyle(Style.ScriptRunner)]
public sealed class GhPackCommand : Command
{
    public override string EnglishName => "GhPack";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var service = new GhPackService();
        if (mode == RunMode.Scripted) return CommandHelpers.ApplyFromCommandLine(service, launch: false);

        var dialog = new GhPackDialog(service);
        dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        if (dialog.LaunchRequested) CommandHelpers.LaunchGrasshopper();
        return Result.Success;
    }
}

/// <summary>GhPackLaunch: pick a profile, apply it, then start Grasshopper.</summary>
[CommandStyle(Style.ScriptRunner)]
public sealed class GhPackLaunchCommand : Command
{
    public override string EnglishName => "GhPackLaunch";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var service = new GhPackService();
        if (mode == RunMode.Scripted) return CommandHelpers.ApplyFromCommandLine(service, launch: true);

        var picker = new ProfilePickerDialog(service);
        picker.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);

        switch (picker.Action)
        {
            case PickerAction.Cancel:
                return Result.Cancel;
            case PickerAction.LaunchAsApplied:
                CommandHelpers.LaunchGrasshopper();
                return Result.Success;
        }

        try
        {
            GhPackService.Report(picker.Profile, service.Apply(service.Store.Load(), picker.Profile));
        }
        catch (Exception e)
        {
            RhinoApp.WriteLine($"GhPack: {e.Message}");
            return Result.Failure;
        }
        CommandHelpers.LaunchGrasshopper();
        return Result.Success;
    }
}

internal static class CommandHelpers
{
    public static Result ApplyFromCommandLine(GhPackService service, bool launch)
    {
        var file = service.Store.Load();
        var names = new[] { GhPackService.AllPlugins }.Concat(file.Profiles.Select(p => p.Name)).ToList();

        var get = new GetString();
        get.SetCommandPrompt($"Profile name ({string.Join(", ", names)})");
        get.SetDefaultString(file.ActiveProfile ?? GhPackService.AllPlugins);
        get.AcceptNothing(true);
        var got = get.GetLiteralString();
        if (got == GetResult.Cancel) return Result.Cancel;

        var name = got == GetResult.Nothing ? (file.ActiveProfile ?? GhPackService.AllPlugins) : get.StringResult().Trim();
        try
        {
            GhPackService.Report(name, service.Apply(file, name));
        }
        catch (ArgumentException e)
        {
            RhinoApp.WriteLine($"GhPack: {e.Message}");
            return Result.Failure;
        }
        if (launch) LaunchGrasshopper();
        return Result.Success;
    }

    public static void LaunchGrasshopper() => RhinoApp.RunScript("_Grasshopper", false);
}
