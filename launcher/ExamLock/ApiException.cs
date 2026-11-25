using System;

namespace ExamLock;

internal sealed class ApiException : Exception
{
	public ApiException(string message)
		: base(message)
	{
	}
}
