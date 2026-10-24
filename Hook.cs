using System.Diagnostics;
using System.Text.Json;

namespace Olserve;

/// <summary>
/// A hook that fires on a program error
/// </summary>
interface IHook<T> where T : Exception
{
	public abstract void Execute(string route, Endpoint endpoint, T exception);
}

class ScriptHook : IHook<CompileFailedException>
{
	public readonly string Path;
	private readonly CancellationToken ct;
	private readonly AwaitableQueue<ProcessStartInfo> backlog = new();
	private volatile bool busy = false;
	private readonly Task worker;

	private async Task run()
	{
		while(true) try
		{
			ProcessStartInfo psi = await backlog.Dequeue(ct);
			Process p = new() {
				StartInfo = psi
			};
			
			try {
				busy = true;
				p.Start();		
			} catch(Exception e) {
				await Console.Error.WriteLineAsync($"[WARN] Failed to execute hook '{Path}': {e.Message}");
				continue;
			}

			do {
				await p.WaitForExitAsync(ct);
			} while(! p.HasExited);

			if(p.ExitCode != 0)
				await Console.Error.WriteLineAsync($"[WARN] Hook '{Path}' failed with exit code {p.ExitCode}");

		} catch(OperationCanceledException) {
			return;
		} finally {
			busy = false;
		}
	}

	public ScriptHook(string path, CancellationToken ct = default)
	{
		this.Path = path;
		this.ct = ct;
		this.worker = run();
	}

	public void Execute(string route, Endpoint endpoint, CompileFailedException exception)
	{
		ProcessStartInfo p = new(Path) {
			UseShellExecute = false,
			CreateNoWindow = true
		};
		var env = p.Environment;

		env.Add("ROUTE", route);
		env.Add("MAIN_FILE", endpoint.MainFile);
		env.Add("LOGS", string.Join('\n', exception.Logs));

		if(exception.Persistent)
			env.Add("PERSISTENT", "true");

		// translate status code back to Overleaf API name
		var apiStatus = JsonNamingPolicy.KebabCaseLower.ConvertName(exception.Status.ToString());
		env.Add("STATUS", apiStatus);
		
		if(exception.RevisionTimestamp.HasValue)
		{
			var unixTime = ((DateTimeOffset)exception.RevisionTimestamp.Value.ToUniversalTime()).ToUnixTimeSeconds();
			env.Add("REVISION_TIME", unixTime.ToString());
		}

		if(busy)
			env.Add("DELAYED", "true");

		backlog.Enqueue(p);
	}
}