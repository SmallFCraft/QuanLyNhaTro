# QuanLyNhaTro project instructions

The client is .NET 8 WinForms with local HTML/CSS/JS in `QuanLyTro/Assets/wwwroot`. It opens `datlich.html` in WebView2. The .NET 8 server handles business rules and MySQL; the client connects to `127.0.0.1:8888` by default. Start MySQL and the server before the client. See [README.md](README.md) for the Windows startup steps.

## Local safety and verification

- Use only the configured development account on localhost. Demo accounts are seeded by `SeedDemoAsync` in `QuanLyTro/QuanLyTro.Server/Program.cs` when `tai_khoan` is empty. Do not invent or print credentials. `Database/seed_demo.sql` writes to `quanly_nhatro`; use it only with the development database.
- `QUANLYTRO_CONN` configures the server, not the test suite. `QuanLyTro/QuanLyTro.Tests/TestHelpers/TestDb.cs` is the source of truth for the local test connection. Integration setup **drops and recreates `quanly_nhatro_test`**. Confirm it is disposable before running the full suite.
- Preserve unrelated edits. Do not create summary, design, or plan Markdown files without permission. Do not commit or push unless asked; omit AI attribution from commit messages and PR bodies.

For UI markup and JavaScript contract checks without MySQL, run from the repo root:

```powershell
dotnet test QuanLyTro/QuanLyTro.Tests/QuanLyTro.Tests.csproj --filter "FullyQualifiedName~UiContractTests"
```

Baseline (2026-11-26): 33 passed. This filter reads source files; it does not test a running WebView2 window. `RealServerE2E` is skipped by default.

## GitNexus: selecting the repo and runner

Start with `ToolSearch("+gitnexus impact")`. In Claude Code the plugin exposes `mcp__plugin_gitnexus_gitnexus__impact`. Pass `repo: "QuanLyNhaTro-Team"` on every repo-scoped call to avoid selecting another checkout. For example, pass `{"target":"BookPublicAsync","direction":"upstream","repo":"QuanLyNhaTro-Team","file_path":"QuanLyTro/QuanLyTro.Server/Services/DatLichService.cs","maxDepth":1,"summaryOnly":true}` to that tool.

If MCP is unavailable, run the matching CLI operation from the repo root. Replace `BookPublicAsync` with the symbol or search topic you are working on.

| Situation | Graph tool | CLI fallback |
| --- | --- | --- |
| Find a feature flow | `query` | `node .gitnexus/run.cjs query BookPublicAsync --repo . --limit 1` |
| Inspect a named symbol | `context` | `node .gitnexus/run.cjs context BookPublicAsync --repo .` |
| Before changing a symbol | `impact` (upstream) | `node .gitnexus/run.cjs impact BookPublicAsync --direction upstream --repo . --depth 1 --limit 3` |
| Before an authorized commit | `detect_changes` (`scope: "all"`) | `node .gitnexus/run.cjs detect-changes --scope all --repo .` |

Check freshness with `node .gitnexus/run.cjs status`. If the wrapper is absent, use the installed `gitnexus` CLI with the same arguments. Refresh a stale index with `node .gitnexus/run.cjs analyze --index-only` (no instruction or skill regeneration). If both graph paths fail, report that the analysis could not run; do not treat the failure as zero impact.

GitNexus 1.6.12 has no `rename` or `explain` CLI subcommands. Preview a rename via MCP `rename` with `dry_run: true`; if it is unavailable, stop rather than substituting search-and-replace. Without MCP `explain`, review the source directly; no taint layer is not a clean scan. Generated project skills under `.claude/skills/` are ignored by Git, so check that a skill is available before relying on it.

The GitNexus block below is generated. Keep project rules above it so reindexing cannot overwrite them.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **QuanLyNhaTro-Team** (1723 symbols, 5397 relationships, 146 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/QuanLyNhaTro-Team/context` | Codebase overview, check index freshness |
| `gitnexus://repo/QuanLyNhaTro-Team/clusters` | All functional areas |
| `gitnexus://repo/QuanLyNhaTro-Team/processes` | All execution flows |
| `gitnexus://repo/QuanLyNhaTro-Team/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
