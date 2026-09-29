using System;
using System.Runtime.InteropServices;

namespace WindowsVirtualDesktopHelper.VirtualDesktopAPI {

	/// <summary>
	/// Subscribes to the shell's virtual desktop notifications (desktop switched/created/destroyed/renamed/moved),
	/// so the app can react instantly instead of polling. The interfaces are undocumented and differ per Windows
	/// build; the definitions here are the Windows 11 24H2/25H2 ones (as used by Ciantic/VirtualDesktopAccessor).
	///
	/// Robustness notes:
	/// - All callback parameters are declared as IntPtr and never dereferenced, and every callback just raises
	///   <see cref="Changed"/>; the app then re-reads the state through the regular API. So even if a future build
	///   reorders the callbacks, nothing breaks - we only care that "something changed".
	/// - The vtable has spare slots, so a build that adds callbacks can never call past the end of our vtable.
	/// - Register must be called on the UI (STA) thread: callbacks then arrive via the message loop, on that thread.
	///   Handlers must not call back into the shell synchronously (the shell is blocked in the call), so
	///   <see cref="Changed"/> handlers should post (BeginInvoke) their work.
	/// </summary>
	public class Notifications : IDisposable {

		public event EventHandler Changed;

		public bool IsRegistered { get { return _cookie != 0; } }

		private static readonly Guid CLSID_ImmersiveShell = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
		private static readonly Guid CLSID_VirtualNotificationService = new Guid("A501FDEC-4A09-464C-AE4E-1B9C21B84918");

		private object _service;
		private Listener _listener;
		private uint _cookie;

		public static bool IsSupportedOnThisBuild(int build) {
			return build >= 26100 && build < 27000;
		}

		public void Register() {
			Unregister();
			var shell = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ImmersiveShell));
			try {
				var service = CLSID_VirtualNotificationService;
				var iid = typeof(IVirtualDesktopNotificationService).GUID;
				var obj = shell.QueryService(ref service, ref iid);
				var notificationService = (IVirtualDesktopNotificationService)obj;
				_listener = new Listener(this);
				uint cookie;
				int hr = notificationService.Register(_listener, out cookie);
				if(hr != 0) throw new COMException("IVirtualDesktopNotificationService.Register failed", hr);
				_service = notificationService;
				_cookie = cookie;
			} finally {
				Marshal.ReleaseComObject(shell);
			}
		}

		public void Unregister() {
			var service = _service as IVirtualDesktopNotificationService;
			if(service != null && _cookie != 0) {
				try {
					service.Unregister(_cookie);
				} catch(Exception) {
					// The shell may already be gone (explorer restart), then the registration is gone too
				}
			}
			if(_service != null) {
				try { Marshal.ReleaseComObject(_service); } catch(Exception) { }
			}
			_service = null;
			_cookie = 0;
			_listener = null;
		}

		public void Dispose() {
			Unregister();
		}

		private void RaiseChanged() {
			try {
				Changed?.Invoke(this, EventArgs.Empty);
			} catch(Exception e) {
				// Never let an exception propagate back into the shell
				Util.Logging.WriteLine("Notifications: Error in change handler: " + e.Message);
			}
		}

		#region COM Interfaces (Windows 11 24H2 / 25H2)

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
		private interface IServiceProvider10 {
			[return: MarshalAs(UnmanagedType.IUnknown)]
			object QueryService(ref Guid service, ref Guid riid);
		}

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("0CD45E71-D927-4F15-8B0A-8FEF525337BF")]
		private interface IVirtualDesktopNotificationService {
			[PreserveSig]
			int Register(IVirtualDesktopNotification notification, out uint cookie);
			[PreserveSig]
			int Unregister(uint cookie);
		}

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("B9E5E94D-233E-49AB-AF5C-2B4541C3AADE")]
		public interface IVirtualDesktopNotification {
			[PreserveSig] int VirtualDesktopCreated(IntPtr desktop);
			[PreserveSig] int VirtualDesktopDestroyBegin(IntPtr desktopDestroyed, IntPtr desktopFallback);
			[PreserveSig] int VirtualDesktopDestroyFailed(IntPtr desktopDestroyed, IntPtr desktopFallback);
			[PreserveSig] int VirtualDesktopDestroyed(IntPtr desktopDestroyed, IntPtr desktopFallback);
			[PreserveSig] int VirtualDesktopMoved(IntPtr desktop, long oldIndex, long newIndex);
			[PreserveSig] int VirtualDesktopNameChanged(IntPtr desktop, IntPtr name);
			[PreserveSig] int ViewVirtualDesktopChanged(IntPtr view);
			[PreserveSig] int CurrentVirtualDesktopChanged(IntPtr desktopOld, IntPtr desktopNew);
			[PreserveSig] int VirtualDesktopWallpaperChanged(IntPtr desktop, IntPtr name);
			[PreserveSig] int VirtualDesktopSwitched(IntPtr desktop);
			[PreserveSig] int RemoteVirtualDesktopConnected(IntPtr desktop);
			// Spare slots in case a future build appends callbacks
			[PreserveSig] int Spare1(IntPtr a, IntPtr b);
			[PreserveSig] int Spare2(IntPtr a, IntPtr b);
			[PreserveSig] int Spare3(IntPtr a, IntPtr b);
			[PreserveSig] int Spare4(IntPtr a, IntPtr b);
		}

		[ComVisible(true)]
		[ClassInterface(ClassInterfaceType.None)]
		public sealed class Listener : IVirtualDesktopNotification {
			private readonly Notifications _owner;
			internal Listener(Notifications owner) { _owner = owner; }
			public int VirtualDesktopCreated(IntPtr desktop) { _owner.RaiseChanged(); return 0; }
			public int VirtualDesktopDestroyBegin(IntPtr desktopDestroyed, IntPtr desktopFallback) { return 0; }
			public int VirtualDesktopDestroyFailed(IntPtr desktopDestroyed, IntPtr desktopFallback) { _owner.RaiseChanged(); return 0; }
			public int VirtualDesktopDestroyed(IntPtr desktopDestroyed, IntPtr desktopFallback) { _owner.RaiseChanged(); return 0; }
			public int VirtualDesktopMoved(IntPtr desktop, long oldIndex, long newIndex) { _owner.RaiseChanged(); return 0; }
			public int VirtualDesktopNameChanged(IntPtr desktop, IntPtr name) { _owner.RaiseChanged(); return 0; }
			public int ViewVirtualDesktopChanged(IntPtr view) { return 0; } // a window moved between desktops, not relevant
			public int CurrentVirtualDesktopChanged(IntPtr desktopOld, IntPtr desktopNew) { _owner.RaiseChanged(); return 0; }
			public int VirtualDesktopWallpaperChanged(IntPtr desktop, IntPtr name) { return 0; }
			public int VirtualDesktopSwitched(IntPtr desktop) { _owner.RaiseChanged(); return 0; }
			public int RemoteVirtualDesktopConnected(IntPtr desktop) { _owner.RaiseChanged(); return 0; }
			public int Spare1(IntPtr a, IntPtr b) { return 0; }
			public int Spare2(IntPtr a, IntPtr b) { return 0; }
			public int Spare3(IntPtr a, IntPtr b) { return 0; }
			public int Spare4(IntPtr a, IntPtr b) { return 0; }
		}

		#endregion
	}
}
