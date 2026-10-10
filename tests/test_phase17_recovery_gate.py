"""The schema-cutover gate must fail closed before a runner touches tester data."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


GATE = Path(__file__).resolve().parents[1] / ".github/scripts/phase17-recovery-gate.py"
SHA = "a" * 40


class RecoveryGateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        root = Path(self.directory.name)
        pg = root / "pre-migration.dump"
        maps = root / "maps.tar.gz"
        pg.write_bytes(b"postgres-backup-fixture")
        maps.write_bytes(b"map-backup-fixture")
        self.manifest_path = root / "private.json"
        self.manifest = {
            "migration": "hex-crawl-9-to-10",
            "deploymentSha": SHA,
            "postgresBackupPath": str(pg),
            "postgresBackupSha256": hashlib.sha256(pg.read_bytes()).hexdigest(),
            "mapAssetsBackupPath": str(maps),
            "mapAssetsBackupSha256": hashlib.sha256(maps.read_bytes()).hexdigest(),
            "isolatedPostgresRestoreVerified": True,
            "isolatedMapAssetRestoreVerified": True,
            "authorizedForCutover": True,
        }

    def run_gate(self, sha=SHA):
        self.manifest_path.write_text(json.dumps(self.manifest), encoding="utf-8")
        return subprocess.run(
            [sys.executable, str(GATE), str(self.manifest_path), sha],
            check=False, capture_output=True, text=True,
        )

    def test_valid_private_attestation_passes(self):
        result = self.run_gate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_wrong_commit_rejected(self):
        result = self.run_gate("b" * 40)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("exact deployment commit", result.stderr)

    def test_unverified_recovery_and_no_authorization_rejected(self):
        for key in (
            "isolatedPostgresRestoreVerified",
            "isolatedMapAssetRestoreVerified",
            "authorizedForCutover",
        ):
            with self.subTest(key=key):
                self.manifest[key] = False
                result = self.run_gate()
                self.assertNotEqual(0, result.returncode)
                self.manifest[key] = True

    def test_backup_corruption_rejected(self):
        backup = Path(self.manifest["postgresBackupPath"])
        backup.write_bytes(b"changed-after-attestation")
        result = self.run_gate()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("does not match", result.stderr)

    def test_missing_backup_rejected(self):
        Path(self.manifest["mapAssetsBackupPath"]).unlink()
        result = self.run_gate()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("missing or empty", result.stderr)


if __name__ == "__main__":
    unittest.main()
