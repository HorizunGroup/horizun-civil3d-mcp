// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - commands typed by the USER in Civil 3D.
//
//   HZ_STATUS  bridge status: version, contract, permission profile, queue,
//              active drawing, last command, discovery file, log path.
//   HZ_PROBE   dumps the real API signatures of the key Civil 3D classes of
//              THIS installation to a .txt on the Desktop.
//   HZ_CSHARP  turns the C# channel (horizun_c3d_execute_csharp) on or off, with
//              a confirmation; it turns itself off at the next Civil 3D start.
//   HZ_FULLWRITE raises the permission profile to full_write (erase, purge, PDF,
//              layout delete, save...) or lowers it back; same session rules.
// -----------------------------------------------------------------------------
using System.Text;
using Autodesk.AutoCAD.Runtime;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin;

public sealed class AcadCommands
{
    [CommandMethod("HZ_STATUS", CommandFlags.Modal)]
    public void Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Horizun Civil 3D MCP " + App.PluginVersion + "  (Civil 3D " + App.Year + ")  " + App.Edition + " edition");
        sb.AppendLine("Contract: " + Contract.Hash + "  protocol " + Contract.ProtocolVersion);
        if (App.StartupError != null)
        {
            sb.AppendLine();
            sb.AppendLine("BRIDGE NOT STARTED: " + App.StartupError);
        }
        else if (App.Published != null)
        {
            var published = File.Exists(Path.Combine(HorizunPaths.DiscoveryDir(), Discovery.FileName(App.Published.Year, App.Published.Pid)));
            sb.AppendLine("Channel: " + App.Published.PipeName + (published ? "  (published: Claude can connect)" : "  (discovery file MISSING)"));
        }
        var s = Settings.Load();
        sb.AppendLine("Permission profile: " + Settings.ProfileName(s.Profile) + (s.Paused ? "  [PAUSED]" : "") + "  (" + s.Source + ")");
        if (App.Bridge != null)
        {
            var q = App.Bridge.Gate.Observe();
            sb.AppendLine("Queue: " + q["waiting"] + " waiting of " + q["capacity"] + (q["running"] != null ? ", running " + q["running"] : ""));
            sb.AppendLine("Last command: " + (App.Bridge.LastCommand ?? "-") +
                          (App.Bridge.LastCommandUtc is { } t ? "  " + t.ToLocalTime().ToString("HH:mm:ss") : ""));
        }
        sb.AppendLine("Active drawing: " + (AcApp.DocumentManager.MdiActiveDocument?.Name ?? "(none)"));
        sb.AppendLine("Data: " + HorizunPaths.DataRoot());
        if (Log.Path != null) sb.AppendLine("Log: " + Log.Path);
        AcApp.ShowAlertDialog(sb.ToString());
    }

    [CommandMethod("HZ_CSHARP", CommandFlags.Modal)]
    public void CSharpSwitch()
    {
        var path = HorizunPaths.SettingsFile();
        try
        {
            var now = Settings.Load(path);
            if (CSharpChannel.IsOn(now))
            {
                var off = CSharpChannel.Disable(path);
                AcApp.ShowAlertDialog("C# channel OFF.\n\nPermission profile: " + Settings.ProfileName(off.Profile) +
                                      "\nhorizun_c3d_execute_csharp is refused again.");
            }
            else
            {
                var answer = System.Windows.MessageBox.Show(
                    "Turning the C# channel on lets the assistant run arbitrary C# code inside Civil 3D " +
                    "(horizun_c3d_execute_csharp). That code does NOT go through the verification of the typed tools: " +
                    "its results are self-reported.\n\n" +
                    "The permission is shared by ALL Civil 3D instances of this Windows user.\n" +
                    "It turns off when you press the button again, or automatically the next time you open Civil 3D.\n\n" +
                    "Current profile: " + Settings.ProfileName(now.Profile) + "\n\nTurn the C# channel on?",
                    "Horizun - C# channel", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) return;
                var on = CSharpChannel.Enable(path);
                Log.Info("C# channel turned ON by the user (HZ_CSHARP)");
                AcApp.ShowAlertDialog(CSharpChannel.IsOn(on)
                    ? "C# channel ON until you turn it off or close Civil 3D."
                    : "Could not turn the C# channel on: " + on.Source);
            }
        }
        catch (System.Exception e) { AcApp.ShowAlertDialog("C# channel: " + e.Message); }
        finally { Ribbon.Refresh(); }
    }

    [CommandMethod("HZ_FULLWRITE", CommandFlags.Modal)]
    public void FullWriteSwitch()
    {
        var path = HorizunPaths.SettingsFile();
        try
        {
            var now = Settings.Load(path);
            if (CSharpChannel.IsFullWriteOn(now))
            {
                var off = CSharpChannel.DisableFullWrite(path);
                Log.Info("full write turned OFF by the user (HZ_FULLWRITE)");
                AcApp.ShowAlertDialog("Full write OFF.\n\nPermission profile: " + Settings.ProfileName(off.Profile) +
                                      (now.Profile == PermissionProfile.UnsafeCode ? "\nThe C# channel was turned off too." : ""));
            }
            else
            {
                var answer = System.Windows.MessageBox.Show(
                    "Full write allows the assistant to take actions that a simple dry run cannot undo:\n" +
                    "erase objects, purge, delete layouts, plot to PDF, delete points, publish data shortcuts and save the drawing.\n\n" +
                    "Each one still requires a dry run + confirmation and is verified, and every change to the drawing can be undone with UNDO, " +
                    "but files that were written (PDF, save) stay on disk.\n\n" +
                    "The permission is shared by ALL Civil 3D instances of this Windows user.\n" +
                    "It turns off when you press the button again, or automatically the next time you open Civil 3D.\n\n" +
                    "Current profile: " + Settings.ProfileName(now.Profile) + "\n\nTurn full write on?",
                    "Horizun - Full write", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) return;
                var on = CSharpChannel.EnableFullWrite(path);
                Log.Info("full write turned ON by the user (HZ_FULLWRITE)");
                AcApp.ShowAlertDialog(CSharpChannel.IsFullWriteOn(on)
                    ? "Full write ON until you turn it off or close Civil 3D."
                    : "Could not turn it on: " + on.Source);
            }
        }
        catch (System.Exception e) { AcApp.ShowAlertDialog("Full write: " + e.Message); }
        finally { Ribbon.Refresh(); }
    }

    [CommandMethod("HZ_BUILD_FIXTURE", CommandFlags.Modal)]
    public void BuildFixture()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null) return;
        try
        {
            var expected = Fixture.Build(doc);
            var s = (System.Text.Json.Nodes.JsonObject)expected["surfaces"]!;
            var vol = s["HZ_EG_FG_VOL"]!;
            ed!.WriteMessage("\nHorizun: test drawing created (HZ_EG, HZ_FG, HZ_EG_FG_VOL, HZ_LOCKED, breakline and boundary)." +
                             "\n  Expected volume: cut " + Math.Round(vol["cut"]!.GetValue<double>(), 3) +
                             " / fill " + Math.Round(vol["fill"]!.GetValue<double>(), 3) + " m3." +
                             "\n  Expected values: " + Path.Combine(HorizunPaths.FixturesDir(), "fixture-expected.json") +
                             "\n  The drawing was NOT saved. Now run scripts/verify_live.py.");
        }
        catch (HzRefusal e) { ed?.WriteMessage("\nHorizun: " + e.Message); }
        catch (System.Exception e) { Log.Error("HZ_BUILD_FIXTURE", e); ed?.WriteMessage("\nHorizun: could not create the test drawing: " + e.Message); }
    }

    [CommandMethod("HZ_PROBE", CommandFlags.Modal)]
    public void Probe()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        var asm = ApiProbe.FindAssembly("AeccDbMgd");
        if (asm == null)
        {
            ed?.WriteMessage("\nHorizun: AeccDbMgd is not loaded; this is not Civil 3D.");
            return;
        }
        var text = ApiProbe.Dump(asm, ApiProbe.DefaultTypes);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, "Horizun-C3D-probe-" + App.Year + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        File.WriteAllText(path, text);
        ed?.WriteMessage("\nHorizun: API signatures dumped to " + path);
    }
}
