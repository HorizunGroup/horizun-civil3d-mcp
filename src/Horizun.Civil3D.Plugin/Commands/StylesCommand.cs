// -----------------------------------------------------------------------------
// horizun_c3d_styles - list / get styles, with WHO USES THEM.
//
// Editing a style changes every object that uses it. The usage reported here
// is what later style-changing tools show in their dry run (blast radius) and
// why they suggest duplicating a shared style instead of editing it.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using CivilEntity = Autodesk.Civil.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class StylesCommand : ICommand
{
    public string Name => "styles";

    /// <summary>Style kind -> catalogue type whose objects carry that style (null = usage not computed).</summary>
    private static readonly Dictionary<string, string?> UsedBy = new()
    {
        ["surface"] = "surface", ["alignment"] = "alignment", ["profile"] = "profile", ["profile_view"] = "profile_view",
        ["feature_line"] = "feature_line", ["corridor"] = "corridor", ["pipe"] = "pipe", ["structure"] = "structure",
        ["point"] = "cogo_point", ["parcel"] = "parcel", ["assembly"] = "assembly",
    };

    internal static StyleCollectionBase Collection(CivilDocument civil, string kind)
    {
        var s = civil.Styles;
        return kind switch
        {
            "surface" => s.SurfaceStyles,
            "alignment" => s.AlignmentStyles,
            "profile" => s.ProfileStyles,
            "profile_view" => s.ProfileViewStyles,
            "feature_line" => s.FeatureLineStyles,
            "corridor" => s.CorridorStyles,
            "pipe" => s.PipeStyles,
            "structure" => s.StructureStyles,
            "point" => s.PointStyles,
            "parcel" => s.ParcelStyles,
            "assembly" => s.AssemblyStyles,
            "sample_line" => s.SampleLineStyles,
            "section" => s.SectionStyles,
            "section_view" => s.SectionViewStyles,
            "grading" => s.GradingStyles,
            "code_set" => s.CodeSetStyles,
            "marker" => s.MarkerStyles,
            "link" => s.LinkStyles,
            "shape" => s.ShapeStyles,
            "slope_pattern" => s.SlopePatternStyles,
            "view_frame" => s.ViewFrameStyles,
            "match_line" => s.MatchLineStyles,
            "label_surface_contour" => s.LabelStyles.SurfaceLabelStyles.ContourLabelStyles,
            "label_surface_spot_elevation" => s.LabelStyles.SurfaceLabelStyles.SpotElevationLabelStyles,
            "label_surface_slope" => s.LabelStyles.SurfaceLabelStyles.SlopeLabelStyles,
            "label_surface_watershed" => s.LabelStyles.SurfaceLabelStyles.WatershedLabelStyles,
            "label_general_note" => s.LabelStyles.GeneralNoteLabelStyles,
            "label_general_line" => s.LabelStyles.GeneralLineLabelStyles,
            "label_general_curve" => s.LabelStyles.GeneralCurveLabelStyles,
            _ => throw new HzRefusal(ErrorCodes.InvalidInput, "Unknown object_type '" + kind + "'."),
        };
    }

    public CommandResult Execute(CommandContext ctx)
    {
        var action = ctx.RequireAction("list", "get");
        var kind = Hz.Str(ctx.Args, "object_type") ?? throw new HzRefusal(ErrorCodes.InvalidInput, "object_type is required.");
        var doc = ctx.Document(forWrite: false);
        var data = new JsonObject { ["object_type"] = kind, ["document"] = doc.Name };

        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            var coll = Collection(civil, kind);
            var usage = Usage(kind, doc.Database, civil, tr, out var usageNote);

            if (action == "list")
            {
                var a = new JsonArray();
                foreach (ObjectId id in coll)
                {
                    var st = (StyleBase)tr.GetObject(id, OpenMode.ForRead);
                    var o = new JsonObject { ["name"] = st.Name, ["handle"] = st.Handle.ToString() };
                    o["used_by_count"] = usage == null ? null : usage.TryGetValue(id, out var l) ? l.Count : 0;
                    a.Add(o);
                }
                data["count"] = a.Count;
                data["styles"] = a;
            }
            else
            {
                var name = Hz.Str(ctx.Args, "name") ?? throw new HzRefusal(ErrorCodes.InvalidInput, "get needs the style name.");
                ObjectId found = ObjectId.Null;
                var names = new List<string>();
                foreach (ObjectId id in coll)
                {
                    var st = (StyleBase)tr.GetObject(id, OpenMode.ForRead);
                    names.Add(st.Name);
                    if (string.Equals(st.Name, name, StringComparison.OrdinalIgnoreCase)) found = id;
                }
                if (found.IsNull)
                    throw new HzRefusal(ErrorCodes.NotFound, "No " + kind + " style named '" + name + "'. Nothing ran.",
                        new JsonObject { ["candidates"] = Hz.Strings(names) });
                var s = (StyleBase)tr.GetObject(found, OpenMode.ForRead);
                var u = new JsonObject();
                var o = new JsonObject { ["name"] = s.Name, ["handle"] = s.Handle.ToString() };
                Safe.Str(o, u, "created_by", () => s.CreateBy);
                Safe.Str(o, u, "date_created", () => s.DateCreated);
                Safe.Str(o, u, "modified_by", () => s.ModifiedBy);
                Safe.Str(o, u, "date_modified", () => s.DateModified);
                if (usage != null)
                {
                    var users = usage.TryGetValue(found, out var l) ? l : new List<JsonObject>();
                    o["used_by_count"] = users.Count;
                    o["used_by"] = Hz.Arr(users.Select(x => (JsonNode?)x));
                    if (users.Count > 1)
                        o["warning"] = "Shared by " + users.Count + " objects: editing this style changes ALL of them. Duplicate it to " +
                                       "change only some.";
                }
                else o["used_by_count"] = null;
                if (u.Count > 0) o["unreadable"] = u;
                data["style"] = o;
            }
            if (usageNote != null) data["usage_note"] = usageNote;
            return 0;
        });
        return CommandResult.Ok(data);
    }

    internal static Dictionary<ObjectId, List<JsonObject>>? Usage(string kind, Database db, CivilDocument civil, Transaction tr, out string? note)
    {
        note = null;
        if (!UsedBy.TryGetValue(kind, out var type) || type == null)
        {
            note = "Usage is not computed for " + kind + " styles in this version (used_by_count is null, not 0).";
            return null;
        }
        var map = new Dictionary<ObjectId, List<JsonObject>>();
        foreach (var id in Catalog.Ids(type, db, civil, tr))
        {
            var obj = tr.GetObject(id, OpenMode.ForRead);
            ObjectId styleId;
            string? name;
            switch (obj)
            {
                case CivilEntity ce: styleId = ce.StyleId; name = ce.Name; break;
                case CogoPoint cp: styleId = cp.StyleId; name = cp.PointNumber.ToString(); break;
                default: continue;
            }
            if (styleId.IsNull) continue;
            if (!map.TryGetValue(styleId, out var list)) map[styleId] = list = new List<JsonObject>();
            list.Add(new JsonObject { ["handle"] = obj.Handle.ToString(), ["name"] = name, ["type"] = type });
        }
        if (type == "cogo_point")
            note = "COGO point usage counts each point's own style; point-group style overrides are not resolved here.";
        return map;
    }
}
