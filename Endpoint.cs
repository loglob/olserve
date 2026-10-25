using Olspy;
using System.Diagnostics;

namespace Olserve;

public class Endpoint
{
	/// <param name="Version"> The overleaf version number. INCLUDES versions skipped due to compile error. </param>
	/// <param name="Timestamp"> The timestamp of the last successful compile, if any. Does NOT include skipped versions. </param>
	/// <param name="Data"> Raw PDF bytes for the compilation result. </param>
	private record Revision(uint Version, DateTime? Timestamp, byte[] Data);

	private static async Task<Endpoint> Create(string route, Uri uri, string? path)
	{
		var proj = await Project.Open(uri, new(TimeSpan.FromMinutes(5)));
		var ep = new Endpoint(route, uri, proj, path);

		await ep.Refresh();

		return ep;
	}

	public static Task<Endpoint> Create(string route, BuildConfig conf)
		=> Create(route, new Uri(conf.Link), conf.MainFile);

	public static Task<Endpoint> Create(Endpoint e)
		=> Create(e.Route, e.Link, e.MainFile);

	public readonly Uri Link;
	public readonly string Route;
	public readonly string? MainFile;
	private readonly Project project;
	private Revision? current = null;

	public byte[] Data
		=> current!.Data;

	private Endpoint(string route, Uri link, Project project, string? file)
	{
		this.Route = route;
		this.Link = link;
		this.MainFile = file;
		this.project = project;
	}

	/// <summary>
	///  Actually runs a compile, without checking the current revision.
	///  Data is only overwritten when the task actually completes successfully
	/// </summary>
	private async Task<Revision> runCompile(uint version, DateTime? timestamp)
	{
		Stopwatch sw = new();
		sw.Start();
		var cmp = await project.Compile(rootResourcePath: MainFile, stopOnFirstError: true);

		if(! cmp.IsSuccess(out var pdf))
		{
			var logs = await project.ReadLogs(cmp).ToListAsync();

			if(logs.Count == 0)
				Console.WriteLine("[Debug] No error log available");
			else
			{
				Console.WriteLine("[Debug] Recent compile logs:");
				foreach(var line in logs)
					Console.WriteLine('\t' + line);
			}

			throw new CompileFailedException(cmp.Status, logs, timestamp, current?.Timestamp);
		}

		var data = await (await project.GetOutFile(pdf)).ReadAsByteArrayAsync();

		sw.Stop();
		Console.WriteLine($"[INFO][{Route}] Compiled in {sw.ElapsedMilliseconds / 1e3}s");

		return new(version, timestamp, data);
	}

	/// <summary>
	///  Checks the current revision and re-compiles if needed.
	/// </summary>
	public async Task Refresh()
	{
		var lastUpdate = (await project.GetUpdateHistory()).FirstOrDefault((Protocol.Update?)null);
		var serverRev = lastUpdate?.ToV ?? 0u;
		var serverTime = lastUpdate?.Meta?.End;

		if(current is null || serverRev > current.Version)
		{
			try
			{
				current = await runCompile(serverRev, serverTime);
			}
			catch(CompileFailedException cfe) when(cfe.Persistent && current is not null)
			{
				current = current with { Version = serverRev };
				throw;
			}
		}
		else if(serverRev < current.Version)
			await Console.Error.WriteLineAsync($"[WARN][{Route}] somehow time traveled from version {current.Version} to {serverRev}");
	}
}