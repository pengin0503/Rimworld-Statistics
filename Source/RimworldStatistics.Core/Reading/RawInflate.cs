namespace RimworldStatistics.Core;

/// <summary>
/// Decompresses raw deflate (RFC 1951) and rejects truncated or malformed input.
/// A port of zlib's reference decoder "puff" (Mark Adler). Used instead of
/// <see cref="System.IO.Compression.DeflateStream"/>, which treats a truncated stream as a clean end of data.
/// Bytes after the final block are ignored.
/// </summary>
internal static class RawInflate
{
    private const int MaxBits = 15;
    private const int MaxLCodes = 286;
    private const int MaxDCodes = 30;
    private const int FixLCodes = 288;
    private const int MaxOutput = 64 * 1024 * 1024;

    private static readonly short[] LengthBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
    private static readonly short[] LengthExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
    private static readonly short[] DistBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
    private static readonly short[] DistExtra = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
    private static readonly short[] CodeLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    private sealed class Huffman(int symbols)
    {
        public readonly short[] Count = new short[MaxBits + 1];
        public readonly short[] Symbol = new short[symbols];
    }

    private sealed class State(byte[] input, int limit)
    {
        public readonly byte[] In = input;
        public readonly int Limit = limit;
        public int InPos;
        public int BitBuf;
        public int BitCnt;
        public byte[] Out = new byte[Math.Min(limit, Math.Max(256, input.Length * 4))];
        public int OutPos;

        public int Bits(int need)
        {
            long val = BitBuf;
            while (BitCnt < need)
            {
                if (InPos >= In.Length)
                    throw new InvalidDataException("deflate data is truncated");
                val |= (long)In[InPos++] << BitCnt;
                BitCnt += 8;
            }
            BitBuf = (int)(val >> need);
            BitCnt -= need;
            return (int)(val & ((1L << need) - 1));
        }

        public void Put(byte b)
        {
            if (OutPos == Out.Length)
                Grow(1);
            Out[OutPos++] = b;
        }

        public void Grow(int extra)
        {
            var needed = (long)OutPos + extra;
            if (needed > Limit)
                throw new InvalidDataException("deflate output exceeds the size limit");
            var size = Math.Max(Out.Length, 1);
            while (size < needed)
                size = (int)Math.Min((long)size * 2, Limit);
            Array.Resize(ref Out, size);
        }
    }

    /// <param name="maxOutput">Larger output is rejected with <see cref="InvalidDataException"/>.</param>
    public static byte[] Inflate(byte[] input, int maxOutput = MaxOutput)
    {
        // Zero bytes means "no data": that is what DeflateStream writes for an empty series (it emits no block at all).
        if (input.Length == 0)
            return Array.Empty<byte>();
        var s = new State(input, Math.Min(maxOutput, MaxOutput));
        int last;
        do
        {
            last = s.Bits(1);
            switch (s.Bits(2))
            {
                case 0: Stored(s); break;
                case 1: Fixed(s); break;
                case 2: Dynamic(s); break;
                default: throw new InvalidDataException("invalid deflate block type");
            }
        } while (last == 0);
        return s.Out.AsSpan(0, s.OutPos).ToArray();
    }

    private static void Stored(State s)
    {
        s.BitBuf = 0;   // discard leftover bits: stored blocks start on a byte boundary
        s.BitCnt = 0;
        if (s.InPos + 4 > s.In.Length)
            throw new InvalidDataException("deflate data is truncated");
        var len = s.In[s.InPos] | (s.In[s.InPos + 1] << 8);
        var nlen = s.In[s.InPos + 2] | (s.In[s.InPos + 3] << 8);
        s.InPos += 4;
        if (len != (~nlen & 0xffff))
            throw new InvalidDataException("invalid stored block length");
        if (s.InPos + len > s.In.Length)
            throw new InvalidDataException("deflate data is truncated");
        if (s.OutPos + len > s.Out.Length)
            s.Grow(len);
        Buffer.BlockCopy(s.In, s.InPos, s.Out, s.OutPos, len);
        s.InPos += len;
        s.OutPos += len;
    }

    private static int Decode(State s, Huffman h)
    {
        int code = 0, first = 0, index = 0;
        for (var len = 1; len <= MaxBits; len++)
        {
            code |= s.Bits(1);
            int count = h.Count[len];
            if (code - count < first)
                return h.Symbol[index + (code - first)];
            index += count;
            first += count;
            first <<= 1;
            code <<= 1;
        }
        throw new InvalidDataException("invalid deflate code");
    }

    /// <summary>Builds canonical code tables. Returns 0 if complete, &gt;0 if incomplete, &lt;0 if over-subscribed.</summary>
    private static int Construct(Huffman h, short[] length, int offset, int n)
    {
        Array.Clear(h.Count);
        for (var symbol = 0; symbol < n; symbol++)
            h.Count[length[offset + symbol]]++;
        if (h.Count[0] == n)
            return 0;
        var left = 1;
        for (var len = 1; len <= MaxBits; len++)
        {
            left <<= 1;
            left -= h.Count[len];
            if (left < 0)
                return left;
        }
        var offs = new short[MaxBits + 1];
        for (var len = 1; len < MaxBits; len++)
            offs[len + 1] = (short)(offs[len] + h.Count[len]);
        for (var symbol = 0; symbol < n; symbol++)
        {
            if (length[offset + symbol] != 0)
                h.Symbol[offs[length[offset + symbol]]++] = (short)symbol;
        }
        return left;
    }

    private static void Codes(State s, Huffman lencode, Huffman distcode)
    {
        int symbol;
        do
        {
            symbol = Decode(s, lencode);
            if (symbol < 256)
            {
                s.Put((byte)symbol);
            }
            else if (symbol > 256)
            {
                symbol -= 257;
                if (symbol >= 29)
                    throw new InvalidDataException("invalid deflate length code");
                var len = LengthBase[symbol] + s.Bits(LengthExtra[symbol]);
                var dsym = Decode(s, distcode);
                if (dsym >= 30)
                    throw new InvalidDataException("invalid deflate distance code");
                var dist = DistBase[dsym] + s.Bits(DistExtra[dsym]);
                if (dist > s.OutPos)
                    throw new InvalidDataException("deflate distance is too far back");
                if (s.OutPos + len > s.Out.Length)
                    s.Grow(len);
                for (var i = 0; i < len; i++)
                {
                    s.Out[s.OutPos] = s.Out[s.OutPos - dist];
                    s.OutPos++;
                }
            }
        } while (symbol != 256);
    }

    // Built once by the type initializer (thread-safe); read-only afterwards.
    private static readonly (Huffman Len, Huffman Dist) FixedCodes = BuildFixed();

    private static (Huffman, Huffman) BuildFixed()
    {
        var lengths = new short[FixLCodes];
        int symbol;
        for (symbol = 0; symbol < 144; symbol++) lengths[symbol] = 8;
        for (; symbol < 256; symbol++) lengths[symbol] = 9;
        for (; symbol < 280; symbol++) lengths[symbol] = 7;
        for (; symbol < FixLCodes; symbol++) lengths[symbol] = 8;
        var lencode = new Huffman(FixLCodes);
        Construct(lencode, lengths, 0, FixLCodes);
        var dl = new short[MaxDCodes];
        Array.Fill(dl, (short)5);
        var distcode = new Huffman(MaxDCodes);
        Construct(distcode, dl, 0, MaxDCodes);
        return (lencode, distcode);
    }

    private static void Fixed(State s) => Codes(s, FixedCodes.Len, FixedCodes.Dist);

    private static void Dynamic(State s)
    {
        var nlen = s.Bits(5) + 257;
        var ndist = s.Bits(5) + 1;
        var ncode = s.Bits(4) + 4;
        if (nlen > MaxLCodes || ndist > MaxDCodes)
            throw new InvalidDataException("invalid deflate code counts");

        var lengths = new short[MaxLCodes + MaxDCodes];
        int index;
        for (index = 0; index < ncode; index++)
            lengths[CodeLengthOrder[index]] = (short)s.Bits(3);
        for (; index < 19; index++)
            lengths[CodeLengthOrder[index]] = 0;

        var lencode = new Huffman(MaxLCodes);
        var distcode = new Huffman(MaxDCodes);
        if (Construct(lencode, lengths, 0, 19) != 0)
            throw new InvalidDataException("incomplete deflate code-length code");

        index = 0;
        while (index < nlen + ndist)
        {
            var symbol = Decode(s, lencode);
            if (symbol < 16)
            {
                lengths[index++] = (short)symbol;
                continue;
            }
            short len = 0;
            if (symbol == 16)
            {
                if (index == 0)
                    throw new InvalidDataException("deflate repeat with no previous length");
                len = lengths[index - 1];
                symbol = 3 + s.Bits(2);
            }
            else if (symbol == 17)
            {
                symbol = 3 + s.Bits(3);
            }
            else
            {
                symbol = 11 + s.Bits(7);
            }
            if (index + symbol > nlen + ndist)
                throw new InvalidDataException("too many deflate code lengths");
            while (symbol-- > 0)
                lengths[index++] = len;
        }

        if (lengths[256] == 0)
            throw new InvalidDataException("deflate block has no end-of-block code");
        var err = Construct(lencode, lengths, 0, nlen);
        if (err < 0 || (err > 0 && nlen - lencode.Count[0] != 1))
            throw new InvalidDataException("invalid deflate literal/length code");
        err = Construct(distcode, lengths, nlen, ndist);
        if (err < 0 || (err > 0 && ndist - distcode.Count[0] != 1))
            throw new InvalidDataException("invalid deflate distance code");
        Codes(s, lencode, distcode);
    }
}
