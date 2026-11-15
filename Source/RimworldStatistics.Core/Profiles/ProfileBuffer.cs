using System.Buffers;

namespace RimworldStatistics.Core;

/// <summary>
/// A growable byte buffer that keeps its array between uses (Issue #195): a profile write serializes into it instead of
/// building a new array of the whole profile (megabytes, on the large-object heap) every time. It grows by half again, so
/// a buffer kept for a profile of n bytes holds at most about 1.5 n.
/// </summary>
internal sealed class ProfileBuffer : IBufferWriter<byte>
{
    private const int MinimumCapacity = 64 * 1024;
    private byte[] _array;
    private int _written;

    public ProfileBuffer(int capacity) => _array = new byte[Math.Max(capacity, MinimumCapacity)];

    public int Capacity => _array.Length;

    /// <summary>The bytes written since <see cref="Clear"/>. Valid until the next write into this buffer.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _array.AsMemory(0, _written);

    public void Clear() => _written = 0;

    public void Advance(int count)
    {
        if (count < 0 || count > _array.Length - _written)
            throw new ArgumentOutOfRangeException(nameof(count));
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _array.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(sizeHint);
        return _array.AsSpan(_written);
    }

    private void Reserve(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        sizeHint = Math.Max(sizeHint, 1);
        if (_array.Length - _written >= sizeHint)
            return;
        var needed = (long)_written + sizeHint;
        if (needed > Array.MaxLength)
            throw new OutOfMemoryException("The profile is too large to serialize.");
        var grown = (int)Math.Min(Array.MaxLength, Math.Max(needed, _array.Length + (long)_array.Length / 2));
        var array = new byte[grown];
        _array.AsSpan(0, _written).CopyTo(array);
        _array = array;
    }
}
