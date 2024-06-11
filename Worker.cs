using System.Net;
using Olspy;

namespace Olserve;

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

	private async Task run()
	{
		while(true)
		{
			var ctx = await incoming.Dequeue();

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
				// TODO try to refresh login tokens
				Console.WriteLine($"[WARN][{endpoint.Route}] Serving possibly outdated data due to unknown exception: {ex}");
			}

			foreach(var c in incoming.DequeueAll().Prepend(ctx))
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