using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExamLock;

internal static class SystemCheck
{
	public static bool IsRemoteSession()
	{
		return SystemInformation.TerminalServerSession;
	}

	public static bool IsVirtualMachine(out string what)
	{
		string[] source = new string[11]
		{
			"vmware", "virtualbox", "vbox", "innotek", "qemu", "kvm", "xen", "parallels", "virtual machine", "bochs",
			"bhyve"
		};
		string[] array;
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS");
			if (registryKey != null)
			{
				array = new string[5] { "SystemManufacturer", "SystemProductName", "BIOSVendor", "BaseBoardManufacturer", "BaseBoardProduct" };
				foreach (string name in array)
				{
					if (registryKey.GetValue(name) is string text)
					{
						string text2 = text.ToLowerInvariant();
						if (source.Any(text2.Contains))
						{
							what = text;
							return true;
						}
					}
				}
			}
		}
		catch
		{
		}
		array = new string[5] { "VBoxGuest", "VBoxSF", "vmhgfs", "vm3dmp", "VMMouse" };
		foreach (string text3 in array)
		{
			try
			{
				using RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + text3);
				if (registryKey2 != null)
				{
					what = text3;
					return true;
				}
			}
			catch
			{
			}
		}
		what = "";
		return false;
	}
}
