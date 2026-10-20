"""Explicit Revit Python-channel receiver; rollback by default, never enables the channel.

Injected variables: doc, uiapp, HORIZUN_ARGS_JSON. Output: __output__.
Revit 2025 API signatures: docs/api-probes/revit2025/terrain-directshape.txt.
"""
from __future__ import print_function
import hashlib
import json
import math
import os
import zipfile
import itertools

MAX_ASSET = 64 * 1024 * 1024
POSITION_TOLERANCE_FT = 1e-7
_TOLERANCE_NOT_GIVEN = object()


def position_tolerance_feet(value=_TOLERANCE_NOT_GIVEN):
    if value is _TOLERANCE_NOT_GIVEN:
        return POSITION_TOLERANCE_FT
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not _finite(value) or value < 0.000001 or value > 1:
        raise ValueError("position_tolerance_mm must be a finite number from 0.000001 to 1 mm.")
    return value / 304.8


def _finite(value):
    return not math.isnan(value) and not math.isinf(value)


def read_package(path, expected_hash):
    if not os.path.isabs(path) or os.path.getsize(path) > 130 * 1024 * 1024:
        raise ValueError("Require an absolute package path within the 130 MiB limit.")
    with open(path, "rb") as stream:
        package_bytes = stream.read(130 * 1024 * 1024 + 1)
    digest = hashlib.sha256(package_bytes).hexdigest()
    if digest != expected_hash:
        raise ValueError("Package SHA-256 differs from the explicitly expected package.")
    # Read the same hashed bytes, so a file replacement cannot change the payload.
    try:
        from io import BytesIO
    except ImportError:
        from StringIO import StringIO as BytesIO
    with zipfile.ZipFile(BytesIO(package_bytes), "r") as archive:
        names = archive.namelist()
        if sorted(names) != ["manifest.json", "terrain.obj", "terrain.xml"]:
            raise ValueError("Expected exactly manifest.json, terrain.obj and terrain.xml.")
        for entry in archive.infolist():
            limit = 128 * 1024 if entry.filename == "manifest.json" else MAX_ASSET
            if entry.file_size > limit or entry.flag_bits & 1:
                raise ValueError("Oversized or encrypted ZIP member.")
        manifest_bytes = archive.read("manifest.json")
        mesh_bytes = archive.read("terrain.obj")
        xml_bytes = archive.read("terrain.xml")
    manifest = json.loads(manifest_bytes.decode("utf-8"))
    mesh = manifest.get("exact_mesh", {})
    if (manifest.get("format") != "horizun.civil3d.revit-terrain/v1" or
            mesh.get("entry") != "terrain.obj" or mesh.get("linear_unit") != "meter" or
            mesh.get("axis_order") != "east north elevation" or
            hashlib.sha256(mesh_bytes).hexdigest() != mesh.get("sha256") or
            hashlib.sha256(xml_bytes).hexdigest() != manifest.get("landxml_sha256")):
        raise ValueError("Manifest format, axes, units or payload hashes are incompatible.")
    vertices, faces = parse_obj(mesh_bytes)
    if len(vertices) != mesh.get("vertices") or len(faces) != mesh.get("faces"):
        raise ValueError("OBJ counts differ from the manifest.")
    return digest, manifest, vertices, faces


def parse_obj(payload):
    vertices, faces = [], []
    if len(payload) > MAX_ASSET:
        raise ValueError("OBJ exceeds 64 MiB.")
    for line in payload.decode("utf-8").splitlines():
        words = line.split()
        if not words or words[0].startswith("#"):
            continue
        if words[0] == "v" and len(words) == 4:
            xyz = tuple(float(n) for n in words[1:])
            if not all(_finite(n) for n in xyz):
                raise ValueError("Nonfinite OBJ vertex.")
            vertices.append(xyz)
            if len(vertices) > 20000:
                raise ValueError("Too many OBJ vertices.")
        elif words[0] == "f" and len(words) == 4:
            indices = tuple(int(n) - 1 for n in words[1:])
            if min(indices) < 0 or len(set(indices)) != 3:
                raise ValueError("Only distinct positive triangle indices are accepted.")
            faces.append(indices)
            if len(faces) > 40000:
                raise ValueError("Too many OBJ faces.")
        else:
            raise ValueError("Unsupported OBJ record; triangles and XYZ vertices only.")
    if len(vertices) < 3 or not faces or any(max(f) >= len(vertices) for f in faces):
        raise ValueError("Missing mesh or out-of-range face index.")
    return vertices, faces


def already_placed(elements, digest):
    return any(e.ApplicationId == "Horizun.Civil3D" and e.ApplicationDataId == digest for e in elements)


def select_coordinate_direction(candidates, tolerance_ft):
    """Candidates are independently readback native shared-coordinate residuals."""
    for name in ("direct", "inverse"):
        residuals = candidates.get(name, [])
        if residuals and all(_finite(v) and 0 <= v <= tolerance_ft for v in residuals):
            return name
    raise ValueError("Neither project transform direction reproduces native shared-coordinate controls.")


def _centroid_bin(triangle, tolerance_ft):
    return tuple(int(math.floor(sum(p[k] for p in triangle) / 3.0 / tolerance_ft)) for k in range(3))


def _triangle_residual(reference, measured):
    return min(max(math.sqrt(sum((a-b) ** 2 for a,b in zip(p,q)))
        for p,q in zip(reference, permutation)) for permutation in itertools.permutations(measured))


def verify_triangles(expected, actual, tolerance_ft=POSITION_TOLERANCE_FT):
    if not _finite(tolerance_ft) or tolerance_ft <= 0:
        raise ValueError("Invalid position verification tolerance.")
    if len(expected) != len(actual):
        raise ValueError("Revit triangle count changed: expected %s, actual %s." % (len(expected), len(actual)))
    expected_groups = {}
    for index, triangle in enumerate(expected):
        expected_groups.setdefault(_centroid_bin(triangle, tolerance_ft), []).append(index)
    consumed = set()
    comparisons = 0
    residual = 0.0
    for triangle in actual:
        center_bin = _centroid_bin(triangle, tolerance_ft)
        candidates = []
        for offset in itertools.product((-1,0,1), repeat=3):
            key = tuple(a+b for a,b in zip(center_bin,offset))
            candidates.extend(i for i in expected_groups.get(key, []) if i not in consumed)
        matching = None
        best = float("inf")
        for index in candidates:
            comparisons += 1
            if comparisons > 2000000:
                raise ValueError("Mesh verification candidate budget exceeded; no placement accepted.")
            distance = _triangle_residual(expected[index], triangle)
            if distance <= tolerance_ft and distance < best:
                matching, best = index, distance
        if matching is None:
            diagnostic = {"matching_tolerance_ft": tolerance_ft,
                          "expected_triangle_sample": expected[:2], "actual_triangle_sample": actual[:2],
                          "scope": "diagnostic only; no tolerance change or accepted geometry"}
            if len(expected) <= 20:
                nearest = []
                for measured in actual:
                    nearest.append(min(_triangle_residual(reference, measured) for reference in expected))
                diagnostic["nearest_triangle_maximum_vertex_residual_ft"] = max(nearest)
                diagnostic["nearest_triangle_residuals_ft"] = nearest
            raise ValueError("Revit changed triangle connectivity or positions beyond the matching guard: " + json.dumps(diagnostic))
        consumed.add(matching)
        residual = max(residual, best)
    if len(consumed) != len(expected):
        raise ValueError("Revit dropped expected triangles.")
    return residual


def successful_report(plan, plan_hash, dry_run, committed, element_id, triangle_count, residual_ft):
    """Revit Python evidence contract, explicitly self-reported rather than host verified."""
    tolerance_ft = plan["position_tolerance_ft"]
    if not _finite(residual_ft) or residual_ft < 0 or residual_ft > tolerance_ft or triangle_count != plan["faces"]:
        raise ValueError("Cannot issue successful evidence for failed mesh measurements.")
    numeric_id = int(element_id)
    if numeric_id <= 0 or dry_run == committed:
        raise ValueError("Inconsistent successful transaction disposition or element id.")
    return {"plan": plan, "plan_hash": plan_hash, "dry_run": dry_run, "committed": committed,
        "status": "verified", "summary": "Stored mesh geometry re-read; transaction group " + ("committed." if committed else "rolled back after rehearsal."),
        "created_ids": [numeric_id] if committed else [], "modified_ids": [], "deleted_ids": [], "warnings": [],
        "host_verified": False,
        "verification": {"checked": True, "evidence": [
            {"check": "geometric triangle count in new transaction", "expected": plan["faces"], "actual": triangle_count, "passed": True},
            {"check": "maximum stored vertex residual", "actual_mm": residual_ft*304.8, "tolerance_mm": tolerance_ft*304.8, "passed": True},
            {"check": "native shared coordinate readback controls", "direction": plan["project_transform_direction"],
             "maximum_residual_mm": plan["native_coordinate_maximum_residual_mm"], "passed": True},
            {"check": "transaction group disposition", "disposition": "committed" if committed else "rolled_back", "passed": True}],
            "source": "script_self_reported_revit_api_reread"},
        "verification_evidence": "script_self_reported_revit_api_reread; independently query created_ids through typed MCP",
        "element_id": numeric_id if committed else None, "stored_triangles": triangle_count,
        "maximum_vertex_residual_ft": residual_ft, "geometry_reread_verified": True,
        "maximum_vertex_residual_mm": residual_ft*304.8, "position_tolerance_mm": tolerance_ft*304.8,
        "verification_scope": "stored geometric triangles and positions; winding and identity of coincident vertices are not verified; shared CRS relationship requires independent survey controls"}


def run_revit(arguments, document):
    import clr
    clr.AddReference("RevitAPI")
    from Autodesk.Revit.DB import (XYZ, Mesh, GeometryInstance, Options, ElementId,
        BuiltInCategory, DirectShape, TessellatedShapeBuilder, TessellatedFace,
        TessellatedShapeBuilderTarget, TessellatedShapeBuilderFallback,
        Transaction, TransactionGroup, TransactionStatus, FilteredElementCollector)
    from System.Collections.Generic import List

    allowed = set(["package", "expected_package_sha256", "target_document", "dry_run", "confirmed_plan_hash", "position_tolerance_mm"])
    if set(arguments) - allowed or not isinstance(arguments.get("dry_run", True), bool):
        raise ValueError("Unexpected argument or invalid dry_run.")
    dry_run = arguments.get("dry_run", True)
    tolerance_ft = position_tolerance_feet(arguments.get("position_tolerance_mm", _TOLERANCE_NOT_GIVEN))
    if document is None or document.IsReadOnly or document.IsFamilyDocument or document.IsModifiable:
        raise ValueError("Require an idle, writable project document without an existing transaction.")
    target = arguments.get("target_document")
    if not target or target not in (document.Title, document.PathName):
        raise ValueError("target_document differs from the active Revit project.")
    digest, manifest, vertices, faces = read_package(arguments["package"], arguments["expected_package_sha256"])
    # A confirmation hash is supplemental, not a reusable create-again instruction.
    # Refuse a package already placed by this receiver rather than making duplicates.
    if already_placed(FilteredElementCollector(document).OfClass(DirectShape), digest):
        raise ValueError("This package already has a Horizun terrain DirectShape in the project.")
    transform = document.ActiveProjectLocation.GetTotalTransform()
    control_indices = sorted(set([0, len(vertices)-1] + list(faces[0]) + [int(i*(len(vertices)-1)/12.0) for i in range(13)]))
    candidate_transforms = {"direct": transform, "inverse": transform.Inverse}
    coordinate_residuals = {}
    for direction, candidate in candidate_transforms.items():
        residuals = []
        for index in control_indices:
            v = vertices[index]
            shared = XYZ(v[0]/0.3048,v[1]/0.3048,v[2]/0.3048)
            internal = candidate.OfPoint(shared)
            measured = document.ActiveProjectLocation.GetProjectPosition(internal)
            residuals.append(math.sqrt((measured.EastWest-shared.X)**2 + (measured.NorthSouth-shared.Y)**2 + (measured.Elevation-shared.Z)**2))
        coordinate_residuals[direction] = residuals
    direction = select_coordinate_direction(coordinate_residuals, min(tolerance_ft, 1e-7))
    placement_transform = candidate_transforms[direction]
    placement = []
    for v in vertices:
        p = placement_transform.OfPoint(XYZ(v[0] / 0.3048, v[1] / 0.3048, v[2] / 0.3048))
        if not XYZ.IsWithinLengthLimits(p):
            raise ValueError("Transformed terrain exceeds Revit design limits.")
        placement.append(p)
    transform_values = [[v.X, v.Y, v.Z] for v in [transform.Origin, transform.BasisX, transform.BasisY, transform.BasisZ]]
    plan = {"package_sha256": digest, "document_title": document.Title, "document_path": document.PathName,
        "project_unique_id": document.ProjectInformation.UniqueId,
        "project_location_unique_id": document.ActiveProjectLocation.UniqueId,
        "shared_transform": transform_values, "vertices": len(vertices), "faces": len(faces),
        "coordinate_operation": "shared metres -> feet -> native-readback-validated active project location transform",
        "project_transform_direction": direction,
        "native_coordinate_control_indices": control_indices,
        "native_coordinate_maximum_residual_mm": max(coordinate_residuals[direction])*304.8,
        "position_tolerance_ft": tolerance_ft, "position_tolerance_mm": tolerance_ft * 304.8,
        "tolerance_source": "explicit_argument" if "position_tolerance_mm" in arguments else "strict_default",
        "receiver": "DirectShape mesh, not editable Toposolid; positions subject to measured tolerance"}
    plan_hash = hashlib.sha256(json.dumps(plan, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()
    if not dry_run and arguments.get("confirmed_plan_hash") != plan_hash:
        raise ValueError("Apply requires the plan hash returned by a matching rollback rehearsal.")
    expected = [[(placement[i].X, placement[i].Y, placement[i].Z) for i in f] for f in faces]
    group = TransactionGroup(document, "Horizun Civil terrain mesh")
    transaction = None
    committed = False
    try:
        if group.Start() != TransactionStatus.Started:
            raise ValueError("Transaction group did not start.")
        transaction = Transaction(document, "Create exact Civil terrain mesh")
        if transaction.Start() != TransactionStatus.Started:
            raise ValueError("Transaction did not start.")
        builder = TessellatedShapeBuilder()
        builder.Target = TessellatedShapeBuilderTarget.Mesh
        # Autodesk supports Mesh/Salvage, not Mesh/Abort. Repair remains refused
        # by HasInvalidData and exact stored-face/position checks before assimilation.
        builder.Fallback = TessellatedShapeBuilderFallback.Salvage
        if not builder.AreTargetAndFallbackCompatible(builder.Target, builder.Fallback):
            raise ValueError("Revit does not support the requested tessellation mode.")
        builder.OpenConnectedFaceSet(False)
        for face in faces:
            loop = List[XYZ]()
            for index in face:
                loop.Add(placement[index])
            builder.AddFace(TessellatedFace(loop, ElementId.InvalidElementId))
        builder.CloseConnectedFaceSet()
        builder.Build()
        result = builder.GetBuildResult()
        if result.HasInvalidData or not result.AreObjectsAvailable:
            raise ValueError("Revit rejected or repaired the tessellation.")
        shape = DirectShape.CreateElement(document, ElementId(BuiltInCategory.OST_GenericModel))
        shape.ApplicationId = "Horizun.Civil3D"
        shape.ApplicationDataId = digest
        shape.SetShape(result.GetGeometricalObjects())
        shape_id = shape.Id
        if transaction.Commit() != TransactionStatus.Committed:
            raise ValueError("Creation transaction did not commit.")
        transaction.Dispose()
        transaction = None
        # NEW transaction after commit: reread actual stored geometry, never builder output.
        verify = Transaction(document, "Verify stored Civil terrain mesh")
        verify.Start()
        try:
            reread = document.GetElement(shape_id)
            actual = []
            def collect(geometry):
                for item in geometry:
                    if isinstance(item, Mesh):
                        for i in range(item.NumTriangles):
                            t = item.get_Triangle(i)
                            actual.append([(t.get_Vertex(j).X, t.get_Vertex(j).Y, t.get_Vertex(j).Z) for j in range(3)])
                    elif isinstance(item, GeometryInstance):
                        collect(item.GetInstanceGeometry())
                    else:
                        raise ValueError("Stored geometry contains an unexpected non-mesh object.")
            collect(reread.get_Geometry(Options()))
            residual = verify_triangles(expected, actual, tolerance_ft)
        finally:
            verify.RollBack()
            verify.Dispose()
        element_id = int(shape_id.Value)
        if dry_run:
            if group.RollBack() != TransactionStatus.RolledBack:
                raise ValueError("Rollback rehearsal did not roll back.")
        else:
            if group.Assimilate() != TransactionStatus.Committed:
                raise ValueError("Verified transaction group did not commit.")
            committed = True
        return successful_report(plan, plan_hash, dry_run, committed, element_id, len(actual), residual)
    finally:
        if transaction is not None:
            if transaction.GetStatus() == TransactionStatus.Started:
                transaction.RollBack()
            transaction.Dispose()
        if group.GetStatus() == TransactionStatus.Started:
            group.RollBack()
        group.Dispose()


if "doc" in globals():
    __output__ = run_revit(json.loads(HORIZUN_ARGS_JSON), doc)
