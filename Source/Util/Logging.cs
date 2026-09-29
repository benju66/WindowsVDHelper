using System;
using System.Collections.Generic;

namespace WindowsVirtualDesktopHelper.Util {
	public class Logging {

		// The log is written from many threads (the monitor threads and the UI thread), so all
		// access is guarded by a lock, and it is capped so that a persistent error condition
		// (which logs every second) can never grow it without bound
		private const int MaxLines = 2000;
		private static readonly object _lock = new object();
		private static readonly List<string> _log = new List<string>();

		public static void WriteLine(string line) {
			try {
				Console.WriteLine(line);
			} catch(Exception) {
				// A WinExe may have no console; logging must never throw
			}
			lock(_lock) {
				_log.Add(line);
				if(_log.Count > MaxLines) _log.RemoveRange(0, _log.Count - MaxLines);
				_writeToFile(line);
			}
		}

		// The log is also written to %APPDATA%\WindowsVirtualDesktopHelper\WindowsVirtualDesktopHelper.log (re-created
		// on each start, capped in size), so problems can be diagnosed after the fact
		private const long MaxFileBytes = 2 * 1024 * 1024;
		private static string _filePath = null;
		private static bool _fileFailed = false;

		private static void _writeToFile(string line) {
			if(_fileFailed) return;
			try {
				if(_filePath == null) {
					var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsVirtualDesktopHelper");
					System.IO.Directory.CreateDirectory(dir);
					_filePath = System.IO.Path.Combine(dir, "WindowsVirtualDesktopHelper.log");
					System.IO.File.WriteAllText(_filePath, "");
				}
				if(new System.IO.FileInfo(_filePath).Length > MaxFileBytes) return;
				System.IO.File.AppendAllText(_filePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
			} catch(Exception) {
				_fileFailed = true; // logging must never break the app
			}
		}

		// Returns a snapshot, so callers can safely enumerate while other threads keep logging
		public static List<string> GetLogHistory() {
			lock(_lock) {
				return new List<string>(_log);
			}
		}

	}
}
