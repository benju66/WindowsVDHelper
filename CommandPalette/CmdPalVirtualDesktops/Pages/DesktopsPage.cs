using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVirtualDesktops.Pages;

/// <summary>All desktops: Enter switches; the context menu renames or closes; plus "New desktop".</summary>
internal sealed partial class DesktopsPage : ListPage
{
    public DesktopsPage()
    {
        Id = "vdh.desktops";
        Name = "Open";
        Title = "Virtual desktops";
        Icon = new IconInfo(Glyphs.Desktop);
        PlaceholderText = "Type a desktop name";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var status = Helper.Status();
            var items = new List<IListItem>();
            foreach (var desktop in status.Desktops)
            {
                var index = desktop.Index;
                var isCurrent = index == status.Current;
                var more = new List<IContextItem>
                {
                    new CommandContextItem(new RenamePage(index, desktop.Name)) { Title = "Rename...", Icon = new IconInfo(Glyphs.Rename) },
                };
                if (status.Count > 1)
                {
                    more.Add(new CommandContextItem(new HelperCommand("Close desktop", Glyphs.Delete, () =>
                    {
                        Helper.Remove(index);
                        RaiseItemsChanged();
                        return CommandResult.ShowToast(new ToastArgs { Message = $"Closed \"{desktop.Name}\" (its windows moved to the next desktop)", Result = CommandResult.KeepOpen() });
                    })) { IsCritical = true });
                }

                items.Add(new ListItem(new HelperCommand("Switch", Glyphs.Desktop, () => Helper.Switch(index)))
                {
                    Title = desktop.Name,
                    Subtitle = isCurrent ? $"Desktop {index + 1}  ·  current desktop" : $"Desktop {index + 1}",
                    Icon = new IconInfo(isCurrent ? Glyphs.Current : Glyphs.Desktop),
                    MoreCommands = [.. more],
                });
            }

            if (status.Extended)
            {
                items.Add(new ListItem(new HelperCommand("Create", Glyphs.Add, () => Helper.NewDesktop()))
                {
                    Title = "New desktop",
                    Subtitle = "Create a desktop and switch to it",
                    Icon = new IconInfo(Glyphs.Add),
                });
            }

            return [.. items];
        }
        catch (Exception ex)
        {
            return Items.Error(ex, () => RaiseItemsChanged());
        }
    }
}

/// <summary>Rename by typing: the search box is the new name, Enter applies it.</summary>
internal sealed partial class RenamePage : DynamicListPage
{
    private readonly int _index;
    private readonly string _current;

    public RenamePage(int index, string current)
    {
        _index = index;
        _current = current;
        Name = "Rename";
        Title = $"Rename \"{current}\"";
        Icon = new IconInfo(Glyphs.Rename);
        PlaceholderText = "Type the new name";
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var name = SearchText?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return
            [
                Items.Message($"Type a new name for \"{_current}\"", "Or press Enter below to reset it to the default name", Glyphs.Rename),
                new ListItem(new HelperCommand("Reset", Glyphs.Rename, () => Helper.Rename(_index, string.Empty), CommandResult.GoBack))
                {
                    Title = $"Reset to \"Desktop {_index + 1}\"",
                    Icon = new IconInfo(Glyphs.Rename),
                },
            ];
        }

        return
        [
            new ListItem(new HelperCommand("Rename", Glyphs.Rename, () => Helper.Rename(_index, name), CommandResult.GoBack))
            {
                Title = $"Rename to \"{name}\"",
                Subtitle = $"Desktop {_index + 1}, now \"{_current}\"",
                Icon = new IconInfo(Glyphs.Rename),
            },
        ];
    }
}
