"""Run each command-lock scenario in a fresh process, including fatal cases."""

import argparse
from pathlib import Path
import subprocess
import sys


SCENARIOS = """
admission depth retained-extra cancel-waiter cancel-running cancel-depth race
overlap discarded completed-discard untyped exit-child exit-fault body-fault
duplicate-declaration null-declaration null-entry foreign missing mutable-array
mutable-registration progress-lock progress-descendant progress-free shutdown
shutdown-timeout facade-isolation no-locks load load-no-locks startup-lock
constructor-lookup registration-null-array registration-null-entry
registration-duplicate registration-value registration-shared
fatal-two fatal-caught fatal-three-caught fatal-three-debug
fatal-three-aborted-caught
aborted-child aborted-ancestor aborted-deep aborted-untyped aborted-finally
aborted-checkpoint aborted-held-gate shutdown-child
""".split()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", default="Debug", choices=["Debug", "Release"])
    parser.add_argument("--repeat", type=int, default=1)
    parser.add_argument("--skip-build", action="store_true")
    args = parser.parse_args()
    if args.repeat < 1:
        parser.error("--repeat must be positive")

    directory = Path(__file__).resolve().parent
    project = directory / "RMVC.ContractLookupChecks.csproj"
    if not args.skip_build:
        subprocess.run(
            ["dotnet", "build", str(project), "-c", args.configuration, "-v", "minimal"],
            check=True,
        )

    assembly = directory / "bin" / args.configuration / "net9.0" / "RMVC.ContractLookupChecks.dll"
    failures = []
    total = 0
    for iteration in range(args.repeat):
        for scenario in SCENARIOS:
            total += 1
            try:
                result = subprocess.run(
                    ["dotnet", str(assembly), "command-lock", scenario],
                    capture_output=True,
                    text=True,
                    timeout=30,
                )
            except subprocess.TimeoutExpired:
                failures.append((iteration + 1, scenario))
                print(f"FAIL {scenario}: subprocess deadline exceeded", flush=True)
                continue

            if scenario.startswith("fatal-"):
                passed = (
                    result.returncode != 0
                    and "Unhandled exception. RMVC.RCommandLockCycleException" in result.stderr
                    and "FAIL_NO_FATAL" not in result.stderr
                    and result.stderr.count("Unhandled exception.") == 1
                )
                if "caught" in scenario:
                    passed = passed and "APPLICATION_CAUGHT_CYCLE" in result.stdout
                passed = passed and "requested gates=" in result.stderr
                passed = passed and "waiting for" in result.stderr
            else:
                passed = result.returncode == 0

            if scenario == "duplicate-declaration":
                passed = passed and "COMMAND LOCK DUPLICATE" in result.stdout

            print(f"{'PASS' if passed else 'FAIL'} {scenario}", flush=True)
            for line in result.stdout.splitlines():
                if line.startswith("MEASURE "):
                    print(line, flush=True)
            if not passed:
                failures.append((iteration + 1, scenario))
                print(result.stdout + result.stderr, flush=True)

    print(f"{total - len(failures)}/{total} subprocess scenarios passed", flush=True)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
