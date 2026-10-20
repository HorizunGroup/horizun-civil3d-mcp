"""Read-only native shared-coordinate direction probe. No transaction or mutation."""
import json
import math
import clr
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import XYZ

args = json.loads(HORIZUN_ARGS_JSON)
if args.get("target_document") not in (doc.Title, doc.PathName):
    raise ValueError("Wrong active project.")
controls = args.get("controls_shared_m")
if not isinstance(controls, list) or not 3 <= len(controls) <= 16:
    raise ValueError("Supply 3..16 explicit shared XYZ controls in metres.")
location = doc.ActiveProjectLocation
transform = location.GetTotalTransform()
results = []
for label, candidate in [("direct", transform), ("inverse", transform.Inverse)]:
    rows = []
    for control in controls:
        if len(control) != 3 or any(math.isnan(float(v)) or math.isinf(float(v)) for v in control):
            raise ValueError("Invalid control.")
        shared = XYZ(*(float(v) / 0.3048 for v in control))
        internal = candidate.OfPoint(shared)
        reread = location.GetProjectPosition(internal)
        measured = [reread.EastWest, reread.NorthSouth, reread.Elevation]
        residual_ft = math.sqrt(sum((a-b)**2 for a,b in zip(measured, [shared.X, shared.Y, shared.Z])))
        rows.append({"expected_shared_m": control, "internal_m": [internal.X*0.3048,internal.Y*0.3048,internal.Z*0.3048],
                     "native_shared_m": [v*0.3048 for v in measured], "residual_mm": residual_ft*304.8})
    results.append({"direction": label, "controls": rows, "maximum_residual_mm": max(r["residual_mm"] for r in rows)})
__output__ = {"read_only": True, "project": doc.Title, "candidates": results,
              "ground_truth_api": "ActiveProjectLocation.GetProjectPosition(candidate internal point)"}
