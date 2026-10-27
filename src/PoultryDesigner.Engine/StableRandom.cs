using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PoultryDesigner.Engine;

// SplitMix64, with rejection sampling for unbiased Fisher-Yates indices.
public sealed class StableRandom
{
    private ulong state;
    public StableRandom(ulong seed) => state = seed;
    public ulong NextUInt64()
    {
        unchecked
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    public int NextIndex(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ulong bound = (ulong)count, threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = NextUInt64(); } while (value < threshold);
        return (int)(value % bound);
    }
    public void Shuffle<T>(T[] items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        { int j = NextIndex(i + 1); (items[i], items[j]) = (items[j], items[i]); }
    }
    public static StableRandom Stream(ulong seed, string domain)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"PED-v1:{seed}:{domain}"));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }
}
