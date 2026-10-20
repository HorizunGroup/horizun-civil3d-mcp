// -----------------------------------------------------------------------------
// horizun_c3d_tables - AutoCAD tables from rows or a CSV file (block C).
// Every written cell is re-read with Table.TextString.
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class TablesCommand : ICommand
{
    public string Name => "tables";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) =>
            {
                var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 200);
                var rows = new JsonArray();
                foreach (ObjectId id in Cad.ModelSpace(doc.Database, tr))
                    if (id.ObjectClass.DxfName == "ACAD_TABLE" && rows.Count < limit) rows.Add(Summary((Table)tr.GetObject(id, OpenMode.ForRead)));
                data["tables"] = rows;
            }),
            "get" => WriteFlow.Read(ctx, (doc, tr, data) => data["table"] = Full(Open(doc, tr, Hz.Str(ctx.Args, "handle")!))),
            "create" => Create(ctx),
            _ => SetCells(ctx),
        };
    }

    private static Table Open(Document doc, Transaction tr, string handle) =>
        Cad.Entity(doc.Database, tr, handle) as Table ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + handle.ToUpperInvariant() + " is not a table. Nothing changed.");

    private static JsonObject Summary(Table t) => new()
    {
        ["handle"] = t.Handle.ToString(), ["position"] = Resolve.Json(t.Position), ["rows"] = t.Rows.Count, ["columns"] = t.Columns.Count,
        ["layer"] = t.Layer, ["width"] = Hz.Finite(t.Width, 6), ["height"] = Hz.Finite(t.Height, 6),
    };

    private static JsonObject Full(Table t)
    {
        var o = Summary(t);
        var rows = new JsonArray();
        for (var r = 0; r < t.Rows.Count; r++)
            rows.Add(new JsonArray(Enumerable.Range(0, t.Columns.Count).Select(c => (JsonNode?)JsonValue.Create(t.Cells[r, c].TextString)).ToArray()));
        o["cells"] = rows;
        return o;
    }

    /// <summary>Minimal RFC 4180 CSV reader (quotes, doubled quotes, commas or semicolons).</summary>
    internal static List<List<string>> ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var first = text.Split('\n')[0];
        var sep = first.Count(ch => ch == ';') > first.Count(ch => ch == ',') ? ';' : ',';
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().TrimEnd('\r')); rows.Add(row); }
        return rows.Where(r => !(r.Count == 1 && r[0].Length == 0)).ToList();
    }

    private static CommandResult Create(CommandContext ctx)
    {
        var pos = Resolve.P(ctx.Args["position"]);
        var title = Hz.Str(ctx.Args, "title");
        var widths = Resolve.Numbers(ctx.Args["column_widths"]);
        var rowH = Hz.Num(ctx.Args, "row_height");
        var textH = Hz.Num(ctx.Args, "text_height");
        var data = new List<List<string>>();
        ObjectId styleId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_TABLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                if (ctx.Args["rows"] is JsonArray rows)
                    data = rows.Select(r => ((JsonArray)r!).Select(c => c is JsonValue v && v.TryGetValue<string>(out var s) ? s : c!.ToJsonString()).ToList()).ToList();
                else
                {
                    var path = Hz.Str(ctx.Args, "csv")!;
                    if (!RuntimeCompat.IsPathFullyQualified(path) || !File.Exists(path)) throw new HzRefusal(ErrorCodes.NotFound, "csv must be an existing absolute path. Nothing changed.");
                    data = ReadCsv(path);
                    if (data.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The CSV has no rows. Nothing changed.");
                    var n = data.Max(r => r.Count);
                    foreach (var r in data) while (r.Count < n) r.Add("");
                    if (data.Count > 5000 || n > 100) throw new HzRefusal(ErrorCodes.InvalidInput, "The CSV is larger than 5000 rows x 100 columns. Nothing changed.");
                }
                var cols = data[0].Count;
                if (widths.Count > 0 && widths.Count != cols) throw new HzRefusal(ErrorCodes.InvalidInput, "column_widths has " + widths.Count + " values for " + cols + " columns. Nothing changed.");
                if (Hz.Str(ctx.Args, "style") is { } sn)
                {
                    var dict = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
                    if (!dict.Contains(sn)) throw new HzRefusal(ErrorCodes.NotFound, "No table style '" + sn + "'. Nothing changed.");
                    styleId = dict.GetAt(sn);
                }
                else styleId = db.Tablestyle;
                layerId = Resolve.Layer(db, tr, Hz.Str(ctx.Args, "layer"));
                plan["position"] = Resolve.Json(pos); plan["data_rows"] = data.Count; plan["columns"] = cols; plan["title_row"] = title != null;
                plan["first_row"] = Hz.Strings(data[0]);
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                var t = Cad.New(db, new Table());
                t.TableStyle = styleId;
                t.Position = pos;
                var off = title != null ? 1 : 0;
                var cols = data[0].Count;
                t.SetSize(data.Count + off, cols);
                if (rowH is { } rh) t.SetRowHeight(rh);
                for (var c = 0; c < cols; c++) if (widths.Count > 0) t.Columns[c].Width = widths[c];
                if (title != null)
                {
                    t.MergeCells(CellRange.Create(t, 0, 0, 0, cols - 1));
                    t.Cells[0, 0].TextString = title;
                    if (textH is { } th0) t.Cells[0, 0].TextHeight = th0 * 1.25;
                }
                for (var r = 0; r < data.Count; r++)
                    for (var c = 0; c < cols; c++)
                    {
                        t.Cells[r + off, c].TextString = data[r][c];
                        if (textH is { } th) t.Cells[r + off, c].TextHeight = th;
                    }
                t.GenerateLayout();
                id = Cad.Append(db, tr, t, layerId);
            },
            (doc, tr, v, after) =>
            {
                var t = (Table)tr.GetObject(id, OpenMode.ForRead);
                var off = title != null ? 1 : 0;
                v.Check("rows", data.Count + off, t.Rows.Count, t.Rows.Count == data.Count + off);
                v.Check("columns", data[0].Count, t.Columns.Count, t.Columns.Count == data[0].Count);
                if (title != null) v.Text("title", title, t.Cells[0, 0].TextString, false);
                var bad = 0;
                for (var r = 0; r < data.Count && r + off < t.Rows.Count; r++)
                    for (var c = 0; c < data[r].Count && c < t.Columns.Count; c++)
                        if (t.Cells[r + off, c].TextString != data[r][c] && bad++ < 20) v.Text("cell " + (r + off) + "," + c, data[r][c], t.Cells[r + off, c].TextString, false);
                v.Check("cells matching", data.Sum(r => r.Count), data.Sum(r => r.Count) - bad, bad == 0);
                after["table"] = Summary(t);
            });
    }

    private static CommandResult SetCells(CommandContext ctx)
    {
        var cells = ((JsonArray)ctx.Args["cells"]!).Select(n =>
        {
            var o = (JsonObject)n!;
            var v = o["value"] is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : o["value"]!.ToJsonString();
            return (R: (int)Hz.Num(o, "row")!.Value, C: (int)Hz.Num(o, "col")!.Value, V: v);
        }).ToList();
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_TABLES",
            (doc, tr, plan) =>
            {
                var t = Open(doc, tr, Hz.Str(ctx.Args, "handle")!);
                Cad.Editable(t, tr);
                id = t.ObjectId;
                var bad = cells.Where(c => c.R >= t.Rows.Count || c.C >= t.Columns.Count).ToList();
                if (bad.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, bad.Count + " cell(s) are outside the " + t.Rows.Count + " x " + t.Columns.Count + " table. Nothing changed.");
                plan["table"] = Summary(t); plan["cells"] = cells.Count;
                plan["before"] = new JsonArray(cells.Take(50).Select(c => (JsonNode)new JsonObject { ["row"] = c.R, ["col"] = c.C, ["value"] = t.Cells[c.R, c.C].TextString, ["new"] = c.V }).ToArray());
            },
            (doc, tr) =>
            {
                var t = (Table)tr.GetObject(id, OpenMode.ForWrite);
                foreach (var c in cells) t.Cells[c.R, c.C].TextString = c.V;
                t.GenerateLayout();
            },
            (doc, tr, v, after) =>
            {
                var t = (Table)tr.GetObject(id, OpenMode.ForRead);
                foreach (var c in cells) v.Text("cell " + c.R + "," + c.C, c.V, t.Cells[c.R, c.C].TextString, false);
                after["table"] = Summary(t);
            });
    }
}
