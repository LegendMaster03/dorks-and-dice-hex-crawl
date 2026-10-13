"""Exercise timing against real shell exits and unavailable diagnostic storage."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HELPER = Path(__file__).resolve().parents[1] / ".github/scripts/measure-action.py"


class DeploymentTimingTests(unittest.TestCase):
    def run_step(self, directory, command, **overrides):
        script = Path(directory) / "step.sh"
        script.write_text(command)
        env = dict(os.environ, RUNNER_TEMP=directory,
                   GITHUB_STEP_SUMMARY=str(Path(directory) / "summary.md"))
        env.update(overrides)
        return subprocess.run([sys.executable, str(HELPER), "test step", str(script)],
                              env=env, capture_output=True, text=True)

    def test_success_records_duration_and_streams_output(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_step(directory, "echo visible-output")
            self.assertEqual(0, result.returncode)
            self.assertIn("visible-output", result.stdout)
            record = json.loads((Path(directory) / "hex-crawl-deploy-timing.jsonl").read_text())
            self.assertEqual(0, record["exit_code"])
            self.assertGreaterEqual(record["duration_seconds"], 0)
            self.assertIn("host_after", record)
            self.assertIn("Deployment timing", (Path(directory) / "summary.md").read_text())

    def test_pipeline_failure_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_step(directory, "false | true\necho should-not-run")
            self.assertEqual(1, result.returncode)
            self.assertNotIn("should-not-run", result.stdout)
            record = json.loads((Path(directory) / "hex-crawl-deploy-timing.jsonl").read_text())
            self.assertEqual(1, record["exit_code"])
            self.assertEqual(23, self.run_step(directory, "exit 23").returncode)

    def test_diagnostic_write_failure_does_not_change_step_result(self):
        with tempfile.TemporaryDirectory() as directory:
            unavailable = str(Path(directory) / "missing" / "directory")
            self.assertEqual(0, self.run_step(directory, "true", RUNNER_TEMP=unavailable).returncode)
            self.assertEqual(23, self.run_step(directory, "exit 23", RUNNER_TEMP=unavailable).returncode)


if __name__ == "__main__":
    unittest.main()
