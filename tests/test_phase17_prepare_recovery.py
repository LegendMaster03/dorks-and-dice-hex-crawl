"""Unit tests for live-data cutover preparation; never call real Docker."""
import importlib.util
import json
import re
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[1] / ".github/scripts/phase17-prepare-recovery.py"
SPEC = importlib.util.spec_from_file_location("phase17_prepare_recovery", SCRIPT)
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)

SHA = "184a2cc0caeb935af450b922561baa10ea7fb612"


def sample_docker(*args, **kwargs):
    if args[:2] == ("inspect", prepare.SERVICE):
        return subprocess.CompletedProcess(args, 0, json.dumps([{
            "State": {"Running": True},
            "Mounts": [{"Destination": "/data", "Type": "volume", "RW": True}]
        }]).encode())
    if args[:2] == ("inspect", prepare.POSTGRES):
        return subprocess.CompletedProcess(args, 0, json.dumps([{
            "State": {"Running": True},
            "Config": {"Image": "custom-postgres-with-no-POSTGRES_DB-init"}
        }]).encode())
    if "SHOW server_version_num" in args:
        return subprocess.CompletedProcess(args, 0, b"180005")
    return subprocess.CompletedProcess(args, 0, b"")


class Phase17PrepareRecoveryTests(unittest.TestCase):
    def test_docker_errors_identify_operation_without_disclosing_stderr(self):
        for command, expected in (
            (("exec", prepare.POSTGRES, "pg_dump", "-U", "hex_crawl"), "Docker exec/pg_dump failed (exit 5)"),
            (("exec", "-i", "isolated", "pg_restore", "-U", "hex_crawl"), "Docker exec/pg_restore failed (exit 5)"),
            (("cp", "service:/data/.", "/private/staging"), "Docker cp failed (exit 5)"),
        ):
            with self.subTest(command=command):
                with mock.patch.object(prepare.subprocess, "run",
                                       side_effect=subprocess.CalledProcessError(
                                           5, ["docker", *command], stderr=b"private credential")):
                    with self.assertRaisesRegex(RuntimeError, re.escape(expected)):
                        prepare.docker(*command)

    def test_restore_cleans_only_isolated_instance_and_retains_private_failure_detail(self):
        with tempfile.TemporaryDirectory() as temporary:
            dump = Path(temporary) / "backup.dump"
            dump.write_bytes(b"mock archive")
            operations = []

            def fake_docker(*args, **kwargs):
                operations.append(args)
                if "pg_restore" in args:
                    self.assertIn("--clean", args)
                    self.assertIn("--if-exists", args)
                    self.assertTrue(args[2].startswith("hex-crawl-phase17-restore-isolated-"))
                    kwargs["stderr"].write(b'pg_restore: error: schema "public" already exists')
                    raise RuntimeError("Docker exec/pg_restore failed (exit 1)")
                return subprocess.CompletedProcess(args, 0, b"")

            # A disposable target is never the actual shared PostgreSQL host.
            with mock.patch.dict(prepare.os.environ, {
                "GITHUB_RUN_ID": "isolated", "GITHUB_RUN_ATTEMPT": "1"
            }):
                with mock.patch.object(prepare, "docker", side_effect=fake_docker):
                    with mock.patch.object(prepare.subprocess, "run",
                                           return_value=subprocess.CompletedProcess([], 0, b"1")) as subprocess_run:
                        with self.assertRaisesRegex(RuntimeError, "duplicate object"):
                            prepare.restore_and_verify_database(
                                "postgres:18", dump, "9|1|id|2|id|2|4")
            self.assertTrue(any(op[0] == "run" and "--network" in op for op in operations))
            self.assertTrue(any(call.args[0][:3] == ["docker", "rm", "-f"]
                                for call in subprocess_run.call_args_list))
            self.assertTrue((Path(temporary) / "isolated-postgres-restore.log").exists())
            self.assertFalse(any(op[0] == "exec" and op[1] == prepare.POSTGRES
                                 for op in operations))

    def test_archive_restoration_matches_files_and_detects_symlinks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "src"
            source.mkdir()
            (source / "assets").mkdir()
            (source / "assets" / "image.png").write_bytes(b"png bytes")
            (source / "assets" / "nested").mkdir()
            (source / "assets" / "nested" / "tile.txt").write_text("tile")
            count = prepare.archive_and_verify(
                source, root / "assets.tar.gz", root / "restored")
            self.assertEqual(2, count)
            self.assertEqual(prepare.file_inventory(source),
                             prepare.file_inventory(root / "restored"))
            (source / "outside").symlink_to(root)
            with self.assertRaises(RuntimeError):
                prepare.archive_and_verify(
                    source, root / "unsafe.tar.gz", root / "not-restored")

    def test_schema_ten_does_not_quiesce_or_back_up_existing_app(self):
        with tempfile.TemporaryDirectory() as temporary:
            with mock.patch.object(prepare, "docker", side_effect=sample_docker) as docker:
                with mock.patch.object(prepare, "inventory", return_value="10|1|id|2|id|2|4"):
                    prepare.prepare(SHA, Path(temporary) / "manifest.json")
            self.assertFalse(any(call.args[0] in ("stop", "cp")
                                 for call in docker.call_args_list))
            self.assertFalse((Path(temporary) / "manifest.json").exists())

    def test_unsupported_schema_rejected_before_stopping_writer(self):
        with tempfile.TemporaryDirectory() as temporary:
            with mock.patch.object(prepare, "docker", side_effect=sample_docker) as docker:
                with mock.patch.object(prepare, "inventory", return_value="7|1|id|2|id|2|4"):
                    with self.assertRaisesRegex(RuntimeError, "Unexpected database schema"):
                        prepare.prepare(SHA, Path(temporary) / "manifest.json")
            self.assertFalse(any(call.args[0] == "stop" for call in docker.call_args_list))

    def test_failure_after_quiescence_restarts_old_app_and_never_attests(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            launches = []

            def docker(*args, **kwargs):
                if args[0] == "stop":
                    launches.append("stop")
                    return subprocess.CompletedProcess(args, 0, b"")
                if "pg_dump" in args:
                    raise RuntimeError("synthetic dump failure")
                return sample_docker(*args, **kwargs)

            with mock.patch.object(prepare, "docker", side_effect=docker):
                with mock.patch.object(prepare, "inventory", return_value="9|1|id|2|id|2|4"):
                    with mock.patch.object(prepare.subprocess, "run") as subprocess_run:
                        with self.assertRaisesRegex(RuntimeError, "synthetic dump failure"):
                            prepare.prepare(SHA, root / "manifest.json")
            self.assertEqual(["stop"], launches)
            self.assertTrue(any(call.args[0][:3] == ["docker", "start", prepare.SERVICE]
                                for call in subprocess_run.call_args_list))
            self.assertFalse((root / "manifest.json").exists())

    def test_pg_isready_is_not_enough_for_isolated_restore_readiness(self):
        with tempfile.TemporaryDirectory() as temporary:
            dump = Path(temporary) / "dump"
            dump.write_bytes(b"archive")
            status = subprocess.CompletedProcess([], 0, b"")
            with mock.patch.object(prepare, "docker", side_effect=sample_docker) as docker:
                with mock.patch.object(prepare.subprocess, "run", return_value=status) as run:
                    with mock.patch.object(prepare.time, "sleep"):
                        with self.assertRaisesRegex(RuntimeError, "not ready"):
                            prepare.restore_and_verify_database(
                                "postgres:18", dump, "9|1|id|2|id|2|4")
            self.assertTrue(any(call.args[0][:4] == ["docker", "exec",
                mock.ANY, "psql"] for call in run.call_args_list
                if len(call.args[0]) >= 4))
            self.assertFalse(any("pg_restore" in call.args for call in docker.call_args_list))

    def test_cleanup_preserves_verified_pair_and_live_map_data(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            backup_root = root / "phase17-private-backups"
            retained = backup_root / (SHA[:12] + "-1234567890123")
            stale = backup_root / (SHA[:12] + "-1234567890124")
            retained.mkdir(parents=True)
            stale.mkdir()
            (stale / "private.log").write_text("temporary")
            (retained / "map-volume-source").mkdir()
            (retained / "map-volume-restored").mkdir()
            (retained / "isolated-postgres-restore.log").write_text("temporary")
            dump = retained / "hex-crawl-before-phase17.dump"
            maps = retained / "map-assets-before-phase17.tar.gz"
            dump.write_bytes(b"verified archive")
            maps.write_bytes(b"verified maps")
            manifest = root / "phase17-recovery-attestation.json"
            manifest.write_text(json.dumps({
                "deploymentSha": SHA,
                "postgresBackupPath": str(dump),
                "mapAssetsBackupPath": str(maps)
            }))
            (root / (manifest.name + ".tmp-stale")).write_text("junk")
            with mock.patch.object(prepare, "inventory", return_value="10|1|id|2|id|2|4"):
                with mock.patch.object(prepare.subprocess, "run") as check:
                    prepare.cleanup_after_verified_deployment(SHA, manifest)
            self.assertEqual(1, check.call_count)
            self.assertTrue(dump.exists())
            self.assertTrue(maps.exists())
            self.assertFalse(stale.exists())
            self.assertFalse((retained / "map-volume-source").exists())
            self.assertFalse((retained / "map-volume-restored").exists())
            self.assertFalse((retained / "isolated-postgres-restore.log").exists())
            self.assertFalse((root / (manifest.name + ".tmp-stale")).exists())

    def test_cleanup_never_deletes_recovery_when_schema_not_upgraded(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            backup = root / "phase17-private-backups" / (SHA[:12] + "-1234567890123")
            backup.mkdir(parents=True)
            dump = backup / "hex-crawl-before-phase17.dump"
            maps = backup / "map-assets-before-phase17.tar.gz"
            dump.write_bytes(b"database")
            maps.write_bytes(b"maps")
            manifest = root / "phase17-recovery-attestation.json"
            manifest.write_text(json.dumps({
                "deploymentSha": SHA,
                "postgresBackupPath": str(dump),
                "mapAssetsBackupPath": str(maps)
            }))
            with mock.patch.object(prepare, "inventory", return_value="9|1|id|2|id|2|4"):
                with mock.patch.object(prepare.subprocess, "run") as check:
                    with self.assertRaisesRegex(RuntimeError, "not at schema 10"):
                        prepare.cleanup_after_verified_deployment(SHA, manifest)
            self.assertTrue(dump.is_file() and maps.is_file())
            self.assertEqual(1, check.call_count)

    def test_manifest_only_after_isolated_database_and_map_restore(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            events = []
            restored_image = []

            def docker(*args, **kwargs):
                if args[0] == "stop":
                    events.append("writer stopped")
                elif "pg_dump" in args:
                    kwargs["stdout"].write(b"synthetic test dump")
                    events.append("backup")
                elif args[0] == "cp":
                    (Path(args[-1]) / "tile.png").write_bytes(b"tile content")
                    events.append("map")
                else:
                    return sample_docker(*args, **kwargs)
                return subprocess.CompletedProcess(args, 0, b"")

            with mock.patch.object(prepare, "docker", side_effect=docker):
                with mock.patch.object(prepare, "inventory", return_value="9|1|id|2|id|2|4"):
                    with mock.patch.object(
                        prepare, "restore_and_verify_database",
                        side_effect=lambda *a: (
                            restored_image.append(a[0]), events.append("isolated db")
                        )):
                        with mock.patch.object(prepare.subprocess, "run") as gate:
                            prepare.prepare(SHA, root / "manifest.json")
            manifest = json.loads((root / "manifest.json").read_text())
            self.assertEqual(SHA, manifest["deploymentSha"])
            self.assertTrue(manifest["isolatedPostgresRestoreVerified"])
            self.assertTrue(manifest["isolatedMapAssetRestoreVerified"])
            self.assertEqual("9|1|id|2|id|2|4", manifest["privateWorldInventory"])
            self.assertEqual(manifest["postgresBackupSha256"],
                             prepare.sha256(Path(manifest["postgresBackupPath"])))
            self.assertEqual(manifest["mapAssetsBackupSha256"],
                             prepare.sha256(Path(manifest["mapAssetsBackupPath"])))
            self.assertEqual(1, manifest["restoredMapAssetCount"])
            self.assertEqual(["writer stopped", "backup", "map", "isolated db"], events)
            self.assertEqual(["postgres:18"], restored_image)
            self.assertFalse((Path(manifest["postgresBackupPath"]).parent / "map-volume-source").exists())
            self.assertFalse((Path(manifest["postgresBackupPath"]).parent / "map-volume-restored").exists())
            self.assertEqual(1, gate.call_count)
            self.assertFalse(any(call.args[0][:3] == ["docker", "start", prepare.SERVICE]
                                 for call in gate.call_args_list))


if __name__ == "__main__":
    unittest.main()
