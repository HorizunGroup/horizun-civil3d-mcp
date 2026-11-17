// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - "Horizun Hub" ribbon tab, panel "Horizun C3D MCP".
//
// Buttons only run AutoCAD commands defined in AcadCommands (HZ_STATUS,
// HZ_PROBE), so everything the ribbon does is also reachable from the command
// line. The tab is shared by id with other Horizun Civil 3D tools: if it
// exists, the panel is added to it instead of creating a second tab.
// Never fatal: a missing ribbon must not stop the bridge.
// -----------------------------------------------------------------------------
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin;

internal static class Ribbon
{
    private const string TabId = "HORIZUN_HUB";
    private const string PanelId = "HORIZUN_C3D_MCP";

    public static void Install()
    {
        if (ComponentManager.Ribbon != null) Build();
        else ComponentManager.ItemInitialized += OnItemInitialized;
    }

    private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        ComponentManager.ItemInitialized -= OnItemInitialized;
        try { Build(); } catch (Exception ex) { Log.Warn("ribbon: " + ex.Message); }
    }

    private static void Build()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon == null) return;

        var tab = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
        if (tab == null)
        {
            tab = new RibbonTab { Title = "Horizun Hub", Id = TabId };
            ribbon.Tabs.Add(tab);
        }
        if (tab.Panels.Any(p => p.Source?.Id == PanelId)) return;

        var source = new RibbonPanelSource { Title = "Horizun C3D MCP", Id = PanelId };
        source.Items.Add(Button("Bridge status", "HZ_STATUS", RibbonIcons.Glyph.Status,
            "Status of the Horizun MCP bridge for Civil 3D: version, contract, permission profile, queue and active drawing."));
        _channel = Button(ChannelText(), "HZ_CSHARP", RibbonIcons.Glyph.Channel,
            "Turns the C# channel (horizun_c3d_execute_csharp) on or off, with a confirmation. It turns itself off when Civil 3D restarts.");
        source.Items.Add(_channel);
        _fullWrite = Button(FullWriteText(), "HZ_FULLWRITE", RibbonIcons.Glyph.FullWrite,
            "Raises the permission profile to full write (erase, purge, PDF, delete layouts, save) with a confirmation, or lowers it back. It turns itself off when Civil 3D restarts.");
        source.Items.Add(_fullWrite);
        tab.Panels.Add(new RibbonPanel { Source = source });
#if HZ_DEVTOOLS
        // Developer panel: only in the development build (install.ps1 default); the release package omits it.
        var dev = new RibbonPanelSource { Title = "Horizun C3D MCP - Development", Id = PanelId + "_DEV" };
        dev.Items.Add(Button("API probe", "HZ_PROBE", RibbonIcons.Glyph.Probe,
            "Dumps the real Civil 3D API signatures of this version to a .txt file on the Desktop."));
        dev.Items.Add(Button("Test drawing", "HZ_BUILD_FIXTURE", RibbonIcons.Glyph.Fixture,
            "Creates surfaces with known volumes in a NEW, unsaved drawing, to verify the MCP live."));
        tab.Panels.Add(new RibbonPanel { Source = dev });
#endif
    }

    private static RibbonButton? _channel;
    private static RibbonButton? _fullWrite;

    private static string FullWriteText()
    {
        try { return Horizun.Civil3D.Core.CSharpChannel.IsFullWriteOn(Horizun.Civil3D.Core.Settings.Load()) ? "Full write\non" : "Full write\noff"; }
        catch { return "Full write"; }
    }

    private static string ChannelText()
    {
        try { return Horizun.Civil3D.Core.CSharpChannel.IsOn(Horizun.Civil3D.Core.Settings.Load()) ? "C# channel\non" : "C# channel\noff"; }
        catch { return "C# channel"; }
    }

    /// <summary>Show the channel state on its button (after HZ_CSHARP).</summary>
    public static void Refresh()
    {
        try
        {
            if (_channel != null) _channel.Text = ChannelText();
            if (_fullWrite != null) _fullWrite.Text = FullWriteText();
        }
        catch (Exception e) { Log.Warn("ribbon refresh: " + e.Message); }
    }

    private static RibbonButton Button(string text, string command, RibbonIcons.Glyph glyph, string tooltip)
    {
        var b = new RibbonButton
        {
            Text = text,
            ShowText = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = tooltip,
            CommandParameter = command,
            CommandHandler = new SendCommand(),
        };
        // Icons are cosmetic: a rendering failure must never cost the button.
        try
        {
            b.ShowImage = true;
            b.LargeImage = RibbonIcons.Make(glyph, 32);
            b.Image = RibbonIcons.Make(glyph, 16);
        }
        catch (Exception e) { Log.Warn("ribbon icon: " + e.Message); }
        return b;
    }

    private sealed class SendCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            var cmd = (parameter as RibbonButton)?.CommandParameter as string ?? parameter as string;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (cmd == null || doc == null) return;
            doc.SendStringToExecute("_" + cmd + " ", true, false, false);
        }
    }
}
