using System.Runtime.InteropServices;
using System.Text;

namespace ExamLock;

internal static class Native
{
	public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	public struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int ptX;

		public int ptY;

		public uint lPrivate;
	}

	public struct GUITHREADINFO
	{
		public int cbSize;

		public uint flags;

		public nint hwndActive;

		public nint hwndFocus;

		public nint hwndCapture;

		public nint hwndMenuOwner;

		public nint hwndMoveSize;

		public nint hwndCaret;

		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	[DllImport("user32.dll", SetLastError = true)]
	public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint threadId);

	[DllImport("user32.dll")]
	public static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern nint GetModuleHandle(string? name);

	[DllImport("kernel32.dll")]
	public static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);

	[DllImport("user32.dll")]
	public static extern bool PostThreadMessage(uint threadId, uint msg, nint w, nint l);

	[DllImport("user32.dll")]
	public static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

	[DllImport("user32.dll")]
	public static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);

	[DllImport("user32.dll")]
	public static extern void SwitchToThisWindow(nint hWnd, bool altTab);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(nint hWnd, int cmd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint hWnd, StringBuilder sb, int max);

	[DllImport("user32.dll")]
	public static extern int GetKeyboardLayoutList(int n, nint[]? list);

	[DllImport("user32.dll")]
	public static extern nint GetKeyboardLayout(uint threadId);

	[DllImport("user32.dll")]
	public static extern bool PostMessage(nint hWnd, uint msg, nint w, nint l);

	[DllImport("user32.dll")]
	public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

	public static string ClassName(nint h)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassName(h, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	public static void HideFromCapture(nint hwnd)
	{
		if (!SetWindowDisplayAffinity(hwnd, 17u))
		{
			SetWindowDisplayAffinity(hwnd, 1u);
		}
	}
}
