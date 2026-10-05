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
        sb.AppendLine("Horizun Civil 3D MCP " + App.PluginVersion + "  (Civil 3D " + App.Year + ")  edicion " + App.Edition);
        sb.AppendLine("Contrato: " + Contract.Hash + "  protocolo " + Contract.ProtocolVersion);
        if (App.StartupError != null)
        {
            sb.AppendLine();
            sb.AppendLine("PUENTE NO INICIADO: " + App.StartupError);
        }
        else if (App.Published != null)
        {
            var published = File.Exists(Path.Combine(HorizunPaths.DiscoveryDir(), Discovery.FileName(App.Published.Year, App.Published.Pid)));
            sb.AppendLine("Canal: " + App.Published.PipeName + (published ? "  (publicado: Claude puede conectarse)" : "  (archivo de descubrimiento AUSENTE)"));
        }
        var s = Settings.Load();
        sb.AppendLine("Perfil de permisos: " + Settings.ProfileName(s.Profile) + (s.Paused ? "  [PAUSADO]" : "") + "  (" + s.Source + ")");
        if (App.Bridge != null)
        {
            var q = App.Bridge.Gate.Observe();
            sb.AppendLine("Cola: " + q["waiting"] + " en espera de " + q["capacity"] + (q["running"] != null ? ", ejecutando " + q["running"] : ""));
            sb.AppendLine("Ultimo comando: " + (App.Bridge.LastCommand ?? "-") +
                          (App.Bridge.LastCommandUtc is { } t ? "  " + t.ToLocalTime().ToString("HH:mm:ss") : ""));
        }
        sb.AppendLine("Dibujo activo: " + (AcApp.DocumentManager.MdiActiveDocument?.Name ?? "(ninguno)"));
        sb.AppendLine("Datos: " + HorizunPaths.DataRoot());
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
                AcApp.ShowAlertDialog("Canal C# APAGADO.\n\nPerfil de permisos: " + Settings.ProfileName(off.Profile) +
                                      "\nhorizun_c3d_execute_csharp vuelve a rechazarse.");
            }
            else
            {
                var answer = System.Windows.MessageBox.Show(
                    "Encender el canal C# permite que el asistente ejecute codigo C# arbitrario dentro de Civil 3D " +
                    "(horizun_c3d_execute_csharp). Ese codigo NO pasa por la verificacion de las herramientas tipadas: " +
                    "sus resultados son auto-reportados.\n\n" +
                    "Se apaga al pulsar de nuevo el boton o automaticamente la proxima vez que abras Civil 3D.\n\n" +
                    "Perfil actual: " + Settings.ProfileName(now.Profile) + "\n\nEncender el canal C#?",
                    "Horizun - Canal C#", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) return;
                var on = CSharpChannel.Enable(path);
                Log.Info("C# channel turned ON by the user (HZ_CSHARP)");
                AcApp.ShowAlertDialog(CSharpChannel.IsOn(on)
                    ? "Canal C# ENCENDIDO hasta que lo apagues o cierres Civil 3D."
                    : "No se pudo encender el canal C#: " + on.Source);
            }
        }
        catch (System.Exception e) { AcApp.ShowAlertDialog("Canal C#: " + e.Message); }
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
                AcApp.ShowAlertDialog("Escritura completa APAGADA.\n\nPerfil de permisos: " + Settings.ProfileName(off.Profile) +
                                      (now.Profile == PermissionProfile.UnsafeCode ? "\nEl canal C# tambien quedo apagado." : ""));
            }
            else
            {
                var answer = System.Windows.MessageBox.Show(
                    "La escritura completa permite al asistente acciones que no se deshacen con un dry run simple:\n" +
                    "borrar objetos, purgar, eliminar layouts, imprimir a PDF, borrar puntos, publicar accesos directos y guardar el dibujo.\n\n" +
                    "Cada una sigue pidiendo dry run + confirmacion y se verifica, y cada cambio en el dibujo se deshace con UNDO, " +
                    "pero los archivos escritos (PDF, guardado) quedan en disco.\n\n" +
                    "Se apaga al pulsar de nuevo el boton o automaticamente la proxima vez que abras Civil 3D.\n\n" +
                    "Perfil actual: " + Settings.ProfileName(now.Profile) + "\n\nEncender la escritura completa?",
                    "Horizun - Escritura completa", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) return;
                var on = CSharpChannel.EnableFullWrite(path);
                Log.Info("full write turned ON by the user (HZ_FULLWRITE)");
                AcApp.ShowAlertDialog(CSharpChannel.IsFullWriteOn(on)
                    ? "Escritura completa ENCENDIDA hasta que la apagues o cierres Civil 3D."
                    : "No se pudo encender: " + on.Source);
            }
        }
        catch (System.Exception e) { AcApp.ShowAlertDialog("Escritura completa: " + e.Message); }
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
            ed!.WriteMessage("\nHorizun: dibujo de ensayo creado (HZ_EG, HZ_FG, HZ_EG_FG_VOL, HZ_LOCKED, breakline y limite)." +
                             "\n  Volumen esperado: corte " + Math.Round(vol["cut"]!.GetValue<double>(), 3) +
                             " / relleno " + Math.Round(vol["fill"]!.GetValue<double>(), 3) + " m3." +
                             "\n  Valores esperados: " + Path.Combine(HorizunPaths.FixturesDir(), "fixture-expected.json") +
                             "\n  NO se guardo el dibujo. Ahora corre scripts/verify_live.py.");
        }
        catch (HzRefusal e) { ed?.WriteMessage("\nHorizun: " + e.Message); }
        catch (System.Exception e) { Log.Error("HZ_BUILD_FIXTURE", e); ed?.WriteMessage("\nHorizun: fallo al crear el dibujo de ensayo: " + e.Message); }
    }

    [CommandMethod("HZ_PROBE", CommandFlags.Modal)]
    public void Probe()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        var asm = ApiProbe.FindAssembly("AeccDbMgd");
        if (asm == null)
        {
            ed?.WriteMessage("\nHorizun: AeccDbMgd no esta cargado; no es Civil 3D.");
            return;
        }
        var text = ApiProbe.Dump(asm, ApiProbe.DefaultTypes);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, "Horizun-C3D-probe-" + App.Year + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        File.WriteAllText(path, text);
        ed?.WriteMessage("\nHorizun: firmas de la API volcadas en " + path);
    }
}
