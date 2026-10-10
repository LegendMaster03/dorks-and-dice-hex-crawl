#!/usr/bin/env python3
"""Fail closed before an authorized schema-10 deployment touches tester data.

The manifest is an operator-produced attestation from a tested restore of
PostgreSQL and separately stored map assets. This does not perform a backup.
It prevents an automatic main-branch deployment from bypassing that release
gate merely because code validation passed.
"""
import hashlib
import json
import os
import pathlib
import sys


def fail(reason: str) -> None:
    raise SystemExit("Phase 17 recovery gate blocked deployment: " + reason)


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def main() -> None:
    if len(sys.argv) != 3:
        fail("provide the private recovery manifest and exact deployment commit SHA")
    manifest_path = pathlib.Path(sys.argv[1])
    sha = sys.argv[2]
    if not manifest_path.is_absolute() or not manifest_path.is_file():
        fail("the private backup-and-restore attestation is missing")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        fail("recovery attestation is unreadable")
    if not isinstance(manifest, dict):
        fail("recovery attestation must be a JSON object")
    if manifest.get("migration") != "hex-crawl-9-to-10":
        fail("recovery attestation targets a different migration")
    if manifest.get("deploymentSha") != sha:
        fail("recovery attestation does not identify this exact deployment commit")
    if manifest.get("isolatedPostgresRestoreVerified") is not True:
        fail("PostgreSQL restore rehearsal has not been verified")
    if manifest.get("isolatedMapAssetRestoreVerified") is not True:
        fail("map-asset restore rehearsal has not been verified")
    if manifest.get("authorizedForCutover") is not True:
        fail("controlled schema cutover has not been authorized")
    for key, hash_key in (
        ("postgresBackupPath", "postgresBackupSha256"),
        ("mapAssetsBackupPath", "mapAssetsBackupSha256"),
    ):
        value = manifest.get(key)
        if not isinstance(value, str) or not os.path.isabs(value):
            fail(key + " must be an absolute backup path")
        path = pathlib.Path(value)
        if not path.is_file() or path.stat().st_size == 0:
            fail(key + " is missing or empty")
        digest = manifest.get(hash_key)
        if not isinstance(digest, str) or len(digest) != 64:
            fail(hash_key + " is missing")
        if sha256(path) != digest.lower():
            fail(hash_key + " does not match stored backup content")
    print("Phase 17 recovery manifest, backup checksums, and operator authorization verified.")


if __name__ == "__main__":
    main()
