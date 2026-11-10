import pathlib
import tempfile
import unittest
from unittest.mock import patch
import publish_current as p


class PublicationTests(unittest.TestCase):
    def test_secrets_removed_from_settings(self):
        value = {"ConnectionStrings": {"Db": "private"}, "AWS": {"ClientSecret": "private"}, "Galaxy": {"ApiKey": "private", "BaseUrl": "https://usegalaxy.eu"}}
        clean = p.scrub_config(value)
        self.assertEqual(clean["ConnectionStrings"]["Db"], "")
        self.assertEqual(clean["AWS"]["ClientSecret"], "")
        self.assertEqual(clean["Galaxy"]["ApiKey"], "")
        self.assertEqual(clean["Galaxy"]["BaseUrl"], "https://usegalaxy.eu")

    def test_snapshot_sanitizes_configuration_without_editing_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            path = root / 'appsettings.json'
            original = '{"ConnectionStrings":{"Db":"private"}}'
            path.write_text(original)
            with patch.object(p, 'files', return_value=['appsettings.json']):
                result = p.snapshot(root)
            self.assertNotIn(b'private', result['appsettings.json'])
            self.assertEqual(path.read_text(), original)

    def test_refuses_to_export_into_source(self):
        root = pathlib.Path('/project')
        with self.assertRaisesRegex(RuntimeError, 'separate sibling'):
            p.publish(root, root)

    def test_snapshot_digest_is_deterministic_and_detects_changes(self):
        self.assertEqual(p.digest({'a': b'1', 'b': b'2'}), p.digest({'b': b'2', 'a': b'1'}))
        self.assertNotEqual(p.digest({'a': b'1'}), p.digest({'a': b'2'}))


if __name__ == '__main__':
    unittest.main()
