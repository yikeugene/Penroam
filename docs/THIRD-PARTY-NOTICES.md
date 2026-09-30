# Third-party notices

Penroam's original code is covered by the [MIT License](../LICENSE). The following components retain their own licenses and copyright notices.

| Component | Version | Copyright / license | Source |
|---|---|---|---|
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.0 | Microsoft Corporation; MIT | [Source](https://github.com/dotnet/efcore) |
| PDFsharp-WPF | 6.2.4 | Copyright 2026 empira; MIT | [Source](https://github.com/empira/PDFsharp) |
| PdfPig | 0.1.16 | UglyToad and contributors; Apache-2.0, with bundled PDFBox/FontBox and Adobe terms | [Source](https://github.com/UglyToad/PdfPig/tree/a7bb35662bbbf405efddad50aedc9bcdcf515afc) |
| SQLitePCLRaw bundle, configuration, core and provider | 3.0.5 | Copyright 2014-2026 SourceGear, LLC; Apache-2.0 | [Source](https://github.com/ericsink/SQLitePCL.raw) |
| SQLite native engine (SQLite NuGet package) | 3.53.4 | Public domain; package copyright 2014-2026 SourceGear, LLC | [Copyright dedication](https://sqlite.org/copyright.html) |
| Microsoft.Extensions.Logging.Abstractions and DependencyInjection.Abstractions | See package manifest | .NET Foundation and Contributors; MIT | [Source](https://github.com/dotnet/runtime) |
| .NET / Windows Desktop runtime and WinRT projections | See package manifest | Microsoft / .NET Foundation and Contributors; accompanying license terms and notices | [Source](https://github.com/dotnet/runtime) |

The Windows SDK projections and Microsoft runtime may contain additional third-party components. See the [Windows SDK and C#/WinRT notice](licenses/WINDOWS-SDK-NOTICE.md) for their applicable terms and source versions. The published distribution includes `third-party/DOTNET-LICENSE.txt`, `third-party/DOTNET-ThirdPartyNotices.txt`, package metadata, available upstream package notices, and `third-party/package-manifest.json`. Exact restored versions in that manifest take precedence over this overview. Penroam's MIT license does not replace any dependency's license terms.

Full Apache 2.0 license: [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt). SQLitePCLRaw is used without modifications. SQLite itself is dedicated to the public domain by its authors.

Penroam 1.0.0 uses unmodified PdfPig 0.1.16 for local PDF text extraction. Its complete upstream combined license is preserved at [licenses/PDFPIG-LICENSE.txt](licenses/PDFPIG-LICENSE.txt), including the PDFBox/FontBox redistribution conditions and Adobe font-metric/CMap terms. The file comes from the [exact source commit recorded in the NuGet package](https://raw.githubusercontent.com/UglyToad/PdfPig/a7bb35662bbbf405efddad50aedc9bcdcf515afc/LICENSE); the packaging whitelist includes it.

## MIT license

The following permission applies to the MIT-licensed components listed above, with each component's copyright notice retained above and in its accompanying package metadata.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Windows installer

The EXE installer is built with [Inno Setup 7.1.0](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0), by Jordan Russell and Martijn Laan. The installed distribution includes the original Inno Setup license at `third-party/INNO-SETUP-LICENSE.txt`. Inno Setup is a build tool; its compiler and local installation files are not part of Penroam's source tree or application payload.
