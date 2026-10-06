using System.Security.Cryptography;

namespace Horizun.Civil3D.Core;

/// <summary>
/// Produce and inspect an output on the destination volume before publishing it.
/// Replacing an existing file preserves its exact previous bytes in a named backup.
/// </summary>
public static class AtomicOutput
{
    public sealed record Result(string Path, string? BackupPath);
    public sealed record DestinationState(bool Exists, string? Sha256);

    public static DestinationState Capture(string destination)
    {
        if (!File.Exists(destination)) return new(false, null);
        using var file = File.OpenRead(destination);
        return new(true, RuntimeCompat.Hex(RuntimeCompat.Sha256(file)));
    }

    public static Result Write(string destination, bool overwrite, Action<string> produce,
        Func<string, bool> validate, DestinationState? expected = null)
    {
        var full = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(full)
            ?? throw new IOException("Output has no directory.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (!overwrite && File.Exists(full)) throw new IOException("Output already exists: " + full);

        // Keep the requested extension: plot engines may choose their format or
        // append an extension based on the name passed to them.
        var stem = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);
        var stage = Path.Combine(directory, "." + stem + "." + Guid.NewGuid().ToString("N") + ".tmp" + ext);
        try
        {
            produce(stage);
            if (!File.Exists(stage) || new FileInfo(stage).Length == 0 || !validate(stage))
                throw new IOException("The staged output failed verification; the previous file was preserved.");

            if (expected != null && Capture(full) != expected)
                throw new IOException("The output changed while the staged file was produced; it was not replaced.");

            if (File.Exists(full))
            {
                if (!overwrite) throw new IOException("Output appeared before promotion: " + full);
                var backup = Path.Combine(directory, Path.GetFileName(full) + ".backup-" + Guid.NewGuid().ToString("N"));
                File.Replace(stage, full, backup);
                return new Result(full, backup);
            }
            File.Move(stage, full);
            return new Result(full, null);
        }
        finally
        {
            // Cleanup is best effort; its failure must not obscure the plot or
            // validation error, and it cannot affect the destination file.
            try { if (File.Exists(stage)) File.Delete(stage); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
