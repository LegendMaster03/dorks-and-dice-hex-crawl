"""Run an Actions Bash step and retain timing without changing its result."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import time


def host_sample():
    sample = {}
    try:
        sample["load_average"] = list(os.getloadavg())
        for line in Path("/proc/meminfo").read_text().splitlines():
            if line.startswith("MemAvailable:"):
                sample["available_memory_mib"] = round(int(line.split()[1]) / 1024, 1)
                break
    except (OSError, ValueError):
        pass
    return sample


def main():
    label, script = sys.argv[1:]
    started_at = datetime.datetime.now(datetime.timezone.utc).isoformat()
    before = host_sample()
    started = time.monotonic()
    result = 1
    try:
        result = subprocess.call(["bash", "--noprofile", "--norc", "-e", "-o", "pipefail", script])
        if result < 0:
            result = 128 - result
    except OSError as error:
        print(f"Could not start Actions step: {error}", file=sys.stderr)
        result = 127
    finally:
        record = {
            "step": label,
            "started_at": started_at,
            "duration_seconds": round(time.monotonic() - started, 3),
            "exit_code": result,
            "host_before": before,
            "host_after": host_sample(),
        }
        try:
            path = Path(os.environ["RUNNER_TEMP"]) / "hex-crawl-deploy-timing.jsonl"
            first = not path.exists()
            with path.open("a") as output:
                output.write(json.dumps(record) + "\n")
            summary = os.environ.get("GITHUB_STEP_SUMMARY")
            if summary:
                with open(summary, "a") as output:
                    if first:
                        output.write("## Deployment timing\n\nHost samples cover the shared runner host, not CPU usage attributable to this build.\n\n")
                    output.write(f"**{label}**: {record['duration_seconds']:.3f} s; exit code {result}.\n\n")
                    for moment in ("before", "after"):
                        sample = record[f"host_{moment}"]
                        output.write(f"Host {moment}: load averages {sample.get('load_average', 'unavailable')}; available memory {sample.get('available_memory_mib', 'unavailable')} MiB.\n\n")
        except (OSError, KeyError) as error:
            print(f"Timing output unavailable: {error}", file=sys.stderr)
        print(f"Step timing: {label}: {record['duration_seconds']:.3f}s, exit={result}", flush=True)
    return result


if __name__ == "__main__":
    sys.exit(main())
