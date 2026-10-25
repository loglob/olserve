using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace Olserve;

/// <summary>
/// A hook that fires on a program error
/// </summary>
public interface IHook<T>
{
	public void Execute(string route, Endpoint endpoint, T data);
}

public class ScriptHook : IHook<CompileFailedException>
{
	public readonly string Path;
	public readonly ImmutableList<string> Args;
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

	public ScriptHook(string path, IEnumerable<string> args, CancellationToken ct = default)
	{
		this.Path = path;
		this.Args = [..args];
		this.ct = ct;
		this.worker = run();
	}

	public ScriptHook(HookConfig conf) : this(conf.Program, conf.Args)
	{}

	private void setTimestamp(IDictionary<string,string?> env, string name, DateTime? time)
	{
		if(time.HasValue)
		{
			var unixTime = ((DateTimeOffset)time.Value.ToUniversalTime()).ToUnixTimeSeconds();
			env.Add(name, unixTime.ToString());
		}
	}

	private void configure(IDictionary<string,string?> env, CompileFailedException data)
	{
		env.Add("LOGS", string.Join('\n', data.Logs));

		if(data.Persistent)
			env.Add("PERSISTENT", "true");

		// translate status code back to Overleaf API name
		var apiStatus = JsonNamingPolicy.KebabCaseLower.ConvertName(data.Status.ToString());
		env.Add("STATUS", apiStatus);

		setTimestamp(env, "REVISION_TIME", data.RevisionTimestamp);
		setTimestamp(env, "LAST_GOOD_REVISION", data.RevisionTimestamp);
	}

	private void execute<T>(string route, Endpoint endpoint, T data, Action<IDictionary<string, string?>, T> conf)
	{
		ProcessStartInfo p = new(Path, Args) {
			UseShellExecute = false,
			CreateNoWindow = true
		};
		var env = p.Environment;

		env.Add("ROUTE", route);

		if(endpoint.MainFile is not null)
			env.Add("MAIN_FILE", endpoint.MainFile);

		if(busy)
			env.Add("DELAYED", "true");

		conf(env, data);

		backlog.Enqueue(p);
	}

	public void Execute(string route, Endpoint endpoint, CompileFailedException data)
		=> execute(route, endpoint, data, configure);
}