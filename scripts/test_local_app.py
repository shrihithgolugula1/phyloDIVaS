import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import local_app


class LauncherTests(unittest.TestCase):
    def test_checkout_relative_and_seven_apis(self):
        self.assertEqual(local_app.ROOT, Path(local_app.__file__).resolve().parents[1])
        self.assertEqual(len(local_app.SERVICES), 7)
        for name, port in local_app.SERVICES:
            self.assertTrue((local_app.ROOT / name / (name + '.csproj')).is_file())
            self.assertIsInstance(port, int)

    def test_local_config_overrides_environment_without_printing_values(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, 'local-config.json').write_text(json.dumps({'ConnectionStrings__DefaultConnection': 'test-only'}))
            with patch.object(local_app, 'ROOT', Path(directory)):
                self.assertEqual(local_app.environment()['ConnectionStrings__DefaultConnection'], 'test-only')

    def test_rejects_persisted_galaxy_key(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, 'local-config.json').write_text('{"Galaxy__ApiKey":"dummy"}')
            with patch.object(local_app, 'ROOT', Path(directory)):
                with self.assertRaisesRegex(RuntimeError, 'enter them in the app'):
                    local_app.environment()

    def test_occupied_port_does_not_start_or_stop_any_process(self):
        with patch.object(local_app, 'port_open', return_value=True), patch.object(local_app.subprocess, 'Popen') as spawn:
            with self.assertRaisesRegex(RuntimeError, 'Ports already in use'):
                local_app.launch({}, {})
            spawn.assert_not_called()

    def test_missing_dependency_is_actionable(self):
        with patch.object(local_app.shutil, 'which', return_value=None):
            with self.assertRaisesRegex(RuntimeError, 'Missing prerequisites'):
                local_app.dependencies()


if __name__ == '__main__':
    unittest.main()
