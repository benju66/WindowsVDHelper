using CmdPalVirtualDesktops.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVirtualDesktops;

public sealed partial class VirtualDesktopsCommandsProvider : CommandProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly ICommandItem[] _fixed;
    private ICommandItem[]? _cached;
    private string _desktopsSignature = string.Empty;
    private readonly Timer _refreshTimer;

    public VirtualDesktopsCommandsProvider()
    {
        DisplayName = "Virtual Desktops";
        Icon = IconHelpers.FromRelativePath("Assets\\Icon.png");

        _fixed =
        [
            new CommandItem(new DesktopsPage()) { Title = "Virtual desktops", Subtitle = "Switch, rename, close or create desktops" },
            new CommandItem(new WindowsPage()) { Title = "Find window on any desktop", Subtitle = "Jump to any open window, move it or pin it" },
            new CommandItem(new PinWindowsPage()) { Title = "Pin windows to all desktops", Subtitle = "Checklist of open windows: Enter pins or unpins" },
            new CommandItem(new MoveWindowPage()) { Title = "Move window to desktop", Subtitle = "Move the window you were working in to another desktop" },
            new CommandItem(TopLevel("vdh.movenew", "Move window to a new desktop", Glyphs.Add, () =>
            {
                var window = Helper.LastWindow() ?? throw new InvalidOperationException("No window to move");
                Helper.MoveToNewDesktop(window.Hwnd);
            })) { Title = "Move window to a new desktop", Subtitle = "Create a desktop and take the window you were working in there" },
            new CommandItem(TopLevel("vdh.togglepin", "Show window on all desktops", Glyphs.Pin, () =>
            {
                var window = Helper.LastWindow() ?? throw new InvalidOperationException("No window to pin");
                var pinned = Helper.SetPinned(window.Hwnd, !window.Pinned);
                return CommandResult.ShowToast(new ToastArgs { Message = pinned ? $"\"{window.Title}\" is shown on all desktops" : $"\"{window.Title}\" unpinned", Result = CommandResult.Dismiss() });
            })) { Title = "Show window on all desktops (toggle)", Subtitle = "Pin or unpin the window you were working in" },
            new CommandItem(TopLevel("vdh.new", "New desktop", Glyphs.Add, () => { Helper.NewDesktop(); })) { Title = "New desktop", Subtitle = "Create a desktop and switch to it" },
        ];

        // Desktops can be added/renamed at any time; check now and then and refresh the "Switch to" commands
        _refreshTimer = new Timer(_ => RefreshIfDesktopsChanged(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    private static HelperCommand TopLevel(string id, string name, string glyph, Action action) =>
        new(name, glyph, action) { Id = id };

    private static HelperCommand TopLevel(string id, string name, string glyph, Func<ICommandResult> action) =>
        new(name, glyph, action) { Id = id };

    /// <summary>The fixed commands plus one "Switch to desktop" command per desktop, so typing a desktop's name in
    /// the main search jumps there.</summary>
    public override ICommandItem[] TopLevelCommands()
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var items = new List<ICommandItem>(_fixed);
            try
            {
                var status = Helper.Status();
                _desktopsSignature = Signature(status);
                foreach (var desktop in status.Desktops)
                {
                    var index = desktop.Index;
                    items.Add(new CommandItem(TopLevel($"vdh.switch.{index}", "Switch", Glyphs.Desktop, () => Helper.Switch(index)))
                    {
                        Title = $"Switch to {desktop.Name}",
                        Subtitle = $"Virtual desktop {index + 1}",
                    });
                }
            }
            catch (Exception)
            {
                // Helper not running: only the fixed commands (they explain how to start it)
            }

            _cached = [.. items];
            return _cached;
        }
    }

    private void RefreshIfDesktopsChanged()
    {
        try
        {
            var signature = Signature(Helper.Status());
            lock (_gate)
            {
                if (signature == _desktopsSignature)
                {
                    return;
                }

                _cached = null;
            }

            RaiseItemsChanged();
        }
        catch (Exception)
        {
            // helper not running (yet)
        }
    }

    private static string Signature(StatusInfo status) => string.Join("\n", status.Desktops.Select(d => d.Name));

    public override void Dispose()
    {
        _refreshTimer.Dispose();
        base.Dispose();
    }
}
