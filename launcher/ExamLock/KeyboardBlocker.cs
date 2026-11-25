using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ExamLock;

internal sealed class KeyboardBlocker : IDisposable
{
	private struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;

		public uint scanCode;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	private readonly Native.LowLevelKeyboardProc proc;

	private readonly Thread thread;

	private nint hook;

	private uint threadId;

	private volatile bool winDown;

	private volatile bool spaceDown;

	public event Action? LanguageSwitch;

	public KeyboardBlocker()
	{
		KeyboardBlocker keyboardBlocker = this;
		proc = HookProc;
		ManualResetEventSlim ready = new ManualResetEventSlim();
		try
		{
			thread = new Thread(() =>
			{
				keyboardBlocker.threadId = Native.GetCurrentThreadId();
				keyboardBlocker.hook = Native.SetWindowsHookEx(13, keyboardBlocker.proc, Native.GetModuleHandle(null), 0u);
				ready.Set();
				Native.MSG msg;
				while (Native.GetMessage(out msg, IntPtr.Zero, 0u, 0u) > 0)
				{
				}
				Native.UnhookWindowsHookEx(keyboardBlocker.hook);
			})
			{
				IsBackground = true,
				Name = "keyboard-guard"
			};
			thread.Start();
			ready.Wait(3000);
		}
		finally
		{
			if (ready != null)
			{
				((IDisposable)ready).Dispose();
			}
		}
	}

	private nint HookProc(int nCode, nint wParam, nint lParam)
	{
		if (nCode >= 0)
		{
			KBDLLHOOKSTRUCT kBDLLHOOKSTRUCT = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
			bool flag = (kBDLLHOOKSTRUCT.flags & 0x80) != 0;
			uint vkCode = kBDLLHOOKSTRUCT.vkCode;
			if (vkCode - 91 <= 1)
			{
				winDown = !flag;
				if (flag)
				{
					spaceDown = false;
				}
				return 1;
			}
			if (kBDLLHOOKSTRUCT.vkCode == 32 && winDown)
			{
				if (!flag && !spaceDown)
				{
					LanguageSwitch?.Invoke();
				}
				spaceDown = !flag;
				return 1;
			}
			if (ShouldBlock(kBDLLHOOKSTRUCT.vkCode, (kBDLLHOOKSTRUCT.flags & 0x20) != 0))
			{
				return 1;
			}
		}
		return Native.CallNextHookEx(hook, nCode, wParam, lParam);
	}

	private static bool Down(int vk)
	{
		return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static bool ShouldBlock(uint vk, bool alt)
	{
		bool flag = Down(17);
		switch (vk)
		{
		case 44u:
			return true;
		case 9u:
			return alt;
		case 27u:
			return alt | flag;
		case 115u:
			return alt;
		case 32u:
			return alt;
		case 166u:
		case 167u:
		case 168u:
		case 169u:
		case 170u:
		case 171u:
		case 172u:
			return true;
		case 180u:
		case 181u:
		case 182u:
		case 183u:
			return true;
		default:
			return false;
		}
	}

	public void Dispose()
	{
		if (threadId != 0)
		{
			Native.PostThreadMessage(threadId, 18u, 0, 0);
		}
		threadId = 0u;
	}
}
