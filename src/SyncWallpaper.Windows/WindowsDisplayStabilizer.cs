using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WindowsDisplayChangeStabilizer : IDisplayChangeStabilizer
{
    private readonly IDisplayTopologyReader _reader;
    private readonly TimeSpan _poll;
    private readonly int _requiredStableReads;

    public WindowsDisplayChangeStabilizer(IDisplayTopologyReader reader, TimeSpan? poll = null, int requiredStableReads = 2)
    {
        _reader = reader; _poll = poll ?? TimeSpan.FromMilliseconds(250); _requiredStableReads = Math.Max(2, requiredStableReads);
    }

    public async Task WaitForStableAsync(CancellationToken cancellationToken)
    {
        var previous = _reader.Capture().NativeSignature;
        var stable = 1;
        while (stable < _requiredStableReads)
        {
            await Task.Delay(_poll, cancellationToken);
            var current = _reader.Capture().NativeSignature;
            if (string.Equals(previous, current, StringComparison.Ordinal)) stable++;
            else { previous = current; stable = 1; }
        }
    }
}
