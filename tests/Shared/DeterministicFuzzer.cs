using System.Text;

namespace McpOAuthDcrBridge.TestSupport;

/// <summary>
/// A small, seeded pseudo-random generator used to build reproducible fuzz inputs for parsing
/// boundary tests. Uses a fixed splitmix64-style stream so the same seed always yields the same
/// sequence across machines and test runs. Linked into every test project so there is one
/// authoritative copy.
/// </summary>
internal sealed class DeterministicFuzzer
{
    private const string StructuralCharacters = "\"'\\,=;&?#/:@%+ \t\r\n\0";

    private ulong _state;

    public DeterministicFuzzer(ulong seed) => _state = seed;

    /// <summary>Returns the next 64-bit value in the deterministic stream.</summary>
    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
        return value ^ (value >> 31);
    }

    /// <summary>Returns a deterministic integer in <c>[0, exclusiveMax)</c>.</summary>
    public int NextInt(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);

    /// <summary>Returns a string mixing ASCII letters/digits with protocol-structural characters and, occasionally, raw Unicode code points, to stress boundary parsers.</summary>
    public string NextText(int maxLength)
    {
        var length = NextInt(maxLength + 1);
        var builder = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            builder.Append(NextInt(3) switch
            {
                0 => (char)('a' + NextInt(26)),
                1 => StructuralCharacters[NextInt(StructuralCharacters.Length)],
                _ => (char)NextInt(0x100),
            });
        }

        return builder.ToString();
    }

    /// <summary>Returns a deterministic byte array, including bytes outside valid UTF-8 sequences.</summary>
    public byte[] NextBytes(int maxLength)
    {
        var length = NextInt(maxLength + 1);
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)NextUInt64();
        }

        return bytes;
    }
}
