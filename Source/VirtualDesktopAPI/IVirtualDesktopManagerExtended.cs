using System;
using System.Collections.Generic;

namespace WindowsVirtualDesktopHelper.VirtualDesktopAPI {
	/// <summary>
	/// Optional desktop and window management features. Only implemented by the implementations for
	/// Windows versions where these have been verified; the app hides the related menu items and
	/// hotkeys when the loaded implementation does not support this interface.
	/// </summary>
	public interface IVirtualDesktopManagerExtended {
		// Display names of all desktops, in order ("Desktop n" for unnamed desktops)
		List<string> GetDesktopNames();

		// Creates a new desktop at the end and returns its index
		int CreateDesktop();

		// Renames a desktop, an empty name resets it to the default "Desktop n"
		void RenameDesktop(int index, string name);

		// Removes a desktop, its windows move to the adjacent desktop
		void RemoveDesktop(int index);

		// Moves a (top level) window of any process to the given desktop
		void MoveWindowToDesktop(IntPtr hWnd, int index);

		// Index of the desktop the window is on, or -1 if unknown/pinned
		int GetWindowDesktop(IntPtr hWnd);

		bool IsWindowPinned(IntPtr hWnd);

		void SetWindowPinned(IntPtr hWnd, bool pinned);
	}
}
