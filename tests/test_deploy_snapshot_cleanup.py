"""Check deployment safety after retiring one-time schema-10 cutover code.

Only inspect committed workflow content. Does not access the live runner or DB.
"""
import ast
from pathlib import Path
import re
import unittest

WORKFLOW = Path(__file__).resolve().parents[1] / ".github/workflows/deploy.yml"


def step_expression(title):
    text = WORKFLOW.read_text(encoding="utf-8")
    marker = "      - name: " + title + "\n"
    assert marker in text, "Missing deployment step: " + title
    block = text.split(marker, 1)[1].split("      - name: ", 1)[0]
    match = re.search(r"^        if: \$\{\{(.*?)\}\}$", block, re.M)
    assert match, "Missing executable guard: " + title
    return match.group(1)


def evaluate_cleanup(*, available=True, deployed="skipped",
                     verified="skipped", resumed="success", cancelled=False):
    expression = step_expression("Erase private known-good configuration after safe completion")
    expression = expression.replace("always()", "True").replace("cancelled()", str(cancelled))
    expression = expression.replace("!", " not ").replace("&&", " and ").replace("||", " or ")
    expression = expression.replace("steps.previous_image.outputs.available",
                                    repr("true" if available else "false"))
    expression = expression.replace("steps.deploy.outcome", repr(deployed))
    expression = expression.replace("steps.verify.outcome", repr(verified))
    expression = expression.replace("steps.resume.outcome", repr(resumed))
    return bool(eval(compile(ast.parse(expression.strip(), mode="eval"),
                             "<GitHub cleanup condition>", "eval"),
                     {"__builtins__": {}}, {}))


class DeploymentSafetyTests(unittest.TestCase):
    def test_no_obsolete_phase17_cutover_steps_or_scripts(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        for removed in (
            "phase17-prepare-recovery.py",
            "phase17-recovery-gate.py",
            "Prepare verified Phase 17",
            "Remove Phase 17 temporary",
            "Require controlled recovery after failed schema-10 cutover",
        ):
            self.assertNotIn(removed, text)
        self.assertFalse((WORKFLOW.parent.parent / "scripts/phase17-prepare-recovery.py").exists())
        self.assertFalse((WORKFLOW.parent.parent / "scripts/phase17-recovery-gate.py").exists())

    def test_preserve_general_deployment_guards(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        for required in (
            "Retain the actual running image before overwriting latest",
            "Validate production deployment configuration",
            "Smoke test PostgreSQL-backed container",
            "- name: Deploy",
            "- name: Verify deployed service",
            "- name: Preserve recovery evidence after failed deployment",
            "- name: Ensure previous service runs when deployment is skipped",
            "phase16-deployment-config.py snapshot",
        ):
            self.assertIn(required, text)
        self.assertLess(text.index("- name: Smoke test PostgreSQL-backed container"),
                        text.index("      - name: Deploy"))
        self.assertNotIn("- name: Restore previous running image after failed deploy", text)

    def test_private_snapshot_retained_when_recovery_uncertain(self):
        for deployed, verified in (
            ("failure", "skipped"), ("success", "failure"),
            ("cancelled", "skipped"), ("success", "cancelled")
        ):
            with self.subTest(deployed=deployed, verified=verified):
                self.assertFalse(evaluate_cleanup(deployed=deployed, verified=verified))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped",
                                          resumed="failure"))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped",
                                          resumed="skipped"))

    def test_private_snapshot_removed_only_after_safe_state(self):
        self.assertTrue(evaluate_cleanup(deployed="success", verified="success"))
        self.assertTrue(evaluate_cleanup(deployed="skipped", verified="skipped"))
        self.assertFalse(evaluate_cleanup(deployed="success", verified="success",
                                          cancelled=True))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped",
                                          cancelled=True))
        self.assertFalse(evaluate_cleanup(available=False, deployed="success",
                                          verified="success"))


if __name__ == "__main__":
    unittest.main()
