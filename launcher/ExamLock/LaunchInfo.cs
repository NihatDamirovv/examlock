namespace ExamLock;

internal sealed record LaunchInfo(string Title, string Student, string GoUrl, string[] Allowed, string? QuitHash);
