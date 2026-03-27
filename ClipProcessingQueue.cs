namespace VeloUploader;

/// <summary>
/// Manages concurrent clip processing to prevent resource exhaustion.
/// Limits: 1 compression, 2 concurrent uploads max.
/// </summary>
public sealed class ClipProcessingQueue : IDisposable
{
    private readonly SemaphoreSlim _compressionSemaphore = new(1, 1);  // 1 compression at a time
    private readonly SemaphoreSlim _uploadSemaphore = new(2, 2);      // 2 concurrent uploads max
    private int _activeProcesses = 0;

    public int ActiveProcesses => _activeProcesses;

    public async Task WaitForCompressionSlotAsync(CancellationToken ct)
    {
        await _compressionSemaphore.WaitAsync(ct);
        Interlocked.Increment(ref _activeProcesses);
    }

    public void ReleaseCompressionSlot()
    {
        Interlocked.Decrement(ref _activeProcesses);
        _compressionSemaphore.Release();
    }

    public async Task WaitForUploadSlotAsync(CancellationToken ct)
    {
        await _uploadSemaphore.WaitAsync(ct);
        Interlocked.Increment(ref _activeProcesses);
    }

    public void ReleaseUploadSlot()
    {
        Interlocked.Decrement(ref _activeProcesses);
        _uploadSemaphore.Release();
    }

    public void Dispose()
    {
        _compressionSemaphore?.Dispose();
        _uploadSemaphore?.Dispose();
    }
}
