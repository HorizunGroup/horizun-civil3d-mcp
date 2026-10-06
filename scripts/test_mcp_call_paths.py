import json
from pathlib import Path
import tempfile
import unittest
from mcp_call import installed_server


class InstalledPaths(unittest.TestCase):
    def test_canonical_fallback(self):
        with tempfile.TemporaryDirectory() as temp:
            self.assertEqual(Path(installed_server(temp)), Path(temp)/'server'/'horizun-civil3d-mcp.exe')

    def test_isolated_server_inside_product(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = str(Path(temp)/'server-releases'/'0.9.2-final')
            (Path(temp)/'manifest.json').write_text(json.dumps(dict(server_dir=directory)), encoding='utf-8')
            self.assertEqual(Path(installed_server(temp)), Path(directory)/'horizun-civil3d-mcp.exe')

    def test_invalid_or_outside_manifest_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            for directory in (None, True, '', 'relative', str(Path(temp).parent/'outside')):
                with self.subTest(directory=directory):
                    (Path(temp)/'manifest.json').write_text(json.dumps(dict(server_dir=directory)), encoding='utf-8')
                    with self.assertRaises(RuntimeError):
                        installed_server(temp)


if __name__ == '__main__':
    unittest.main()
