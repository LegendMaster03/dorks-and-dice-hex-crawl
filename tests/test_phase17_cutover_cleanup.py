"""Check the actual Phase 17 deployment cleanup condition across cutover outcomes.

Only inspect workflow text. Never access the deployment runner or any data.
"""
import ast
from pathlib import Path
import re
import unittest


WORKFLOW = Path(__file__).resolve().parents[1] / ".github/workflows/deploy.yml"


def cleanup_expression():
    text = WORKFLOW.read_text(encoding="utf-8")
    title = "Erase private known-good configuration after safe completion"
    marker = "      - name: " + title
    assert marker in text, "Missing actual cleanup step"
    block = text.split(marker, 1)[1].split("      - name: ", 1)[0]
    match = re.search(r"^        if: \$\{\{(.*?)\}\}$", block, re.M)
    assert match, "Missing executable cleanup expression"
    return match.group(1)


def evaluate_cleanup(*, available=True, deployed="skipped",
                     verified="skipped", resumed="success", cancelled=False):
    expression = cleanup_expression()
    expression = expression.replace("always()", "True")
    expression = expression.replace("cancelled()", str(cancelled))
    expression = expression.replace("!", " not ")
    expression = expression.replace("&&", " and ").replace("||", " or ")
    expression = expression.replace("steps.previous_image.outputs.available",
                                    repr("true" if available else "false"))
    expression = expression.replace("steps.deploy.outcome", repr(deployed))
    expression = expression.replace("steps.verify.outcome", repr(verified))
    expression = expression.replace("steps.resume.outcome", repr(resumed))
    tree = ast.parse(expression.strip(), mode="eval")
    return bool(eval(compile(tree, "<GitHub cleanup condition>", "eval"),
                     {"__builtins__": {}}, {}))


class Phase17CutoverCleanupTests(unittest.TestCase):
    def test_retain_snapshot_on_failed_or_uncertain_cutovers(self):
        for deployed, verified in (
            ("failure", "skipped"),
            ("success", "failure"),
            ("cancelled", "skipped"),
            ("success", "cancelled"),
        ):
            with self.subTest(deployed=deployed, verified=verified):
                self.assertFalse(evaluate_cleanup(deployed=deployed, verified=verified))

    def test_clean_only_after_success_or_proven_predeployment_abort(self):
        self.assertTrue(evaluate_cleanup(deployed="success", verified="success"))
        self.assertTrue(evaluate_cleanup(deployed="skipped", verified="skipped"))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="cancelled"))
        self.assertFalse(evaluate_cleanup(deployed="failure", verified="failure"))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped", resumed="failure"))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped", resumed="skipped"))

    def test_cancelled_workflow_never_deletes_recovery_snapshot(self):
        self.assertFalse(evaluate_cleanup(deployed="success", verified="success", cancelled=True))
        self.assertFalse(evaluate_cleanup(deployed="skipped", verified="skipped", cancelled=True))

    def test_no_snapshot_means_no_cleanup(self):
        self.assertFalse(evaluate_cleanup(available=False, deployed="success", verified="success"))

    def test_backup_preparation_precedes_deploy_and_follows_disposable_smoke(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        smoke = text.index("- name: Smoke test PostgreSQL-backed container")
        cutover = text.index("- name: Prepare verified Phase 17 live-data recovery before cutover")
        deploy = text.index("- name: Deploy\\n")
        self.assertLess(smoke, cutover)
        self.assertLess(cutover, deploy)
        self.assertIn("python3 .github/scripts/phase17-prepare-recovery.py", text)
        self.assertIn("- name: Resume original service if the cutover did not begin", text)

    def test_incompatible_image_only_auto_restore_stays_disabled(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertNotIn("- name: Restore previous running image after failed deploy", text)
        self.assertIn("- name: Require controlled recovery after failed schema-10 cutover", text)


if __name__ == "__main__":
    unittest.main()
