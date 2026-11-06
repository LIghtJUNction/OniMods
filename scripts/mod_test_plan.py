"""Shared host-test invocation policy, independent of process execution."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PERFORMANCE_PROJECT = Path(
    "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"
)


def _dotnet_run(
    path: Path,
    dotnet: str,
    *,
    properties: tuple[str, ...] = (),
    arguments: tuple[str, ...] = (),
) -> list[str]:
    command = [
        dotnet,
        "run",
        "--project",
        str(path),
        "--configuration",
        "Release",
        "-p:ImportDirectoryBuildProps=false",
        "-p:TreatWarningsAsErrors=true",
        *properties,
    ]
    if arguments:
        command.extend(("--", *arguments))
    return command


def host_regression_checks(
    dotnet: str,
    skip_synthetic_performance: bool,
    root: Path = ROOT,
) -> list[tuple[str, list[str]]]:
    projects = sorted((root / "tests").glob("*/*.csproj"))
    if not projects:
        return []

    checks = []
    for path in projects:
        arguments = (
            ("--skip-synthetic-performance",)
            if skip_synthetic_performance and path == root / PERFORMANCE_PROJECT
            else ()
        )
        checks.append((str(path.relative_to(root)), _dotnet_run(path, dotnet, arguments=arguments)))
    adapter = root / "tests/OniOnline.Tests/OniOnline.Tests.csproj"
    if adapter in projects:
        checks.append((
            "OniOnline native-origin adapter regressions",
            _dotnet_run(adapter, dotnet, properties=("-p:OriginAdapterTests=true",)),
        ))
    return checks


def build_probe_command(dotnet: str = "dotnet", root: Path = ROOT) -> list[str]:
    return _dotnet_run(
        root / PERFORMANCE_PROJECT,
        dotnet,
        arguments=("--synthetic-performance-only",),
    )
