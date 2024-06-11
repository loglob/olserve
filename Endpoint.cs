using Olspy;
using System.Diagnostics;

namespace Olserve;

public class Endpoint
{
	private record Revision( uint Version, byte[] Data );

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
	private async Task<byte[]> runCompile()
	{
		string? mainID = null;

		if(MainFile is not null)
		{
			var info = await project.GetInfo(false);
			var file = info.Project.RootFolder[0].Lookup(MainFile);


			if(file is null)
			{
				Console.WriteLine($"[WARN][{Route}] Path '{MainFile}' does not exist in project");
				throw new CompileFailedException(Protocol.CompileStatus.Failure);
			}


			mainID = file.ID;
		}

		Stopwatch sw = new();
		sw.Start();
		var cmp = await project.Compile(mainID, stopOnFirstError: true);

		if(! cmp.IsSuccess(out var pdf))
			throw new CompileFailedException(cmp.Status);

		var dat = await (await project.GetOutFile(pdf)).ReadAsByteArrayAsync();

		sw.Stop();
		Console.WriteLine($"[INFO][{Route}] Compiled in {sw.ElapsedMilliseconds / 1e3}s");

		return dat;
	}

	/// <summary>
	///  Checks the current revision and re-compiles if needed.
	/// </summary>
	public async Task Refresh()
	{
		var serverRev = (await project.GetUpdateHistory()).Select(h => h.ToV).FirstOrDefault(0u);

		if(current is null || serverRev > current.Version)
		{
			try
			{
				var data = await runCompile();
				current = new(serverRev, data);
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