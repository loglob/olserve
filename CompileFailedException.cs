using Olspy;

using static Olspy.Protocol.CompileStatus;

namespace Olserve;

public class CompileFailedException(Protocol.CompileStatus status, List<string> logs, DateTime? timestamp) : Exception($"Compilation failed to produce a PDF with error code {status}")
{
	public readonly Protocol.CompileStatus Status = status;
	public readonly List<string> Logs = logs;
	public readonly DateTime? RevisionTimestamp = timestamp;

	public readonly bool Persistent = status switch {
		Success => true,
		Failure => true,
		StoppedOnFirstError => true,
		_ => false
	};
}