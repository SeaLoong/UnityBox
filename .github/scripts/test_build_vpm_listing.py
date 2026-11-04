import functools
import hashlib
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest import mock
from urllib.error import HTTPError
from zipfile import ZipFile, ZIP_DEFLATED

from build_vpm_listing import build_listing, read_bytes
from package_vpm import build_package


class HistoryHandler(SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        if self.path == "/denied.json":
            self.send_error(403)
        elif self.path == "/unavailable.json":
            self.send_error(503)
        else:
            super().do_GET()


class ListingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.web = self.root / "web"
        self.web.mkdir()
        self.public = self.web / "UnityBox"
        self.public.mkdir()
        self.manifests = []
        handler = functools.partial(HistoryHandler, directory=str(self.web))
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.base = f"http://127.0.0.1:{self.server.server_port}/UnityBox"
        self.environment = mock.patch.dict(os.environ, {"NO_PROXY": "127.0.0.1,localhost", "no_proxy": "127.0.0.1,localhost"})
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def package(self, name="example.one", version="1.0.0"):
        folder = self.repo / "Assets/UnityBox" / name
        folder.mkdir(parents=True)
        manifest = {"name": name, "version": version, "displayName": name}
        path = folder / "package.json"
        path.write_text(json.dumps(manifest))
        (folder / "Tool.cs").write_text("public class Tool {}\n")
        (folder / "Tool.cs.meta").write_text("guid: 0123456789abcdef0123456789abcdef\n")
        self.manifests.append(path)
        return folder

    def publish(self, folder, *, legacy_zip=False):
        manifest = json.loads((folder / "package.json").read_text())
        filename = f"{manifest['name']}-{manifest['version']}.zip"
        archive = self.public / filename
        if legacy_zip:
            with ZipFile(archive, "w", ZIP_DEFLATED) as zipped:
                for path in sorted(folder.rglob("*")):
                    if path.is_file():
                        zipped.write(path, path.relative_to(folder).as_posix())
        else:
            build_package(folder, archive)
        entry = {**manifest, "url": self.base + "/" + filename,
                 "zipSHA256": hashlib.sha256(archive.read_bytes()).hexdigest()}
        listing = {"name": "UnityBox", "packages": {manifest["name"]: {"versions": {manifest["version"]: entry}}}}
        (self.public / "vpm.json").write_text(json.dumps(listing))
        return listing, archive

    def build(self, *, previous=None, output="site"):
        with mock.patch("build_vpm_listing.discover_packages", return_value=self.manifests):
            return build_listing(self.repo, self.root / output, self.base, previous)

    def test_history_zip_and_entry_survive_and_each_manifest_keeps_its_version(self):
        first = self.package(version="0.9.0")
        history, old_archive = self.publish(first)
        old_bytes = old_archive.read_bytes()
        current = json.loads((first / "package.json").read_text())
        current["version"] = "1.0.0"
        (first / "package.json").write_text(json.dumps(current))
        self.package("example.two", "2.3.4")
        with mock.patch.dict(os.environ, {"GITHUB_REF": "refs/tags/v99.0.0", "GITHUB_REF_NAME": "v99.0.0"}):
            self.build()
        result = json.loads((self.root / "site/vpm.json").read_text())
        self.assertEqual(result["packages"]["example.one"]["versions"]["0.9.0"], history["packages"]["example.one"]["versions"]["0.9.0"])
        self.assertEqual((self.root / "site" / old_archive.name).read_bytes(), old_bytes)
        self.assertEqual(set(result["packages"]["example.one"]["versions"]), {"0.9.0", "1.0.0"})
        self.assertEqual(set(result["packages"]["example.two"]["versions"]), {"2.3.4"})
        self.assertIn("vcc://vpm/addRepo?url=" + self.base + "/vpm.json", (self.root / "site/index.html").read_text())
        self.assertEqual(json.loads((first / "package.json").read_text())["version"], "1.0.0")

    def test_same_version_legacy_timestamp_keeps_original_artifact(self):
        folder = self.package()
        history, published = self.publish(folder, legacy_zip=True)
        original = published.read_bytes()
        self.build()
        self.assertEqual((self.root / "site" / published.name).read_bytes(), original)
        self.assertEqual(json.loads((self.root / "site/vpm.json").read_text())["packages"], history["packages"])

    def test_changed_published_version_requires_manifest_increment(self):
        folder = self.package()
        self.publish(folder)
        (folder / "Tool.cs").write_text("public class Changed {}\n")
        with self.assertRaisesRegex(ValueError, "increment its package.json version"):
            self.build()
        self.assertFalse((self.root / "site/vpm.json").exists())

    def test_corrupted_published_archive_aborts_before_listing(self):
        folder = self.package()
        _, published = self.publish(folder)
        published.write_bytes(published.read_bytes() + b"corruption")
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            self.build()
        self.assertFalse((self.root / "site/vpm.json").exists())

    def test_only_first_listing_404_is_empty_history(self):
        self.package()
        self.build()
        self.assertTrue((self.root / "site/vpm.json").is_file())
        for suffix, status in [("denied.json", 403), ("unavailable.json", 503)]:
            with self.assertRaises(HTTPError) as raised:
                read_bytes(self.base.removesuffix("/UnityBox") + "/" + suffix, first_listing=True)
            self.assertEqual(raised.exception.code, status)

    def test_local_history_and_sibling_archives_work_without_http(self):
        folder = self.package(version="0.9.0")
        history, old_archive = self.publish(folder)
        manifest = json.loads((folder / "package.json").read_text())
        manifest["version"] = "1.0.0"
        (folder / "package.json").write_text(json.dumps(manifest))
        with mock.patch("build_vpm_listing.urlopen", side_effect=AssertionError("offline fixture used HTTP")):
            self.build(previous=self.public / "vpm.json")
        result = json.loads((self.root / "site/vpm.json").read_text())
        self.assertEqual(result["packages"]["example.one"]["versions"]["0.9.0"], history["packages"]["example.one"]["versions"]["0.9.0"])
        self.assertEqual((self.root / "site" / old_archive.name).read_bytes(), old_archive.read_bytes())


if __name__ == "__main__":
    unittest.main()
