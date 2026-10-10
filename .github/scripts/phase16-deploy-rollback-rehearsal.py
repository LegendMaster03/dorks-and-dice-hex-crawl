#!/usr/bin/env python3
"""Rehearse the production YAML's actual deploy/verify/rollback shell blocks on ephemeral CI Docker."""
import argparse
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile


def command(args, cwd=None, env=None, expect_success=True):
    result = subprocess.run(args, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    assert (result.returncode == 0) == expect_success, (
        f"{args[:5]} returned {result.returncode}\n{result.stdout[-8000:]}")
    return result.stdout


def shell_step(yaml, title):
    start = re.search(r"^      - name: " + re.escape(title) + r"$", yaml, re.M)
    assert start, title
    rest = yaml[start.end():]
    end = re.search(r"^      - name: ", rest, re.M)
    section = rest[:end.start()] if end else rest
    marker = re.search(r"^        run: \|$\n", section, re.M)
    assert marker, title + " must use a literal shell block"
    lines = []
    for line in section[marker.end():].splitlines():
        if line and not line.startswith("          "):
            break
        lines.append(line[10:] if line else "")
    result = "\n".join(lines)
    assert "docker" in result
    return result


def run(script, root, env, expected_success=True, previous=None):
    if previous is not None:
        target = "$" + "{{ steps.previous_image.outputs.image }}"
        script = script.replace(target, previous)
    assert "$" + "{{ steps." not in script
    return command(["bash", "-e", "-o", "pipefail", "-c", script],
                   cwd=root, env=env, expect_success=expected_success)


def fixture(root, version, healthy):
    d = root / version
    (d / "srv" / "health").mkdir(parents=True)
    (d / "srv" / "health" / "live").write_text('{"status":"live"}')
    (d / "srv" / "health" / "ready").write_text(
        '{"status":"ready"}' if healthy else '{"status":"unready"}')
    (d / "srv" / "ready").write_text(
        '{"status":"ready","persistence":"postgresql-ready","mapAssets":"filesystem"}'
        if healthy else '{"status":"unready","persistence":"offline","mapAssets":"filesystem"}')
    (d / "srv" / "app.js").write_text("// isolated CI fixture")
    (d / "srv" / "version").write_text(version)
    (d / "nginx.conf").write_text(
        "events { worker_connections 64; }\n"
        "http { server { listen 8080; root /srv; "
        "location = /health { return 200 " + ('"Healthy"' if healthy else '"Unhealthy"') +
        "; } location / { try_files $uri =404; } } }\n")
    (d / "Dockerfile").write_text(
        "FROM mirror.gcr.io/library/nginx:stable-alpine\n"
        "COPY nginx.conf /etc/nginx/nginx.conf\nCOPY srv /srv\n")
    return d


def image_id(service):
    return command(["docker", "inspect", "--type", "container",
                    "--format", "{{.Image}}", service]).strip()


def build(directory, service, revision, tag):
    command(["docker", "build", "--quiet", "--label",
             f"org.opencontainers.image.revision={revision}",
             "-t", f"{service}:latest", "-t", f"{service}:{tag}", str(directory)])


def outputs(path):
    return dict(line.split("=", 1) for line in path.read_text().splitlines() if "=" in line)


def preserved(service, image):
    assert image_id(service) == image, "Restored wrong image"
    value = command(["docker", "exec", service, "cat", "/data/phase16-record"]).strip()
    assert value == "unchanged-tester-fixture", "Persisted record changed"


def main(workflow, service):
    yaml = workflow.read_text()
    names = {"capture": "Retain the actual running image before overwriting latest",
             "preflight": "Validate production deployment configuration",
             "build": "Build image", "deploy": "Deploy",
             "verify": "Verify deployed service",
             "restore": "Restore previous running image after failed deploy or verification"}
    steps = {key: shell_step(yaml, title) for key, title in names.items()}
    assert 'org.opencontainers.image.revision=$DEPLOY_SHA' in steps["build"]
    assert "cancel-in-progress: false" in yaml
    assert "github.event.workflow_run.head_branch == 'main'" in yaml
    assert "steps.verify.outcome == 'failure'" in yaml
    assert "steps.deploy.outcome == 'failure'" in yaml
    assert "steps.previous_image.outputs.available == 'true'" in yaml
    assert "$" + "{DEPLOY_ENV_FILE:-" in steps["preflight"]
    assert " config >/dev/null" in steps["preflight"]
    assert "phase16-deployment-config.py verify" in steps["preflight"]
    assert 'DEPLOY_SNAPSHOT_DIR/compose.env' in steps["deploy"]
    assert 'DEPLOY_SNAPSHOT_DIR/compose.env' in steps["restore"]
    assert "phase16-deployment-config.py snapshot" in steps["capture"]

    with tempfile.TemporaryDirectory(prefix="phase16-rollback-") as tmp:
        root = Path(tmp)
        helper = workflow.resolve().parents[1] / "scripts" / "phase16-deployment-config.py"
        (root / ".github" / "scripts").mkdir(parents=True)
        shutil.copyfile(helper, root / ".github" / "scripts" / helper.name)
        snapshots = []
        (root / "docker-compose.yml").write_text(
            f"services:\n  {service}:\n    image: {service}:latest\n"
            f"    container_name: {service}\n    restart: unless-stopped\n"
            "    environment:\n      PHASE16_SENTINEL: \"${PHASE16_SENTINEL:-valid}\"\n"
            "    volumes:\n      - data:/data\n"
            "    networks:\n      - dorks-and-dice-backend\n"
            "volumes:\n  data:\n"
            "networks:\n  dorks-and-dice-backend:\n    external: true\n")
        env_file = root / ".env"
        env_file.write_text("PHASE16_SENTINEL=valid\n")
        output = root / "step-output"
        env = dict(os.environ, DEPLOY_ENV_FILE=str(env_file),
                   DEPLOY_HEALTH_ATTEMPTS="3", GITHUB_OUTPUT=str(output))
        def captured():
            fields = captured()
            if "snapshot_dir" in fields:
                env["DEPLOY_SNAPSHOT_DIR"] = fields["snapshot_dir"]
                snapshots.append(fields["snapshot_dir"])
            return fields
        compose = ["docker", "compose", "--project-name", service,
                   "--env-file", str(env_file), "-f", "docker-compose.yml"]
        command(["docker", "network", "create", "dorks-and-dice-backend"])
        try:
            build(fixture(root, "initial", True), service, "0" * 40, "initial")
            command(compose + ["up", "-d", "--force-recreate", "--remove-orphans"], cwd=root)
            command(["docker", "exec", service, "sh", "-c",
                     "printf unchanged-tester-fixture > /data/phase16-record"])
            original = image_id(service)
            preserved(service, original)

            # Capture the *actual* running configuration before production
            # preflight; never use an untrusted new host config for rollback.
            initial = captured()
            assert initial["available"] == "true" and initial["image"] == original
            run(steps["preflight"], root, env)
            missing_env = dict(env, DEPLOY_ENV_FILE=str(root / "missing.env"))
            run(steps["preflight"], root, missing_env, expected_success=False)
            env_file.write_text("PHASE16_SENTINEL=invalid\n")
            run(steps["preflight"], root, env, expected_success=False)
            preserved(service, original)
            env_file.write_text("PHASE16_SENTINEL=valid\n")
            print(f"{service}: app-invalid deployment config fails before replacement PASS", flush=True)
            preserved(service, original)
            assert command(
                ["docker", "image", "inspect", "--format", "{{.Id}}",
                 f"{service}:latest"]).strip() == original
            print(f"{service}: failed Compose preflight preserves running service PASS", flush=True)

            # Successful deployment uses the unmodified capture/deploy/verify steps.
            fields = captured()
            assert fields["available"] == "true" and fields["image"] == original
            env["DEPLOY_SHA"] = "1" * 40
            build(fixture(root, "healthy-new", True), service, env["DEPLOY_SHA"], "healthy")
            run(steps["deploy"], root, env)
            run(steps["verify"], root, env)
            healthy = image_id(service)
            assert healthy != original
            preserved(service, healthy)
            print(f"{service}: good rollout and persisted record PASS", flush=True)

            # Negative readiness must fail verification, then restore exact image and data.
            fields = captured()
            assert fields["available"] == "true" and fields["image"] == healthy
            env["DEPLOY_SHA"] = "2" * 40
            build(fixture(root, "unready-new", False), service, env["DEPLOY_SHA"], "unready")
            # Host configuration becomes invalid *after* preflight. The new
            # deployment and old-image recovery must both use the captured
            # known-good runtime, without repairing the host file first.
            env_file.write_text("PHASE16_SENTINEL=invalid\n")
            run(steps["deploy"], root, env)
            run(steps["verify"], root, env, expected_success=False)
            run(steps["restore"], root, env, previous=healthy)
            preserved(service, healthy)
            sentinel = command(["docker", "exec", service, "printenv", "PHASE16_SENTINEL"]).strip()
            assert sentinel == "valid"
            assert "invalid" in env_file.read_text()
            env_file.write_text("PHASE16_SENTINEL=valid\n")
            print(f"{service}: persistent bad-host-config rollback preserves known-good env PASS", flush=True)

            # Even a healthy container must fail and roll back for an incorrect revision.
            fields = captured()
            assert fields["available"] == "true" and fields["image"] == healthy
            env["DEPLOY_SHA"] = "3" * 40
            build(fixture(root, "wrong-sha", True), service, "4" * 40, "wrong-sha")
            run(steps["deploy"], root, env)
            run(steps["verify"], root, env, expected_success=False)
            run(steps["restore"], root, env, previous=healthy)
            preserved(service, healthy)
            print(f"{service}: revision mismatch rollback PASS", flush=True)

            # Case 4: the deployment reaches container replacement, then
            # Compose fails because a host port is occupied. This is a real
            # docker compose up failure, not a simulated nonzero exit code.
            fields = captured()
            assert fields["available"] == "true" and fields["image"] == healthy
            env["DEPLOY_SHA"] = "5" * 40
            build(fixture(root, "start-failure", True), service,
                  env["DEPLOY_SHA"], "start-failure")
            compose_file = root / "docker-compose.yml"
            base_compose = compose_file.read_text()
            with socket.socket() as occupied_port:
                occupied_port.bind(("127.0.0.1", 0))
                occupied_port.listen(1)
                port = occupied_port.getsockname()[1]
                compose_file.write_text(base_compose.replace(
                    "    restart: unless-stopped\n",
                    "    restart: unless-stopped\n"
                    + f'    ports:\n      - "127.0.0.1:{port}:8080"\n'))
                try:
                    run(steps["deploy"], root, env, expected_success=False)
                finally:
                    compose_file.write_text(base_compose)
            run(steps["restore"], root, env, previous=healthy)
            preserved(service, healthy)
            print(f"{service}: failed container replacement rollback PASS", flush=True)

            # Case 5: never overwrite the current image when there is no
            # running service to establish a verified rollback baseline.
            command(["docker", "rm", "-f", service])
            latest_before = command(
                ["docker", "image", "inspect", "--format", "{{.Id}}",
                 f"{service}:latest"]).strip()
            output.write_text("")
            run(steps["capture"], root, env, expected_success=False)
            assert outputs(output)["available"] == "false"
            latest_after = command(
                ["docker", "image", "inspect", "--format", "{{.Id}}",
                 f"{service}:latest"]).strip()
            assert latest_after == latest_before == healthy
            print(f"{service}: absent running image aborts before tag mutation PASS", flush=True)
        finally:
            command(compose + ["down", "-v", "--remove-orphans"], cwd=root)
            command(["docker", "network", "rm", "dorks-and-dice-backend"])
            for directory in snapshots:
                command(["python3", str(helper), "cleanup", directory])


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--workflow", required=True, type=Path)
    cli.add_argument("--service", required=True,
                     choices=["dorks-and-dice-surveyor", "dorks-and-dice-hex-crawl"])
    args = cli.parse_args()
    main(args.workflow, args.service)
