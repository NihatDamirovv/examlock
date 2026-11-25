using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ExamLock;

internal static class ProcessGuard
{
	private static readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "arc", "thorium",
		"chromium", "discord", "telegram", "whatsapp", "whatsapp.root", "skype", "teams", "ms-teams", "zoom", "slack",
		"viber", "signal", "messenger", "anydesk", "teamviewer", "teamviewer_service", "rustdesk", "parsec", "mstsc", "remoting_desktop",
		"ammyy_admin", "supremo", "ultraviewer_desktop", "obs64", "obs32", "obs", "sharex", "lightshot", "snippingtool", "screenclippinghost",
		"screensketch", "bandicam", "camtasia", "flashbackrecorder", "chatgpt", "claude", "copilot", "perplexity", "gemini", "notepad",
		"notepad++", "wordpad", "winword", "excel", "powerpnt", "onenote", "code", "sublime_text", "obsidian", "notion",
		"cmd", "powershell", "pwsh", "windowsterminal", "wt", "taskmgr", "processhacker", "systeminformer", "procexp", "procexp64",
		"cheatengine-x86_64", "x64dbg", "x32dbg", "ollydbg", "dnspy", "fiddler", "wireshark", "charles", "httpdebuggerui", "virtualbox",
		"virtualboxvm", "vmware", "vmplayer", "vmconnect"
	};

	private static readonly HashSet<int> alreadyKilled = new HashSet<int>();

	private static IEnumerable<Process> Candidates()
	{
		int me = Environment.ProcessId;
		int session;
		using (Process process = Process.GetCurrentProcess())
		{
			session = process.SessionId;
		}
		Process[] processes = Process.GetProcesses();
		foreach (Process process2 in processes)
		{
			bool flag;
			try
			{
				flag = process2.Id != me && process2.SessionId == session && Blocked.Contains(process2.ProcessName);
			}
			catch
			{
				flag = false;
			}
			if (flag)
			{
				yield return process2;
			}
			else
			{
				process2.Dispose();
			}
		}
	}

	public static List<string> FindRunning()
	{
		SortedSet<string> sortedSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Process item in Candidates())
		{
			using (item)
			{
				sortedSet.Add(item.ProcessName);
			}
		}
		return sortedSet.ToList();
	}

	public static List<string> KillAll()
	{
		List<string> list = new List<string>();
		lock (alreadyKilled)
		{
			foreach (Process item in Candidates())
			{
				using (item)
				{
					try
					{
						string processName = item.ProcessName;
						item.Kill();
						if (alreadyKilled.Add(item.Id))
						{
							list.Add(processName);
						}
					}
					catch
					{
					}
				}
			}
			return list;
		}
	}
}
