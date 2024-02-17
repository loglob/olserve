using Olspy;
using System.Diagnostics;

namespace Olserve;

public class Endpoint
{
	private record Revision( uint Version, byte[] Data );

	public static async Task<Endpoint> Create(string route, Uri uri)
	{
		var proj = await Project.Open(uri, new(TimeSpan.FromMinutes(5)));
		var ep = new Endpoint(route, uri, proj);

		await ep.Refresh();

		return ep;
	}

	public readonly Uri Link;
	public readonly string Route;
	private readonly Project project;
	private Revision? current = null;

	public byte[] Data
		=> current!.Data;

	private Endpoint(string route, Uri link, Project project)
	{
		this.Route = route;
		this.Link = link;
		this.project = project;
	}

	/// <summary>
	///  Actually runs a compile, without checking the current revision.
	///  Data is only overwritten when the task actually completes successfully
	/// </summary>
	private async Task<byte[]> runCompile()
	{
		Stopwatch sw = new();
		sw.Start();
		var cmp = await project.Compile(stopOnFirstError: true);

		if(! cmp.IsSuccess(out var pdf))
			throw new CompileFailedException();

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
			var data = await runCompile();
			current = new(serverRev, data);
		}
		else if(serverRev < current.Version)
			await Console.Error.WriteLineAsync($"[WARN][{Route}] somehow time traveled from version {current.Version} to {serverRev}");
	}
}