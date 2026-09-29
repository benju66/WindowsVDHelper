using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CmdPalVirtualDesktops.Pages;

/// <summary>
/// The window list for the fallback below. UpdateQuery runs on every keystroke, so it never asks the helper
/// app directly: it matches against this cache, which refreshes itself in the background when it's a few
/// seconds old and then tells its listener to match again.
/// </summary>
internal static class WindowCache
{
    private static readonly object Gate = new();
    private static List<WindowInfo> _windows = [];
    private static StatusInfo? _status;
    private static DateTime _loaded = DateTime.MinValue;
    private static bool _refreshing;

    public static event Action? Refreshed;

    public static (List<WindowInfo> Windows, StatusInfo? Status) Get()
    {
        lock (Gate)
        {
            if (!_refreshing && DateTime.UtcNow - _loaded > TimeSpan.FromSeconds(3))
            {
                _refreshing = true;
                _ = Task.Run(Refresh);
            }

            return (_windows, _status);
        }
    }

    private static void Refresh()
    {
        try
        {
            var status = Helper.Status();
            var windows = Helper.Windows();
            lock (Gate)
            {
                _status = status;
                _windows = windows;
                _loaded = DateTime.UtcNow;
            }

            Refreshed?.Invoke();
        }
        catch (Exception)
        {
            lock (Gate)
            {
                _loaded = DateTime.UtcNow; // helper not running: try again later, not on every keystroke
            }
        }
        finally
        {
            lock (Gate)
            {
                _refreshing = false;
            }
        }
    }
}

/// <summary>
/// "Go to window" right in Command Palette's main search: type part of a window title or app name
/// (e.g. "outlook", "mckenna") and Enter switches to that window's desktop and focuses it. Shows the best
/// match; the most recently used window wins ties. Hidden for very short queries.
/// </summary>
internal sealed partial class GoToWindowFallback : FallbackCommandItem
{
    public const string FallbackId = "vdh.fallback.window";

    private readonly FocusCommand _command;
    private string _query = string.Empty;

    public GoToWindowFallback()
        : this(new FocusCommand())
    {
    }

    private GoToWindowFallback(FocusCommand command)
        : base(command, "Go to a window on any desktop", FallbackId)
    {
        _command = command;
        Icon = new IconInfo(Glyphs.Window);
        Hide();
        WindowCache.Refreshed += () => Match(_query);
    }

    public override void UpdateQuery(string query)
    {
        _query = query?.Trim() ?? string.Empty;
        Match(_query);
    }

    private void Match(string query)
    {
        try
        {
            if (query.Length < 2)
            {
                Hide();
                return;
            }

            var (windows, status) = WindowCache.Get();
            var best = Best(windows, query);
            if (best is null || status is null)
            {
                Hide();
                return;
            }

            _command.Hwnd = best.Hwnd;
            _command.Name = "Go to window";
            Title = $"Go to {best.Title}";
            Subtitle = $"{best.App}  ·  {Items.Where(status, best)}";
            Icon = Items.WindowIcon(best);
        }
        catch (Exception)
        {
            Hide();
        }
    }

    // All words must match the title or app name; app name and title starts score higher; the window list
    // is in most recently used order, so earlier wins ties
    private static WindowInfo? Best(List<WindowInfo> windows, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        WindowInfo? best = null;
        var bestScore = 0;
        foreach (var w in windows)
        {
            var text = w.Title + " " + w.App;
            if (!words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var score = 1;
            if (w.App.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }

            if (w.Title.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }

            if (score > bestScore)
            {
                best = w;
                bestScore = score;
            }
        }

        return best;
    }

    // A blank title falls back to the command's name, so both are cleared to hide the item
    private void Hide()
    {
        _command.Name = string.Empty;
        Title = string.Empty;
        Subtitle = string.Empty;
    }

    private sealed partial class FocusCommand : InvokableCommand
    {
        public long Hwnd { get; set; }

        public FocusCommand()
        {
            Icon = new IconInfo(Glyphs.Window);
        }

        public override ICommandResult Invoke()
        {
            try
            {
                Helper.Focus(Hwnd);
                return CommandResult.Dismiss();
            }
            catch (Exception ex)
            {
                return CommandResult.ShowToast(new ToastArgs { Message = ex.Message, Result = CommandResult.KeepOpen() });
            }
        }
    }
}
