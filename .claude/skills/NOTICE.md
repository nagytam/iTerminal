# Third-party .NET skills

Everything in this directory except `compare-console`, `fix-main` and `incremental-improvement` is
vendored from [dotnet/skills](https://github.com/dotnet/skills) at commit
`96c3d68dad2855e3ab5178aa1a262789d86a1466` (main, 2026-11-06).

Vendored plugins: `dotnet`, `dotnet-advanced`, `dotnet-diag`, `dotnet-msbuild`, `dotnet-nuget`,
`dotnet-test` and `dotnet11`. The MAUI, WinForms, Blazor, ASP.NET Core, data, template-engine,
upgrade, test-migration and experimental plugins are deliberately not included.

## Local changes

- Each skill directory was flattened from `plugins/<plugin>/skills/<skill>` to `.claude/skills/<skill>`.
- `csharp-expert` was rewritten to route among this vendored catalog instead of the full
  marketplace. Its `references/dotnet-skills-marketplace.md` was replaced with
  `references/skill-catalog.md`.
- References to skills outside the vendored subset were reworded so they do not point at missing
  skills.

## License

MIT

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
