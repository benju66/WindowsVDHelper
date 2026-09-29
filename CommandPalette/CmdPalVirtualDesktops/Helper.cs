using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CmdPalVirtualDesktops;

internal sealed record DesktopInfo(int Index, string Name);

internal sealed record StatusInfo(int Current, int Count, IReadOnlyList<DesktopInfo> Desktops, bool Extended);

internal sealed record WindowInfo(long Hwnd, string Title, string App, string Process, int Desktop, bool Pinned, bool AppPinned, bool AutoPinned)
{
    public bool OnAllDesktops => Pinned || AppPinned;
}

internal sealed class HelperNotRunningException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Talks to the running Windows Virtual Desktop Helper over its per-user named pipe (see ControlServer.cs in the
/// app): one JSON line per request and response. The helper does all the virtual desktop work, so this extension
/// needs none of the undocumented Windows interfaces itself.
/// </summary>
internal static class Helper
{
    private static readonly string PipeName = "WindowsVDHelper-" + WindowsIdentity.GetCurrent().User!.Value;

    public static StatusInfo Status()
    {
        using var doc = Send(("cmd", "status"));
        var root = doc.RootElement;
        var desktops = root.GetProperty("desktops").EnumerateArray()
            .Select(d => new DesktopInfo(d.GetProperty("index").GetInt32(), d.GetProperty("name").GetString() ?? string.Empty))
            .ToList();
        return new StatusInfo(root.GetProperty("current").GetInt32(), root.GetProperty("count").GetInt32(), desktops, root.GetProperty("extended").GetBoolean());
    }

    public static List<WindowInfo> Windows()
    {
        using var doc = Send(("cmd", "windows"));
        return doc.RootElement.GetProperty("windows").EnumerateArray().Select(ReadWindow).ToList();
    }

    // The window the user was working in before opening Command Palette
    public static WindowInfo? LastWindow()
    {
        using var doc = Send(("cmd", "lastwindow"));
        var window = doc.RootElement.GetProperty("window");
        return window.ValueKind == JsonValueKind.Object ? ReadWindow(window) : null;
    }

    public static void Switch(int desktop) => Run(("cmd", "switch"), ("desktop", desktop));

    public static void Focus(long hwnd) => Run(("cmd", "focus"), ("hwnd", hwnd));

    public static void Move(long hwnd, int desktop) => Run(("cmd", "move"), ("hwnd", hwnd), ("desktop", desktop));

    public static void MoveToNewDesktop(long hwnd) => Run(("cmd", "movenew"), ("hwnd", hwnd));

    public static bool SetPinned(long hwnd, bool pinned)
    {
        using var doc = Send(("cmd", "pin"), ("hwnd", hwnd), ("pinned", pinned));
        return doc.RootElement.GetProperty("pinned").GetBoolean();
    }

    public static bool SetAppPinned(long hwnd, bool pinned)
    {
        using var doc = Send(("cmd", "pinapp"), ("hwnd", hwnd), ("pinned", pinned));
        return doc.RootElement.GetProperty("pinned").GetBoolean();
    }

    public static void SetAutoPinned(string process, bool on) => Run(("cmd", "autopin"), ("process", process), ("on", on));

    public static void Gather(long hwnd) => Run(("cmd", "gather"), ("hwnd", hwnd));

    public static int NewDesktop(bool switchTo = true)
    {
        using var doc = Send(("cmd", "new"), ("switch", switchTo));
        return doc.RootElement.GetProperty("desktop").GetInt32();
    }

    public static void Rename(int desktop, string name) => Run(("cmd", "rename"), ("desktop", desktop), ("name", name));

    public static void Remove(int desktop) => Run(("cmd", "remove"), ("desktop", desktop));

    // Where the helper app is installed (it writes this file when it starts), to offer starting it
    public static string? AppPath()
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsVirtualDesktopHelper", "app-path.txt");
            var path = File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            return path is not null && File.Exists(path) ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static WindowInfo ReadWindow(JsonElement w) => new(
        w.GetProperty("hwnd").GetInt64(),
        w.GetProperty("title").GetString() ?? string.Empty,
        w.GetProperty("app").GetString() ?? string.Empty,
        w.GetProperty("process").GetString() ?? string.Empty,
        w.GetProperty("desktop").GetInt32(),
        w.GetProperty("pinned").GetBoolean(),
        w.GetProperty("appPinned").GetBoolean(),
        w.GetProperty("autoPinned").GetBoolean());

    private static void Run(params (string Key, object Value)[] request)
    {
        using var _ = Send(request);
    }

    private static JsonDocument Send(params (string Key, object Value)[] request)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in request)
            {
                switch (value)
                {
                    case string s: writer.WriteString(key, s); break;
                    case int i: writer.WriteNumber(key, i); break;
                    case long l: writer.WriteNumber(key, l); break;
                    case bool b: writer.WriteBoolean(key, b); break;
                    default: throw new ArgumentException($"unsupported value for {key}");
                }
            }

            writer.WriteEndObject();
        }

        string? line;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(1500);
            buffer.WriteByte((byte)'\n');
            pipe.Write(buffer.ToArray());
            pipe.Flush();
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            line = reader.ReadLine();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            throw new HelperNotRunningException("Windows Virtual Desktop Helper isn't running", ex);
        }

        if (string.IsNullOrEmpty(line))
        {
            throw new HelperNotRunningException("Windows Virtual Desktop Helper didn't answer");
        }

        var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.GetProperty("ok").GetBoolean())
        {
            var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
            doc.Dispose();
            throw new InvalidOperationException(error);
        }

        return doc;
    }
}
