// The plan for save must bind to the in-memory drawing, not just DBMOD or the
// file timestamp. Subscribe before the first rehearsal and retain the observer
// for this database's lifetime. All callbacks merely advance a counter: they
// never read/write an object, start a transaction or drive Civil 3D's UI.
// Confirmed signatures: docs/api-probes/2025/{acdbmgd,accoremgd}.revision.txt.
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Plugin.Civil;

internal sealed class DrawingRevision
{
    // Live finding (v0.3.2): doc.Database returns a NEW managed wrapper on each access, so a
    // ConditionalWeakTable keyed by the wrapper created a fresh observer (new lifetime id, more
    // event subscriptions) on every call and every apply was "stale". Key by the native
    // database pointer, which is stable for the drawing's lifetime; removed when it is destroyed.
    // Only touched on Civil 3D's main thread.
    private static readonly Dictionary<IntPtr, DrawingRevision> Observers = new();
    private IntPtr _key;
    private readonly Database _db;
    private readonly Document _doc;
    private readonly RevisionClock _clock = new();
    private bool _disposed;

    private DrawingRevision(Document doc)
    {
        _doc = doc;
        _db = doc.Database;
        try
        {
            _db.ObjectAppended += ObjectAppended;
            _db.ObjectModified += ObjectModified;
            _db.ObjectErased += ObjectErased;
            _db.ObjectUnappended += ObjectChanged;
            _db.ObjectReappended += ObjectChanged;
            _db.SystemVariableChanged += VariableChanged;
            _doc.ViewChanged += ViewChanged;
            _db.DatabaseToBeDestroyed += DatabaseClosing;
        }
        catch
        {
            Detach();
            throw; // An unobserved drawing must never receive a save token.
        }
    }

    // Live finding (v0.3.1, fixture run): the bridge's OWN reads fire database events
    // (ComputeFingerPrint, transient volume surfaces created then aborted, ...), so every
    // dry run advanced the clock and every apply was refused as stale_plan. While a bridge
    // command runs on the main thread, nothing else can edit the drawing (the pump only runs
    // when Civil 3D is quiescent), so events in that window are ours and are ignored. A
    // COMMITTED bridge write advances the clock explicitly via Bump(), so earlier tokens
    // still go stale after our own edits.
    [ThreadStatic] private static int _quiet;

    public static IDisposable Quiet()
    {
        _quiet++;
        return new QuietScope();
    }

    private sealed class QuietScope : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _quiet--;
        }
    }

    /// <summary>A bridge write committed: advance the drawing's clock regardless of Quiet().</summary>
    public static void Bump(Document doc)
    {
        if (Observers.TryGetValue(doc.Database.UnmanagedObject, out var o)) o._clock.Advance();
    }

    public static string Capture(Document doc)
    {
        var key = doc.Database.UnmanagedObject;
        if (!Observers.TryGetValue(key, out var observer) || observer._disposed)
        {
            observer = new DrawingRevision(doc) { _key = key };
            Observers[key] = observer;
        }
        if (observer._disposed)
            throw new HzRefusal(ErrorCodes.NoDocument, "The drawing is closing; a save cannot be confirmed.");
        return observer._clock.Snapshot;
    }

    private void Observe() { if (_quiet == 0) _clock.Advance(); }

    // Live finding (v0.6.0, fixture run): once a dynamic offset alignment exists, the AutoCAD
    // associative framework re-evaluates its AcDbAssocNetwork on every idle (about one ObjectModified
    // per second with nobody touching the drawing), so every token went stale. Assoc* objects are the
    // framework's own bookkeeping; a real edit also modifies real entities, which still count.
    private static bool Bookkeeping(DBObject? o)
    {
        try { return o != null && o.GetRXClass().Name.StartsWith("AcDbAssoc", StringComparison.Ordinal); }
        catch { return false; }
    }

    private void ObjectChanged(object sender, ObjectEventArgs e) { if (!Bookkeeping(e.DBObject)) Observe(); }

    private void ObjectAppended(object sender, ObjectEventArgs e)
    {
        if (Bookkeeping(e.DBObject)) return;
        try { _appended?.Add(e.DBObject.ObjectId); } catch { }
        Observe();
    }

    private void ObjectModified(object sender, ObjectEventArgs e)
    {
        if (Bookkeeping(e.DBObject)) return;
        try { _modified?.Add(e.DBObject.ObjectId); } catch { }
        Observe();
    }

    // ---- what one bridge write touched (recorded even inside Quiet scopes) ----
    [ThreadStatic] private static HashSet<ObjectId>? _appended;
    [ThreadStatic] private static HashSet<ObjectId>? _modified;

    public static void StartRecording() { _appended = new(); _modified = new(); }

    public static (List<ObjectId> Appended, List<ObjectId> Modified) StopRecording()
    {
        var a = _appended?.ToList() ?? new(); var m = _modified?.ToList() ?? new();
        _appended = null; _modified = null;
        return (a, m.Where(id => !a.Contains(id)).ToList());
    }

    /// <summary>The last committed bridge write on a drawing, for horizun_c3d_document undo_last.</summary>
    public sealed record LastWrite(string Revision, string Tool, string Action, string UndoLabel, List<ObjectId> Created, int Modified, DateTime Utc);

    private static readonly Dictionary<IntPtr, LastWrite> LastWrites = new();
    public static void SetLastWrite(Document doc, LastWrite w) => LastWrites[doc.Database.UnmanagedObject] = w;
    public static LastWrite? GetLastWrite(Document doc) => LastWrites.TryGetValue(doc.Database.UnmanagedObject, out var w) ? w : null;
    public static void ClearLastWrite(Document doc) => LastWrites.Remove(doc.Database.UnmanagedObject);
    private void ObjectErased(object sender, ObjectErasedEventArgs e)
    {
        if (Bookkeeping(e.DBObject)) return;
        try { _modified?.Add(e.DBObject.ObjectId); } catch { }
        Observe();
    }
    private void VariableChanged(object sender, Autodesk.AutoCAD.DatabaseServices.SystemVariableChangedEventArgs e) => Observe();
    private void ViewChanged(object? sender, EventArgs e) => Observe();
    private void DatabaseClosing(object? sender, EventArgs e) => Detach();

    private void Detach()
    {
        _disposed = true;
        LastWrites.Remove(_key);
        if (_key != IntPtr.Zero && Observers.TryGetValue(_key, out var current) && current == this) Observers.Remove(_key);
        try
        {
            _db.ObjectAppended -= ObjectAppended;
            _db.ObjectModified -= ObjectModified;
            _db.ObjectErased -= ObjectErased;
            _db.ObjectUnappended -= ObjectChanged;
            _db.ObjectReappended -= ObjectChanged;
            _db.SystemVariableChanged -= VariableChanged;
            _doc.ViewChanged -= ViewChanged;
            _db.DatabaseToBeDestroyed -= DatabaseClosing;
        }
        catch (Exception e) { Log.Warn("drawing revision observer detach: " + e.Message); }
    }
}
