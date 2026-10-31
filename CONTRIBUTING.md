# Contributing

Thanks for helping. Bug reports, ideas, documentation fixes and code are all welcome. Report bugs and
ideas as [GitHub issues](https://github.com/tervforge/invoklet/issues) — for a bug, include your OS,
the Invoklet version (Help → About Invoklet), `dotnet --list-sdks` and the smallest `.inv` script that
shows the problem. For anything larger than a small fix, open an issue first and agree on the approach.
For what does not belong in a public issue, write to svnpub@proton.me.

## Getting started

You need the .NET 10 SDK (pinned in `global.json`). Debugging also needs
[netcoredbg](https://github.com/Samsung/netcoredbg); Invoklet's setup window can install it.

```bash
dotnet build
dotnet run --project src/main/Invoklet/Invoklet.csproj
```

Before opening a pull request, run what CI runs (on Windows, Linux and macOS):

```bash
dotnet format --verify-no-changes                  # `dotnet format` fixes it
dotnet build --configuration Release -warnaserror
dotnet test                                        # or one project: dotnet test tests/Invoklet.NonUI.Tests
```

The slow tests that use the real compiler, decompiler and highlighter (`tests/Invoklet.IntegrationTests`) are
built and formatted with the solution but not run when the `CI` environment variable is set; run them with
`dotnet test tests/Invoklet.IntegrationTests`.

### Tests against real database servers

`tests/Invoklet.Connectors.IntegrationTests` reads a real PostgreSQL and a real SQL Server through the
connection drivers. Each test skips itself unless its environment variable holds a connection string, so a
plain `dotnet test` needs nothing. CI starts both servers as service containers and sets the variables.

To run them locally you need Docker (or Podman) and the .NET 10 SDK.

1. Start the servers. The passwords are throwaway values for a local container; the SQL Server image is
   about 1.5 GB and does not run natively on some ARM hosts (on Apple silicon enable Rosetta emulation in
   Docker Desktop, or use `--platform linux/amd64`).

   ```bash
   docker run -d --name invoklet-pg -p 5432:5432 \
     -e POSTGRES_PASSWORD=invoklet -e POSTGRES_DB=shop postgres:16

   docker run -d --name invoklet-mssql -p 1433:1433 \
     -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Invoklet_Passw0rd!' \
     mcr.microsoft.com/mssql/server:2022-latest
   ```

2. Wait until they accept connections (SQL Server takes up to a minute):

   ```bash
   docker exec invoklet-pg pg_isready -U postgres
   docker exec invoklet-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Invoklet_Passw0rd!' -Q 'SELECT 1'
   ```

3. Point the tests at them and run (PowerShell: `$env:INVOKLET_TEST_POSTGRES = '...'`):

   ```bash
   export INVOKLET_TEST_POSTGRES='Host=localhost;Port=5432;Database=shop;Username=postgres;Password=invoklet'
   export INVOKLET_TEST_SQLSERVER='Server=localhost,1433;Database=master;User Id=sa;Password=Invoklet_Passw0rd!;TrustServerCertificate=true'
   dotnet test tests/Invoklet.Connectors.IntegrationTests
   ```

   Set only one variable to run only that server's tests. The tests create and drop their own schema
   `invoklet_it` in the given database and touch nothing else, but use a disposable database anyway.

4. Stop and remove the containers when done: `docker rm -f invoklet-pg invoklet-mssql`.

If a test fails with a connection error, check that the port is not taken by a local server
(`lsof -i :5432`, `lsof -i :1433`) and that the container is healthy (`docker ps`). Running the tests
against another server works the same way: put its connection string in the variable.

## Contributor License Agreement

By opening a pull request you agree to the [Contributor License Agreement](CLA.md) — nothing to sign.
You keep the copyright in your work and grant the maintainer a license to distribute it as part of
Invoklet, under the project's license and, if the licensing ever changes, under other terms.

## Rules

- **Language:** C# on .NET 10 only, with Avalonia UI for the interface. No web views or embedded
  browsers — Invoklet is a native app.
- **License:** contributions are GPL-3.0 ([`LICENSE`](LICENSE)); `src/runtime/Invoklet.Runtime` is MIT
  ([its `LICENSE`](src/runtime/Invoklet.Runtime/LICENSE)) and must stay free of dependencies and of code
  copied from other Invoklet projects, because it ships inside users' programs.
- **Clean-room:** do not copy code, icons or other assets from proprietary software.
  Matching a public API shape for compatibility is fine; the implementation must be your own, from
  documentation and observed behavior. Third-party code or assets need a permissive license compatible
  with GPL-3.0, keep that license next to them, and credit the source in the file (as the analyzers
  ported from Roslynator ideas do).
- **Never crash:** a broken script, bad input or a failing tool (SDK, NuGet, debugger) must never take
  Invoklet down. Report the error in the output or a dialog, catch at every `async void` boundary, and
  ship a regression test with every crash fix.
- **Layering:** `src/core` → `src/languages` → `src/main`; controls in `src/controls` stay
  language-agnostic — per-language behavior comes from the language host, never from a control.
- **AvaloniaEdit fork:** `src/controls/AvaloniaEdit` and `src/controls/AvaloniaEdit.TextMate` are a
  fork of AvaloniaEdit under its own MIT license (see
  [their README](src/controls/AvaloniaEdit/README.md)). Change them only when the control itself has to
  change — otherwise use `Invoklet.Avalonia.CodeTextEditor`; keep upstream's style and license headers,
  and leave the upstream suite in `tests/AvaloniaEdit.Tests` as it is (Invoklet's tests for the fork go
  to `tests/Invoklet.Runtime.UI.Tests`).
- **MarkView.Avalonia fork:** `src/controls/MarkView.Avalonia` and
  `src/controls/MarkView.Avalonia.SyntaxHighlighting` are a fork of MarkView.Avalonia under its own MIT
  license (see [their README](src/controls/MarkView.Avalonia/README.md)). The same rules as for the
  AvaloniaEdit fork apply; its upstream suites live in `tests/MarkView.Avalonia.Tests` and
  `tests/MarkView.Avalonia.SyntaxHighlighting.Tests`.
- **Architecture:** follow the [MVVM guidelines](MVVM_GUIDELINES.md) and
  [SOLID guidelines](SOLID_GUIDELINES.md) for new and changed code. Use ReactiveUI only (no
  CommunityToolkit.Mvvm); register services in `App.axaml.cs`.
- **Packages:** versions live in `Directory.Packages.props`; a `.csproj` references packages by name only.
  A new package must have a license compatible with GPL-3.0; after adding, removing or updating one,
  regenerate `THIRD-PARTY-NOTICES.txt` with `python scripts/make-third-party-notices.py` and commit it.
- **Tests:** required for every change. UI tests that create real Avalonia controls go to
  `tests/Invoklet.Runtime.UI.Tests`, everything else to `tests/Invoklet.NonUI.Tests` or the project's own
  test project. A test of a window or control replaces the classes it only uses with mocks or fakes and
  does not compile or decompile anything; what needs the real thing goes to `tests/Invoklet.IntegrationTests`.
  New and modified unit tests follow the [unit testing guidelines](UNIT_TESTING_GUIDELINES.md).
- **Style:** `.editorconfig` (CRLF, 4 spaces, Allman braces, file-scoped namespaces) and no warnings.
  Match the surrounding code. Comments are in English, explain *why* and stand on their own — no
  references to issue numbers or notes outside the repository.
- **UI:** use the theme resources from `Invoklet.Avalonia.Themes` instead of hard-coded colors, check
  both the light and dark themes, and attach before/after screenshots to the pull request.
- **Commits:** small and focused, with a short imperative subject (`Fix …`, `Add …`); one topic per
  pull request.
- **Docs:** if users can see the change, update the [user guide](https://github.com/tervforge/invoklet-docs)
  too or mention it in the pull request.
- **Changelog:** if users can see the change, add a line for them under `## [Unreleased]` in
  [`CHANGELOG.md`](CHANGELOG.md) — it becomes the release notes of the next version.

## Adding an analyzer

Invoklet ships its own C# analyzers (`RPADxxxx`), shown as squiggles while typing and next to the
lightbulb.

1. **Check it is new.** Search `src/languages/Invoklet.Languages.CSharp/IntelliSense/Analyzers/` and the
   built-in Roslyn analyzers — extend an existing analyzer rather than add a near-duplicate.
2. **Write the analyzer** as one file in that folder: a `[DiagnosticAnalyzer(LanguageNames.CSharp)]`
   class with a public `DiagnosticId` constant — the next free `RPADxxxx` number — and a
   `DiagnosticDescriptor` with a title, message and description. Register only the syntax or operation
   kinds you need, call `EnableConcurrentExecution()` and skip generated code. Invoklet discovers it by
   reflection; there is no list to update.
3. **Keep false positives near zero.** Use the semantic model rather than names alone, and when the
   rule is "this value always throws", confirm it against the real .NET API.
4. **Credit the source** in a comment at the top if the idea comes from another project.
5. **Unit tests** in `tests/Invoklet.NonUI.Tests/InvokletAnalyzersTests.cs`: cases that must be flagged
   and cases that must not be.
6. **End-to-end test** in `tests/Invoklet.NonUI.Tests/CSharpIntelliSenseAnalyzersTests.cs` proving the
   diagnostic reaches the editor through the real IntelliSense pipeline.
