using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using static System.StringComparison;

namespace Olserve;

public class Program(Config Conf, Dictionary<string, Worker> Routes)
{
	private static readonly JsonSerializerOptions opt = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Skip,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	private static readonly string[] helpArgs = [ "help", "--help", "h" ];

	private static readonly string usage =
		$"Usage: {Environment.GetCommandLineArgs()[0]} [<config.json>]\n" + """
		Starts a server that maps HTTP routes onto the output pdf of overleaf projects.

		The configuration is an object containing:
			- port (optional, default 80): The port to listen on for HTTP
			- routes: A dictionary that maps HTTP paths onto build configurations, which are objects with:
				- An object containing:
					- link: A join link to an overleaf project
					- mainFile (optional): A path within that project to the file to use as main file for compiling
		Note that route links must be read-write links, read-only links won't work.
		""";

	private readonly HttpListener listener = new() {
		Prefixes = { $"http://*:{Conf.Port}/" }
	};

	private static readonly byte[] noUrlMessage = Encoding.UTF8.GetBytes("No URL information (how did that happen)");
	private static readonly byte[] noSuchFileMessage = Encoding.UTF8.GetBytes("The requested path doesn't exist on the server");

	public static async Task CloseWith(HttpListenerContext ctx, int status, byte[] message, string contentType = "text/plain")
	{
		try
		{
			ctx.Response.ContentType = contentType;
			ctx.Response.StatusCode = status;
			ctx.Response.ContentLength64 = message.Length;
			await ctx.Response.OutputStream.WriteAsync(message);
			ctx.Response.OutputStream.Close();

			ctx.Response.Close();
		}
		catch(Exception ex)
		{
			await Console.Error.WriteLineAsync($"Failed to send response: {ex.Message}");
		}
	}

	private async Task run()
	{
		listener.Start();
		Console.CancelKeyPress += (_1, _2) => listener.Stop();

		while(listener.IsListening)
		{
			var ctx = await listener.GetContextAsync();

			if(ctx.Request.Url is null)
			{
				await CloseWith(ctx, 400, noUrlMessage);
				continue;
			}

			var p = WebUtility.UrlDecode(ctx.Request.Url.AbsolutePath);

			if(! Routes.TryGetValue(p, out var ep))
			{
				Console.WriteLine($"[INFO] Missing path '{p}' requested");
				await CloseWith(ctx, 404, noSuchFileMessage);
				continue;
			}

			ep.Serve(ctx);
		}
	}

	private static async Task<Program> init(Config conf)
	{
		foreach(var r in conf.Routes)
		{
			if(! r.Key.StartsWith('/'))
				await Console.Error.WriteLineAsync($"[WARN] Route '{r.Key}' should start with '/'");
			if(r.Value.Link.Contains("/read/"))
				await Console.Error.WriteLineAsync($"[WARN] Project link '{r.Value.Link}' looks like a read-only link. You must use read-write links.");
		}

		var endpoints = new Worker[conf.Routes.Count];

		Console.WriteLine("Initializing projects...");
		// Overleaf groups builds on the same project, so we can't build two targets on the same project in parallel
		// or we'll get a TooRecentlyCompiled or CompileInProgress error
		var concurrentGroups = conf.Routes
			.Select((kvp, i) => (path: kvp.Key, conf: kvp.Value, i))
			.GroupBy(ent => ent.conf.Link)
			.Select(gr => gr.ToArray())
			.ToArray();

		await Parallel.ForAsync(0, concurrentGroups.Length, async (i, _) => {
			foreach (var r in concurrentGroups[i])
			{
				try
				{
					endpoints[r.i] = new Worker(await Endpoint.Create(r.path, r.conf));
				}
				catch(Exception ex)
				{
					throw new Exception($"Failed to initialize route {r.path}", ex);
				}
			}
		});

		Console.WriteLine($"Done setting up {endpoints.Length} routes");
		return new(conf, endpoints.ToDictionary(e => e.Route));
	}

	public static async Task Main(string[] args)
	{
		if(args.Any(a => helpArgs.Any(h => a.Equals(h, CurrentCultureIgnoreCase))))
		{
			Console.WriteLine(usage);
			return;
		}

		string confPath;

		switch(args.Length)
		{
			case 0:
				confPath = "config.json";
			break;

			case 1:
				confPath = args[0];
			break;

			default:
				Environment.ExitCode = 1;
				await Console.Error.WriteLineAsync(usage);
			return;
		}

		Config? conf;

		using(var f = File.OpenRead(confPath))
			conf = await JsonSerializer.DeserializeAsync<Config>(f, opt);

		if(conf is null || conf.Routes is null || conf.Routes.Any(kvp => kvp.Key is null || kvp.Value is null))
			throw new NullReferenceException("Config contains null values");

		var prog = await init(conf);
		Console.WriteLine($"Starting listener on port {conf.Port}...");

		await prog.run();
	}

}