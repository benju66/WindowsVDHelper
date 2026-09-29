using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVirtualDesktops.Pages;

/// <summary>
/// Every open window on every desktop: type part of its title or app, Enter switches to its desktop and focuses
/// it. The context menu moves it, pins it or its app, or brings all of the app's windows here.
/// </summary>
internal sealed partial class WindowsPage : ListPage
{
    public WindowsPage()
    {
        Id = "vdh.windows";
        Name = "Open";
        Title = "Find window on any desktop";
        Icon = new IconInfo(Glyphs.Window);
        PlaceholderText = "Type part of a window title or app name";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var status = Helper.Status();
            var windows = Helper.Windows();
            if (windows.Count == 0)
            {
                return [Items.Message("No windows", string.Empty, Glyphs.Window)];
            }

            // This desktop first, then the others in order, then the ones on all desktops
            return [.. windows
                .OrderBy(w => w.OnAllDesktops ? int.MaxValue : w.Desktop == status.Current ? -1 : w.Desktop)
                .Select(w => (IListItem)new ListItem(new HelperCommand("Go to window", Glyphs.Window, () => Helper.Focus(w.Hwnd)))
                {
                    Title = w.Title,
                    Subtitle = $"{w.App}  ·  {Items.Where(status, w)}",
                    Icon = Items.WindowIcon(w),
                    Tags = [Items.DesktopTag(status, w)],
                    MoreCommands = status.Extended ? Items.WindowCommands(status, w, () => RaiseItemsChanged()) : [],
                })];
        }
        catch (Exception ex)
        {
            return Items.Error(ex, () => RaiseItemsChanged());
        }
    }
}

/// <summary>
/// A checklist of every open window: Enter shows/stops showing it on all desktops and stays on the page,
/// so several windows can be pinned in a row.
/// </summary>
internal sealed partial class PinWindowsPage : ListPage
{
    public PinWindowsPage()
    {
        Id = "vdh.pin";
        Name = "Open";
        Title = "Pin windows to all desktops";
        Icon = new IconInfo(Glyphs.Pin);
        PlaceholderText = "Type part of a window title, Enter pins or unpins it";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var status = Helper.Status();
            // Pinned first, then this desktop, then the others
            return [.. Helper.Windows()
                .OrderBy(w => w.Pinned ? -2 : w.Desktop == status.Current ? -1 : w.Desktop < 0 ? int.MaxValue : w.Desktop)
                .Select(w => (IListItem)new ListItem(new HelperCommand(w.Pinned ? "Unpin" : "Pin", w.Pinned ? Glyphs.Unpin : Glyphs.Pin, () =>
                {
                    Helper.SetPinned(w.Hwnd, !w.Pinned);
                    RaiseItemsChanged();
                    return CommandResult.KeepOpen();
                }))
                {
                    Title = w.Title,
                    Subtitle = w.Pinned ? $"{w.App}  ·  shown on all desktops" : w.AppPinned ? $"{w.App}  ·  all windows of this app are shown on all desktops" : $"{w.App}  ·  {Items.Where(status, w)}",
                    Icon = Items.WindowIcon(w),
                    Tags = w.Pinned ? [new Tag("Pinned") { Icon = new IconInfo(Glyphs.Pin), ToolTip = "Shown on all desktops" }] : [],
                })];
        }
        catch (Exception ex)
        {
            return Items.Error(ex, () => RaiseItemsChanged());
        }
    }
}

/// <summary>Pick a desktop for a window (from the window lists, or for the window you were working in).</summary>
internal sealed partial class MoveWindowPage : ListPage
{
    private readonly WindowInfo? _window;

    // window = null: the window the user was working in before opening Command Palette
    public MoveWindowPage(WindowInfo? window = null)
    {
        _window = window;
        Id = window is null ? "vdh.moveactive" : string.Empty;
        Name = "Move";
        Title = window is null ? "Move the window you were working in to..." : $"Move \"{window.Title}\" to...";
        Icon = new IconInfo(Glyphs.Move);
        PlaceholderText = "Type a desktop name";
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var window = _window ?? Helper.LastWindow();
            if (window is null)
            {
                return [Items.Message("No window to move", "Switch to the window first, then open Command Palette", Glyphs.Window)];
            }

            Title = $"Move \"{window.Title}\" to...";
            Icon = Items.WindowIcon(window);
            var status = Helper.Status();
            var items = new List<IListItem>();
            foreach (var desktop in status.Desktops)
            {
                if (desktop.Index == window.Desktop)
                {
                    continue;
                }

                var index = desktop.Index;
                items.Add(new ListItem(new HelperCommand("Move", Glyphs.Move, () => Helper.Move(window.Hwnd, index)))
                {
                    Title = desktop.Name,
                    Subtitle = index == status.Current ? $"Desktop {index + 1}  ·  current desktop" : $"Desktop {index + 1}",
                    Icon = new IconInfo(Glyphs.Desktop),
                });
            }

            items.Add(new ListItem(new HelperCommand("Move", Glyphs.Add, () => Helper.MoveToNewDesktop(window.Hwnd)))
            {
                Title = "New desktop",
                Subtitle = "Create a desktop and take the window there",
                Icon = new IconInfo(Glyphs.Add),
            });
            return [.. items];
        }
        catch (Exception ex)
        {
            return Items.Error(ex, () => RaiseItemsChanged());
        }
    }
}
