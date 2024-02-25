using Olspy;

using static Olspy.Protocol.CompileStatus;

namespace Olserve;

public class CompileFailedException(Protocol.CompileStatus status) : Exception("Compilation failed to produce a PDF")
{
	public readonly Protocol.CompileStatus Status = status;

	public readonly bool Persistent = status switch {
		Success => true,
		Failure => true,
		StoppedOnFirstError => true,
		_ => false
	};
}