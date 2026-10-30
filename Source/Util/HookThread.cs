using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// A background thread with its own message loop for the low level mouse hooks (<see cref="TrayMouseWheel"/>,
	/// <see cref="TitleBarMenuHook"/>). Windows calls a low level hook on the thread which installed it, for every
	/// mouse event of the whole system, and the mouse waits for the answer. On the UI thread, anything slow there (a
	/// call into a busy explorer.exe, a long menu build) made the whole system's mouse lag, and after a few timeouts
	/// Windows silently removed the hook. This thread does nothing but answer the hooks.
	///
	/// Note: the thread has a plain message loop and creates no window. Creating a window here while the UI thread
	/// waits for us deadlocks (the window creation can need the UI thread to answer messages, e.g. for the IME).
	/// </summary>
	public static class HookThread {

		private static readonly object _lock = new object();
		private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
		private static uint _threadId = 0;

		private const uint WM_RUNQUEUE = 0x8000 + 1; // WM_APP + 1
		private const int InvokeTimeoutMs = 5000;

		// Runs the action on the hook thread and waits for it (installing and removing a hook must happen there).
		// Never waits forever: returns false if the action did not run in time
		public static bool Invoke(Action action) {
			var threadId = _ensureStarted();
			Exception error = null;
			var done = new ManualResetEvent(false); // not disposed: the hook thread may still set it after a timeout
			_queue.Enqueue(() => {
				try { action(); } catch (Exception e) { error = e; } finally { done.Set(); }
			});
			if (!PostThreadMessage(threadId, WM_RUNQUEUE, IntPtr.Zero, IntPtr.Zero)) {
				Logging.WriteLine("HookThread: Error: could not post to the hook thread");
				return false;
			}
			if (!done.WaitOne(InvokeTimeoutMs)) {
				Logging.WriteLine("HookThread: Error: the hook thread did not answer");
				return false;
			}
			if (error != null) throw error;
			return true;
		}

		private static uint _ensureStarted() {
			lock (_lock) {
				if (_threadId != 0) return _threadId;
				var ready = new ManualResetEvent(false);
				var thread = new Thread(() => {
					_threadId = GetCurrentThreadId();
					MSG msg;
					PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE); // creates this thread's message queue
					ready.Set();
					while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) {
						if (msg.hwnd == IntPtr.Zero && msg.message == WM_RUNQUEUE) {
							Action queued;
							while (_queue.TryDequeue(out queued)) queued();
						} else {
							TranslateMessage(ref msg);
							DispatchMessage(ref msg);
						}
					}
				}) { IsBackground = true, Name = "MouseHooks" };
				thread.Start();
				ready.WaitOne();
				return _threadId;
			}
		}

		#region Native

		[StructLayout(LayoutKind.Sequential)]
		private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

		private const uint PM_NOREMOVE = 0;

		[DllImport("user32.dll")]
		private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

		[DllImport("user32.dll")]
		private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

		[DllImport("user32.dll")]
		private static extern bool TranslateMessage(ref MSG msg);

		[DllImport("user32.dll")]
		private static extern IntPtr DispatchMessage(ref MSG msg);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentThreadId();

		#endregion
	}
}
