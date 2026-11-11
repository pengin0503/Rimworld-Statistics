using System.Security.Cryptography;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
    /// <summary>
    /// Serves the XML reader from blocks read from the file and hashes each block on a second thread, so hashing adds
    /// little import time. The hash covers exactly the bytes served, in order. Blocks circulate between a free list and a
    /// queue of filled blocks; nothing is allocated per block.
    /// </summary>
    private sealed class HashingStream : Stream
    {
        private const int BlockSize = 1 << 16;
        private const int Blocks = 4;
        private readonly Stream _inner;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _free = new();
        private readonly System.Collections.Concurrent.BlockingCollection<(byte[] Block, int Length)> _filled = new();
        private readonly Task _worker;
        private byte[]? _block;
        private int _pos;
        private int _len;
        private bool _eof;

        public HashingStream(Stream inner)
        {
            _inner = inner;
            for (var i = 0; i < Blocks; i++)
                _free.Add(new byte[BlockSize]);
            _worker = Task.Factory.StartNew(() =>
            {
                foreach (var (block, length) in _filled.GetConsumingEnumerable())
                {
                    _hash.AppendData(block, 0, length);
                    _free.Add(block);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
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
                _filled.Add((_block, _len));
            _block = _free.Take();
            _pos = 0;
            _len = Math.Max(0, _inner.Read(_block, 0, BlockSize));
            BytesRead += _len;
            _eof = _len == 0;
            return !_eof;
        }

        /// <summary>Reads (and hashes) what the XML reader left after the end of the document.</summary>
        public void ReadToEnd()
        {
            _pos = _len;
            while (Refill())
                _pos = _len;
        }

        public byte[] Hash()
        {
            if (_block is not null && _len > 0)
                _filled.Add((_block, _len));
            _block = null;
            _len = 0;
            _filled.CompleteAdding();
            _worker.Wait();
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
            if (disposing)
            {
                if (!_filled.IsAddingCompleted)
                    _filled.CompleteAdding();
                try
                {
                    _worker.Wait();
                }
                catch (AggregateException)
                {
                    // the worker only hashes; nothing to report after a failed read
                }
                _filled.Dispose();
                _free.Dispose();
                _hash.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
