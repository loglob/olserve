using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Olserve;

/// <summary>
///  A MRMW queue where Dequeue can be await'ed 
/// </summary>
internal class AwaitableQueue<T>
{
	private readonly SemaphoreSlim count = new(0);
	/// <summary>
	///  Contains at least `count` items.
	///  Eventually contains exactly `count` items.
	/// </summary>
	private readonly ConcurrentQueue<T> items = new();

	public AwaitableQueue(){}

	public int Count
		=> count.CurrentCount;

	public void Enqueue(T x)
	{
		items.Enqueue(x);
		count.Release();
	}

	public List<T> DequeueAll()
	{
		var ls = new List<T>();

		while(TryDequeue(out var x))
			ls.Add(x);

		return ls;
	}

	public bool TryDequeue([MaybeNullWhen(false)] out T value)
	{
		if(count.Wait(0))
		{
			if(! items.TryDequeue(out value))
				throw new InvalidOperationException("Semaphore and queue out of sync");

			return true;
		}

		value = default;
		return false;
	}

	public async Task<T> Dequeue(CancellationToken ct)
	{
		await count.WaitAsync(ct);
		
		if(! items.TryDequeue(out var x))
			// this is impossible
			throw new InvalidOperationException("Semaphore and queue out of sync");

		return x;
	}

	public Task<T> Dequeue()
		=> Dequeue(CancellationToken.None);
}