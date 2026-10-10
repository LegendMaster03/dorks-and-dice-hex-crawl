#!/usr/bin/env python3
"""Prepare a *real* private Phase 17 recovery attestation on the deployment host.

Requires Docker on the self-hosted deployment runner. Never runs DDL or writes
to the live PostgreSQL database. The old application is stopped to make the
DB dump and map-volume copy a consistent single-writer snapshot. Both artifacts
are restored and independently compared before emitting the existing attestation.
An unsuccessful preparation restarts the old application. A successful one
leaves it stopped for the immediately following deployment step.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tarfile
import time


SERVICE = "dorks-and-dice-hex-crawl"
POSTGRES = os.environ.get("PHASE17_POSTGRES_CONTAINER", "ix-dorks-and-dice-postgres-postgres-1")
DATABASE = "hex_crawl"
DB_USER = "hex_crawl"
DEFAULT_MANIFEST = "/mnt/HDDs/www/dorks-and-dice-hex-crawl/phase17-recovery-attestation.json"

# Compare database generation, complete row counts and sorted ID/version
# fingerprints after the isolated restore. No saved content reaches CI logs.
INVENTORY_SQL = """
SELECT
  (SELECT coalesce(max(version),0) FROM hex_crawl_schema_migrations),
  (SELECT count(*) FROM overworlds),
  (SELECT coalesce(md5(string_agg(id::text || ':' || version::text, ',' ORDER BY id)), md5('')) FROM overworlds),
  (SELECT count(*) FROM expeditions),
  (SELECT coalesce(md5(string_agg(id::text || ':' || version::text, ',' ORDER BY id)), md5('')) FROM expeditions),
  (SELECT count(*) FROM campaign_procedure_revisions),
  (SELECT count(*) FROM expedition_events);
"""


def docker(*args, stdout=None, stdin=None):
    # Docker, PostgreSQL and restore diagnostics could contain private values.
    # Fail closed while redacting subprocess stderr from public Actions logs.
    return subprocess.run(["docker", *args], check=True, stdin=stdin,
                          stdout=stdout if stdout is not None else subprocess.PIPE,
                          stderr=subprocess.DEVNULL)


def inventory(container):
    return docker("exec", container, "psql", "-X", "-At", "-v", "ON_ERROR_STOP=1",
                  "-U", DB_USER, "-d", DATABASE, "-c", INVENTORY_SQL).stdout.decode().strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def file_inventory(directory):
    result = {}
    for path in sorted(directory.rglob("*")):
        if path.is_symlink():
            raise RuntimeError("Map volume contains a symbolic link; require operator-assisted backup")
        if path.is_file():
            result[path.relative_to(directory).as_posix()] = (path.stat().st_size, sha256(path))
        elif not path.is_dir():
            raise RuntimeError("Map volume contains an unsupported special file")
    return result


def archive_and_verify(source, backup_path, restored):
    original = file_inventory(source)
    with tarfile.open(backup_path, "w:gz") as output:
        for path in sorted(source.rglob("*")):
            output.add(path, arcname=path.relative_to(source).as_posix(), recursive=False)
    backup_path.chmod(0o600)
    restored.mkdir(mode=0o700)
    with tarfile.open(backup_path, "r:gz") as archive:
        for member in archive:
            dest = restored / member.name
            if dest.resolve() != restored.resolve() and restored.resolve() not in dest.resolve().parents:
                raise RuntimeError("Unsafe path encountered in map recovery rehearsal")
            if member.isdir():
                dest.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                dest.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(member) as stream, dest.open("xb") as output:
                    shutil.copyfileobj(stream, output)
            else:
                raise RuntimeError("Map recovery archive contains an unsupported entry")
    if file_inventory(restored) != original:
        raise RuntimeError("Isolated map-asset recovery did not match its source")
    return len(original)


def restore_and_verify_database(postgres_image, dump, expected_inventory):
    name = "hex-crawl-phase17-restore-" + os.environ.get("GITHUB_RUN_ID", str(os.getpid()))
    name += "-" + os.environ.get("GITHUB_RUN_ATTEMPT", "1")
    # This container is disposable and has NO network, production volume or
    # credentials. Use the installed server's exact image to avoid pg version
    # mismatches. It is destroyed even if the rehearsal fails.
    docker("run", "-d", "--rm", "--network", "none", "--name", name,
           "-e", "POSTGRES_USER=" + DB_USER,
           "-e", "POSTGRES_DB=" + DATABASE,
           "-e", "POSTGRES_PASSWORD=isolated-test-only",
           postgres_image)
    try:
        for _ in range(60):
            ready = subprocess.run(["docker", "exec", name, "pg_isready",
                                    "-U", DB_USER, "-d", DATABASE],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if ready.returncode == 0:
                break
            time.sleep(1)
        else:
            raise RuntimeError("Isolated PostgreSQL was not ready for restore")
        with dump.open("rb") as stream:
            docker("exec", "-i", name, "pg_restore", "--exit-on-error",
                   "--no-owner", "--no-privileges", "-U", DB_USER,
                   "-d", DATABASE, stdin=stream)
        if inventory(name) != expected_inventory:
            raise RuntimeError("Isolated PostgreSQL restore inventory differs from source")
    finally:
        subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, check=False)


def prepare(sha, manifest):
    if len(sha) != 40 or any(ch not in "0123456789abcdef" for ch in sha):
        raise ValueError("Expected a full lowercase deployment commit SHA")
    # Refuse to write to the wrong database or map storage.
    image_record = json.loads(docker("inspect", SERVICE).stdout)[0]
    if not image_record["State"]["Running"]:
        raise RuntimeError("Previous application is not running")
    mounts = image_record["Mounts"]
    if len([m for m in mounts if m.get("Destination") == "/data"
            and m.get("Type") == "volume" and m.get("RW")]) != 1:
        raise RuntimeError("Expected private map-asset Docker volume at /data")
    postgres_record = json.loads(docker("inspect", POSTGRES).stdout)[0]
    if not postgres_record["State"]["Running"]:
        raise RuntimeError("Expected PostgreSQL instance is not running")
    before = inventory(POSTGRES)
    version = int(before.split("|", 1)[0])
    if version == 10:
        print("Existing database is already schema 10; Phase 17 cutover backup is no longer necessary.")
        return
    if version not in (8, 9):
        raise RuntimeError("Unexpected database schema; do not reset the database")

    manifest = manifest.resolve()
    if not manifest.parent.is_dir() or manifest.is_symlink():
        raise RuntimeError("Private recovery manifest directory does not exist or is unsafe")
    root = manifest.parent / "phase17-private-backups"
    root.mkdir(mode=0o700, exist_ok=True)
    if root.is_symlink():
        raise RuntimeError("Recovery backup root cannot be a symlink")
    root.chmod(0o700)
    backup_dir = root / (sha[:12] + "-" + str(time.time_ns()))
    backup_dir.mkdir(mode=0o700)

    # All remaining work until the next deployment step has exclusive Hex
    # Crawl writer quiescence. If ANY backup/rehearsal check fails, start the
    # original container again. The production DB is never restored or reset.
    stopped = False
    succeeded = False
    try:
        stopped = True
        docker("stop", SERVICE)
        if inventory(POSTGRES) != before:
            raise RuntimeError("Database changed during writer cutover")
        dump = backup_dir / "hex-crawl-before-phase17.dump"
        with dump.open("xb") as output:
            docker("exec", POSTGRES, "pg_dump", "-Fc", "--no-owner",
                   "-U", DB_USER, "-d", DATABASE, stdout=output)
        dump.chmod(0o600)
        if dump.stat().st_size == 0:
            raise RuntimeError("Pre-cutover database dump is empty")
        source = backup_dir / "map-volume-source"
        source.mkdir(mode=0o700)
        docker("cp", SERVICE + ":/data/.", str(source))
        archive = backup_dir / "map-assets-before-phase17.tar.gz"
        count = archive_and_verify(source, archive, backup_dir / "map-volume-restored")
        if archive.stat().st_size == 0:
            raise RuntimeError("Pre-cutover map-asset backup is empty")
        restored_before = inventory(POSTGRES)
        if restored_before != before:
            raise RuntimeError("Database changed while taking the backup")
        postgres_image = postgres_record["Config"]["Image"]
        restore_and_verify_database(postgres_image, dump, before)

        record = {
            "migration": "hex-crawl-9-to-10",
            "deploymentSha": sha,
            "postgresBackupPath": str(dump),
            "postgresBackupSha256": sha256(dump),
            "mapAssetsBackupPath": str(archive),
            "mapAssetsBackupSha256": sha256(archive),
            "isolatedPostgresRestoreVerified": True,
            "isolatedMapAssetRestoreVerified": True,
            # The owner approved a safe Phase 17 dev/test cutover. This flag
            # is set only on completion of both actual recovery rehearsals.
            "authorizedForCutover": True,
            "previousSchemaVersion": version,
            "privateWorldInventory": before,
            "restoredMapAssetCount": count,
        }
        temporary = manifest.with_name(manifest.name + ".tmp-" + sha[:12])
        with temporary.open("x", encoding="utf-8") as output:
            json.dump(record, output, sort_keys=True)
        temporary.chmod(0o600)
        os.replace(temporary, manifest)
        # Reuse the audited SHA/path/restore attestation verifier.
        subprocess.run([sys.executable,
                        str(Path(__file__).with_name("phase17-recovery-gate.py")),
                        str(manifest), sha],
                       check=True, stdout=subprocess.DEVNULL)
        succeeded = True
        print("Phase 17 pre-cutover backups and isolated restores verified; "
              "old writer stopped for schema migration.")
        print("Retained private backup directory: " + str(backup_dir))
    finally:
        if stopped and not succeeded:
            subprocess.run(["docker", "start", SERVICE], check=False,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    os.umask(0o077)
    if len(sys.argv) != 2:
        raise ValueError("Usage: phase17-prepare-recovery.py DEPLOY_SHA")
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    manifest = Path(os.environ.get("PHASE17_RECOVERY_MANIFEST", DEFAULT_MANIFEST))
    prepare(sys.argv[1], manifest)


if __name__ == "__main__":
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        # No credentials, dump content, or saved data is printed.
        print("Phase 17 backup/recovery preflight failed: " + type(error).__name__
              + ". Existing database was not modified.", file=sys.stderr)
        sys.exit(1)
