---
name: build-and-test-after-scaffold
description: Builds and tests .NET projects after scaffold or code generation. Use when creating/updating project structure, adding features, or before finalizing changes to ensure build and tests pass.
---
# Build And Test After Scaffold

## When To Use
- After creating a new project or solution structure.
- After generating or refactoring code in this repository.
- Before committing when user asks for production readiness.

## Required Workflow
1. Restore and build solution:
   - `dotnet restore "CricketScheduler.sln"`
   - `dotnet build "CricketScheduler.sln" -c Debug`
2. Run test suite:
   - `dotnet test "CricketScheduler.sln" -c Debug --no-build`
3. If build/test fails:
   - Fix errors.
   - Re-run build and tests until both pass.
4. Report status clearly:
   - Build result
   - Test result (passed/failed + failed test names)
   - Any remaining blockers

## Notes
- Prefer running at solution level (`CricketScheduler.sln`) so app and tests stay aligned.
- If `dotnet` is unavailable in the current environment, state that explicitly and provide the exact commands for local execution.
