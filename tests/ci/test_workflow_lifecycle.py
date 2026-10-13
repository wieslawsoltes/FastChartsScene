"""Static guardrails for cancellation-aware orchestration; runtime gates remain separate."""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[2] / '.github/workflows/validate.yml'


class WorkflowLifecycleTests(unittest.TestCase):
    def test_cancelled_runs_do_not_start_or_pin_aggregate_jobs(self):
        evidence = WORKFLOW.read_text().split('\n  evidence:\n', 1)[1]
        self.assertIn('    if: ${{ !cancelled() }}\n', evidence)
        self.assertNotIn('if: always()', evidence)
        self.assertIn('needs: [core, uno, browser]', evidence)
        self.assertIn('timeout-minutes: 10', evidence)

    def test_aggregate_still_requires_every_exact_build_result(self):
        evidence = WORKFLOW.read_text().split('\n  evidence:\n', 1)[1]
        self.assertIn('tests/ci/browser_validation.py aggregate', evidence)
        self.assertIn('--manifest-hash "$MANIFEST_HASH" --artifact-id "$BUILD_ARTIFACT"', evidence)
        for dependency in ('core', 'uno', 'browser'):
            self.assertIn('${{ needs.' + dependency + '.result }}', evidence)
        self.assertNotIn('continue-on-error', evidence)

    def test_stable_branch_scoped_concurrency_cancels_obsolete_work(self):
        workflow = WORKFLOW.read_text()
        self.assertIn('group: validate-cancellable-${{ github.ref }}', workflow)
        self.assertIn('cancel-in-progress: true', workflow)
        self.assertNotIn('github.run_id', workflow)


if __name__ == '__main__':
    unittest.main()
