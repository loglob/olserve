using System.Net;
using Olspy;

namespace Olserve;

/// <summary>
///  Endpoint for HTTP requests that handles asynchronously recompiling the requested documents
/// </summary>
public class Worker
{
	private Endpoint endpoint;
	private readonly AwaitableQueue<HttpListenerContext> incoming = new();
	private readonly Task workerTask;


	public string Route
		=> endpoint.Route;

	public Worker(Endpoint endpoint)
	{
		this.endpoint = endpoint;
		this.workerTask = run();
	}

	/// <summary>
    ///  Computes a timespan after which the document should be refreshed, even if no request was received.
    ///  This way, changes to the document are received early and likely to be available for the next actual request.
    ///  The duration is randomized so that multiple Workers don't accidentally sync up and hit overleaf with multiple concurrent compiles.
    /// </summary>
	private static TimeSpan refreshTime()
		// 4h ± 15min
		=> TimeSpan.FromMinutes(4*60 - 15) + Random.Shared.NextDouble() * TimeSpan.FromMinutes(30);
	
	private async Task run()
	{
		while(true)
		{
			HttpListenerContext? ctx = null;

			try
			{
				var cts = new CancellationTokenSource(refreshTime());
				ctx = await incoming.Dequeue(cts.Token);
			}
			catch(OperationCanceledException)
			{ }

			try
			{
				await endpoint.Refresh();
			}
			catch(CompileFailedException cfe)
			{
				Console.WriteLine(cfe.Persistent
					? $"[WARN][{endpoint.Route}] Current revision doesn't compile, skipping it"
					: $"[WARN][{endpoint.Route}] Transient compile error: {cfe.Status}"
				);
			}
			catch(HttpStatusException hse) when (hse.StatusCode == HttpStatusCode.Forbidden)
			{
				Console.WriteLine($"[WARN][{endpoint.Route}] Session lapsed, re-authenticating");

				try
				{
					endpoint = await Endpoint.Create(endpoint);
				}
				catch(Exception ex)
				{
					Console.WriteLine($"[WARN][{endpoint.Route}] Failed to re-authenticate: {ex.Message}");
				}
			}
			catch(Exception ex)
			{
				Console.WriteLine($"[WARN][{endpoint.Route}] Serving possibly outdated data due to unknown exception: {ex}");
			}

			var q = incoming.DequeueAll();

			if(ctx is not null)
				q.Insert(0, ctx);

			foreach(var c in q)
				await Program.CloseWith(c, 200, endpoint.Data, "application/pdf");
		}
	}

	/// <summary>
	///  Pushes an incoming HTTP connection onto the backlog.
	/// </summary>
	public void Serve(HttpListenerContext ctx)
	{
		if(workerTask.IsCanceled || workerTask.IsCompleted)
			throw new OperationCanceledException("sender task was already stopped");

		incoming.Enqueue(ctx);
	}
}