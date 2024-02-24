namespace Olserve;

public class CompileFailedException(bool transient) : Exception("Compilation failed to produce a PDF")
{
	public readonly bool Transient = transient;
}