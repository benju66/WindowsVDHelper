using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WindowsVirtualDesktopHelper {

	/// <summary>
	/// Lets other programs control the app (the Command Palette extension, scripts, AutoHotkey, Stream Deck, ...)
	/// through a named pipe. Only the current Windows user can connect, and only locally (the pipe's ACL), and the
	/// pipe name contains the user's SID and the session, so other users (and other sessions of the same user) on the
	/// same machine have their own. Clients are served in parallel, so a slow or misbehaving one never blocks others.
	///
	/// Protocol: the client sends one line of JSON, e.g. {"cmd":"switch","desktop":2}, and gets one line back:
	/// {"ok":true,...} or {"ok":false,"error":"..."}. Desktop numbers are 0-based, window handles are numbers.
	/// Commands: status, windows, lastwindow, switch, focus, move, movenew, pin, pinapp, gather, new, rename,
	/// remove, autopin, action (any action of the Actions API, e.g. "MoveWindowForward").
	/// </summary>
	public static class ControlServer {

		public static string PipeName {
			get { return "WindowsVDHelper-" + WindowsIdentity.GetCurrent().User.Value + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId; }
		}

		// How long a request may wait for the UI thread (where all the work runs) before the client is told the app is busy
		private const int UIThreadTimeoutMs = 5000;
		// How long a client may take to send its request or to read the answer
		private const int ClientTimeoutMs = 3000;

		private static Thread _thread;

		public static void Start() {
			if (_thread != null) return;
			_thread = new Thread(_serve) { IsBackground = true, Name = "ControlServer" };
			_thread.Start();
			Util.Logging.WriteLine("ControlServer: listening on pipe " + PipeName);
			// Lets clients (the Command Palette extension) offer to start the app when it isn't running.
			// Note: not a .config file, as all .config files in this folder are loaded as settings
			try {
				System.IO.File.WriteAllText(System.IO.Path.Combine(Settings.GetConfigDirectory(), "app-path.txt"), System.Windows.Forms.Application.ExecutablePath);
			} catch (Exception e) {
				Util.Logging.WriteLine("ControlServer: could not write app-path.txt: " + e.Message);
			}
		}

		// Pipe squatting: the pipe namespace is shared by all users of the machine, so another account's process could
		// create a pipe with our name first, and our clients would then talk to it. So whenever none of our instances
		// exists, the next one is created with FILE_FLAG_FIRST_PIPE_INSTANCE, which fails if the name is taken: then we
		// don't serve (and retry later). While one of our instances exists, the name is ours. (Clients check the pipe's
		// owner as well, see Send and the Command Palette extension.) The instances are counted under a lock.
		private static readonly object _instancesLock = new object();
		private static int _liveInstances = 0;

		private static void _serve() {
			var security = new PipeSecurity();
			security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
			// Local programs only: the same user connecting from another machine (over the network) is refused
			security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
			var securityDescriptor = security.GetSecurityDescriptorBinaryForm();
			var createFailed = false;
			while (true) {
				NamedPipeServerStream server;
				try {
					server = _createInstance(securityDescriptor);
					createFailed = false;
				} catch (Exception e) {
					// Logged once, not on every retry
					if (!createFailed) Util.Logging.WriteLine("ControlServer: Error: could not create the pipe: " + e.Message);
					createFailed = true;
					Thread.Sleep(5000);
					continue;
				}
				try {
					server.WaitForConnection();
				} catch (Exception e) {
					Util.Logging.WriteLine("ControlServer: Error: " + e.Message);
					_release(server);
					continue;
				}
				// Each client on its own task, while the loop already waits for the next one
				Task.Run(() => _serveClient(server));
			}
		}

		private static NamedPipeServerStream _createInstance(byte[] securityDescriptor) {
			lock (_instancesLock) {
				var first = _liveInstances == 0;
				var descriptor = GCHandle.Alloc(securityDescriptor, GCHandleType.Pinned);
				SafePipeHandle handle;
				try {
					var attributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), lpSecurityDescriptor = descriptor.AddrOfPinnedObject(), bInheritHandle = 0 };
					var openMode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (first ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0);
					handle = CreateNamedPipe(@"\\.\pipe\" + PipeName, openMode, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, PIPE_UNLIMITED_INSTANCES, 64 * 1024, 64 * 1024, 0, ref attributes);
				} finally {
					descriptor.Free();
				}
				if (handle.IsInvalid) {
					var error = Marshal.GetLastWin32Error();
					handle.Dispose();
					if (first && (error == ERROR_ACCESS_DENIED || error == ERROR_PIPE_BUSY)) throw new Exception("the pipe name is already used by another process, the control channel is off until it is free");
					throw new System.ComponentModel.Win32Exception(error);
				}
				_liveInstances++;
				return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
			}
		}

		private static void _release(NamedPipeServerStream server) {
			lock (_instancesLock) {
				try { server.Dispose(); } catch (Exception) { }
				_liveInstances--;
			}
		}

		private static async Task _serveClient(NamedPipeServerStream server) {
			try {
				var reader = new StreamReader(server, new UTF8Encoding(false));
				var readTask = reader.ReadLineAsync();
				if (await Task.WhenAny(readTask, Task.Delay(ClientTimeoutMs)) != readTask) return; // never sent anything
				var response = _handle(await readTask);
				var bytes = new UTF8Encoding(false).GetBytes(response + "\n");
				var writeTask = server.WriteAsync(bytes, 0, bytes.Length);
				if (await Task.WhenAny(writeTask, Task.Delay(ClientTimeoutMs)) != writeTask) return; // never reads the answer
				await writeTask;
			} catch (IOException) {
				// the client went away
			} catch (Exception e) {
				Util.Logging.WriteLine("ControlServer: Error: " + e.Message);
			} finally {
				_release(server);
			}
		}

		#region Native

		[StructLayout(LayoutKind.Sequential)]
		private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

		private const uint PIPE_ACCESS_DUPLEX = 0x00000003;
		private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
		private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
		private const uint PIPE_TYPE_BYTE = 0x00000000, PIPE_READMODE_BYTE = 0x00000000, PIPE_WAIT = 0x00000000;
		private const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
		private const uint PIPE_UNLIMITED_INSTANCES = 255;
		private const int ERROR_ACCESS_DENIED = 5, ERROR_PIPE_BUSY = 231;

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SECURITY_ATTRIBUTES securityAttributes);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr handle);

		#endregion

		private static string _handle(string line) {
			var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
			try {
				if (string.IsNullOrWhiteSpace(line)) throw new Exception("empty request");
				var request = json.Deserialize<Dictionary<string, object>>(line);
				// Everything runs on the UI thread, like the tray menu and hotkeys. Posted rather than Invoke'd, so a
				// busy UI thread makes this request fail after a while instead of hanging the client forever
				object result = null;
				Exception error = null;
				var done = new ManualResetEventSlim(false); // not disposed: the UI thread may still set it after a timeout
				App.Instance.AppForm.BeginInvoke((Action)(() => {
					try { result = _run(request); } catch (Exception e) { error = e; } finally { done.Set(); }
				}));
				if (!done.Wait(UIThreadTimeoutMs)) throw new Exception("the app is busy, try again");
				if (error != null) throw error;
				var response = new Dictionary<string, object> { { "ok", true } };
				var resultDict = result as Dictionary<string, object>;
				if (resultDict != null) foreach (var kvp in resultDict) response[kvp.Key] = kvp.Value;
				return json.Serialize(response);
			} catch (Exception e) {
				return json.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", e.Message } });
			}
		}

		private static Dictionary<string, object> _run(Dictionary<string, object> request) {
			var app = App.Instance;
			var ext = app.VDAPIExtended;
			var cmd = (_str(request, "cmd") ?? "").ToLowerInvariant();
			switch (cmd) {
				case "status":
					return _status();
				case "windows":
					return new Dictionary<string, object> { { "windows", _windows() } };
				case "lastwindow": {
						var hwnd = app.GetActiveUserWindow(false);
						return new Dictionary<string, object> { { "window", hwnd == IntPtr.Zero ? null : _windowInfo(hwnd) } };
					}
				case "switch":
					if (!app.SwitchToDesktop(_int(request, "desktop"))) throw new Exception("could not switch to that desktop");
					return null;
				case "focus":
					app.FocusWindow(_hwnd(request));
					return null;
				case "move":
					var moved = request.ContainsKey("follow")
						? app.MoveWindowToDesktop(_hwnd(request), _int(request, "desktop"), _bool(request, "follow"))
						: app.MoveWindowToDesktop(_hwnd(request), _int(request, "desktop"));
					if (!moved) throw new Exception("the window could not be moved to that desktop");
					return null;
				case "movenew":
					app.MoveWindowToNewDesktop(_hwnd(request));
					return null;
				case "pin": {
						_requireExtended(ext);
						var hwnd = _hwnd(request);
						var pinned = request.ContainsKey("pinned") ? _bool(request, "pinned") : !ext.IsWindowPinned(hwnd);
						ext.SetWindowPinned(hwnd, pinned);
						return new Dictionary<string, object> { { "pinned", pinned } };
					}
				case "pinapp": {
						_requireExtended(ext);
						var hwnd = _hwnd(request);
						var pinned = request.ContainsKey("pinned") ? _bool(request, "pinned") : !ext.IsAppPinned(hwnd);
						ext.SetAppPinned(hwnd, pinned);
						return new Dictionary<string, object> { { "pinned", pinned } };
					}
				case "gather":
					app.GatherAppWindows(_hwnd(request));
					return null;
				case "new": {
						_requireExtended(ext);
						var index = ext.CreateDesktop();
						if (!request.ContainsKey("switch") || _bool(request, "switch")) app.SwitchToDesktop(index);
						return new Dictionary<string, object> { { "desktop", index } };
					}
				case "rename":
					_requireExtended(ext);
					ext.RenameDesktop(_int(request, "desktop"), _str(request, "name") ?? "");
					return null;
				case "remove":
					_requireExtended(ext);
					ext.RemoveDesktop(_int(request, "desktop"));
					return null;
				case "autopin":
					app.SetAutoPinnedProcess(_str(request, "process"), _bool(request, "on"));
					return null;
				case "action": {
						var error = app.RunAction(_str(request, "name") ?? "");
						if (error != null) throw new Exception(error);
						return null;
					}
				default:
					throw new Exception("unknown command \"" + cmd + "\"");
			}
		}

		private static Dictionary<string, object> _status() {
			var app = App.Instance;
			var ext = app.VDAPIExtended;
			List<string> names = null;
			try { if (ext != null) names = ext.GetDesktopNames(); } catch (Exception) { }
			var count = app.GetVDDisplayCount();
			if (names == null) names = Enumerable.Range(1, count).Select(i => "Desktop " + i).ToList();
			return new Dictionary<string, object> {
				{ "current", (int)app.GetVDDisplayNumber(true) },
				{ "count", count },
				{ "desktops", names.Select((name, i) => new Dictionary<string, object> { { "index", i }, { "name", name } }).ToList() },
				{ "extended", ext != null },
			};
		}

		private static List<Dictionary<string, object>> _windows() {
			var list = new List<Dictionary<string, object>>();
			foreach (var hwnd in Util.OS.GetAppWindows()) {
				var info = _windowInfo(hwnd);
				if (info != null) list.Add(info);
			}
			return list;
		}

		private static Dictionary<string, object> _windowInfo(IntPtr hwnd) {
			var app = App.Instance;
			var ext = app.VDAPIExtended;
			if (!app.IsUserWindow(hwnd)) return null;
			bool pinned = false, appPinned = false;
			int desktop = -1;
			if (ext != null) {
				try { pinned = ext.IsWindowPinned(hwnd); } catch (Exception) { return null; } // not a normal app window
				try { appPinned = ext.IsAppPinned(hwnd); } catch (Exception) { }
				desktop = pinned || appPinned ? -1 : ext.GetWindowDesktop(hwnd);
			}
			var process = Util.OS.GetWindowProcessName(hwnd);
			return new Dictionary<string, object> {
				{ "hwnd", hwnd.ToInt64() },
				{ "title", Util.OS.GetHandleWndName(hwnd) },
				{ "app", Util.OS.GetWindowAppName(hwnd) },
				{ "process", process },
				{ "desktop", desktop },
				{ "pinned", pinned },
				{ "appPinned", appPinned },
				{ "autoPinned", app.IsAutoPinned(process) },
				{ "icon", Util.WindowIcons.GetIconPath(hwnd) }, // PNG file path, or null
			};
		}

		private static void _requireExtended(VirtualDesktopAPI.IVirtualDesktopManagerExtended ext) {
			if (ext == null) throw new Exception("not supported on this Windows version");
		}

		private static string _str(Dictionary<string, object> request, string key) {
			object value;
			return request.TryGetValue(key, out value) && value != null ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;
		}

		private static int _int(Dictionary<string, object> request, string key) {
			object value;
			if (!request.TryGetValue(key, out value) || value == null) throw new Exception("missing \"" + key + "\"");
			return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
		}

		private static bool _bool(Dictionary<string, object> request, string key) {
			object value;
			if (!request.TryGetValue(key, out value) || value == null) throw new Exception("missing \"" + key + "\"");
			return value is bool ? (bool)value : bool.Parse(value.ToString());
		}

		private static IntPtr _hwnd(Dictionary<string, object> request) {
			object value;
			if (!request.TryGetValue("hwnd", out value) || value == null) throw new Exception("missing \"hwnd\"");
			var hwnd = new IntPtr(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
			if (!Util.OS.IsWindow(hwnd)) throw new Exception("that window no longer exists");
			return hwnd;
		}

		#region Client (used by the command line: WindowsVirtualDesktopHelper.exe --action X)

		// Sends a request to the running app, returns the response line (or throws if the app isn't running)
		public static string Send(Dictionary<string, object> request, int timeoutMs = 3000) {
			var json = new JavaScriptSerializer();
			// Identification: the server may see who we are, but can never act as us (if the pipe were someone else's)
			using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification)) {
				client.Connect(timeoutMs);
				_verifyServerIsCurrentUser(client);
				var bytes = new UTF8Encoding(false).GetBytes(json.Serialize(request) + "\n");
				client.Write(bytes, 0, bytes.Length);
				client.Flush();
				// The answer can take up to the server's UI thread timeout: wait a bit longer, but never forever
				var readTask = new StreamReader(client, new UTF8Encoding(false)).ReadLineAsync();
				if (!readTask.Wait(UIThreadTimeoutMs + timeoutMs)) throw new TimeoutException("the app did not answer");
				return readTask.Result;
			}
		}

		// Makes sure the process serving the pipe runs as the current user (see the note on pipe squatting above)
		private static void _verifyServerIsCurrentUser(NamedPipeClientStream client) {
			uint serverProcessId;
			if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out serverProcessId)) throw new UnauthorizedAccessException("could not check the pipe's server");
			var process = OpenProcess(0x1000, false, serverProcessId); // PROCESS_QUERY_LIMITED_INFORMATION
			if (process == IntPtr.Zero) throw new UnauthorizedAccessException("the pipe is served by another user's process");
			try {
				IntPtr token;
				if (!OpenProcessToken(process, 0x0008, out token)) throw new UnauthorizedAccessException("the pipe is served by another user's process"); // TOKEN_QUERY
				try {
					using (var identity = new WindowsIdentity(token)) {
						if (identity.User != WindowsIdentity.GetCurrent().User) throw new UnauthorizedAccessException("the pipe is served by another user's process");
					}
				} finally {
					CloseHandle(token);
				}
			} finally {
				CloseHandle(process);
			}
		}

		// Handles the command line forms "--action <name>", "--switch <number>" and "--send <json>".
		// Returns null if the arguments are not a remote command, otherwise the process exit code.
		public static int? RunCommandLine(string[] args) {
			if (args.Length < 2) return null;
			if (args[0] != "--action" && args[0] != "--switch" && args[0] != "--send") return null;
			Dictionary<string, object> request;
			try {
				if (args[0] == "--action") request = new Dictionary<string, object> { { "cmd", "action" }, { "name", args[1] } };
				else if (args[0] == "--switch") request = new Dictionary<string, object> { { "cmd", "switch" }, { "desktop", int.Parse(args[1]) - 1 } };
				else request = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(args[1]);
			} catch (Exception e) {
				try { Console.Error.WriteLine("Invalid arguments: " + e.Message); } catch (Exception) { }
				return 3;
			}
			try {
				var response = Send(request);
				try { Console.WriteLine(response); } catch (Exception) { }
				return response != null && response.Contains("\"ok\":true") ? 0 : 1;
			} catch (Exception e) {
				try { Console.Error.WriteLine("WindowsVirtualDesktopHelper is not running: " + e.Message); } catch (Exception) { }
				return 2;
			}
		}

		#endregion
	}
}
