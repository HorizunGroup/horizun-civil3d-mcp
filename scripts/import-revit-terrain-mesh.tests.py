"""Pure payload and geometry guards; no Revit host or model is opened."""
import importlib.util
import pathlib
import unittest
import tempfile
import zipfile
import json
import hashlib
import math
from collections import namedtuple

spec = importlib.util.spec_from_file_location("receiver", pathlib.Path(__file__).with_name("import-revit-terrain-mesh.py"))
receiver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(receiver)


class ReceiverGuards(unittest.TestCase):
    def test_evidence_contract_exposes_integer_ids_and_never_claims_host_verification(self):
        plan = {"position_tolerance_ft": 0.01/304.8, "faces": 2, "project_transform_direction": "direct",
                "native_coordinate_maximum_residual_mm": 6.125e-12}
        apply = receiver.successful_report(plan,"hash",False,True,1059845,2,0.00299379/304.8)
        # JSON integer is required by the real bridge's CreatedIds observer.
        encoded = json.loads(json.dumps(apply))
        self.assertIsInstance(encoded["created_ids"][0], int)
        self.assertEqual([1059845], encoded["created_ids"])
        self.assertEqual("verified", encoded["status"])
        self.assertTrue(encoded["verification"]["checked"])
        self.assertGreater(len(encoded["verification"]["evidence"]),0)
        self.assertFalse(encoded["host_verified"])
        rehearsal = receiver.successful_report(plan,"hash",True,False,1059845,2,0.00299379/304.8)
        self.assertEqual([], rehearsal["created_ids"])
        self.assertIsNone(rehearsal["element_id"])
        self.assertEqual("rolled_back", rehearsal["verification"]["evidence"][-1]["disposition"])
        with self.assertRaises(ValueError):
            receiver.successful_report(plan,"hash",False,True,1059845,2,0.1/304.8)
        with self.assertRaises(ValueError):
            receiver.successful_report(plan,"hash",False,True,1059845,3,0)

    def test_native_readback_selects_shared_to_internal_direction_for_rotated_translated_project(self):
        angle = math.radians(37)
        cosine, sine = math.cos(angle), math.sin(angle)
        translation = (27.1509466473, 3.0908362624, 0)
        controls = [(0,0,100), (10,0,100), (0,10,102)]
        def direct(p):
            return (cosine*p[0]+sine*p[1]+translation[0], -sine*p[0]+cosine*p[1]+translation[1], p[2])
        def inverse(p):
            x,y = p[0]-translation[0],p[1]-translation[1]
            return (cosine*x-sine*y,sine*x+cosine*y,p[2])
        candidates = {}
        for label, transform in [("direct",direct),("inverse",inverse)]:
            candidates[label] = [math.sqrt(sum((a-b)**2 for a,b in zip(inverse(transform(p)),p)))/0.3048 for p in controls]
        self.assertEqual("direct", receiver.select_coordinate_direction(candidates,1e-7))
        self.assertGreater(max(candidates["inverse"]),1)
        self.assertEqual("direct",receiver.select_coordinate_direction({"direct":[0],"inverse":[0]},1e-7))
        with self.assertRaises(ValueError):
            receiver.select_coordinate_direction({"direct":[1],"inverse":[1]},1e-7)

    def test_repeated_package_is_refused_without_confusing_other_importers(self):
        Shape = namedtuple("Shape", "ApplicationId ApplicationDataId")
        self.assertTrue(receiver.already_placed([Shape("Horizun.Civil3D", "abc")], "abc"))
        self.assertFalse(receiver.already_placed([Shape("Other", "abc")], "abc"))
        self.assertFalse(receiver.already_placed([Shape("Horizun.Civil3D", "different")], "abc"))

    def test_mesh_indices_and_coordinates_survive(self):
        vertices, faces = receiver.parse_obj(b"# meter\nv 0 0 1\nv 1 0 2\nv 0 1 3\nf 1 2 3\n")
        self.assertEqual([(0, 1, 2)], faces)
        self.assertEqual((0, 1, 3), vertices[2])

    def test_invalid_and_nonfinite_records_refuse(self):
        for payload in [b"v nan 0 0\n", b"v 0 0 0\nf 1 2 3\n", b"g Object\n",
                        b"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 1 3\n"]:
            with self.assertRaises(ValueError):
                receiver.parse_obj(payload)

    def test_triangle_reordering_is_not_connectivity_change(self):
        a = [(0, 0, 0), (1, 0, 0), (0, 1, 1)]
        self.assertEqual(0, receiver.verify_triangles([a], [[a[2], a[0], a[1]]]))

    def test_missing_changed_and_duplicate_faces_refuse(self):
        a = [(0, 0, 0), (1, 0, 0), (0, 1, 1)]
        b = [(0, 0, 0), (1, 0, 0), (0, 1, 2)]
        for actual in [[], [b], [a, a]]:
            with self.assertRaises(ValueError):
                receiver.verify_triangles([a], actual)

    def test_matching_reports_numeric_residual(self):
        a = [(0, 0, 0), (1, 0, 0), (0, 1, 1)]
        b = [(0, 0, 1e-9), (1, 0, 0), (0, 1, 1)]
        self.assertAlmostEqual(1e-9, receiver.verify_triangles([a], [b]))

    def test_numeric_rounding_boundary_does_not_refuse_positions_within_tolerance(self):
        # These vertices round to different previous keys, although only 2e-9 ft apart.
        a = [(0.49e-7, 0, 0), (1, 0, 0), (0, 1, 1)]
        b = [(0.51e-7, 0, 0), (1, 0, 0), (0, 1, 1)]
        self.assertAlmostEqual(2e-9, receiver.verify_triangles([a], [b]))

    def test_float_mesh_error_above_tolerance_remains_refused_with_measured_diagnostic(self):
        a = [(0, 0, 328.083989501312), (1, 0, 329), (0, 1, 330)]
        b = [(0, 0, 328.083984375), (1, 0, 329), (0, 1, 330)]
        with self.assertRaisesRegex(ValueError, "nearest_triangle_maximum_vertex_residual_ft"):
            receiver.verify_triangles([a], [b])

    def test_explicit_engineering_tolerance_accepts_measured_float_precision(self):
        a = [(0, 0, 334.64566929133855), (1, 0, 329), (0, 1, 330)]
        b = [(0, 0, 334.64566040039062), (1, 0, 329), (0, 1, 330)]
        with self.assertRaises(ValueError):
            receiver.verify_triangles([a], [b])
        residual = receiver.verify_triangles([a], [b], receiver.position_tolerance_feet(0.01))
        self.assertLess(residual * 304.8, 0.01)
        self.assertGreater(residual * 304.8, 0.002)
        with self.assertRaises(ValueError):
            receiver.verify_triangles([a], [b], receiver.position_tolerance_feet(0.001))

    def test_tolerance_is_explicit_bounded_and_never_automatically_enlarged(self):
        self.assertEqual(1e-7, receiver.position_tolerance_feet())
        self.assertEqual(1 / 304.8, receiver.position_tolerance_feet(1))
        self.assertGreater(receiver.position_tolerance_feet(0.000001), 0)
        for value in [None, 0, -1, 1.01, 0.0000001, True, "0.01", float("nan"), float("inf")]:
            with self.assertRaises(ValueError):
                receiver.position_tolerance_feet(value)

    def test_package_binds_manifest_and_payload_hashes(self):
        obj = b"v 0 0 1\nv 1 0 2\nv 0 1 3\nf 1 2 3\n"
        xml = b"<LandXML/>"
        manifest = {"format": "horizun.civil3d.revit-terrain/v1", "landxml_sha256": hashlib.sha256(xml).hexdigest(),
                    "exact_mesh": {"entry": "terrain.obj", "linear_unit": "meter", "axis_order": "east north elevation",
                                   "sha256": hashlib.sha256(obj).hexdigest(), "vertices": 3, "faces": 1}}
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "terrain.zip"
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("manifest.json", json.dumps(manifest))
                archive.writestr("terrain.obj", obj)
                archive.writestr("terrain.xml", xml)
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(3, len(receiver.read_package(str(path), digest)[2]))
            with self.assertRaises(ValueError):
                receiver.read_package(str(path), "0" * 64)
            manifest["exact_mesh"]["sha256"] = "0" * 64
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("manifest.json", json.dumps(manifest))
                archive.writestr("terrain.obj", obj)
                archive.writestr("terrain.xml", xml)
            with self.assertRaises(ValueError):
                receiver.read_package(str(path), hashlib.sha256(path.read_bytes()).hexdigest())

    def test_zip_extra_members_are_refused_without_extracting(self):
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "terrain.zip"
            with zipfile.ZipFile(path, "w") as archive:
                for name in ["manifest.json", "terrain.obj", "terrain.xml", "../outside"]:
                    archive.writestr(name, b"")
            with self.assertRaises(ValueError):
                receiver.read_package(str(path), hashlib.sha256(path.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
