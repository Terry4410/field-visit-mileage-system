using System.Threading.Channels;
using FieldVisit.Application;

namespace FieldVisit.Api;

public sealed class BackgroundJobSignal
    : IBackgroundJobSignal
{
    private readonly Channel<bool> signals =
        Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode =
                    BoundedChannelFullMode.DropWrite
            });

    public void Signal()
    {
        signals.Writer.TryWrite(true);
    }

    public async Task WaitAsync(
        CancellationToken ct)
    {
        await signals.Reader.ReadAsync(ct);
    }
}
