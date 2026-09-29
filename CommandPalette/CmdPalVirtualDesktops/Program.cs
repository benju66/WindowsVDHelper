using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer.CsWinRT;

namespace CmdPalVirtualDesktops;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            var server = new global::Shmuelie.WinRTServer.ComServer();
            var disposed = new ManualResetEvent(false);

            // One extension instance, returned every time Command Palette asks for it; the process exits when
            // Command Palette disposes it
            var extension = new VirtualDesktopsExtension(disposed);
            server.RegisterClass<VirtualDesktopsExtension, IExtension>(() => extension);
            server.Start();
            disposed.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("This is a Command Palette extension, it is started by Command Palette.");
        }
    }
}
