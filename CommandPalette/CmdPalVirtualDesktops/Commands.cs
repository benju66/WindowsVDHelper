using CmdPalVirtualDesktops.Pages;
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVirtualDesktops;

internal static class Glyphs
{
    public const string Desktop = "";   // Task View
    public const string Window = "";    // window
    public const string Pin = "";
    public const string Unpin = "";
    public const string Add = "";
    public const string Rename = "";
    public const string Delete = "";
    public const string Move = "";
    public const string Gather = "";    // folder open / gather
    public const string Warning = "";
    public const string Play = "";
    public const string Current = "";   // check mark
}

/// <summary>Runs an action against the helper; turns errors into a toast instead of failing silently.</summary>
internal sealed partial class HelperCommand : InvokableCommand
{
    private readonly Func<ICommandResult> _action;

    public HelperCommand(string name, string glyph, Func<ICommandResult> action)
    {
        Name = name;
        Icon = new IconInfo(glyph);
        _action = action;
    }

    public HelperCommand(string name, string glyph, Action action, Func<ICommandResult>? result = null)
        : this(name, glyph, () =>
        {
            action();
            return (result ?? CommandResult.Dismiss)();
        })
    {
    }

    public override ICommandResult Invoke()
    {
        try
        {
            return _action();
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast(new ToastArgs { Message = ex.Message, Result = CommandResult.KeepOpen() });
        }
    }
}

internal static class Items
{
    public static ListItem Message(string title, string subtitle, string glyph, ICommand? command = null) =>
        new(command ?? new NoOpCommand()) { Title = title, Subtitle = subtitle, Icon = new IconInfo(glyph) };

    /// <summary>The message shown when the helper app isn't running, with a command to start it if it can be found.</summary>
    public static IListItem[] NotRunning(Action? afterStart = null)
    {
        var path = Helper.AppPath();
        if (path is null)
        {
            return [Message("Windows Virtual Desktop Helper isn't running", "Start it, then try again", Glyphs.Warning)];
        }

        var start = new HelperCommand("Start", Glyphs.Play, () =>
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            Thread.Sleep(1500); // give it a moment to start listening
            afterStart?.Invoke();
            return CommandResult.KeepOpen();
        });
        return [Message("Windows Virtual Desktop Helper isn't running", "Press Enter to start it", Glyphs.Warning, start)];
    }

    public static IListItem[] Error(Exception ex, Action? afterStart = null) =>
        ex is HelperNotRunningException ? NotRunning(afterStart) : [Message("Something went wrong", ex.Message, Glyphs.Warning)];

    // The window's own icon (a PNG the helper app extracted), or the generic window symbol
    public static IconInfo WindowIcon(WindowInfo w) =>
        !string.IsNullOrEmpty(w.IconPath) && File.Exists(w.IconPath) ? new IconInfo(w.IconPath) : new IconInfo(Glyphs.Window);

    public static string DesktopName(StatusInfo status, int index) =>
        index >= 0 && index < status.Desktops.Count ? status.Desktops[index].Name : $"Desktop {index + 1}";

    public static string Where(StatusInfo status, WindowInfo w) =>
        w.OnAllDesktops ? "All desktops" : w.Desktop >= 0 ? DesktopName(status, w.Desktop) : string.Empty;

    public static Tag DesktopTag(StatusInfo status, WindowInfo w)
    {
        if (w.OnAllDesktops)
        {
            return new Tag("All desktops") { ToolTip = w.AppPinned ? "All windows of this app are shown on all desktops" : "Shown on all desktops" };
        }

        return new Tag(DesktopName(status, w.Desktop)) { ToolTip = w.Desktop == status.Current ? "On this desktop" : "On another desktop" };
    }

    /// <summary>The actions for a window (the context menu in the window lists).</summary>
    public static IContextItem[] WindowCommands(StatusInfo status, WindowInfo w, Action refresh)
    {
        var list = new List<IContextItem>
        {
            new CommandContextItem(new MoveWindowPage(w)) { Title = "Move to desktop...", Icon = new IconInfo(Glyphs.Move) },
            new CommandContextItem(new HelperCommand("Move to a new desktop", Glyphs.Add, () => Helper.MoveToNewDesktop(w.Hwnd))),
            new CommandContextItem(new HelperCommand(w.Pinned ? "Stop showing on all desktops" : "Show on all desktops", w.Pinned ? Glyphs.Unpin : Glyphs.Pin, () =>
            {
                var pinned = Helper.SetPinned(w.Hwnd, !w.Pinned);
                refresh();
                return CommandResult.ShowToast(new ToastArgs { Message = pinned ? $"\"{w.Title}\" is shown on all desktops" : $"\"{w.Title}\" unpinned", Result = CommandResult.KeepOpen() });
            })),
        };

        if (!string.IsNullOrEmpty(w.App))
        {
            list.Add(new CommandContextItem(new HelperCommand(w.AppPinned ? $"Stop showing all {w.App} windows on all desktops" : $"Show all {w.App} windows on all desktops", w.AppPinned ? Glyphs.Unpin : Glyphs.Pin, () =>
            {
                var pinned = Helper.SetAppPinned(w.Hwnd, !w.AppPinned);
                refresh();
                return CommandResult.ShowToast(new ToastArgs { Message = pinned ? $"All {w.App} windows are shown on all desktops" : $"{w.App} unpinned", Result = CommandResult.KeepOpen() });
            })));
            list.Add(new CommandContextItem(new HelperCommand(w.AutoPinned ? $"Stop always showing {w.App} on all desktops" : $"Always show {w.App} on all desktops", Glyphs.Pin, () =>
            {
                Helper.SetAutoPinned(w.Process, !w.AutoPinned);
                refresh();
                return CommandResult.ShowToast(new ToastArgs { Message = w.AutoPinned ? $"{w.App} is no longer shown on all desktops automatically" : $"{w.App} will always be shown on all desktops", Result = CommandResult.KeepOpen() });
            })));
            list.Add(new CommandContextItem(new HelperCommand($"Bring all {w.App} windows here", Glyphs.Gather, () => Helper.Gather(w.Hwnd))));
        }

        return [.. list];
    }
}
