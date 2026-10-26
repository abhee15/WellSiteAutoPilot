using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

internal static class PackageUploadChecks
{
    public static async Task RunAsync()
    {
        var repository = new ProbeRepository();
        var service = new LogicModulePackageService(repository, TimeProvider.System);
        var subject = new AccessSubject(Guid.NewGuid());
        var actor = new SecurityActorContext(subject.UserId, "offline-upload-check", "offline");
        // This source has no Length/seek support, matching chunked HTTP bodies.
        using var tooLarge = new ChunkedSource(LogicModulePackageService.MaximumUploadBytes + 1);
        try { await service.UploadAsync(tooLarge, subject, actor); throw new InvalidOperationException("Oversized upload was accepted."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_PACKAGE_UPLOAD_TOO_LARGE") { }
        using var malformed = new MemoryStream([1, 2, 3]);
        try { await service.UploadAsync(malformed, subject, actor); throw new InvalidOperationException("Malformed upload was accepted."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_PACKAGE_INVALID") { }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var input = new ChunkedSource(1);
        try { await service.UploadAsync(input, subject, actor, canceled.Token); throw new InvalidOperationException("Canceled upload was accepted."); }
        catch (OperationCanceledException) { }
        if (repository.Calls != 0) throw new InvalidOperationException("Invalid upload reached persistence.");
        Console.WriteLine("Bounded non-seekable upload, malformed content and cancellation checks passed.");
    }

    private sealed class ProbeRepository : ILogicModulePackageRepository
    {
        public int Calls { get; private set; }
        public Task<InstalledLogicModule> UploadAuthorizedAsync(InstalledLogicModule module, byte[] archive,
            AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(module);
        }
    }
    private sealed class ChunkedSource(int length) : Stream
    {
        private int _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, _remaining);
            buffer.AsSpan(offset, read).Clear();
            _remaining -= read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Math.Min(buffer.Length, _remaining);
            buffer.Span[..read].Clear();
            _remaining -= read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
