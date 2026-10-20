"""No-host checks for the live acceptance client's write safety gates."""
import unittest
from verify_engineering_live import Acceptance


class FakeSession:
    def __init__(self, replies): self.replies, self.requests = list(replies), []
    def call(self, tool, request):
        self.requests.append((tool, request)); return self.replies.pop(0)


def result(data, error=False): return {"structuredContent": data, "isError": error}


class ClientSafetyTests(unittest.TestCase):
    def test_missing_token_never_applies(self):
        fake = FakeSession([result({"committed": False})])
        with self.assertRaises(RuntimeError): Acceptance(fake, "Fixture.dwg").write("typed", {"action": "edit"})
        self.assertEqual(1, len(fake.requests))

    def test_rehearsal_claiming_commit_never_applies(self):
        fake = FakeSession([result({"committed": True, "confirmation_token": "token"})])
        with self.assertRaises(RuntimeError): Acceptance(fake, "Fixture.dwg").write("typed", {"action": "edit"})
        self.assertEqual(1, len(fake.requests))

    def test_failed_apply_is_never_retried(self):
        fake = FakeSession([result({"committed": False, "confirmation_token": "token"}), result({"committed": True, "verified": {"status": "mismatch"}}, True)])
        with self.assertRaises(RuntimeError): Acceptance(fake, "Fixture.dwg").write("typed", {"action": "edit"})
        self.assertEqual(2, len(fake.requests))

    def test_verified_write_uses_same_resolved_request_and_selected_document(self):
        fake = FakeSession([result({"committed": False, "confirmation_token": "token"}), result({"committed": True, "verified": {"status": "match"}})])
        Acceptance(fake, "Fixture.dwg").write("typed", {"action": "edit", "value": 12})
        self.assertEqual({"action": "edit", "value": 12, "dry_run": False, "confirmation_token": "token", "target_document": "Fixture.dwg"}, fake.requests[1][1])

    def test_committed_stale_result_is_not_retried(self):
        fake = FakeSession([result({"committed": False, "confirmation_token": "token"}), result({"committed": True, "code": "confirmation_refused", "confirmation_state": "stale_plan"}, True)])
        with self.assertRaises(RuntimeError): Acceptance(fake, "Fixture.dwg").write("typed", {"action": "edit"})
        self.assertEqual(2, len(fake.requests))


if __name__ == "__main__": unittest.main()
