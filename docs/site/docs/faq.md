---
title: FAQ
---

# FAQ

## Why not just grep the source files?

Grep finds text patterns. `roslyn-codelens-mcp` understands semantic structure — types, interfaces, inheritance, call graphs, DI registrations. Grep on `IGreeter` misses implementations that store the service under a different variable name, and can't answer "what calls this method" or "what implements this interface."

## Does it support multiple solutions?

Yes. Use `list_solutions`, `set_active_solution`, `load_solution`, and `unload_solution` to manage multiple solutions. Only one is active at a time for tool calls.

## How does hot reload work?

The server runs a `FileChangeTracker` that watches for file system changes. Saving a `.cs` file updates the Roslyn workspace automatically — no `sync_documents` call needed.

## Why does `find_references` return nothing for an external type?

The package must be referenced by at least one project in the loaded solution. Call `get_nuget_dependencies` to check, then make sure the assembly name matches exactly.

## How fast is it?

Solution load takes 5–30 seconds (once at startup). After that, most calls run in under 1ms–3ms. Heavier operations:

| Tool | Typical latency |
|------|----------------|
| `find_naming_violations` | ~8ms |
| `find_references` | ~3ms |
| `analyze_change_impact` | ~4ms |
| `search_symbols` | ~1.4ms |

Full benchmark results are in the [repository](https://github.com/MarcelRoozekrans/roslyn-codelens-mcp/tree/main/benchmarks).

## What .NET versions are supported?

The server targets .NET 10 and .NET 11 and can analyse solutions targeting any .NET version (net48, net6.0, net8.0, net10.0, etc.). **SDK-style** projects (`<Project Sdk="...">`) — including SDK-style `net48` — work everywhere the .NET SDK works.

## Are legacy (non-SDK-style) .NET Framework projects supported?

**Yes on Windows with Visual Studio or Build Tools; otherwise they are skipped and the rest of the solution still loads.**

Classic projects use the old MSBuild XML format (`<Project ToolsVersion="..." xmlns="http://schemas.microsoft.com/developer/msbuild/2003">`) and need Framework MSBuild targets that ship with Visual Studio / Build Tools. Roslyn evaluates them in an out-of-process `BuildHost-net472`; the MCP process itself still registers the .NET SDK via `MSBuildLocator.RegisterDefaults`. A Developer Command Prompt alone is not what enables this — the install of VS/Build Tools MSBuild is.

What the server does:

- Probes for VS/Build Tools MSBuild (`vswhere` and well-known install paths). Override with `ROSLYN_CODELENS_FRAMEWORK_MSBUILD_AVAILABLE` (`0`/`1`) for tests.
- When the probe succeeds: loads classic projects alongside SDK-style ones (mixed solutions use both BuildHosts).
- When the probe fails (Linux, Docker, or Windows without VS/Build Tools): skips classic projects into `SkippedProjects` with `Kind: "Legacy"`, and loads all SDK-style projects normally.
- Exposes the skipped list through `list_solutions` and in the `load_solution` summary.

Without VS/Build Tools, convert classic projects to SDK-style csproj format to analyse them.

## Why is `list_solutions` showing `SkippedProjects` for my solution?

Those are projects that couldn't be loaded by MSBuildWorkspace. Each entry tells you the reason — typically `Legacy` (classic Framework, no Framework MSBuild available), `Failed` / `Timeout` (open attempted but evaluation failed), or `Missing` (referenced from the `.sln` but the file doesn't exist on disk). All other projects in the solution loaded normally and can be queried by any tool.
