import unittest

from package_b_approval_hold import require_approve_deny_all

SIGNATURE = """public async Task<V180B3RequestView> ApproveAsync(
        Guid id,V180B3Review input,CancellationToken ct)
    {
"""
VALID_BODY = """
        await ReadyAsync(ct);var(_,admin,_)=await LiveActorAsync(ct);
        if(!admin)throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        // No approval/apply until Owner and IT approve the real executor.
        throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");
"""
SUFFIX = """    }
}
"""

class ExactB3ApprovalHoldTests(unittest.TestCase):
    def source(self, body=VALID_BODY, suffix=SUFFIX):
        return "namespace Fixture {\n    "+SIGNATURE + body + suffix

    def test_exact_method_remains_denied(self):
        require_approve_deny_all(self.source())

    def test_rejects_comment_only_denial(self):
        body = """
        await ReadyAsync(ct);var(_,admin,_)=await LiveActorAsync(ct);
        if(!admin)throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        // throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");
        return FakeApproval();
"""
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(body))

    def test_rejects_actual_return_before_denial(self):
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(
                VALID_BODY.replace('throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");',
                    'return FakeApproval();\n        throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");')))

    def test_rejects_conditional_denial_that_can_fall_through(self):
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(
                VALID_BODY.replace('throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");',
                    'if(false)throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");')))

    def test_rejects_any_mutation_even_with_trailing_denial(self):
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(
                VALID_BODY.replace('throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");',
                    'await db.SaveChangesAsync(ct);\n        throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");')))

    def test_rejects_unauthorized_delegation_or_new_helper(self):
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(
                VALID_BODY.replace('await ReadyAsync(ct);',
                    'await ReadinessAndMaybeApplyAsync(ct);')))

    def test_rejects_approval_body_with_denial_only_in_next_method(self):
        body='\n        return FakeApproval();\n'
        suffix='''    }
    public void Dummy()
    {
        throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");
    }
}
'''
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(body, suffix))

    def test_rejects_duplicate_signature(self):
        s=self.source() + "\n"+SIGNATURE+VALID_BODY+SUFFIX
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_ENTRYPOINT_MISSING_OR_DUPLICATE"):
            require_approve_deny_all(s)

    def test_rejects_method_signature_changed_or_missing(self):
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_ENTRYPOINT_MISSING_OR_DUPLICATE"):
            require_approve_deny_all(
                self.source().replace('Task<V180B3RequestView> ApproveAsync',
                                      'Task<V180B3RequestView> ApproveLocationAsync'))

    def test_rejects_inline_comment_masquerading_as_throw(self):
        body = """
        await ReadyAsync(ct);var(_,admin,_)=await LiveActorAsync(ct);
        if(!admin)throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        return FakeApproval(); // throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");
"""
        with self.assertRaisesRegex(ValueError,"B3_APPROVAL_EXECUTOR_BODY_NOT_DENY_ALL"):
            require_approve_deny_all(self.source(body))

if __name__ == "__main__":
    unittest.main()
