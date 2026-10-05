namespace Horizun.Civil3D.Core;

/// <summary>
/// A lifetime-specific, monotonic change stamp. Every notification advances it,
/// including undo and rollback: returning to an old value never restores an old
/// confirmation. A reopened database gets a different lifetime identifier.
/// </summary>
public sealed class RevisionClock
{
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private long _revision;

    public string Snapshot => _instance + ":" + Interlocked.Read(ref _revision);

    public void Advance() => Interlocked.Increment(ref _revision);
}
