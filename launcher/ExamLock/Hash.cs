using System;
using System.Security.Cryptography;
using System.Text;

namespace ExamLock;

internal static class Hash
{
	public static string Sha256Hex(string s)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
	}

	public static bool CheckQuit(string code, string password, string expected)
	{
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sha256Hex(code + ":" + password)), Encoding.ASCII.GetBytes(expected));
	}
}
