using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Raises <see cref="Shown"/> the first time a top level window which was created while the watcher runs becomes
	/// visible (windows which already existed, and windows shown again after hiding, are left out). Uses window events
	/// (SetWinEventHook, out of context), delivered through the message loop of the thread which starts it, so start
	/// it on the UI thread. Every window of the system raises these events, so the callback only does cheap checks.
	/// </summary>
	public class NewWindowWatcher : IDisposable {

		public event Action<IntPtr> Shown;

		private OS.WinEventDelegate _proc; // must be kept alive while hooked
		private IntPtr _hook = IntPtr.Zero;
		// Created, not yet shown. Most top level windows are never shown (message windows, ...): they are removed when
		// destroyed, and the set is emptied if it gets too big anyway (a missed window is only not moved)
		private readonly HashSet<IntPtr> _created = new HashSet<IntPtr>();
		private const int MaxCreated = 5000;

		public bool Start() {
			if (_hook != IntPtr.Zero) return true;
			_proc = _onEvent;
			_hook = OS.SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0, OS.WINEVENT_OUTOFCONTEXT | OS.WINEVENT_SKIPOWNPROCESS);
			return _hook != IntPtr.Zero;
		}

		public void Stop() {
			if (_hook != IntPtr.Zero) OS.UnhookWinEvent(_hook);
			_hook = IntPtr.Zero;
			_created.Clear();
		}

		public void Dispose() {
			Stop();
		}

		private void _onEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
			try {
				if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hwnd == IntPtr.Zero) return;
				if (eventType == EVENT_OBJECT_CREATE) {
					if (GetAncestor(hwnd, GA_ROOT) != hwnd) return; // a control inside a window
					if (_created.Count >= MaxCreated) _created.Clear();
					_created.Add(hwnd);
				} else if (eventType == EVENT_OBJECT_DESTROY) {
					_created.Remove(hwnd);
				} else if (eventType == EVENT_OBJECT_SHOW) {
					if (_created.Remove(hwnd)) Shown?.Invoke(hwnd);
				}
			} catch (Exception e) {
				Logging.WriteLine("NewWindowWatcher: Error: " + e.Message);
			}
		}

		#region Native

		private const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002;
		private const int OBJID_WINDOW = 0, CHILDID_SELF = 0;
		private const uint GA_ROOT = 2;

		[DllImport("user32.dll")]
		private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

		#endregion
	}
}
