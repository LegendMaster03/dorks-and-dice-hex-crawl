#!/usr/bin/env python3
"""Check the exact production job conditions across cancellation/failure states.

Evaluates the expressions extracted from both production workflow files, not
an independently typed approximation. Does not access a production runner.
"""
import argparse
import ast
from pathlib import Path
import re


def condition(workflow, title):
    source = Path(workflow).read_text()
    match = re.search(r"^      - name: " + re.escape(title) + r"$", source, re.M)
    assert match, title
    tail = source[match.end():]
    next_step = re.search(r"^      - name: ", tail, re.M)
    section = tail[:next_step.start()] if next_step else tail
    found = re.search(r"^        if: \$\{\{(.*?)\}\}$", section, re.M)
    assert found, "Missing executable workflow condition: " + title
    return found.group(1).strip()


def evaluate(expression, *, available, deploy, verify, restore, failing):
    values = {"deploy": deploy, "verify": verify, "restore": restore}
    code = expression
    code = code.replace("always()", "True").replace("failure()", str(failing))
    code = code.replace("success()", str(not failing))
    code = code.replace("steps.previous_image.outputs.available", repr("true" if available else "false"))
    code = re.sub(r"steps\.(deploy|verify|restore)\.outcome",
                  lambda m: repr(values[m.group(1)]), code)
    code = code.replace("&&", " and ").replace("||", " or ")
    node = ast.parse(code, mode="eval")

    def visit(expr):
        if isinstance(expr, ast.Constant):
            assert isinstance(expr.value, (str, bool))
            return expr.value
        if isinstance(expr, ast.BoolOp) and isinstance(expr.op, (ast.And, ast.Or)):
            arguments = [bool(visit(item)) for item in expr.values]
            return all(arguments) if isinstance(expr.op, ast.And) else any(arguments)
        if isinstance(expr, ast.Compare) and len(expr.ops) == len(expr.comparators) == 1:
            assert isinstance(expr.ops[0], ast.Eq)
            return visit(expr.left) == visit(expr.comparators[0])
        raise AssertionError("Unexpected workflow expression: " + ast.dump(expr))
    return bool(visit(node.body))


def main(workflow):
    cleanup = condition(workflow, "Erase private known-good configuration after safe completion")
    recovery = condition(workflow, "Restore previous running image after failed deploy or verification")
    # Each tuple is (deploy, verify, restore, GitHub failure(), expected cleanup, expected restore step).
    checks = [
        ("success", "success", "skipped", False, True, False),
        ("failure", "skipped", "success", True, True, True),
        ("success", "failure", "success", True, True, True),
        ("skipped", "skipped", "skipped", True, True, False),
        ("cancelled", "skipped", "skipped", False, False, False),
        ("success", "cancelled", "skipped", False, False, False),
        ("failure", "skipped", "failure", True, False, True),
        ("failure", "skipped", "skipped", True, False, True),
        ("success", "failure", "skipped", True, False, True),
        ("cancelled", "cancelled", "skipped", False, False, False),
        ("skipped", "cancelled", "skipped", False, False, False),
    ]
    for deploy, verify, restore, failing, expected_cleanup, expected_restore in checks:
        args = dict(available=True, deploy=deploy, verify=verify,
                    restore=restore, failing=failing)
        actual_cleanup = evaluate(cleanup, **args)
        actual_restore = evaluate(recovery, **args)
        assert actual_cleanup == expected_cleanup, (workflow, args, "cleanup", actual_cleanup)
        assert actual_restore == expected_restore, (workflow, args, "restore", actual_restore)
        assert not (actual_cleanup and not (
            verify == "success" or restore == "success"
            or deploy == "skipped" and verify == "skipped"))
        assert not evaluate(cleanup, **{**args, "available": False})
    print(f"PHASE16_OUTCOME PASS {workflow}: cancelled/unverified snapshots retained", flush=True)


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--workflow", required=True)
    main(cli.parse_args().workflow)
