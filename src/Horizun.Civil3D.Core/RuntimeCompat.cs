using System.Diagnostics;
using System.Security.Cryptography;

namespace Horizun.Civil3D.Core;

/// <summary>Small BCL differences between Civil 3D 2024 and modern hosts.</summary>
public static class RuntimeCompat
{
    public static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static int ProcessId { get { using var p = Process.GetCurrentProcess(); return p.Id; } }
    public static string? ProcessPath { get { using var p = Process.GetCurrentProcess(); return p.MainModule?.FileName; } }
    public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
    public static double Clamp(double value, double min, double max)
    {
        if (min > max) throw new ArgumentException("min must be <= max.");
        return value < min ? min : value > max ? max : value;
    }
    public static int Clamp(int value, int min, int max)
    {
        if (min > max) throw new ArgumentException("min must be <= max.");
        return value < min ? min : value > max ? max : value;
    }
    public static byte[] Sha256(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
    public static byte[] Sha256(Stream stream) { using var sha = SHA256.Create(); return sha.ComputeHash(stream); }
    public static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    public static byte[] RandomBytes(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }
    public static bool SecretEquals(byte[] a, byte[] b)
    {
#if NET48
        if (a.Length != b.Length) return false;
        var difference = 0;
        for (var i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
        return difference == 0;
#else
        return CryptographicOperations.FixedTimeEquals(a, b);
#endif
    }
    public static bool IsPathFullyQualified(string path)
    {
#if NET48
        // .NET Framework hosts are Windows only: reject root-relative and drive-relative paths.
        if (string.IsNullOrEmpty(path) || path.Length < 2) return false;
        bool Separator(char c) => c == '\\' || c == '/';
        return Separator(path[0]) && Separator(path[1]) ||
            path.Length >= 3 && (path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && path[1] == ':' && Separator(path[2]);
#else
        return Path.IsPathFullyQualified(path);
#endif
    }
    public static void MoveReplacing(string source, string destination)
    {
#if NET48
        if (File.Exists(destination)) File.Replace(source, destination, null);
        else File.Move(source, destination);
#else
        File.Move(source, destination, overwrite: true);
#endif
    }
    public static string[] SplitTrimmed(string text, char separator, bool removeEmpty = false) =>
        text.Split(new[] { separator }).Select(s => s.Trim()).Where(s => !removeEmpty || s.Length > 0).ToArray();
    public static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
            count -= read;
        }
    }
}

#if NET48
/// <summary>Extension overloads absent in .NET Framework; excluded on modern targets.</summary>
public static class LegacyExtensions
{
    public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;
    public static IEnumerable<(TFirst First, TSecond Second)> Zip<TFirst, TSecond>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second) => Enumerable.Zip(first, second, (a, b) => (a, b));
    public static T FirstOrDefault<T>(this IEnumerable<T> source, T fallback) { foreach (var item in source) return item; return fallback; }
    public static bool Contains(this string text, string value, StringComparison comparison) => text.IndexOf(value, comparison) >= 0;
    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;
    public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;
    public static string[] Split(this string text, char separator, StringSplitOptions options) => text.Split(new[] { separator }, options);
    public static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value) { key = pair.Key; value = pair.Value; }
    public static V? GetValueOrDefault<K, V>(this IReadOnlyDictionary<K, V> map, K key) => map.TryGetValue(key, out var value) ? value : default;
    public static void Write(this Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}
#endif
