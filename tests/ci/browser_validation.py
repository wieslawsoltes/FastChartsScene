#!/usr/bin/env python3
"""Build-once browser validation with exact bundle identity and fail-closed evidence.

Standard library only. This does not replace any browser scenario: `run` executes the
existing Node suite, then verifies its completed results before issuing a receipt.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from typing import Any

MANIFEST = "validation-manifest.json"
SUITES = {
    "webgl": ("browser-tests.mjs", "browser-results", "browser-tests.txt", 145, "pageErrors"),
    "skia": ("fallback-tests.mjs", "fallback-results", "fallback-tests.txt", 140, "pageErrors"),
    "axis": ("axis-tick-tests.mjs", "axis-ticks-results", "axis-ticks-tests.txt", 48, "errors"),
}


class ValidationError(ValueError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path: Path) -> Any:
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValidationError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)


def save_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def identity(commit: str, tree: str) -> dict[str, str]:
    if not all(re.fullmatch(r"[0-9a-f]{40}", value) for value in (commit, tree)):
        raise ValidationError("Expected full lowercase Git commit and tree IDs.")
    return {"commit": commit, "tree": tree}


def inventory(site: Path) -> dict[str, dict[str, Any]]:
    if not site.is_dir() or site.is_symlink():
        raise ValidationError("Browser bundle must be an ordinary directory.")
    files = {}
    for path in sorted(site.rglob("*")):
        if path.is_symlink():
            raise ValidationError(f"Symlinks are not permitted in the browser bundle: {path}")
        if path.is_dir():
            continue
        relative = path.relative_to(site).as_posix()
        if relative == MANIFEST:
            continue
        files[relative] = {"bytes": path.stat().st_size, "sha256": sha256(path)}
    if "index.html" not in files or "revision.json" not in files:
        raise ValidationError("Browser bundle is missing index.html or revision.json.")
    return files


def prepare(site: Path, commit: str, tree: str) -> str:
    source = identity(commit, tree)
    if load_json(site / "revision.json") != source:
        raise ValidationError("Built revision.json does not match the checked-out source.")
    save_json(site / MANIFEST, {
        "format": "FastChartsScene.BrowserBundle",
        "version": 1, **source, "files": inventory(site),
    })
    return sha256(site / MANIFEST)


def verify(site: Path, commit: str, tree: str, manifest_hash: str) -> None:
    source = identity(commit, tree)
    if not re.fullmatch(r"[0-9a-f]{64}", manifest_hash):
        raise ValidationError("Expected SHA-256 of the build job's manifest.")
    if sha256(site / MANIFEST) != manifest_hash:
        raise ValidationError("Browser manifest differs from the build job output.")
    manifest = load_json(site / MANIFEST)
    if (manifest.get("format"), manifest.get("version")) != ("FastChartsScene.BrowserBundle", 1):
        raise ValidationError("Unsupported browser manifest.")
    if {key: manifest.get(key) for key in source} != source:
        raise ValidationError("Browser manifest belongs to another source revision.")
    if load_json(site / "revision.json") != source:
        raise ValidationError("Browser revision belongs to another source revision.")
    if manifest.get("files") != inventory(site):
        raise ValidationError("Browser bundle file hashes, sizes or inventory changed.")


def verify_results(path: Path, suite: str) -> int:
    _, _, _, minimum, errors_key = SUITES[suite]
    document = load_json(path)
    rows = document.get("results")
    if not isinstance(rows, list) or len(rows) < minimum:
        raise ValidationError(f"{suite}: expected at least {minimum} completed scenarios.")
    if (type(document.get("total")) is not int or type(document.get("passed")) is not int
            or document["total"] != len(rows) or document["passed"] != len(rows)):
        raise ValidationError(f"{suite}: result totals do not prove a complete passing run.")
    if document.get(errors_key) != []:
        raise ValidationError(f"{suite}: unhandled-error evidence is missing or nonempty.")
    names = set()
    for row in rows:
        if not isinstance(row, dict) or row.get("passed") is not True:
            raise ValidationError(f"{suite}: failed or malformed scenario.")
        name = row.get("name")
        if not isinstance(name, str) or not name.strip() or name in names:
            raise ValidationError(f"{suite}: missing or duplicate scenario name.")
        names.add(name)
    return len(rows)


def receipt_path(output: Path, suite: str) -> Path:
    return output / f"{suite}-receipt.json"


def run_suite(repo: Path, site: Path, output: Path, suite: str,
              commit: str, tree: str, manifest_hash: str, artifact_id: str) -> None:
    if not artifact_id.isdecimal():
        raise ValidationError("Expected the exact build artifact ID.")
    verify(site, commit, tree, manifest_hash)
    script, directory, log_name, _, _ = SUITES[suite]
    output.mkdir(parents=True, exist_ok=True)
    result_path = output / directory / "results.json"
    if result_path.exists():
        raise ValidationError("Refusing to certify pre-existing browser results.")
    receipt = {"version": 1, "suite": suite, **identity(commit, tree),
               "manifestSha256": manifest_hash, "artifactId": artifact_id, "status": "started"}
    save_json(receipt_path(output, suite), receipt)
    env = dict(os.environ)
    # These suites always serve the downloaded build; external test URLs/alternate outputs
    # must not make a different site look like validation of this artifact.
    for name in ("TEST_URL", "TEST_OUTPUT", "AXIS_TEST_OUTPUT", "RENDER_MODE"):
        env.pop(name, None)
    env["SITE_ROOT"] = str(site.parent.resolve())
    env["TEST_OUTPUT"] = str((output / directory).resolve())
    env["AXIS_TEST_OUTPUT"] = str((output / directory).resolve())
    try:
        with (output / log_name).open("w", encoding="utf-8") as log:
            process = subprocess.Popen(["node", script], cwd=repo / "tests/browser",
                                       env=env, stdout=subprocess.PIPE,
                                       stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                                       errors="replace")
            assert process.stdout is not None
            for line in process.stdout:
                sys.stdout.write(line)
                sys.stdout.flush()
                log.write(line)
            code = process.wait()
        if code != 0:
            raise ValidationError(f"{suite}: Node suite exited with {code}.")
        count = verify_results(result_path, suite)
        # Verify again: neither tests nor preparation may mutate the payload being certified.
        verify(site, commit, tree, manifest_hash)
        receipt.update(status="success", scenarios=count, resultsSha256=sha256(result_path))
    except Exception as error:
        receipt.update(status="failure", error=str(error))
        save_json(receipt_path(output, suite), receipt)
        raise
    save_json(receipt_path(output, suite), receipt)


def aggregate(root: Path, commit: str, tree: str, manifest_hash: str,
              artifact_id: str, dependencies: dict[str, str]) -> dict[str, Any]:
    source = identity(commit, tree)
    if dependencies != {"core": "success", "uno": "success", "browser": "success"}:
        raise ValidationError(f"Required jobs did not all pass: {dependencies}")
    counts = {}
    for suite, (_, directory, _, _, _) in SUITES.items():
        folder = root / f"browser-evidence-{suite}"
        receipt = load_json(receipt_path(folder, suite))
        expected = {"version": 1, "suite": suite, **source, "status": "success",
                    "manifestSha256": manifest_hash, "artifactId": artifact_id}
        if any(receipt.get(key) != value for key, value in expected.items()):
            raise ValidationError(f"{suite}: receipt does not certify this build.")
        result_path = folder / directory / "results.json"
        counts[suite] = verify_results(result_path, suite)
        if receipt.get("scenarios") != counts[suite] or receipt.get("resultsSha256") != sha256(result_path):
            raise ValidationError(f"{suite}: result evidence changed after certification.")
    return {**source, "manifestSha256": manifest_hash, "artifactId": artifact_id,
            "suites": counts, "totalBrowserScenarios": sum(counts.values()), "status": "success"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "verify", "run", "aggregate"))
    parser.add_argument("--site", type=Path, default=Path("artifacts/site/FastChartsScene"))
    parser.add_argument("--output", type=Path, default=Path("artifacts"))
    parser.add_argument("--commit", required=True)
    parser.add_argument("--tree", required=True)
    parser.add_argument("--manifest-hash", default="")
    parser.add_argument("--artifact-id", default="")
    parser.add_argument("--suite", choices=tuple(SUITES))
    args = parser.parse_args()
    if args.command == "prepare":
        digest = prepare(args.site, args.commit, args.tree)
        print(f"Verified browser manifest: {digest}")
        if output := os.environ.get("GITHUB_OUTPUT"):
            with open(output, "a", encoding="utf-8") as stream:
                stream.write(f"manifest={digest}\ncommit={args.commit}\ntree={args.tree}\n")
    elif args.command == "verify":
        verify(args.site, args.commit, args.tree, args.manifest_hash)
    elif args.command == "run":
        if args.suite is None:
            parser.error("--suite is required for run")
        run_suite(Path.cwd(), args.site, args.output, args.suite, args.commit, args.tree,
                  args.manifest_hash, args.artifact_id)
    else:
        summary = aggregate(args.output, args.commit, args.tree, args.manifest_hash,
                            args.artifact_id, json.loads(os.environ.get("VALIDATION_DEPENDENCIES", "{}")))
        save_json(args.output / "validation-summary.json", summary)
        print(json.dumps(summary, indent=2))
        if output := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(output, "a", encoding="utf-8") as stream:
                stream.write("## Exact-build browser validation\n\n")
                stream.write(f"Commit `{args.commit}`; tree `{args.tree}`.\n\n")
                for suite, count in summary["suites"].items():
                    stream.write(f"- {suite}: {count}/{count} passed.\n")
                stream.write("\nSame build artifact for every suite; software graphics adapter.\n")


if __name__ == "__main__":
    try:
        main()
    except (ValidationError, OSError, ValueError, KeyError, TypeError) as error:
        print(f"VALIDATION FAILED: {error}", file=sys.stderr)
        sys.exit(1)
