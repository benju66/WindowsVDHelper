using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace CmdPalVirtualDesktops;

[Guid("a89ba1e4-8287-4326-ba89-b274b074e8c1")]
public sealed partial class VirtualDesktopsExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly VirtualDesktopsCommandsProvider _provider = new();

    public VirtualDesktopsExtension(ManualResetEvent disposed)
    {
        _disposed = disposed;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        _provider.Dispose();
        _disposed.Set();
    }
}
