using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ExamLock;

internal static class KeyboardLayouts
{
	private const uint WM_INPUTLANGCHANGEREQUEST = 80u;

	public static int Count => All().Length;

	public static nint[] All()
	{
		int keyboardLayoutList = Native.GetKeyboardLayoutList(0, null);
		nint[] array = new nint[Math.Max(keyboardLayoutList, 0)];
		if (keyboardLayoutList > 0)
		{
			Native.GetKeyboardLayoutList(keyboardLayoutList, array);
		}
		return array;
	}

	private static nint FocusWindow(nint top)
	{
		uint windowThreadProcessId = Native.GetWindowThreadProcessId(top, out var _);
		Native.GUITHREADINFO info = new Native.GUITHREADINFO
		{
			cbSize = Marshal.SizeOf<Native.GUITHREADINFO>()
		};
		if (!Native.GetGUIThreadInfo(windowThreadProcessId, ref info) || info.hwndFocus == IntPtr.Zero)
		{
			return top;
		}
		return info.hwndFocus;
	}

	public static nint Current(nint top)
	{
		uint pid;
		return Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(FocusWindow(top), out pid));
	}

	public static void Next(nint top)
	{
		nint[] array = All();
		if (array.Length >= 2)
		{
			nint value = Current(top);
			int num = Array.IndexOf(array, value);
			nint l = array[(num + 1) % array.Length];
			nint num2 = FocusWindow(top);
			Native.PostMessage(num2, 80u, 0, l);
			if (num2 != top)
			{
				Native.PostMessage(top, 80u, 0, l);
			}
		}
	}

	public static string Label(nint hkl)
	{
		int culture = (int)((long)hkl & 0xFFFFL);
		try
		{
			return new CultureInfo(culture).TwoLetterISOLanguageName.ToUpperInvariant();
		}
		catch
		{
			return "??";
		}
	}
}
