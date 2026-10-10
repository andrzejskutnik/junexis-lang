namespace JunexisLang.Compiler.CLI.Project;

internal sealed class SizeLimitedWriteStream : Stream
{
    private readonly Stream destination;
    private readonly long maximumLength;
    private long written;

    internal SizeLimitedWriteStream(Stream destination, long maximumLength)
    {
        this.destination = destination;
        this.maximumLength = maximumLength;
    }

    public override bool CanRead => false;
    public override bool CanSeek => destination.CanSeek;
    public override bool CanWrite => true;
    public override long Length => destination.CanSeek ? destination.Length : written;
    public override long Position
    {
        get => destination.CanSeek ? destination.Position : written;
        set
        {
            if (!destination.CanSeek)
                throw new NotSupportedException();
            if (value < 0 || value > maximumLength)
                throw new ArgumentOutOfRangeException(nameof(value));
            destination.Position = value;
        }
    }

    public override void Flush() => destination.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        destination.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Reserve(count);
        destination.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Reserve(buffer.Length);
        destination.Write(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Reserve(buffer.Length);
        await destination.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        Reserve(count);
        return destination.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!destination.CanSeek)
            throw new NotSupportedException();
        var previous = destination.Position;
        var position = destination.Seek(offset, origin);
        if (position < 0 || position > maximumLength)
        {
            destination.Position = previous;
            throw new InvalidDataException("Package artifact exceeds the size limit.");
        }
        return position;
    }

    public override void SetLength(long value)
    {
        if (!destination.CanSeek)
            throw new NotSupportedException();
        if (value < 0 || value > maximumLength)
            throw new InvalidDataException("Package artifact exceeds the size limit.");
        destination.SetLength(value);
        written = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            destination.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await destination.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void Reserve(int count)
    {
        var start = destination.CanSeek ? destination.Position : written;
        if (count < 0 || start > maximumLength - count)
            throw new InvalidDataException("Package artifact exceeds the size limit.");
        written = Math.Max(written, start + count);
    }
}
