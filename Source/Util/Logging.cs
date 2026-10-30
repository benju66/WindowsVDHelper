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

		// The log is also written to %APPDATA%\WindowsVirtualDesktopHelper\WindowsVirtualDesktopHelper.log, so problems can be
		// diagnosed after the fact. At each start the log of the previous run is kept as WindowsVirtualDesktopHelper.previous.log
		// (so a restart after a crash doesn't erase what led to it), and a log which reaches the size cap is moved there too.
		// The file stays open (instead of being opened for every line), shared, so it can be read while the app runs.
		private const long MaxFileBytes = 2 * 1024 * 1024;
		private static string _filePath = null;
		private static string _previousFilePath = null;
		private static System.IO.StreamWriter _writer = null;
		private static bool _fileFailed = false;

		private static void _writeToFile(string line) {
			if(_fileFailed) return;
			try {
				if(_filePath == null) {
					var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsVirtualDesktopHelper");
					System.IO.Directory.CreateDirectory(dir);
					_filePath = System.IO.Path.Combine(dir, "WindowsVirtualDesktopHelper.log");
					_previousFilePath = System.IO.Path.Combine(dir, "WindowsVirtualDesktopHelper.previous.log");
					_startNewFile();
				} else if(_writer.BaseStream.Length > MaxFileBytes) {
					_startNewFile();
				}
				_writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line);
			} catch(Exception) {
				_fileFailed = true; // logging must never break the app
			}
		}

		private static void _startNewFile() {
			if(_writer != null) {
				_writer.Dispose();
				_writer = null;
			}
			try {
				if(System.IO.File.Exists(_filePath)) {
					if(System.IO.File.Exists(_previousFilePath)) System.IO.File.Delete(_previousFilePath);
					System.IO.File.Move(_filePath, _previousFilePath);
				}
			} catch(Exception) {
				// e.g. the previous log is open in an editor which locks it: then the log just starts over
			}
			var stream = new System.IO.FileStream(_filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
			_writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
		}

		// Returns a snapshot, so callers can safely enumerate while other threads keep logging
		public static List<string> GetLogHistory() {
			lock(_lock) {
				return new List<string>(_log);
			}
		}

	}
}
