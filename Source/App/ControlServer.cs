using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WindowsVirtualDesktopHelper {

	/// <summary>
	/// Lets other programs control the app (the Command Palette extension, scripts, AutoHotkey, Stream Deck, ...)
	/// through a named pipe. Only the current Windows user can connect (the pipe's ACL), and the pipe name contains
	/// the user's SID, so other users on the same machine have their own.
	///
	/// Protocol: the client sends one line of JSON, e.g. {"cmd":"switch","desktop":2}, and gets one line back:
	/// {"ok":true,...} or {"ok":false,"error":"..."}. Desktop numbers are 0-based, window handles are numbers.
	/// Commands: status, windows, lastwindow, switch, focus, move, movenew, pin, pinapp, gather, new, rename,
	/// remove, autopin, action (any action of the Actions API, e.g. "MoveWindowForward").
	/// </summary>
	public static class ControlServer {

		public static string PipeName {
			get { return "WindowsVDHelper-" + WindowsIdentity.GetCurrent().User.Value; }
		}

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

		private static void _serve() {
			var security = new PipeSecurity();
			security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
			while (true) {
				try {
					using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, security)) {
						server.WaitForConnection();
						var reader = new StreamReader(server, new UTF8Encoding(false));
						var readTask = reader.ReadLineAsync();
						if (!readTask.Wait(3000)) continue; // a client which never sends anything must not block others
						var response = _handle(readTask.Result);
						var bytes = new UTF8Encoding(false).GetBytes(response + "\n");
						server.Write(bytes, 0, bytes.Length);
						server.Flush();
						try { server.WaitForPipeDrain(); } catch (IOException) { }
					}
				} catch (Exception e) {
					Util.Logging.WriteLine("ControlServer: Error: " + e.Message);
					Thread.Sleep(500);
				}
			}
		}

		private static string _handle(string line) {
			var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
			try {
				if (string.IsNullOrWhiteSpace(line)) throw new Exception("empty request");
				var request = json.Deserialize<Dictionary<string, object>>(line);
				// Everything runs on the UI thread, like the tray menu and hotkeys
				object result = null;
				Exception error = null;
				App.Instance.AppForm.Invoke((Action)(() => {
					try { result = _run(request); } catch (Exception e) { error = e; }
				}));
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
					app.SwitchToDesktop(_int(request, "desktop"));
					return null;
				case "focus":
					app.FocusWindow(_hwnd(request));
					return null;
				case "move":
					if (request.ContainsKey("follow")) app.MoveWindowToDesktop(_hwnd(request), _int(request, "desktop"), _bool(request, "follow"));
					else app.MoveWindowToDesktop(_hwnd(request), _int(request, "desktop"));
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
			using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut)) {
				client.Connect(timeoutMs);
				var bytes = new UTF8Encoding(false).GetBytes(json.Serialize(request) + "\n");
				client.Write(bytes, 0, bytes.Length);
				client.Flush();
				return new StreamReader(client, new UTF8Encoding(false)).ReadLine();
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
