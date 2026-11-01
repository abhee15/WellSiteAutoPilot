"""Negative and positive tests for machine-enforceable repository conventions."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "ci"))
import check_conventions as conventions


class ConventionTests(unittest.TestCase):
    def test_generated_api_code_is_protected(self):
        errors = conventions.validate_changed_paths([
            "src/web/src/api/generated/v1/types.gen.ts",
            "src/web/src/api/generated/README.md",
            "src/web/src/App.tsx",
        ])
        self.assertEqual(len(errors), 1)
        self.assertIn("types.gen.ts", errors[0])

    def test_application_cannot_reference_persistence_but_can_reference_domain(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app = root / "src/server/WellSiteAutoPilot.Application/App.csproj"
            domain = root / "src/server/WellSiteAutoPilot.Domain/Domain.csproj"
            persistence = root / "src/persistence/Data/Data.csproj"
            for project in [app, domain, persistence]:
                project.parent.mkdir(parents=True, exist_ok=True)
                project.write_text("<Project />", encoding="utf8")
            app.write_text("""<Project><ItemGroup>
            <ProjectReference Include="../WellSiteAutoPilot.Domain/Domain.csproj" />
            <ProjectReference Include="../../persistence/Data/Data.csproj" />
            </ItemGroup></Project>""", encoding="utf8")
            errors = conventions.validate_references(root)
            self.assertEqual(len(errors), 1)
            self.assertIn("application depends on implementation", errors[0])

    def test_worker_must_not_reference_gateway_implementation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            worker = root / "src/workers/WellSiteAutoPilot.Worker.DotNet/Worker.csproj"
            gateway = root / "src/integration/WellSiteAutoPilot.IntegrationGateway/Gateway.csproj"
            worker.parent.mkdir(parents=True, exist_ok=True)
            gateway.parent.mkdir(parents=True, exist_ok=True)
            gateway.write_text("<Project />", encoding="utf8")
            worker.write_text("""<Project><ItemGroup>
            <ProjectReference Include="../../integration/WellSiteAutoPilot.IntegrationGateway/Gateway.csproj" />
            </ItemGroup></Project>""", encoding="utf8")
            errors = conventions.validate_references(root)
            self.assertEqual(len(errors), 1)
            self.assertIn("worker bypasses integration boundary", errors[0])

    def test_missing_project_reference_fails_loudly(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "src/server/WellSiteAutoPilot.Domain/Domain.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("""<Project><ItemGroup>
            <ProjectReference Include="Missing.csproj" />
            </ItemGroup></Project>""", encoding="utf8")
            self.assertIn("missing ProjectReference",
                conventions.validate_references(root)[0])

    def test_occurrences_are_only_computed_inside_the_scheduling_folder(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            allowed = root / "src/server/WellSiteAutoPilot.Application/Scheduling/EffectiveSchedule.cs"
            offender = root / "src/server/WellSiteAutoPilot.Server/Program.cs"
            for source, text in [(allowed, "ScheduleOccurrences.NextAfter(schedule, now);"),
                                 (offender, "var next = ScheduleOccurrences.NextAfter(schedule, now);")]:
                source.parent.mkdir(parents=True, exist_ok=True)
                source.write_text(text, encoding="utf8")
            errors = conventions.validate_schedule_resolution(root)
            self.assertEqual(len(errors), 1)
            self.assertIn("Program.cs", errors[0])
            self.assertIn("EffectiveSchedule", errors[0])


if __name__ == "__main__":
    unittest.main()
