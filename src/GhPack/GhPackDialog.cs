using System.Collections.ObjectModel;
using Eto.Drawing;
using Eto.Forms;
using GhPack.Core;

namespace GhPack;

internal sealed class PluginRow
{
    public bool Load { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
}

/// <summary>Profile editor: pick a profile, tick the plugins it loads, save, apply, launch.</summary>
internal sealed class GhPackDialog : Dialog
{
    private const string NotInstalled = "(not installed)";

    private readonly GhPackService _service;
    private readonly ProfileFile _file;
    private readonly DropDown _profiles = new();
    private readonly GridView _grid = new() { Height = 320, ShowHeader = true };
    private readonly Label _status = new() { Wrap = WrapMode.Word };
    private readonly Button _delete = new() { Text = "Delete" };
    private readonly Button _save = new() { Text = "Save" };
    private ObservableCollection<PluginRow> _rows = new();

    public bool LaunchRequested { get; private set; }

    /// <param name="openOn">Profile to show first. Null shows the active profile.</param>
    public GhPackDialog(GhPackService service, string? openOn = null)
    {
        _service = service;
        _file = service.Store.Load();

        Title = "GhPack — Grasshopper plugin profiles";
        Padding = 12;
        Resizable = true;
        MinimumSize = new Size(560, 480);
        Size = new Size(680, 520);

        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Load",
            Editable = true,
            Width = 50,
            DataCell = new CheckBoxCell { Binding = Binding.Property<PluginRow, bool?>(r => r.Load) }
        });
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Plugin",
            Width = 200,
            DataCell = new TextBoxCell { Binding = Binding.Property<PluginRow, string>(r => r.Name) }
        });
        _grid.Columns.Add(new GridColumn
        {
            HeaderText = "Folder",
            Expand = true,
            DataCell = new TextBoxCell { Binding = Binding.Property<PluginRow, string>(r => r.Folder) }
        });

        var create = new Button { Text = "New…" };
        create.Click += (_, _) => NewProfile();
        _delete.Click += (_, _) => DeleteProfile();
        _save.Click += (_, _) => SaveProfile();
        _profiles.SelectedIndexChanged += (_, _) => LoadRows();

        var apply = new Button { Text = "Apply" };
        apply.Click += (_, _) => ApplySelected(launch: false);
        var launch = new Button { Text = "Apply + Open Grasshopper" };
        launch.Click += (_, _) => ApplySelected(launch: true);
        var close = new Button { Text = "Close" };
        close.Click += (_, _) => Close();
        DefaultButton = launch;
        AbortButton = close;

        Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(new TableLayout
                {
                    Spacing = new Size(8, 0),
                    Rows = { new TableRow(new Label { Text = "Profile", VerticalAlignment = VerticalAlignment.Center },
                        new TableCell(_profiles, true), create, _save, _delete) }
                }),
                new TableRow(_grid) { ScaleHeight = true },
                new TableRow(_status),
                new TableRow(new TableLayout
                {
                    Spacing = new Size(8, 0),
                    Rows = { new TableRow(new TableCell(null, true), close, apply, launch) }
                })
            }
        };

        RefreshProfileList(openOn ?? _file.ActiveProfile);
    }

    private string SelectedName => _profiles.SelectedKey ?? GhPackService.AllPlugins;
    private bool AllSelected => GhPackService.IsAll(SelectedName);

    private void RefreshProfileList(string? select)
    {
        _profiles.Items.Clear();
        _profiles.Items.Add(new ListItem { Key = GhPackService.AllPlugins, Text = "All plugins" });
        foreach (var profile in _file.Profiles)
            _profiles.Items.Add(new ListItem { Key = profile.Name, Text = profile.Name });

        var target = !GhPackService.IsAll(select) && _file.Find(select!) is { } found ? found.Name : GhPackService.AllPlugins;
        _profiles.SelectedKey = target;
        LoadRows();
    }

    private void LoadRows()
    {
        var plugins = _service.ScanPlugins(_file);
        var profile = AllSelected ? null : _file.Find(SelectedName);

        var rows = plugins.Select(p => new PluginRow
        {
            Load = profile is null || profile.Includes(p.Name),
            Name = p.Name,
            Folder = ShortFolder(p.Folder)
        }).ToList();

        if (profile != null)
            rows.AddRange(profile.Enabled
                .Where(name => !plugins.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Select(name => new PluginRow { Load = true, Name = name, Folder = NotInstalled }));

        _rows = new ObservableCollection<PluginRow>(rows);
        _grid.DataStore = _rows;
        _grid.Enabled = !AllSelected;
        _save.Enabled = !AllSelected;
        _delete.Enabled = !AllSelected;

        var onDisk = plugins.Count(p => p.Enabled);
        _status.Text =
            $"{plugins.Count} plugin(s) found; {onDisk} set to load now. Active profile: {_file.ActiveProfile ?? "none"}.\n" +
            (GhPackService.IsGrasshopperLoaded
                ? "Grasshopper has already loaded its plugins. A change takes effect after you restart Rhino."
                : "Grasshopper has not loaded its plugins yet. A change takes effect when Grasshopper opens.") +
            (AllSelected ? "\n\"All plugins\" loads everything. Use New… to make a profile." : string.Empty);
    }

    // Shortens the long Grasshopper Libraries path and the home folder so the grid stays readable.
    private static string ShortFolder(string folder)
    {
        var normal = folder.Replace('\\', '/');
        var index = normal.IndexOf("/Libraries", StringComparison.OrdinalIgnoreCase);
        if (index >= 0 && normal.Substring(0, index).Contains("Grasshopper", StringComparison.OrdinalIgnoreCase))
            return "Grasshopper" + normal.Substring(index);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(home) && folder.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + folder.Substring(home.Length)
            : folder;
    }

    private void NewProfile()
    {
        var name = new NameDialog().ShowModal(this)?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (GhPackService.IsAll(name) || _file.Find(name) != null)
        {
            MessageBox.Show(this, $"A profile named '{name}' already exists.", MessageBoxType.Warning);
            return;
        }
        // A new profile starts from the ticks on screen.
        _file.Profiles.Add(new Profile { Name = name, Enabled = _rows.Where(r => r.Load).Select(r => r.Name).ToList() });
        _service.Store.Save(_file);
        RefreshProfileList(name);
    }

    private void SaveProfile()
    {
        if (AllSelected || _file.Find(SelectedName) is not { } profile) return;
        profile.Enabled = _rows.Where(r => r.Load).Select(r => r.Name).ToList();
        _service.Store.Save(_file);
        LoadRows();
    }

    private void DeleteProfile()
    {
        if (AllSelected || _file.Find(SelectedName) is not { } profile) return;
        if (MessageBox.Show(this, $"Delete profile '{profile.Name}'?", MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
        _file.Profiles.Remove(profile);
        if (string.Equals(_file.ActiveProfile, profile.Name, StringComparison.OrdinalIgnoreCase)) _file.ActiveProfile = null;
        _service.Store.Save(_file);
        RefreshProfileList(null);
    }

    private void ApplySelected(bool launch)
    {
        SaveProfile();
        var name = SelectedName;
        try
        {
            GhPackService.Report(name, _service.Apply(_file, name));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, MessageBoxType.Error);
            return;
        }
        if (launch)
        {
            LaunchRequested = true;
            Close();
        }
        else LoadRows();
    }
}

internal enum PickerAction { Cancel, Launch, LaunchAsApplied }

/// <summary>
/// Start screen for GhPackLaunch: choose a profile and open Grasshopper. Also the way in to the
/// profile editor, where the plugins of a profile are selected.
/// </summary>
internal sealed class ProfilePickerDialog : Dialog
{
    private readonly GhPackService _service;
    private readonly ListBox _list = new() { Height = 180 };

    public PickerAction Action { get; private set; } = PickerAction.Cancel;
    public string Profile => _list.SelectedKey ?? GhPackService.AllPlugins;

    public ProfilePickerDialog(GhPackService service)
    {
        _service = service;
        Title = "Open Grasshopper with profile";
        Padding = 12;
        MinimumSize = new Size(420, 260);

        var open = new Button { Text = "Open Grasshopper" };
        open.Click += (_, _) => Finish(PickerAction.Launch);
        _list.MouseDoubleClick += (_, _) => Finish(PickerAction.Launch);
        var cancel = new Button { Text = "Cancel" };
        cancel.Click += (_, _) => Finish(PickerAction.Cancel);
        var edit = new Button { Text = "Select Plugins…", ToolTip = "Choose which plugins the selected profile loads." };
        edit.Click += (_, _) => EditProfiles();
        DefaultButton = open;
        AbortButton = cancel;

        Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows =
            {
                new TableRow(_list) { ScaleHeight = true },
                new TableRow(new TableLayout
                {
                    Spacing = new Size(8, 0),
                    Rows = { new TableRow(edit, new TableCell(null, true), cancel, open) }
                })
            }
        };

        Reload(null);
    }

    private void Reload(string? select)
    {
        var file = _service.Store.Load();
        var wanted = select ?? file.ActiveProfile;
        _list.Items.Clear();
        _list.Items.Add(new ListItem { Key = GhPackService.AllPlugins, Text = "All plugins" });
        foreach (var profile in file.Profiles)
            _list.Items.Add(new ListItem { Key = profile.Name, Text = $"{profile.Name}  ({profile.Enabled.Count})" });
        _list.SelectedKey = !GhPackService.IsAll(wanted) && file.Find(wanted!) is { } found
            ? found.Name
            : GhPackService.AllPlugins;
    }

    private void EditProfiles()
    {
        var editor = new GhPackDialog(_service, Profile);
        editor.ShowModal(this);
        if (editor.LaunchRequested)
        {
            // The editor already applied the profile the user chose there.
            Finish(PickerAction.LaunchAsApplied);
            return;
        }
        Reload(Profile);
    }

    private void Finish(PickerAction action)
    {
        Action = action;
        Close();
    }
}

internal sealed class NameDialog : Dialog<string?>
{
    public NameDialog()
    {
        Title = "New profile";
        Padding = 12;
        var box = new TextBox { Width = 240, PlaceholderText = "Profile name" };
        var ok = new Button { Text = "Create" };
        ok.Click += (_, _) => Close(box.Text);
        var cancel = new Button { Text = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        DefaultButton = ok;
        AbortButton = cancel;
        Content = new TableLayout
        {
            Spacing = new Size(8, 8),
            Rows = { new TableRow(box), new TableRow(new TableLayout { Spacing = new Size(8, 0), Rows = { new TableRow(new TableCell(null, true), cancel, ok) } }) }
        };
    }
}
