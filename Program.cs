using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using static System.StringComparison;

namespace Olserve;

public class Program(Config Conf, Dictionary<string, Endpoint> Routes)
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
			- routes: A string -> string dictionary that maps paths onto overleaf project join links.
		Note that route links must be read-write links, read-only links won't work.
		""";

	private readonly HttpListener listener = new() {
		Prefixes = { $"http://*:{Conf.Port}/" }
	};

	private static readonly byte[] noUrlMessage = Encoding.UTF8.GetBytes("No URL information (how did that happen)");
	private static readonly byte[] noSuchFileMessage = Encoding.UTF8.GetBytes("The requested path doesn't exist on the server");

	private static async Task closeWith(HttpListenerContext ctx, int status, byte[] message, string contentType = "text/plain")
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
		catch (System.Exception ex)
		{
			await Console.Error.WriteLineAsync($"Failed to send response: {ex.Message}");
		}

	}

	private async Task run()
	{
		Console.CancelKeyPress += (_1, _2) => listener.Stop();
		AppDomain.CurrentDomain.ProcessExit += (_1, _2) => listener.Stop();

		listener.Start();

		while(listener.IsListening)
		{
			var ctx = await listener.GetContextAsync();

			if(ctx.Request.Url is null)
			{
				await closeWith(ctx, 400, noUrlMessage);
				continue;
			}

			var p = WebUtility.UrlDecode(ctx.Request.Url.AbsolutePath);

			if(! Routes.TryGetValue(p, out var ep))
			{
				Console.WriteLine($"[INFO] Missing path '{p}' requested");
				await closeWith(ctx, 404, noSuchFileMessage);
				continue;
			}

			try
			{
				await ep.Refresh();
			}
			catch(CompileFailedException)
			{
				Console.WriteLine($"[WARN][{ep.Route}] Serving possibly outdated data because the compilation failed");
			}
			catch(Exception ex)
			{
				// TODO try to refresh logins
				Console.WriteLine($"[WARN][{ep.Route}] Serving possibly outdated data due to unknown exception: {ex}");
			}

			await closeWith(ctx, 200, ep.Data, "application/pdf");
		}
	}

	private static async Task<Program> init(Config conf)
	{
		foreach(var r in conf.Routes)
		{
			if(! r.Key.StartsWith('/'))
				await Console.Error.WriteLineAsync($"[WARN] Route '{r.Key}' should start with '/'");
			if(r.Value.Contains("/read/"))
				await Console.Error.WriteLineAsync($"[WARN] Project link '{r.Value}' looks like a read-only link. You must use read-write links.");
		}

		var routes = conf.Routes.ToArray();
		var endpoints = new Endpoint[routes.Length];

		Console.WriteLine("Initializing projects...");
		await Parallel.ForAsync(0, routes.Length, async (i, _) => {
			try
			{
				var r = routes[i];
				endpoints[i] = await Endpoint.Create(r.Key, new Uri(r.Value));
			}
			catch(Exception ex)
			{
				throw new Exception($"Failed to initialize route {routes[i].Key}", ex);
			}
		});

		Console.WriteLine($"Done setting up {routes.Length} routes");
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

		var prog = await Program.init(conf);
		Console.WriteLine($"Starting listener on port {conf.Port}...");

		await prog.run();
	}

}