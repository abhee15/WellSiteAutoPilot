"""Positive and negative regressions for reviewed HTTP authorization metadata."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "ci"))
import check_api_authorization as api_authorization

ROOT = Path(__file__).resolve().parents[2]
PROGRAM = (ROOT / "src/server/WellSiteAutoPilot.Server/Program.cs").read_text(
    encoding="utf-8"
)


class ApiAuthorizationInventoryTests(unittest.TestCase):
    def test_current_api_matches_reviewed_inventory(self):
        self.assertEqual(api_authorization.validate_routes(PROGRAM), [])
        self.assertEqual(len(api_authorization.EXPECTED_ENDPOINTS), 34)

    def test_new_unreviewed_endpoint_is_rejected(self):
        original = 'app.MapHealthChecks("/health/live"'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(
            original,
            'v1.MapGet("/unreviewed", () => Results.Ok()).WithName("Unreviewed");\n'
            + original,
            1,
        )
        self.assertIn("Unreviewed API route: GET /unreviewed",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_removing_named_permission_is_rejected(self):
        original = '.WithName("ListAssets")\n    .RequireAuthorization(WellSitePolicies.AssetsRead)'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(original, '.WithName("ListAssets")', 1)
        self.assertIn("expected exactly one WellSitePolicies.AssetsRead",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_wrong_permission_is_rejected(self):
        original = '.WithName("ListAssets")\n    .RequireAuthorization(WellSitePolicies.AssetsRead)'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(
            original,
            '.WithName("ListAssets")\n    .RequireAuthorization(WellSitePolicies.AssetsManage)',
            1,
        )
        self.assertIn("expected exactly one WellSitePolicies.AssetsRead",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_anonymous_bypass_is_rejected(self):
        original = '.WithName("GetAsset")\n    .RequireAuthorization(WellSitePolicies.AssetsRead)'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(
            original, original + '\n    .AllowAnonymous()', 1
        )
        self.assertIn("protected API must not allow anonymous access",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_group_authentication_removal_is_rejected(self):
        original = '    .HasApiVersion(1.0)\n    .RequireAuthorization();'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(original, '    .HasApiVersion(1.0);', 1)
        self.assertIn("Product v1 API group must require authentication",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_middleware_order_is_checked(self):
        self.assertIn("app.UseAuthorization();", PROGRAM)
        altered = PROGRAM.replace("app.UseAuthorization();", "", 1)
        self.assertIn("Authentication middleware must precede authorization",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_unreviewed_root_api_endpoint_is_rejected(self):
        original = 'app.MapHealthChecks("/health/live"'
        altered = PROGRAM.replace(
            original, 'app.MapGet("/internal-secret", () => Results.Ok());\n' + original,
            1,
        )
        self.assertIn("outside the reviewed v1 group",
                      "\n".join(api_authorization.validate_routes(altered)))

    def test_deleting_known_route_is_rejected(self):
        original = 'v1.MapGet(\n    "/system/info"'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(original, 'unmapped.MapGet(\n    "/system/info"', 1)
        messages = "\n".join(api_authorization.validate_routes(altered))
        self.assertIn("Reviewed API route missing: GET /system/info", messages)

    def test_renamed_endpoint_requires_review(self):
        original = '.WithName("ListAssets")'
        self.assertIn(original, PROGRAM)
        altered = PROGRAM.replace(original, '.WithName("ListAllAssets")', 1)
        self.assertIn("expected endpoint name ListAssets",
                      "\n".join(api_authorization.validate_routes(altered)))


if __name__ == "__main__":
    unittest.main()
