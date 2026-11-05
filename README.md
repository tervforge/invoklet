# <img src="src/main/Invoklet/Assets/icon.svg" width="40" height="40" alt=""> Invoklet

[![Latest release](https://img.shields.io/github/v/release/tervforge/invoklet?label=release)](https://github.com/tervforge/invoklet/releases/latest)
[![CI](https://github.com/tervforge/invoklet/actions/workflows/dotnet-desktop.yml/badge.svg?branch=main)](https://github.com/tervforge/invoklet/actions/workflows/dotnet-desktop.yml?query=branch%3Amain)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](global.json)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-475569)](https://github.com/tervforge/invoklet/releases/latest)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

**A desktop scratchpad for C# and .NET.** Try a LINQ query, call an API, or check what's in a database.
Write a few lines, hit Run, and use `.Dump()` to see what came back. If the experiment turns into
something you'll use again, save it as a `.inv` file with your notes right alongside the code.

Invoklet is free and open source and runs on Windows, Linux, and macOS.

![Invoklet main window](https://tervforge.github.io/invoklet-docs/assets/screenshots/overview.png)

## ✨ Features

- 📝 **Start small.** Run one expression, a few statements, or a full `Main` in C# or Visual Basic.
  Keep notes in the `.inv` file and preview them beside the code.
- 🔎 **Find that script from last week.** Quick Open searches your scripts and bundled samples.
  Unsaved tabs can come back when you restart.
- 💡 **Let the editor lend a hand.** C# IntelliSense suggests code and imports, and the lightbulb
  offers quick fixes and refactorings.
- 🐛 **See where it went sideways.** Set a breakpoint, step through the script, and inspect locals
  and watches.
- 🧰 **Bring your own libraries.** Add NuGet packages or DLL references, then peek at decompiled C#
  and IL in Assembly Explorer.
- 🔌 **Call an API.** Use plain HTTP or generated OpenAPI/Swagger and gRPC clients. Browse the
  contract before you call it.
- 🗃️ **Poke around a database.** Browse SQLite, PostgreSQL, and SQL Server schemas and query them
  through typed EF Core contexts.
- 🔐 **Keep secrets out of scripts.** Store credentials by name and reuse them in scripts and
  connection settings.
- 📊 **Give it a `.Dump()`.** Inspect objects and tables, show live progress, or prompt for input.
  Export a table to CSV or save the whole result as HTML.
- ▶️ **Run it again with different inputs.** Save sets of typed `Arguments` and pick one in the
  toolbar or via `--args-set` in the CLI. Bring your old LINQPad queries along with the `.linq` importer.

## 📦 Download

Installers and portable builds are on the [releases page](https://github.com/tervforge/invoklet/releases/latest):

| OS | Package |
| --- | --- |
| Windows | `Invoklet-win-x64.msi`, or the portable `Invoklet-win-x64.zip` |
| Linux | `invoklet_<version>_amd64.deb`, `Invoklet-x86_64.AppImage` or `Invoklet-linux-x64.tar.gz` |
| macOS | `Invoklet-osx-arm64.dmg` (Apple Silicon) or `Invoklet-osx-x64.dmg` (Intel) |

Invoklet needs the [.NET SDK](https://dotnet.microsoft.com/download) to compile scripts. The macOS build
is not notarized yet — see the [installation guide](https://tervforge.github.io/invoklet-docs/install/)
for the first-launch steps on each OS.

## 📚 Documentation

- [User guide](https://tervforge.github.io/invoklet-docs/) — from your first run to your first breakpoint.
- [Scripts and `.inv` files](https://tervforge.github.io/invoklet-docs/scripts/) — how code, notes, and settings fit in one file.
- [Changelog](CHANGELOG.md) — what's new.
- [Roadmap](ROADMAP.md) — what's next.

## 🛠️ Building from source

You need the .NET 10 SDK (`global.json` pins the version).

```bash
dotnet build                                         # whole solution
dotnet run --project src/main/Invoklet/Invoklet.csproj   # start the app
dotnet test                                          # all tests
```

How to send changes, and the rules the code follows, are in [`CONTRIBUTING.md`](CONTRIBUTING.md).

## 🤝 Contributing

Bug reports, ideas and pull requests are welcome — please read [`CONTRIBUTING.md`](CONTRIBUTING.md)
first. Report problems as [GitHub issues](https://github.com/tervforge/invoklet/issues). Opening a
pull request means you agree to the [Contributor License Agreement](CLA.md).

## AvaloniaEdit fork

Invoklet's code editor is built on a **fork of [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
and AvaloniaEdit.TextMate**, taken from the 12.0.0 packages and kept in
[`src/controls/AvaloniaEdit`](src/controls/AvaloniaEdit) and
[`src/controls/AvaloniaEdit.TextMate`](src/controls/AvaloniaEdit.TextMate), so the editor control
itself can be changed for Invoklet — completion behavior, font rendering, macOS clipboard and
highlighting fixes. The fork keeps its upstream MIT license (`LICENSE` in each folder); its origin
and changes are described in [`src/controls/AvaloniaEdit/README.md`](src/controls/AvaloniaEdit/README.md).

## MarkView.Avalonia fork

A **fork of [MarkView.Avalonia](https://github.com/Kryptos-FR/MarkView.Avalonia)** and its
SyntaxHighlighting extension, taken from the 12.3.0 release, is kept in
[`src/controls/MarkView.Avalonia`](src/controls/MarkView.Avalonia) and
[`src/controls/MarkView.Avalonia.SyntaxHighlighting`](src/controls/MarkView.Avalonia.SyntaxHighlighting)
for the Markdown preview of scripts. It keeps its upstream MIT license (`LICENSE.md` in each
folder); its origin and changes are described in
[`src/controls/MarkView.Avalonia/README.md`](src/controls/MarkView.Avalonia/README.md).

## 🙌 Acknowledgements

- [netcoredbg](https://github.com/Samsung/netcoredbg) — the debugger, driven over the Debug Adapter
  Protocol.
- [Avalonia UI](https://avaloniaui.net/), and [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
  with its predecessor AvalonEdit from SharpDevelop — the editor Invoklet's fork is based on.
- [MarkView.Avalonia](https://github.com/Kryptos-FR/MarkView.Avalonia) and
  [Markdig](https://github.com/xoofx/markdig) — the Markdown viewer Invoklet's fork is based on.
- [Roslyn](https://github.com/dotnet/roslyn) for compiling and IntelliSense;
  [ILSpy](https://github.com/icsharpcode/ILSpy) for decompiling;
  [Spectre.Console](https://spectreconsole.net/) for the command-line output.
- [Roslynator](https://github.com/dotnet/roslynator) — the idea behind several analyzers.
- [LINQPad](https://www.linqpad.net/), [NetPad](https://github.com/tareqimbasher/NetPad) and
  [RoslynPad](https://github.com/roslynpad/roslynpad) — inspiration.

## 📄 License

Copyright (C) 2026 Vadim S ([svncore](https://github.com/svncore), <svnpub@proton.me>). Invoklet is licensed under the
[GNU General Public License v3.0](LICENSE).

The `Invoklet.Runtime` library ([`src/runtime/Invoklet.Runtime`](src/runtime/Invoklet.Runtime)) — the only
Invoklet assembly your scripts are compiled against and shipped with — is licensed under the
[MIT License](src/runtime/Invoklet.Runtime/LICENSE), so programs written in Invoklet can be distributed
under any license. The installers show an [end-user agreement](installer/EULA.txt) that summarizes
these terms; it adds no restrictions to the GPL-3.0.

Invoklet as distributed by the maintainer through an application store (the Apple App Store, the Mac App
Store, the Microsoft Store) — the application with its icon and other resources — is licensed under the
[Mozilla Public License 2.0](https://mozilla.org/MPL/2.0/) instead, an additional permission under section 7
of the GPL-3.0 ([`APP-STORE-EXCEPTION.txt`](APP-STORE-EXCEPTION.txt)). The source code stays GPL-3.0.

Third-party code keeps its own license: the [AvaloniaEdit fork](#avaloniaedit-fork) (MIT), the
[MarkView.Avalonia fork](#markviewavalonia-fork) (MIT) and the HTML renderer in [`src/controls/Invoklet.Avalonia.HtmlRenderer`](src/controls/Invoklet.Avalonia.HtmlRenderer)
(BSD-3-Clause and MIT), with license files next to the code. Every third-party component Invoklet ships,
with its license text, is listed in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
