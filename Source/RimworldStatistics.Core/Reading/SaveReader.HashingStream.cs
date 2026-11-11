using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
    /// <summary>
    /// Hashes exactly the bytes served, in order. Small inputs use the XML reader buffer directly; larger reads
    /// overlap XML parsing and hashing using pooled blocks and a worker.
    /// </summary>
    private sealed class HashingStream : Stream
    {
        private const int BlockSize = 1 << 16;
        private const int Blocks = 4;
        private readonly Stream _inner;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly BlockingCollection<byte[]>? _free;
        private readonly BlockingCollection<(byte[] Block, int Length)>? _filled;
        private readonly Task? _worker;
        private readonly byte[][]? _buffers;
        private bool _disposed;
        private byte[]? _block;
        private int _pos;
        private int _len;
        private bool _eof;

        public HashingStream(Stream inner, long expectedSize)
        {
            _inner = inner;
            if (expectedSize <= BlockSize)
                return;

            var free = _free = new BlockingCollection<byte[]>();
            var filled = _filled = new BlockingCollection<(byte[] Block, int Length)>();
            _buffers = new byte[Blocks][];
            for (var i = 0; i < Blocks; i++)
            {
                _buffers[i] = ArrayPool<byte>.Shared.Rent(BlockSize);
                free.Add(_buffers[i]);
            }
            _worker = Task.Factory.StartNew(() =>
            {
                foreach (var (block, length) in filled.GetConsumingEnumerable())
                {
                    _hash.AppendData(block, 0, length);
                    free.Add(block);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;
            if (_worker is null)
            {
                var read = _inner.Read(buffer);
                _hash.AppendData(buffer[..read]);
                BytesRead += read;
                return read;
            }
            if (_pos == _len && !Refill())
                return 0;
            var n = Math.Min(buffer.Length, _len - _pos);
            _block.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        private bool Refill()
        {
            if (_eof)
                return false;
            if (_block is not null && _len > 0)
                _filled!.Add((_block, _len));
            _block = _free!.Take();
            _pos = 0;
            _len = Math.Max(0, _inner.Read(_block, 0, BlockSize));
            BytesRead += _len;
            _eof = _len == 0;
            return !_eof;
        }

        /// <summary>Reads (and hashes) what the XML reader left after the end of the document.</summary>
        public void ReadToEnd()
        {
            if (_worker is null)
            {
                Span<byte> buffer = stackalloc byte[4096];
                while (Read(buffer) > 0) { }
                return;
            }
            _pos = _len;
            while (Refill())
                _pos = _len;
        }

        public byte[] Hash()
        {
            if (_block is not null && _len > 0)
                _filled!.Add((_block, _len));
            _block = null;
            _len = 0;
            _filled?.CompleteAdding();
            _worker?.Wait();
            return _hash.GetHashAndReset();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_filled is { IsAddingCompleted: false })
                    _filled.CompleteAdding();
                try
                {
                    _worker?.Wait();
                }
                catch (AggregateException)
                {
                    // the worker only hashes; nothing to report after a failed read
                }
                _filled?.Dispose();
                _free?.Dispose();
                if (_buffers is not null)
                    foreach (var buffer in _buffers)
                        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                _hash.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
