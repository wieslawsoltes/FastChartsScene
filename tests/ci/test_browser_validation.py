"""Portable regressions for exact-build validation; no browser/network needed."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("browser_validation", Path(__file__).with_name("browser_validation.py"))
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.site = self.root / "site" / "FastChartsScene"
        self.site.mkdir(parents=True)
        self.commit = "a" * 40
        self.tree = "b" * 40
        self.artifact = "123456"
        (self.site / "index.html").write_text("<html>build</html>", encoding="utf-8")
        (self.site / ".nojekyll").write_text("", encoding="utf-8")
        (self.site / "app.wasm").write_bytes(b"\0asm-current")
        v.save_json(self.site / "revision.json", {"commit": self.commit, "tree": self.tree})
        self.digest = v.prepare(self.site, self.commit, self.tree)

    def result(self, suite):
        _, _, _, count, error_key = v.SUITES[suite]
        return {"passed": count, "total": count, error_key: [],
                "results": [{"name": f"{suite} case {i}", "passed": True} for i in range(count)]}

    def evidence(self):
        for suite, (_, directory, _, _, _) in v.SUITES.items():
            folder = self.root / f"browser-evidence-{suite}"
            path = folder / directory / "results.json"
            document = self.result(suite)
            v.save_json(path, document)
            v.save_json(v.receipt_path(folder, suite), {
                "version": 1, "suite": suite, "commit": self.commit, "tree": self.tree,
                "manifestSha256": self.digest, "artifactId": self.artifact,
                "status": "success", "scenarios": document["total"],
                "resultsSha256": v.sha256(path),
            })

    def aggregate(self, **changes):
        args = dict(root=self.root, commit=self.commit, tree=self.tree,
                    manifest_hash=self.digest, artifact_id=self.artifact,
                    dependencies={"core": "success", "uno": "success", "browser": "success"})
        args.update(changes)
        return v.aggregate(**args)

    def test_manifest_roundtrip_covers_hidden_files(self):
        v.verify(self.site, self.commit, self.tree, self.digest)
        self.assertIn(".nojekyll", v.load_json(self.site / v.MANIFEST)["files"])
        self.assertEqual(self.digest, v.prepare(self.site, self.commit, self.tree))

    def test_modified_file_rejected(self):
        (self.site / "app.wasm").write_bytes(b"\0asm-older!!")
        with self.assertRaises(v.ValidationError):
            v.verify(self.site, self.commit, self.tree, self.digest)

    def test_missing_file_rejected(self):
        (self.site / "app.wasm").unlink()
        with self.assertRaises(v.ValidationError):
            v.verify(self.site, self.commit, self.tree, self.digest)

    def test_unexpected_file_rejected(self):
        (self.site / "old.js").write_text("stale", encoding="utf-8")
        with self.assertRaises(v.ValidationError):
            v.verify(self.site, self.commit, self.tree, self.digest)

    def test_changed_manifest_hash_rejected(self):
        with self.assertRaises(v.ValidationError):
            v.verify(self.site, self.commit, self.tree, "0" * 64)

    def test_other_commit_and_tree_rejected(self):
        for commit, tree in (("c"*40, self.tree), (self.commit, "c"*40)):
            with self.subTest(commit=commit, tree=tree), self.assertRaises(v.ValidationError):
                v.verify(self.site, commit, tree, self.digest)

    def test_preparation_rejects_wrong_source(self):
        with self.assertRaises(v.ValidationError):
            v.prepare(self.site, "c"*40, self.tree)

    def test_symlink_rejected(self):
        target = self.root / "external.js"
        target.write_text("outside", encoding="utf-8")
        (self.site / "linked.js").symlink_to(target)
        with self.assertRaises(v.ValidationError):
            v.prepare(self.site, self.commit, self.tree)

    def test_duplicate_json_keys_rejected(self):
        path = self.root / "duplicate.json"
        path.write_text('{"passed":1,"passed":2}', encoding="utf-8")
        with self.assertRaises(v.ValidationError):
            v.load_json(path)

    def test_full_sha_required(self):
        for commit, tree in (("a"*7, self.tree), (self.commit, "B"*40)):
            with self.assertRaises(v.ValidationError):
                v.identity(commit, tree)

    def test_all_existing_suites_accepted(self):
        self.evidence()
        result = self.aggregate()
        self.assertEqual(397, result["totalBrowserScenarios"])
        self.assertEqual({"webgl": 177, "skia": 172, "axis": 48}, result["suites"])

    def test_completed_results_required_not_progress_file(self):
        document = self.result("axis")
        del document["passed"]
        path = self.root / "results.json"
        v.save_json(path, document)
        with self.assertRaises(v.ValidationError):
            v.verify_results(path, "axis")

    def test_empty_and_reduced_suites_rejected(self):
        for count in (0, 43):
            document = self.result("axis")
            document.update(results=document["results"][:count], total=count, passed=count)
            path = self.root / "results.json"
            v.save_json(path, document)
            with self.subTest(count=count), self.assertRaises(v.ValidationError):
                v.verify_results(path, "axis")

    def test_failed_or_malformed_results_rejected(self):
        changes = (
            lambda d: d["results"][0].update(passed=False),
            lambda d: d.update(total=True),
            lambda d: d.update(passed=43),
            lambda d: d["results"][0].update(passed="true"),
            lambda d: d["results"][0].update(name=""),
            lambda d: d["results"][0].update(name=d["results"][1]["name"]),
            lambda d: d.pop("errors"),
            lambda d: d.update(errors=["Unhandled page exception"]),
        )
        path = self.root / "results.json"
        for change in changes:
            document = self.result("axis")
            change(document)
            v.save_json(path, document)
            with self.subTest(document=document), self.assertRaises(v.ValidationError):
                v.verify_results(path, "axis")

    def test_missing_suite_receipt_rejected(self):
        self.evidence()
        v.receipt_path(self.root / "browser-evidence-skia", "skia").unlink()
        with self.assertRaises(OSError):
            self.aggregate()

    def test_mismatched_receipt_rejected(self):
        self.evidence()
        path = v.receipt_path(self.root / "browser-evidence-axis", "axis")
        original = v.load_json(path)
        for key, value in (("commit", "c"*40), ("tree", "c"*40), ("artifactId", "999"),
                           ("manifestSha256", "0"*64), ("status", "started"),
                           ("status", "failure"), ("scenarios", 43)):
            with self.subTest(key=key, value=value):
                document = dict(original, **{key: value})
                v.save_json(path, document)
                with self.assertRaises(v.ValidationError):
                    self.aggregate()

    def test_post_certification_result_changes_rejected(self):
        self.evidence()
        path = self.root / "browser-evidence-axis/axis-ticks-results/results.json"
        document = v.load_json(path)
        document["results"][0]["name"] = "Different passing scenario"
        v.save_json(path, document)
        with self.assertRaises(v.ValidationError):
            self.aggregate()

    def test_failed_skipped_or_cancelled_dependency_rejected(self):
        self.evidence()
        for job in ("core", "uno", "browser"):
            for status in ("failure", "skipped", "cancelled"):
                dependencies = {"core": "success", "uno": "success", "browser": "success", job: status}
                with self.subTest(job=job, status=status), self.assertRaises(v.ValidationError):
                    self.aggregate(dependencies=dependencies)

    def test_run_rejects_stale_results_before_launch(self):
        output = self.root / "artifacts"
        v.save_json(output / "axis-ticks-results/results.json", self.result("axis"))
        with self.assertRaisesRegex(v.ValidationError, "pre-existing"):
            v.run_suite(self.root, self.site, output, "axis", self.commit, self.tree,
                        self.digest, self.artifact)


if __name__ == "__main__":
    unittest.main()
