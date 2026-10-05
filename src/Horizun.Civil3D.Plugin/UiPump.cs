// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - crossing to Civil 3D's main thread.
//
// The pipe thread must never touch the drawing. Work is queued in the
// RequestGate and executed HERE, on the main thread, in APPLICATION CONTEXT
// (no command running), under an explicit document lock.
//
// Three wake-ups drive the pump, all on the main thread:
//   * Kick()           BeginInvoke on a hidden control - immediate pickup
//   * a 150 ms timer   keeps the state snapshot fresh, also inside modal loops
//   * Application.Idle runs queued work as soon as a user command ends
//
// The pump NEVER runs work while CMDACTIVE != 0 (a command, script or modal
// dialog is active). It never sends ESC and never cancels the user: it reports
// "busy" with the active command name and lets the Dispatcher refuse. A modal
// window with no command (CMDACTIVE=0) is detected by the main window being
// disabled, and counts as busy too.
//
// Every tick publishes a snapshot (CMDACTIVE, CMDNAMES, active drawing, time)
// that the pipe thread reads without touching Civil 3D. A snapshot that stops
// refreshing means the main thread itself is blocked.
// -----------------------------------------------------------------------------
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin;

internal sealed record UiSnapshot(DateTime AtUtc, int CmdActive, string CmdNames, string? ActiveDocument, int OpenDocuments, bool ModalOpen)
{
    /// <summary>No command, script or dialog, and the main window accepts input (no modal dialog).</summary>
    public bool Quiescent => CmdActive == 0 && !ModalOpen;
    public double AgeSeconds => (DateTime.UtcNow - AtUtc).TotalSeconds;

    public string BusyDescription()
    {
        var parts = new List<string>();
        if (ModalOpen) parts.Add("a modal window is open (Civil 3D main window disabled)");
        if ((CmdActive & 1) != 0) parts.Add("a command is active");
        if ((CmdActive & 2) != 0) parts.Add("a transparent command is active");
        if ((CmdActive & 4) != 0) parts.Add("a script is running");
        if ((CmdActive & 8) != 0) parts.Add("a dialog box is open");
        if ((CmdActive & 16) != 0) parts.Add("DDE is active");
        if ((CmdActive & 32) != 0) parts.Add("AutoLISP is active");
        if ((CmdActive & 64) != 0) parts.Add("an ObjectARX command is active");
        var what = parts.Count == 0 ? "CMDACTIVE=" + CmdActive : string.Join(", ", parts) + " (CMDACTIVE=" + CmdActive + ")";
        return string.IsNullOrWhiteSpace(CmdNames) ? what : what + ", command: " + CmdNames;
    }
}

internal static class UiPump
{
    private static Control? _marshal;
    private static System.Windows.Forms.Timer? _timer;
    private static Dispatcher? _dispatcher;
    private static bool _inPump;
    private static volatile UiSnapshot _state = new(DateTime.MinValue, 0, "", null, 0, false);

    public static UiSnapshot State => _state;

    /// <summary>Must be called on Civil 3D's main thread (IExtensionApplication.Initialize).</summary>
    public static void Start(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _marshal = new Control();
        _marshal.CreateControl();
        _ = _marshal.Handle; // force the window handle on THIS thread
        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => Pump();
        _timer.Start();
        AcApp.Idle += OnIdle;
        Pump();
    }

    public static void Stop()
    {
        try { AcApp.Idle -= OnIdle; } catch { }
        try { _timer?.Stop(); _timer?.Dispose(); } catch { }
        try { _marshal?.Dispose(); } catch { }
        _timer = null;
        _marshal = null;
    }

    /// <summary>Thread-safe: ask the main thread to pump as soon as it can.</summary>
    public static void Kick()
    {
        var m = _marshal;
        if (m == null) return;
        try { m.BeginInvoke(new Action(Pump)); }
        catch (Exception e) { Log.Warn("kick failed: " + e.Message); }
    }

    private static void OnIdle(object? sender, EventArgs e) => Pump();

    private static void Pump()
    {
        if (_inPump) return; // never re-enter while our own command pumps messages
        _inPump = true;
        try
        {
            var snap = Snapshot();
            _state = snap;
            if (!snap.Quiescent || _dispatcher == null || !_dispatcher.Gate.HasPending) return;
            if (!AcApp.DocumentManager.IsApplicationContext) return;
            _dispatcher.RunNext(snap);
            if (_dispatcher.Gate.HasPending) Kick();
        }
        catch (Exception e)
        {
            Log.Error("pump failed", e);
        }
        finally
        {
            _inPump = false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    private static UiSnapshot Snapshot()
    {
        var cmdActive = 0;
        var cmdNames = "";
        string? active = null;
        var count = 0;
        var modal = false;
        try { cmdActive = Convert.ToInt32(AcApp.GetSystemVariable("CMDACTIVE")); } catch { }
        try { cmdNames = Convert.ToString(AcApp.GetSystemVariable("CMDNAMES")) ?? ""; } catch { }
        try
        {
            var dm = AcApp.DocumentManager;
            active = dm.MdiActiveDocument?.Name;
            count = dm.Count;
        }
        catch { }
        try
        {
            var hwnd = AcApp.MainWindow.Handle;
            modal = hwnd != IntPtr.Zero && !IsWindowEnabled(hwnd);
        }
        catch { }
        return new UiSnapshot(DateTime.UtcNow, cmdActive, cmdNames, active, count, modal);
    }
}
