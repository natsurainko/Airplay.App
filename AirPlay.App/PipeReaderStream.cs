using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

public class PipeReaderStream(PipeReader reader) : Stream
{
    private ReadResult _latestResult;
    private ReadOnlySequence<byte> _currentBuffer;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_currentBuffer.Length == 0)
        {
            _latestResult = await reader.ReadAsync(cancellationToken);
            _currentBuffer = _latestResult.Buffer;
            if (_latestResult.IsCompleted && _currentBuffer.Length == 0)
                return 0;
        }

        var toCopy = (int)Math.Min(count, _currentBuffer.Length);
        if (toCopy > 0)
        {
            _currentBuffer.Slice(0, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
            _currentBuffer = _currentBuffer.Slice(toCopy);
            reader.AdvanceTo(_currentBuffer.Start, _currentBuffer.End);
        }

        // 如果没内容且已结束，返回0
        if (toCopy == 0 && _latestResult.IsCompleted)
            return 0;

        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        reader.Complete();
    }
}