using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace UrlShortener.Api.Analytics;

/// <summary>
/// In-memory hand-off from the redirect to <see cref="ClickWriterService"/> (ADR-0004). Writing never waits: when the
/// queue is full the click is dropped and counted, so analytics can never slow down or fail a redirect.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Name used by ADR-0004 and the impact analysis; it is a queue, just not a Queue<T> subclass.")]
public sealed class ClickQueue
{
    private readonly Channel<ClickEvent> _channel;
    private long _dropped;

    /// <param name="capacity">10,000 is about 11 MB worst case (ADR-0004); tests pass a small value.</param>
    public ClickQueue(int capacity = 10_000)
    {
        // DropWrite, not the default Wait: with Wait a full queue would make writers wait (CLAUDE.md §9).
        _channel = Channel.CreateBounded<ClickEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Clicks lost because the queue was full or already completed (shutdown).</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    /// <summary>
    /// Never waits. Under DropWrite a full queue calls the itemDropped callback above and still returns true, so true
    /// does not mean "queued"; false means the channel is already completed (shutdown) and is counted here.
    /// </summary>
    public bool TryWrite(ClickEvent click)
    {
        if (_channel.Writer.TryWrite(click))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>No more writes; the reader drains what is queued and then stops.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
