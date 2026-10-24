using Olspy;
using System.Diagnostics;

namespace Olserve;

public class Endpoint
{
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
		string? mainID = null;

		if(MainFile is not null)
		{
			var info = await project.GetInfo(false);
			var file = info.Project.RootFolder[0].Lookup(MainFile);

			if(file is null)
			{
				var msg = $"Path '{MainFile}' does not exist in project";
				Console.WriteLine($"[WARN][{Route}] ${msg}");
				throw new CompileFailedException(Protocol.CompileStatus.Failure, [msg], null);
			}

			mainID = file.ID;
		}

		Stopwatch sw = new();
		sw.Start();
		var cmp = await project.Compile(rootDoc: mainID, stopOnFirstError: true);

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

			throw new CompileFailedException(cmp.Status, logs, timestamp);
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
				current = current with { Version = serverRev, Timestamp = serverTime };
				throw;
			}
		}
		else if(serverRev < current.Version)
			await Console.Error.WriteLineAsync($"[WARN][{Route}] somehow time traveled from version {current.Version} to {serverRev}");
	}
}