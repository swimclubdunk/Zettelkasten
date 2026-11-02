# Zettelkasten Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fully offline WPF Zettelkasten app with unique-ID notes, inline + manual links with backlinks, tags with semantic duplicate suggestions, and hybrid keyword/semantic search, all stored in one local SQLite file.

**Architecture:** Four projects: `Core` (pure domain logic + interfaces, no packages), `Infrastructure` (EF Core/SQLite + FTS5, ONNX embeddings, vector index, search), `App` (WPF + MVVM, DI composition root), `Tests` (xUnit). Every note save is one SQLite transaction covering note, links, tags, FTS row and embedding. Semantic search embeds text locally with all-MiniLM-L6-v2 and does brute-force dot-product search over an in-memory index.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, EF Core 10 (SQLite), SQLite FTS5, Microsoft.ML.OnnxRuntime, AvalonEdit, Markdig, Serilog, xUnit, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-11-02-zettelkasten-design.md`

**Deviations from spec (decided while planning):**
- `IClock` is replaced by the BCL `TimeProvider` (testable with `FakeTimeProvider`).
- Instead of `Microsoft.ML.Tokenizers`, the plan implements a small BERT WordPiece tokenizer in Infrastructure (fully testable, no API uncertainty).
- Task 22 updates the spec to record both.

## Global Constraints

- Target frameworks: `net10.0` for Core and Infrastructure; `net10.0-windows` for App and Tests.
- `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`.
- Core has **no** NuGet package dependencies.
- No network access at runtime. The only network use is `scripts/fetch-model.ps1`, run once at setup.
- Database: `%LocalAppData%\Zettelkasten\zettelkasten.db`, WAL mode, schema via EF Core migrations.
- Logs: `%LocalAppData%\Zettelkasten\logs\`, rolling daily, 14 files retained.
- Note ID format: `yyyyMMddHHmmss` (14 digits, local creation time); on collision add one second until free.
- Tag names normalized: trim, lowercase, collapse whitespace runs to `-`.
- Embedding: 384-dim float32, L2-normalized; max 256 tokens incl. `[CLS]`/`[SEP]`.
- Thresholds: semantic search score ≥ 0.35; tag suggestion score ≥ 0.75; max 5 tag suggestions.
- Search: debounce 250 ms; FTS prefix matching ranked by `bm25`; merge with RRF k = 60; empty query → 50 most recently updated notes; results capped at 50.
- Auto-save: 1 s after the last edit, on note switch, on window close; `Ctrl+S` forces a save.
- Package versions (pin exactly; if one fails to restore, use the nearest newer stable version supporting net10 and mention it in the commit message):
  - CommunityToolkit.Mvvm 8.4.0
  - Microsoft.Extensions.Hosting 10.0.0
  - Microsoft.EntityFrameworkCore.Sqlite 10.0.0, Microsoft.EntityFrameworkCore.Design 10.0.0, dotnet-ef tool 10.0.0
  - Microsoft.ML.OnnxRuntime 1.22.0
  - AvalonEdit 6.3.0.90
  - Markdig 0.40.0
  - Serilog.Extensions.Hosting 9.0.0, Serilog.Sinks.File 6.0.0
  - xunit 2.9.3, xunit.runner.visualstudio 3.1.0, Microsoft.NET.Test.Sdk 17.14.1, Microsoft.Extensions.TimeProvider.Testing 10.0.0
- Commit after every task; messages end with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (pass as a second `-m`).
- Commands are PowerShell and run from the repository root.

## Review Focus

1. **Search input containing FTS5 syntax** (`"`, `*`, `-`, `:`, `(`, `AND`, `NOT`, `NEAR`, unbalanced quotes, punctuation only) must never throw; it returns matches or an empty list. Pinned in Task 13 (`Search_never_throws_on_fts_syntax`).
2. **Switching notes or closing during the 1 s auto-save delay** must save the pending edits to the *original* note, never to the newly opened one. Pinned in Task 16 (`Loading_another_note_flushes_pending_edits_to_previous_note`).
3. **Creating several notes within the same second** (holding Ctrl+N) must yield distinct IDs. Pinned in Task 8 (`Create_twice_in_same_second_yields_distinct_ids`).
4. **Self-links, repeated `[[id]]` links, and a manual link duplicating an inline link** must save without primary-key violations. Pinned in Task 8 (`Save_with_duplicate_and_self_links_succeeds`).
5. **Accents/Unicode in tags and search** (`Café` vs `cafe`, emoji): tags normalize consistently and keyword search ignores diacritics. Pinned in Task 3 (`Normalize_handles_unicode`) and Task 13 (`Keyword_search_ignores_diacritics`).

---

## File Map

```
Zettelkasten.slnx
Directory.Build.props
.config/dotnet-tools.json                 dotnet-ef local tool
scripts/fetch-model.ps1                   one-time model download
models/                                   model.onnx + vocab.txt (git-ignored)
src/Zettelkasten.Core/
  Ids/NoteIdGenerator.cs                  timestamp IDs with collision handling
  Links/LinkParser.cs                     [[id]] extraction + position lookup
  Links/WikiLinkRewriter.cs               [[id]] -> Markdown links for preview
  Tags/TagNormalizer.cs                   tag name normalization
  Tags/InitialsMatcher.cs                 ml <-> machine-learning
  Search/RankFusion.cs                    Reciprocal Rank Fusion
  Search/SearchQuery.cs                   parsed query record + parser
  Search/SemanticSettings.cs              thresholds
  Search/VectorMath.cs                    dot, normalize, float[] <-> byte[]
  Common/Debouncer.cs                     TimeProvider-based debounce
  Common/NavigationHistory.cs             back/forward stacks
  Model/Models.cs                         Note, NoteSummary, LinkKind, LinkInfo, Tag, SearchResult, TagSuggestion(s)
  Abstractions/Abstractions.cs            repository/service interfaces
src/Zettelkasten.Infrastructure/
  AppPaths.cs                             data/log/model paths
  Data/Entities.cs, Data/AppDbContext.cs, Data/DesignTimeFactory.cs
  Data/Migrations/*                       EF migrations (InitialCreate incl. FTS5)
  Data/DbInitializer.cs                   backup-before-migrate, migrate, WAL
  Data/FtsIndex.cs                        FTS5 upsert/delete/match helpers
  Data/SqliteBackup.cs                    online backup helper
  Notes/NoteRepository.cs
  Tags/TagRepository.cs, Tags/TagSuggestionService.cs
  Embeddings/ContentHash.cs, WordPieceTokenizer.cs, OnnxEmbeddingService.cs, VectorIndex.cs, EmbeddingBackfillService.cs
  Search/SearchService.cs
  Backup/BackupService.cs
  DependencyInjection.cs                  AddZettelkastenInfrastructure()
src/Zettelkasten.App/
  App.xaml(.cs)                           host, logging, startup, global exception handlers
  AppServices.cs                          composition root (AddZettelkastenApp)
  Themes/Theme.xaml                       colors + styles
  Converters/InverseBooleanToVisibilityConverter.cs
  Services/IDialogService.cs, DialogService.cs
  ViewModels/MainViewModel.cs, SearchViewModel.cs, NoteEditorViewModel.cs, NotePanelViewModel.cs
  Views/MainWindow.xaml(.cs), NoteEditorView.xaml(.cs), NotePanelView.xaml(.cs), NotePickerWindow.xaml(.cs)
  Editor/Markdown.xshd, Editor/WikiLinkCompletionData.cs
  Preview/FlowDocumentRenderer.cs
tests/Zettelkasten.Tests/
  Core/*Tests.cs, Infrastructure/*Tests.cs, App/*Tests.cs
  Support/TestDb.cs, FakeEmbeddingService.cs, FakeNoteRepository.cs, FakeSearchService.cs,
          FakeDialogService.cs, StaRunner.cs
```

---

### Task 1: Solution scaffolding

**Files:**
- Create: `Directory.Build.props`, `Zettelkasten.slnx`, `src/Zettelkasten.Core/Zettelkasten.Core.csproj`, `src/Zettelkasten.Infrastructure/Zettelkasten.Infrastructure.csproj`, `src/Zettelkasten.App/*` (template), `tests/Zettelkasten.Tests/Zettelkasten.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: buildable solution; references App → Infrastructure → Core, App → Core, Tests → all three; `InternalsVisibleTo("Zettelkasten.Tests")` on all three src projects.

- [ ] **Step 1: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- Package advisories / version bumps must not break the build -->
    <WarningsNotAsErrors>NU1603;NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create projects and solution**

```powershell
dotnet new sln -n Zettelkasten --format slnx
dotnet new classlib -n Zettelkasten.Core -o src/Zettelkasten.Core -f net10.0
dotnet new classlib -n Zettelkasten.Infrastructure -o src/Zettelkasten.Infrastructure -f net10.0
dotnet new wpf -n Zettelkasten.App -o src/Zettelkasten.App -f net10.0
Remove-Item src/Zettelkasten.Core/Class1.cs, src/Zettelkasten.Infrastructure/Class1.cs
New-Item -ItemType Directory -Force tests/Zettelkasten.Tests | Out-Null
```

- [ ] **Step 3: Write `tests/Zettelkasten.Tests/Zettelkasten.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <!-- UseWPF removes System.IO from implicit usings (Path clash with System.Windows.Shapes) -->
    <Using Include="System.IO" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Zettelkasten.Core\Zettelkasten.Core.csproj" />
    <ProjectReference Include="..\..\src\Zettelkasten.Infrastructure\Zettelkasten.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\Zettelkasten.App\Zettelkasten.App.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Real embedding model for sanity tests; those tests skip when absent -->
    <None Include="..\..\models\**" Link="models\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add references, solution entries, assembly name, InternalsVisibleTo**

```powershell
dotnet add src/Zettelkasten.Infrastructure reference src/Zettelkasten.Core
dotnet add src/Zettelkasten.App reference src/Zettelkasten.Core src/Zettelkasten.Infrastructure
dotnet sln Zettelkasten.slnx add src/Zettelkasten.Core src/Zettelkasten.Infrastructure src/Zettelkasten.App tests/Zettelkasten.Tests
```

In `src/Zettelkasten.App/Zettelkasten.App.csproj`, add inside the main `<PropertyGroup>`:

```xml
    <AssemblyName>Zettelkasten</AssemblyName>
```

In each of the three `src` csproj files add:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Zettelkasten.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Build and test**

Run: `dotnet build Zettelkasten.slnx` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test Zettelkasten.slnx` → Expected: exit code 0 (no tests yet).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "chore: scaffold solution with Core, Infrastructure, App and Tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```


### Task 2: Core – note IDs and link parsing

**Files:**
- Create: `src/Zettelkasten.Core/Ids/NoteIdGenerator.cs`, `src/Zettelkasten.Core/Links/LinkParser.cs`, `src/Zettelkasten.Core/Links/WikiLinkRewriter.cs`
- Test: `tests/Zettelkasten.Tests/Core/NoteIdGeneratorTests.cs`, `tests/Zettelkasten.Tests/Core/LinkParserTests.cs`, `tests/Zettelkasten.Tests/Core/WikiLinkRewriterTests.cs`

**Interfaces:**
- Produces:
  - `static string NoteIdGenerator.Generate(DateTimeOffset localNow, Func<string, bool> exists)`
  - `static bool NoteIdGenerator.IsValid(string? id)`
  - `readonly record struct LinkMatch(int Start, int Length, string TargetId)`
  - `static IReadOnlyList<LinkMatch> LinkParser.Matches(string text)`
  - `static IReadOnlyList<string> LinkParser.ExtractTargets(string text)` (distinct, first-occurrence order)
  - `static LinkMatch? LinkParser.FindAt(string text, int offset)`
  - `static string WikiLinkRewriter.Rewrite(string markdown, Func<string, string?> titleLookup)`; constants `NoteScheme = "zk:"`, `BrokenScheme = "zk-broken:"`

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Core/NoteIdGeneratorTests.cs`
```csharp
using Zettelkasten.Core.Ids;

namespace Zettelkasten.Tests.Core;

public class NoteIdGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 14, 30, 15, 789, TimeSpan.FromHours(1));

    [Fact]
    public void Generate_formats_local_time_to_the_second()
    {
        Assert.Equal("20261102143015", NoteIdGenerator.Generate(Now, _ => false));
    }

    [Fact]
    public void Generate_skips_taken_ids_by_one_second()
    {
        var taken = new HashSet<string> { "20261102143015", "20261102143016" };
        Assert.Equal("20261102143017", NoteIdGenerator.Generate(Now, taken.Contains));
    }

    [Theory]
    [InlineData("20261102143015", true)]
    [InlineData("2026110214301", false)]
    [InlineData("2026110214301a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_checks_fourteen_ascii_digits(string? id, bool expected)
    {
        Assert.Equal(expected, NoteIdGenerator.IsValid(id));
    }
}
```

`tests/Zettelkasten.Tests/Core/LinkParserTests.cs`
```csharp
using Zettelkasten.Core.Links;

namespace Zettelkasten.Tests.Core;

public class LinkParserTests
{
    [Fact]
    public void ExtractTargets_returns_distinct_ids_in_order()
    {
        var text = "See [[20261102143015]] and [[20261030091200]], again [[20261102143015]].";
        Assert.Equal(new[] { "20261102143015", "20261030091200" }, LinkParser.ExtractTargets(text));
    }

    [Theory]
    [InlineData("[[123]]")]
    [InlineData("[[2026110214301a]]")]
    [InlineData("[20261102143015]")]
    [InlineData("[[ 20261102143015 ]]")]
    [InlineData("[[202611021430150]]")]
    public void ExtractTargets_ignores_invalid_links(string text)
    {
        Assert.Empty(LinkParser.ExtractTargets(text));
    }

    [Fact]
    public void Matches_reports_positions()
    {
        var m = Assert.Single(LinkParser.Matches("ab [[20261102143015]] cd"));
        Assert.Equal(3, m.Start);
        Assert.Equal(18, m.Length);
        Assert.Equal("20261102143015", m.TargetId);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(10, true)]
    [InlineData(21, true)]
    [InlineData(2, false)]
    [InlineData(22, false)]
    public void FindAt_finds_link_under_offset(int offset, bool found)
    {
        var result = LinkParser.FindAt("ab [[20261102143015]] cd", offset);
        Assert.Equal(found, result is not null);
    }
}
```

`tests/Zettelkasten.Tests/Core/WikiLinkRewriterTests.cs`
```csharp
using Zettelkasten.Core.Links;

namespace Zettelkasten.Tests.Core;

public class WikiLinkRewriterTests
{
    [Fact]
    public void Rewrite_turns_known_links_into_markdown_links_with_title()
    {
        var md = WikiLinkRewriter.Rewrite("go [[20261102143015]]!", id => "Spaced [rep]");
        Assert.Equal(@"go [Spaced \[rep\]](zk:20261102143015)!", md);
    }

    [Fact]
    public void Rewrite_marks_unknown_links_as_broken()
    {
        var md = WikiLinkRewriter.Rewrite("[[20261102143015]]", _ => null);
        Assert.Equal("[⚠ 20261102143015](zk-broken:20261102143015)", md);
    }

    [Fact]
    public void Rewrite_uses_id_when_title_is_blank()
    {
        var md = WikiLinkRewriter.Rewrite("[[20261102143015]]", _ => "  ");
        Assert.Equal("[20261102143015](zk:20261102143015)", md);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NoteIdGenerator|FullyQualifiedName~LinkParser|FullyQualifiedName~WikiLinkRewriter"`
Expected: build FAILS (types don't exist).

- [ ] **Step 3: Implement**

`src/Zettelkasten.Core/Ids/NoteIdGenerator.cs`
```csharp
using System.Globalization;

namespace Zettelkasten.Core.Ids;

/// <summary>Creates Zettelkasten IDs from the local creation time (yyyyMMddHHmmss).</summary>
public static class NoteIdGenerator
{
    public const string Format = "yyyyMMddHHmmss";

    public static string Generate(DateTimeOffset localNow, Func<string, bool> exists)
    {
        var t = new DateTime(localNow.Year, localNow.Month, localNow.Day,
            localNow.Hour, localNow.Minute, localNow.Second);
        while (true)
        {
            var id = t.ToString(Format, CultureInfo.InvariantCulture);
            if (!exists(id)) return id;
            t = t.AddSeconds(1);
        }
    }

    public static bool IsValid(string? id) => id is { Length: 14 } && id.All(char.IsAsciiDigit);
}
```

`src/Zettelkasten.Core/Links/LinkParser.cs`
```csharp
using System.Text.RegularExpressions;

namespace Zettelkasten.Core.Links;

public readonly record struct LinkMatch(int Start, int Length, string TargetId);

/// <summary>Finds inline [[yyyyMMddHHmmss]] links in Markdown text.</summary>
public static partial class LinkParser
{
    [GeneratedRegex(@"\[\[(\d{14})\]\]")]
    private static partial Regex LinkRegex();

    public static IReadOnlyList<LinkMatch> Matches(string text) =>
        LinkRegex().Matches(text)
            .Select(m => new LinkMatch(m.Index, m.Length, m.Groups[1].Value))
            .ToList();

    public static IReadOnlyList<string> ExtractTargets(string text) =>
        Matches(text).Select(m => m.TargetId).Distinct().ToList();

    public static LinkMatch? FindAt(string text, int offset)
    {
        foreach (var m in Matches(text))
            if (offset >= m.Start && offset <= m.Start + m.Length) return m;
        return null;
    }
}
```

`src/Zettelkasten.Core/Links/WikiLinkRewriter.cs`
```csharp
namespace Zettelkasten.Core.Links;

/// <summary>Turns [[id]] into Markdown links the preview renderer understands.</summary>
public static class WikiLinkRewriter
{
    public const string NoteScheme = "zk:";
    public const string BrokenScheme = "zk-broken:";

    public static string Rewrite(string markdown, Func<string, string?> titleLookup)
    {
        var matches = LinkParser.Matches(markdown);
        if (matches.Count == 0) return markdown;

        var sb = new System.Text.StringBuilder(markdown.Length + matches.Count * 16);
        var pos = 0;
        foreach (var m in matches)
        {
            sb.Append(markdown, pos, m.Start - pos);
            var title = titleLookup(m.TargetId);
            if (title is null)
                sb.Append($"[⚠ {m.TargetId}]({BrokenScheme}{m.TargetId})");
            else
            {
                var text = string.IsNullOrWhiteSpace(title) ? m.TargetId : Escape(title);
                sb.Append($"[{text}]({NoteScheme}{m.TargetId})");
            }
            pos = m.Start + m.Length;
        }
        sb.Append(markdown, pos, markdown.Length - pos);
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NoteIdGenerator|FullyQualifiedName~LinkParser|FullyQualifiedName~WikiLinkRewriter"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): note id generation and wiki link parsing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Core – tag normalization and initials matching

**Files:**
- Create: `src/Zettelkasten.Core/Tags/TagNormalizer.cs`, `src/Zettelkasten.Core/Tags/InitialsMatcher.cs`
- Test: `tests/Zettelkasten.Tests/Core/TagNormalizerTests.cs`, `tests/Zettelkasten.Tests/Core/InitialsMatcherTests.cs`

**Interfaces:**
- Produces: `static string TagNormalizer.Normalize(string input)` (returns `""` when nothing usable remains); `static bool InitialsMatcher.IsMatch(string a, string b)` (inputs already normalized).

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Core/TagNormalizerTests.cs`
```csharp
using Zettelkasten.Core.Tags;

namespace Zettelkasten.Tests.Core;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("Machine Learning", "machine-learning")]
    [InlineData("  machine   learning  ", "machine-learning")]
    [InlineData("#Memory", "memory")]
    [InlineData("a - b", "a-b")]
    [InlineData("--x--", "x")]
    [InlineData("   ", "")]
    [InlineData("#", "")]
    public void Normalize_basic(string input, string expected)
    {
        Assert.Equal(expected, TagNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_handles_unicode()
    {
        Assert.Equal("café-society", TagNormalizer.Normalize("  Café Society "));
        // decomposed e + combining acute must equal the composed form
        Assert.Equal(TagNormalizer.Normalize("Café"), TagNormalizer.Normalize("Café"));
        Assert.Equal("🧠-brain", TagNormalizer.Normalize("🧠 Brain"));
    }
}
```

`tests/Zettelkasten.Tests/Core/InitialsMatcherTests.cs`
```csharp
using Zettelkasten.Core.Tags;

namespace Zettelkasten.Tests.Core;

public class InitialsMatcherTests
{
    [Theory]
    [InlineData("ml", "machine-learning", true)]
    [InlineData("machine-learning", "ml", true)]
    [InlineData("nlp", "natural-language-processing", true)]
    [InlineData("ml", "machine-learning-ops", false)]
    [InlineData("m", "memory", false)]
    [InlineData("ml", "ml", false)]
    [InlineData("ai-ml", "artificial-intelligence", false)]
    public void IsMatch(string a, string b, bool expected)
    {
        Assert.Equal(expected, InitialsMatcher.IsMatch(a, b));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TagNormalizer|FullyQualifiedName~InitialsMatcher"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Core/Tags/TagNormalizer.cs`
```csharp
using System.Text;

namespace Zettelkasten.Core.Tags;

public static class TagNormalizer
{
    public static string Normalize(string input)
    {
        var s = input.Normalize(NormalizationForm.FormC).Trim().TrimStart('#').ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        var pendingDash = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                pendingDash = sb.Length > 0;
                continue;
            }
            if (pendingDash) sb.Append('-');
            pendingDash = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
```

`src/Zettelkasten.Core/Tags/InitialsMatcher.cs`
```csharp
namespace Zettelkasten.Core.Tags;

/// <summary>Detects abbreviations: "ml" is the initials of "machine-learning".</summary>
public static class InitialsMatcher
{
    public static bool IsMatch(string a, string b)
    {
        var (shortTag, longTag) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shortTag.Length < 2 || shortTag.Contains('-')) return false;
        var parts = longTag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != shortTag.Length) return false;
        for (var i = 0; i < parts.Length; i++)
            if (parts[i][0] != shortTag[i]) return false;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TagNormalizer|FullyQualifiedName~InitialsMatcher"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): tag normalization and initials matching" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Core – search primitives (query parsing, rank fusion, vector math, settings)

**Files:**
- Create: `src/Zettelkasten.Core/Search/SearchQuery.cs`, `RankFusion.cs`, `VectorMath.cs`, `SemanticSettings.cs`
- Test: `tests/Zettelkasten.Tests/Core/SearchQueryTests.cs`, `RankFusionTests.cs`, `VectorMathTests.cs`

**Interfaces:**
- Consumes: `TagNormalizer.Normalize` (Task 3)
- Produces:
  - `sealed record SearchQuery(string FreeText, IReadOnlyList<string> Tags, string? IdPrefix)` with `bool IsEmpty` and `static SearchQuery Parse(string? input)`
  - `static IReadOnlyList<string> RankFusion.Fuse(IReadOnlyList<IReadOnlyList<string>> rankings, int k = SemanticSettings.RrfK)`
  - `static float VectorMath.Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)`, `static void VectorMath.Normalize(Span<float> v)`, `static byte[] VectorMath.ToBytes(float[] v)`, `static float[] VectorMath.FromBytes(byte[] b)`
  - `static class SemanticSettings` consts: `Dimensions = 384`, `MaxTokens = 256`, `SearchMinScore = 0.35f`, `TagSuggestionMinScore = 0.75f`, `MaxTagSuggestions = 5`, `RrfK = 60`, `MaxResults = 50`, `CandidateLimit = 100`

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Core/SearchQueryTests.cs`
```csharp
using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class SearchQueryTests
{
    [Fact]
    public void Parse_splits_text_tags_and_id_prefix()
    {
        var q = SearchQuery.Parse("  how memory  #Learning #deep-work id:202611 works ");
        Assert.Equal("how memory works", q.FreeText);
        Assert.Equal(new[] { "learning", "deep-work" }, q.Tags);
        Assert.Equal("202611", q.IdPrefix);
        Assert.False(q.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    public void Parse_empty_inputs(string? input)
    {
        Assert.True(SearchQuery.Parse(input).IsEmpty);
    }

    [Fact]
    public void Parse_treats_non_numeric_id_prefix_as_text()
    {
        var q = SearchQuery.Parse("id:abc");
        Assert.Null(q.IdPrefix);
        Assert.Equal("id:abc", q.FreeText);
    }
}
```

`tests/Zettelkasten.Tests/Core/RankFusionTests.cs`
```csharp
using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class RankFusionTests
{
    [Fact]
    public void Items_in_both_lists_rank_first()
    {
        var fused = RankFusion.Fuse([["a", "b", "c"], ["c", "d"]]);
        Assert.Equal("c", fused[0]);
        Assert.Equal(new[] { "c", "a", "d", "b" }, fused);
    }

    [Fact]
    public void Single_list_keeps_order_and_empty_lists_are_fine()
    {
        Assert.Equal(new[] { "x", "y" }, RankFusion.Fuse([["x", "y"], []]));
        Assert.Empty(RankFusion.Fuse([]));
    }
}
```

`tests/Zettelkasten.Tests/Core/VectorMathTests.cs`
```csharp
using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class VectorMathTests
{
    [Fact]
    public void Normalize_then_dot_with_self_is_one()
    {
        var v = new float[] { 3, 4 };
        VectorMath.Normalize(v);
        Assert.Equal(0.6f, v[0], 5);
        Assert.Equal(1f, VectorMath.Dot(v, v), 5);
    }

    [Fact]
    public void Normalize_zero_vector_stays_zero()
    {
        var v = new float[3];
        VectorMath.Normalize(v);
        Assert.All(v, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Bytes_round_trip()
    {
        var v = new[] { 1.5f, -2f, 0.25f };
        Assert.Equal(v, VectorMath.FromBytes(VectorMath.ToBytes(v)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchQuery|FullyQualifiedName~RankFusion|FullyQualifiedName~VectorMath"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Core/Search/SemanticSettings.cs`
```csharp
namespace Zettelkasten.Core.Search;

/// <summary>All tuning knobs for search and semantic matching in one place.</summary>
public static class SemanticSettings
{
    public const int Dimensions = 384;
    public const int MaxTokens = 256;
    public const float SearchMinScore = 0.35f;
    public const float TagSuggestionMinScore = 0.75f;
    public const int MaxTagSuggestions = 5;
    public const int RrfK = 60;
    public const int MaxResults = 50;
    public const int CandidateLimit = 100;
}
```

`src/Zettelkasten.Core/Search/SearchQuery.cs`
```csharp
using Zettelkasten.Core.Tags;

namespace Zettelkasten.Core.Search;

public sealed record SearchQuery(string FreeText, IReadOnlyList<string> Tags, string? IdPrefix)
{
    public bool IsEmpty => FreeText.Length == 0 && Tags.Count == 0 && IdPrefix is null;

    public static SearchQuery Parse(string? input)
    {
        var words = new List<string>();
        var tags = new List<string>();
        string? idPrefix = null;

        foreach (var token in (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('#'))
            {
                var tag = TagNormalizer.Normalize(token);
                if (tag.Length > 0) tags.Add(tag);
            }
            else if (token.StartsWith("id:", StringComparison.OrdinalIgnoreCase)
                     && token.Length > 3 && token[3..].All(char.IsAsciiDigit))
            {
                idPrefix = token[3..];
            }
            else
            {
                words.Add(token);
            }
        }
        return new SearchQuery(string.Join(' ', words), tags, idPrefix);
    }
}
```

`src/Zettelkasten.Core/Search/RankFusion.cs`
```csharp
namespace Zettelkasten.Core.Search;

/// <summary>Reciprocal Rank Fusion: score(d) = Σ 1 / (k + rank). Needs no score calibration.</summary>
public static class RankFusion
{
    public static IReadOnlyList<string> Fuse(IReadOnlyList<IReadOnlyList<string>> rankings, int k = SemanticSettings.RrfK)
    {
        var scores = new Dictionary<string, double>();
        var firstSeen = new List<string>();
        foreach (var ranking in rankings)
        {
            for (var i = 0; i < ranking.Count; i++)
            {
                var id = ranking[i];
                if (!scores.ContainsKey(id)) { scores[id] = 0; firstSeen.Add(id); }
                scores[id] += 1.0 / (k + i + 1);
            }
        }
        // OrderByDescending is stable: ties keep first-seen order
        return firstSeen.OrderByDescending(id => scores[id]).ToList();
    }
}
```

`src/Zettelkasten.Core/Search/VectorMath.cs`
```csharp
using System.Runtime.InteropServices;

namespace Zettelkasten.Core.Search;

public static class VectorMath
{
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vector lengths differ.");
        var sum = 0f;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    public static void Normalize(Span<float> v)
    {
        var norm = MathF.Sqrt(Dot(v, v));
        if (norm == 0) return;
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
    }

    public static byte[] ToBytes(float[] v) => MemoryMarshal.AsBytes(v.AsSpan()).ToArray();

    public static float[] FromBytes(byte[] b) => MemoryMarshal.Cast<byte, float>(b).ToArray();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchQuery|FullyQualifiedName~RankFusion|FullyQualifiedName~VectorMath"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): search query parsing, rank fusion and vector math" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Core – debouncer and navigation history

**Files:**
- Create: `src/Zettelkasten.Core/Common/Debouncer.cs`, `src/Zettelkasten.Core/Common/NavigationHistory.cs`
- Test: `tests/Zettelkasten.Tests/Core/DebouncerTests.cs`, `tests/Zettelkasten.Tests/Core/NavigationHistoryTests.cs`

**Interfaces:**
- Produces:
  - `sealed class Debouncer(TimeSpan delay, TimeProvider timeProvider) : IDisposable` with `Task Trigger(Func<Task> action)` (task completes after the action ran, or immediately-ish when superseded/cancelled) and `void Cancel()`
  - `sealed class NavigationHistory` with `string? Current`, `bool CanGoBack`, `bool CanGoForward`, `void Visit(string id)`, `string? GoBack()`, `string? GoForward()`, `string? Remove(string id)` (returns new `Current`)

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Core/DebouncerTests.cs`
```csharp
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Common;

namespace Zettelkasten.Tests.Core;

public class DebouncerTests
{
    [Fact]
    public async Task Runs_action_only_after_delay()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;

        var t = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, runs);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await t;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Retrigger_restarts_delay_and_runs_once()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;

        var first = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(600));
        var second = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(600));
        await first;
        Assert.Equal(0, runs);

        time.Advance(TimeSpan.FromMilliseconds(400));
        await second;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Cancel_prevents_run()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;
        var t = d.Trigger(() => { runs++; return Task.CompletedTask; });
        d.Cancel();
        time.Advance(TimeSpan.FromSeconds(2));
        await t;
        Assert.Equal(0, runs);
    }
}
```

`tests/Zettelkasten.Tests/Core/NavigationHistoryTests.cs`
```csharp
using Zettelkasten.Core.Common;

namespace Zettelkasten.Tests.Core;

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.Visit("c");
        Assert.Equal("b", h.GoBack());
        Assert.Equal("a", h.GoBack());
        Assert.False(h.CanGoBack);
        Assert.Equal("b", h.GoForward());
        Assert.True(h.CanGoForward);
    }

    [Fact]
    public void Visit_clears_forward_and_ignores_same_id()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.GoBack();
        h.Visit("a");
        Assert.True(h.CanGoForward);  // re-visiting current is a no-op
        h.Visit("c");
        Assert.False(h.CanGoForward);
        Assert.Equal("a", h.GoBack());
    }

    [Fact]
    public void Remove_drops_id_everywhere_and_falls_back()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.Visit("a"); h.Visit("c");
        // back = [a, b, a], current = c
        Assert.Equal("a", h.Remove("c"));   // current removed -> previous entry
        Assert.Equal("b", h.Remove("a"));   // every "a" removed -> falls back to b
        Assert.False(h.CanGoBack);
        Assert.Equal("b", h.Current);
        Assert.Null(new NavigationHistory().GoBack());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Debouncer|FullyQualifiedName~NavigationHistory"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Core/Common/Debouncer.cs`
```csharp
namespace Zettelkasten.Core.Common;

/// <summary>Runs the most recently triggered action once no new trigger arrived for <c>delay</c>.</summary>
public sealed class Debouncer(TimeSpan delay, TimeProvider timeProvider) : IDisposable
{
    private CancellationTokenSource? _cts;

    public async Task Trigger(Func<Task> action)
    {
        Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, timeProvider, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await action();
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Cancel();
}
```

`src/Zettelkasten.Core/Common/NavigationHistory.cs`
```csharp
namespace Zettelkasten.Core.Common;

public sealed class NavigationHistory
{
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];

    public string? Current { get; private set; }
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void Visit(string id)
    {
        if (Current == id) return;
        if (Current is not null) _back.Add(Current);
        Current = id;
        _forward.Clear();
    }

    public string? GoBack() => Move(_back, _forward);

    public string? GoForward() => Move(_forward, _back);

    public string? Remove(string id)
    {
        _back.RemoveAll(x => x == id);
        _forward.RemoveAll(x => x == id);
        if (Current == id)
        {
            Current = null;
            if (_back.Count > 0) { Current = _back[^1]; _back.RemoveAt(_back.Count - 1); }
        }
        RemoveAdjacentDuplicates(_back);
        return Current;
    }

    private string? Move(List<string> from, List<string> to)
    {
        if (from.Count == 0) return null;
        if (Current is not null) to.Add(Current);
        Current = from[^1];
        from.RemoveAt(from.Count - 1);
        return Current;
    }

    private void RemoveAdjacentDuplicates(List<string> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
            if (list[i] == list[i - 1]) list.RemoveAt(i);
        if (list.Count > 0 && list[^1] == Current) list.RemoveAt(list.Count - 1);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Debouncer|FullyQualifiedName~NavigationHistory"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): debouncer and navigation history" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Core – domain models and service interfaces

**Files:**
- Create: `src/Zettelkasten.Core/Model/Models.cs`, `src/Zettelkasten.Core/Abstractions/Abstractions.cs`
- Test: `tests/Zettelkasten.Tests/Core/NoteTests.cs`

**Interfaces:**
- Produces (all later tasks use these exact names):

```csharp
namespace Zettelkasten.Core.Model;
public sealed class Note { string Id; string Title; string Content; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt;
                           List<string> Tags; List<string> ManualLinks; Note Clone(); }
public sealed record NoteSummary(string Id, string Title, DateTimeOffset UpdatedAt) { string DisplayTitle; }
public enum LinkKind { Inline = 0, Manual = 1 }
public sealed record LinkInfo(string NoteId, string? Title, LinkKind Kind) { bool IsBroken; string DisplayTitle; }
public sealed record Tag(int Id, string Name);
public sealed record TagWithEmbedding(Tag Tag, float[]? Embedding);
public sealed record SearchResult(NoteSummary Note, bool MatchedKeyword, bool MatchedSemantic);
public sealed record TagSuggestion(Tag Tag, float Score, bool IsInitialsMatch);
public sealed record TagSuggestions(Tag? ExactMatch, IReadOnlyList<TagSuggestion> Similar);

namespace Zettelkasten.Core.Abstractions;
INoteRepository, ITagRepository, ISearchService, IEmbeddingService, ITagSuggestionService, IBackupService (see code)
```

- [ ] **Step 1: Write failing test**

`tests/Zettelkasten.Tests/Core/NoteTests.cs`
```csharp
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Core;

public class NoteTests
{
    [Fact]
    public void Clone_copies_lists_independently()
    {
        var n = new Note { Id = "20261102143015", Title = "t", Tags = ["a"], ManualLinks = ["20261102143016"] };
        var c = n.Clone();
        c.Tags.Add("b");
        c.ManualLinks.Clear();
        Assert.Equal(new[] { "a" }, n.Tags);
        Assert.Single(n.ManualLinks);
        Assert.Equal("t", c.Title);
    }

    [Fact]
    public void Display_titles_fall_back()
    {
        Assert.Equal("(untitled)", new NoteSummary("1", " ", DateTimeOffset.Now).DisplayTitle);
        Assert.True(new LinkInfo("20261102143015", null, LinkKind.Inline).IsBroken);
        Assert.Equal("20261102143015", new LinkInfo("20261102143015", null, LinkKind.Inline).DisplayTitle);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NoteTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Core/Model/Models.cs`
```csharp
namespace Zettelkasten.Core.Model;

public sealed class Note
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Normalized tag names.</summary>
    public List<string> Tags { get; set; } = [];
    /// <summary>Target note IDs of manual ("related") links.</summary>
    public List<string> ManualLinks { get; set; } = [];

    public Note Clone() => new()
    {
        Id = Id, Title = Title, Content = Content, CreatedAt = CreatedAt, UpdatedAt = UpdatedAt,
        Tags = [.. Tags], ManualLinks = [.. ManualLinks],
    };
}

public sealed record NoteSummary(string Id, string Title, DateTimeOffset UpdatedAt)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title;
}

public enum LinkKind { Inline = 0, Manual = 1 }

/// <summary>A link as shown in the side panel. <see cref="Title"/> is null when the target note doesn't exist.</summary>
public sealed record LinkInfo(string NoteId, string? Title, LinkKind Kind)
{
    public bool IsBroken => Title is null;
    public string DisplayTitle => Title is null ? NoteId : string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title;
}

public sealed record Tag(int Id, string Name);

public sealed record TagWithEmbedding(Tag Tag, float[]? Embedding);

public sealed record SearchResult(NoteSummary Note, bool MatchedKeyword, bool MatchedSemantic);

public sealed record TagSuggestion(Tag Tag, float Score, bool IsInitialsMatch);

public sealed record TagSuggestions(Tag? ExactMatch, IReadOnlyList<TagSuggestion> Similar);
```

`src/Zettelkasten.Core/Abstractions/Abstractions.cs`
```csharp
using Zettelkasten.Core.Model;

namespace Zettelkasten.Core.Abstractions;

public interface INoteRepository
{
    /// <summary>Creates and persists an empty note with a fresh unique ID.</summary>
    Task<Note> CreateAsync(CancellationToken ct = default);
    Task<Note?> GetAsync(string id, CancellationToken ct = default);
    /// <summary>Saves everything about the note in one transaction and sets <see cref="Note.UpdatedAt"/>.</summary>
    Task SaveAsync(Note note, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default);
    /// <summary>Summaries for existing IDs, in the order of <paramref name="ids"/>; missing IDs are skipped.</summary>
    Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default);
    /// <summary>Removes the tag from all notes and deletes it.</summary>
    Task DeleteAsync(int tagId, CancellationToken ct = default);
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public interface IEmbeddingService
{
    bool IsAvailable { get; }
    event EventHandler? AvailabilityChanged;
    /// <summary>Loads the model. Never throws; on failure <see cref="IsAvailable"/> stays false.</summary>
    Task InitializeAsync(CancellationToken ct = default);
    /// <summary>Returns an L2-normalized vector. Only call when <see cref="IsAvailable"/> is true.</summary>
    float[] Embed(string text);
}

public interface ITagSuggestionService
{
    Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default);
}

public interface IBackupService
{
    Task ExportAsync(string destinationPath, CancellationToken ct = default);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~NoteTests"` → Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): domain models and service interfaces" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 7: Infrastructure – database schema, migrations and initializer

**Files:**
- Create: `src/Zettelkasten.Infrastructure/AppPaths.cs`, `Data/Entities.cs`, `Data/AppDbContext.cs`, `Data/DesignTimeFactory.cs`, `Data/DbInitializer.cs`, `Data/SqliteBackup.cs`, `Data/Migrations/*` (generated), `.config/dotnet-tools.json`
- Test: `tests/Zettelkasten.Tests/Support/TestDb.cs`, `tests/Zettelkasten.Tests/Infrastructure/DbInitializerTests.cs`

**Interfaces:**
- Consumes: `LinkKind` (Task 6)
- Produces:
  - `sealed class AppPaths(string dataDirectory, string modelDirectory)` with `DataDirectory`, `DatabasePath`, `LogDirectory`, `ModelDirectory`, `ModelPath` (`model.onnx`), `VocabPath` (`vocab.txt`), `ConnectionString`, `static AppPaths Default()`
  - Entities `NoteEntity`, `TagEntity`, `NoteTagEntity`, `NoteLinkEntity`, `NoteEmbeddingEntity`; `AppDbContext` with `DbSet`s `Notes`, `Tags`, `NoteTags`, `NoteLinks`, `NoteEmbeddings`; `static DbContextOptions<AppDbContext> AppDbContext.CreateOptions(string connectionString)`
  - `sealed class DbInitializer(IDbContextFactory<AppDbContext>, AppPaths, TimeProvider, ILogger<DbInitializer>)` with `Task InitializeAsync(CancellationToken ct = default)`
  - `static class SqliteBackup` with `void Copy(string sourceConnectionString, string destinationPath)`
  - Test support: `TestDb` (`static Task<TestDb> CreateAsync(TimeProvider? time = null)`, `AppPaths Paths`, `IDbContextFactory<AppDbContext> Factory`, `Task<object?> ScalarAsync(string sql)`, `Task ExecAsync(string sql)`, `IAsyncDisposable`)

- [ ] **Step 1: Add packages and the EF tool**

```powershell
dotnet add src/Zettelkasten.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.0
dotnet add src/Zettelkasten.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 10.0.0
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.0
```

- [ ] **Step 2: Write `AppPaths`, entities and the DbContext**

`src/Zettelkasten.Infrastructure/AppPaths.cs`
```csharp
using Microsoft.Data.Sqlite;

namespace Zettelkasten.Infrastructure;

public sealed class AppPaths(string dataDirectory, string modelDirectory)
{
    public string DataDirectory { get; } = dataDirectory;
    public string ModelDirectory { get; } = modelDirectory;
    public string DatabasePath => Path.Combine(DataDirectory, "zettelkasten.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string ModelPath => Path.Combine(ModelDirectory, "model.onnx");
    public string VocabPath => Path.Combine(ModelDirectory, "vocab.txt");

    public string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();

    public static AppPaths Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zettelkasten"),
        Path.Combine(AppContext.BaseDirectory, "models"));
}
```

`src/Zettelkasten.Infrastructure/Data/Entities.cs`
```csharp
using Zettelkasten.Core.Model;

namespace Zettelkasten.Infrastructure.Data;

public sealed class NoteEntity
{
    public required string Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public long CreatedAtMs { get; set; }
    public long UpdatedAtMs { get; set; }
    public List<NoteTagEntity> NoteTags { get; set; } = [];
    public NoteEmbeddingEntity? Embedding { get; set; }
}

public sealed class TagEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public byte[]? Embedding { get; set; }
    public List<NoteTagEntity> NoteTags { get; set; } = [];
}

public sealed class NoteTagEntity
{
    public required string NoteId { get; set; }
    public int TagId { get; set; }
    public NoteEntity? Note { get; set; }
    public TagEntity? Tag { get; set; }
}

public sealed class NoteLinkEntity
{
    public required string SourceId { get; set; }
    /// <summary>Not a foreign key: links may point to notes that don't exist (broken links).</summary>
    public required string TargetId { get; set; }
    public LinkKind Kind { get; set; }
}

public sealed class NoteEmbeddingEntity
{
    public required string NoteId { get; set; }
    public required byte[] Vector { get; set; }
    public required string ContentHash { get; set; }
}
```

`src/Zettelkasten.Infrastructure/Data/AppDbContext.cs`
```csharp
using Microsoft.EntityFrameworkCore;

namespace Zettelkasten.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<NoteTagEntity> NoteTags => Set<NoteTagEntity>();
    public DbSet<NoteLinkEntity> NoteLinks => Set<NoteLinkEntity>();
    public DbSet<NoteEmbeddingEntity> NoteEmbeddings => Set<NoteEmbeddingEntity>();

    public static DbContextOptions<AppDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<NoteEntity>(e =>
        {
            e.ToTable("Notes");
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.UpdatedAtMs);
            e.HasOne(n => n.Embedding).WithOne()
                .HasForeignKey<NoteEmbeddingEntity>(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TagEntity>(e =>
        {
            e.ToTable("Tags");
            e.Property(t => t.Name).UseCollation("NOCASE");
            e.HasIndex(t => t.Name).IsUnique();
        });

        b.Entity<NoteTagEntity>(e =>
        {
            e.ToTable("NoteTags");
            e.HasKey(x => new { x.NoteId, x.TagId });
            e.HasOne(x => x.Note).WithMany(n => n.NoteTags).HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.NoteTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NoteLinkEntity>(e =>
        {
            e.ToTable("NoteLinks");
            e.HasKey(x => new { x.SourceId, x.TargetId, x.Kind });
            e.HasIndex(x => x.TargetId);
            e.HasOne<NoteEntity>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NoteEmbeddingEntity>(e =>
        {
            e.ToTable("NoteEmbeddings");
            e.HasKey(x => x.NoteId);
        });
    }
}
```

`src/Zettelkasten.Infrastructure/Data/DesignTimeFactory.cs`
```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Zettelkasten.Infrastructure.Data;

/// <summary>Used only by `dotnet ef` to create migrations.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(AppDbContext.CreateOptions("Data Source=design.db"));
}
```

- [ ] **Step 3: Generate the migration and add the FTS5 table**

```powershell
dotnet ef migrations add InitialCreate -p src/Zettelkasten.Infrastructure -s src/Zettelkasten.Infrastructure -o Data/Migrations
```

Open the generated `src/Zettelkasten.Infrastructure/Data/Migrations/<timestamp>_InitialCreate.cs`. At the **end** of `Up(...)` add:

```csharp
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE NotesFts USING fts5(" +
                "NoteId UNINDEXED, Title, Content, Tags, " +
                "tokenize = 'unicode61 remove_diacritics 2');");
```

At the **start** of `Down(...)` add:

```csharp
            migrationBuilder.Sql("DROP TABLE IF EXISTS NotesFts;");
```

Delete `design.db` if the tool created one in the repo root, and add `design.db` to `.gitignore`.

- [ ] **Step 4: Write `SqliteBackup` and `DbInitializer`**

`src/Zettelkasten.Infrastructure/Data/SqliteBackup.cs`
```csharp
using Microsoft.Data.Sqlite;

namespace Zettelkasten.Infrastructure.Data;

public static class SqliteBackup
{
    /// <summary>Consistent copy via SQLite's online backup API (safe while the DB is in use).</summary>
    public static void Copy(string sourceConnectionString, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var source = new SqliteConnection(sourceConnectionString);
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }
}
```

`src/Zettelkasten.Infrastructure/Data/DbInitializer.cs`
```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Zettelkasten.Infrastructure.Data;

public sealed class DbInitializer(
    IDbContextFactory<AppDbContext> factory, AppPaths paths, TimeProvider time, ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var existed = File.Exists(paths.DatabasePath);

        await using var db = await factory.CreateDbContextAsync(ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (existed && pending.Count > 0)
        {
            var stamp = time.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var backupPath = $"{paths.DatabasePath}.bak-{stamp}";
            SqliteBackup.Copy(paths.ConnectionString, backupPath);
            logger.LogInformation("Backed up database to {BackupPath} before applying {Count} migration(s)",
                backupPath, pending.Count);
        }

        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
}
```

- [ ] **Step 5: Write test support and failing tests**

`tests/Zettelkasten.Tests/Support/TestDb.cs`
```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Tests.Support;

public sealed class TestDb : IAsyncDisposable
{
    private TestDb(AppPaths paths)
    {
        Paths = paths;
        Factory = new Factory_(paths.ConnectionString);
    }

    public AppPaths Paths { get; }
    public IDbContextFactory<AppDbContext> Factory { get; }

    public static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "zk-tests", Guid.NewGuid().ToString("N"));

    public static async Task<TestDb> CreateAsync(TimeProvider? time = null, string? directory = null)
    {
        var db = new TestDb(new AppPaths(directory ?? NewDirectory(), Path.Combine(AppContext.BaseDirectory, "models")));
        await new DbInitializer(db.Factory, db.Paths, time ?? TimeProvider.System, NullLogger<DbInitializer>.Instance)
            .InitializeAsync();
        return db;
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var c = new SqliteConnection(Paths.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    public async Task ExecAsync(string sql)
    {
        await using var c = new SqliteConnection(Paths.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Paths.DataDirectory, recursive: true); } catch (IOException) { }
        return ValueTask.CompletedTask;
    }

    private sealed class Factory_(string cs) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(AppDbContext.CreateOptions(cs));
    }
}
```

`tests/Zettelkasten.Tests/Infrastructure/DbInitializerTests.cs`
```csharp
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class DbInitializerTests
{
    [Fact]
    public async Task Creates_schema_including_fts_table()
    {
        await using var db = await TestDb.CreateAsync();
        var count = await db.ScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Notes','Tags','NoteTags','NoteLinks','NoteEmbeddings','NotesFts')");
        Assert.Equal(6L, count);
    }

    [Fact]
    public async Task Enables_wal_mode()
    {
        await using var db = await TestDb.CreateAsync();
        Assert.Equal("wal", await db.ScalarAsync("PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task Backs_up_existing_database_before_migrating_once()
    {
        var dir = TestDb.NewDirectory();
        Directory.CreateDirectory(dir);
        await using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "zettelkasten.db")};Pooling=False"))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE Legacy(x INTEGER);";
            await cmd.ExecuteNonQueryAsync();
        }
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

        await using var db = await TestDb.CreateAsync(time, dir);
        Assert.True(File.Exists(Path.Combine(dir, "zettelkasten.db.bak-20261102090000")));

        time.Advance(TimeSpan.FromMinutes(1));
        await TestDb.CreateAsync(time, dir); // second start: nothing pending -> no new backup
        Assert.Single(Directory.GetFiles(dir, "*.bak-*"));
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DbInitializerTests"`
Expected: all 3 PASS. If `Creates_schema_including_fts_table` fails, the FTS `migrationBuilder.Sql` from Step 3 is missing.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(infra): SQLite schema with FTS5, migrations and initializer with pre-migration backup" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Infrastructure – note repository (transactions, links, FTS, embeddings)

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Data/FtsIndex.cs`, `Embeddings/ContentHash.cs`, `Embeddings/VectorIndex.cs`, `Notes/NoteRepository.cs`
- Test: `tests/Zettelkasten.Tests/Support/FakeEmbeddingService.cs`, `tests/Zettelkasten.Tests/Infrastructure/NoteRepositoryTests.cs`, `tests/Zettelkasten.Tests/Infrastructure/FtsIndexTests.cs`

**Interfaces:**
- Consumes: `INoteRepository`, `IEmbeddingService`, models (Task 6); `NoteIdGenerator`, `LinkParser` (Task 2); `TagNormalizer` (Task 3); `VectorMath` (Task 4); `AppDbContext`, `TestDb` (Task 7)
- Produces:
  - `internal static class FtsIndex`: `Task UpsertAsync(AppDbContext db, string noteId, string title, string content, IEnumerable<string> tags, CancellationToken ct)`, `Task DeleteAsync(AppDbContext db, string noteId, CancellationToken ct)`, `string? BuildMatchExpression(string freeText)`, `Task<List<string>> MatchAsync(AppDbContext db, string matchExpression, int limit, CancellationToken ct)`
  - `static class ContentHash`: `string NoteText(string title, string content)`, `string TagText(string tagName)`, `string Compute(string title, string content)`
  - `sealed record VectorHit(string Id, float Score)`; `sealed class VectorIndex`: `int Count`, `void Set(string id, float[] v)`, `void Remove(string id)`, `void Clear()`, `IReadOnlyList<VectorHit> Search(float[] query, float minScore, int limit)`
  - `sealed class NoteRepository(IDbContextFactory<AppDbContext>, IEmbeddingService, VectorIndex, TimeProvider) : INoteRepository`
  - Test support `FakeEmbeddingService : IEmbeddingService` with settable `IsAvailable`, `int EmbedCount`, `Dictionary<string, string> Synonyms`

- [ ] **Step 1: Write the fake embedding service (test support)**

`tests/Zettelkasten.Tests/Support/FakeEmbeddingService.cs`
```csharp
using System.Text;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Support;

/// <summary>Deterministic bag-of-words embedding: texts sharing words are similar.</summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    private bool _isAvailable = true;

    public bool IsAvailable
    {
        get => _isAvailable;
        set { _isAvailable = value; AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
    }

    public int EmbedCount { get; private set; }
    /// <summary>Words mapped to another word before hashing, to simulate meaning without shared words.</summary>
    public Dictionary<string, string> Synonyms { get; } = [];
    public event EventHandler? AvailabilityChanged;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public float[] Embed(string text)
    {
        if (!IsAvailable) throw new InvalidOperationException("Model not available.");
        EmbedCount++;
        var v = new float[SemanticSettings.Dimensions];
        var word = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(ch)) { word.Append(ch); continue; }
            if (word.Length > 0)
            {
                var w = word.ToString();
                v[Fnv(Synonyms.GetValueOrDefault(w, w)) % (uint)v.Length] += 1;
                word.Clear();
            }
        }
        VectorMath.Normalize(v);
        return v;
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return h;
    }
}
```

- [ ] **Step 2: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/FtsIndexTests.cs`
```csharp
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Tests.Infrastructure;

public class FtsIndexTests
{
    [Theory]
    [InlineData("memory", "\"memory\"*")]
    [InlineData("how  memory", "\"how\"* \"memory\"*")]
    [InlineData("e-mail \"quoted", "\"e\"* \"mail\"* \"quoted\"*")]
    [InlineData("NOT AND NEAR", "\"NOT\"* \"AND\"* \"NEAR\"*")]
    [InlineData("Café", "\"Café\"*")]
    public void BuildMatchExpression_quotes_every_token(string input, string expected)
    {
        Assert.Equal(expected, FtsIndex.BuildMatchExpression(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*:-()\"")]
    public void BuildMatchExpression_returns_null_without_tokens(string input)
    {
        Assert.Null(FtsIndex.BuildMatchExpression(input));
    }
}
```

`tests/Zettelkasten.Tests/Infrastructure/NoteRepositoryTests.cs`
```csharp
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class NoteRepositoryTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 14, 30, 15, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _repo = new NoteRepository(_db.Factory, _embeddings, _index, _time);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Note> CreateNextAsync()
    {
        _time.Advance(TimeSpan.FromSeconds(10));
        return await _repo.CreateAsync();
    }

    [Fact]
    public async Task Create_returns_persisted_note_with_id_from_clock()
    {
        var note = await _repo.CreateAsync();
        Assert.Equal("20261102143015", note.Id);
        Assert.NotNull(await _repo.GetAsync(note.Id));
        Assert.Equal(1, await _repo.CountAsync());
    }

    [Fact]
    public async Task Create_twice_in_same_second_yields_distinct_ids()
    {
        var a = await _repo.CreateAsync();
        var b = await _repo.CreateAsync();
        var c = await _repo.CreateAsync();
        Assert.Equal(new[] { "20261102143015", "20261102143016", "20261102143017" }, new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public async Task Save_and_get_round_trip_normalizes_tags()
    {
        var target = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Title = "Spaced repetition";
        note.Content = "Review at growing intervals.";
        note.Tags = ["Learning", "learning ", "Deep Work", "  "];
        note.ManualLinks = [target.Id];
        _time.Advance(TimeSpan.FromMinutes(1));

        await _repo.SaveAsync(note);
        var loaded = (await _repo.GetAsync(note.Id))!;

        Assert.Equal("Spaced repetition", loaded.Title);
        Assert.Equal("Review at growing intervals.", loaded.Content);
        Assert.Equal(new[] { "deep-work", "learning" }, loaded.Tags.Order());
        Assert.Equal(new[] { target.Id }, loaded.ManualLinks);
        Assert.Equal(_time.GetLocalNow().ToUnixTimeMilliseconds(), loaded.UpdatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Save_rebuilds_inline_links_and_keeps_manual_links()
    {
        var a = await CreateNextAsync();
        var b = await CreateNextAsync();
        var c = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Content = $"see [[{a.Id}]]";
        note.ManualLinks = [c.Id];
        await _repo.SaveAsync(note);

        note.Content = $"now [[{b.Id}]]";
        await _repo.SaveAsync(note);

        var links = await _repo.GetOutgoingLinksAsync(note.Id);
        Assert.Contains(links, l => l.NoteId == b.Id && l.Kind == LinkKind.Inline);
        Assert.Contains(links, l => l.NoteId == c.Id && l.Kind == LinkKind.Manual);
        Assert.DoesNotContain(links, l => l.NoteId == a.Id);
    }

    [Fact]
    public async Task Save_with_duplicate_and_self_links_succeeds()
    {
        var a = await CreateNextAsync();
        var note = await CreateNextAsync();
        note.Content = $"[[{a.Id}]] [[{a.Id}]] [[{note.Id}]]";
        note.ManualLinks = [a.Id, a.Id, note.Id, "not-an-id"];

        await _repo.SaveAsync(note);

        var links = await _repo.GetOutgoingLinksAsync(note.Id);
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.Equal(a.Id, l.NoteId));
        Assert.Equal(new[] { a.Id }, (await _repo.GetAsync(note.Id))!.ManualLinks);
    }

    [Fact]
    public async Task Backlinks_include_inline_and_manual()
    {
        var target = await CreateNextAsync();
        var inline = await CreateNextAsync();
        var manual = await CreateNextAsync();
        inline.Title = "Inline";
        inline.Content = $"[[{target.Id}]]";
        manual.Title = "Manual";
        manual.ManualLinks = [target.Id];
        await _repo.SaveAsync(inline);
        await _repo.SaveAsync(manual);

        var back = await _repo.GetBacklinksAsync(target.Id);
        Assert.Contains(back, l => l.NoteId == inline.Id && l.Kind == LinkKind.Inline && l.Title == "Inline");
        Assert.Contains(back, l => l.NoteId == manual.Id && l.Kind == LinkKind.Manual);
    }

    [Fact]
    public async Task Broken_link_resolves_when_target_is_created()
    {
        var note = await CreateNextAsync();
        var futureId = _time.GetLocalNow().AddSeconds(10).ToString("yyyyMMddHHmmss");
        note.Content = $"[[{futureId}]]";
        await _repo.SaveAsync(note);
        Assert.True(Assert.Single(await _repo.GetOutgoingLinksAsync(note.Id)).IsBroken);

        var created = await CreateNextAsync();
        Assert.Equal(futureId, created.Id);
        Assert.False(Assert.Single(await _repo.GetOutgoingLinksAsync(note.Id)).IsBroken);
    }

    [Fact]
    public async Task Save_updates_fts_and_delete_removes_it()
    {
        var note = await CreateNextAsync();
        note.Title = "Zettel";
        note.Content = "forgetting curve";
        note.Tags = ["memory"];
        await _repo.SaveAsync(note);

        await using (var ctx = _db.Factory.CreateDbContext())
        {
            Assert.Equal(new[] { note.Id }, await FtsIndex.MatchAsync(ctx, "\"forget\"*", 10, default));
            Assert.Equal(new[] { note.Id }, await FtsIndex.MatchAsync(ctx, "\"memory\"*", 10, default));
        }

        await _repo.DeleteAsync(note.Id);
        await using (var ctx = _db.Factory.CreateDbContext())
            Assert.Empty(await FtsIndex.MatchAsync(ctx, "\"forget\"*", 10, default));
    }

    [Fact]
    public async Task Save_reembeds_only_when_text_changes()
    {
        var note = await CreateNextAsync();
        note.Title = "t";
        await _repo.SaveAsync(note);
        var count = _embeddings.EmbedCount;
        Assert.True(_index.Count >= 1);

        note.Tags = ["new-tag"];          // tag embedding is computed, note text unchanged
        await _repo.SaveAsync(note);
        Assert.Equal(count + 1, _embeddings.EmbedCount);

        note.Content = "changed";
        await _repo.SaveAsync(note);
        Assert.Equal(count + 2, _embeddings.EmbedCount);
    }

    [Fact]
    public async Task Save_without_model_still_saves()
    {
        _embeddings.IsAvailable = false;
        var note = await CreateNextAsync();
        note.Title = "offline";
        note.Tags = ["t"];
        await _repo.SaveAsync(note);
        Assert.Equal("offline", (await _repo.GetAsync(note.Id))!.Title);
        Assert.Equal(0, _index.Count);
    }

    [Fact]
    public async Task Delete_keeps_incoming_links_as_broken()
    {
        var target = await CreateNextAsync();
        var source = await CreateNextAsync();
        source.Content = $"[[{target.Id}]]";
        target.Content = $"[[{source.Id}]]";
        await _repo.SaveAsync(source);
        await _repo.SaveAsync(target);

        await _repo.DeleteAsync(target.Id);

        Assert.Null(await _repo.GetAsync(target.Id));
        Assert.True(Assert.Single(await _repo.GetOutgoingLinksAsync(source.Id)).IsBroken);
        Assert.Equal(0L, await _db.ScalarAsync($"SELECT COUNT(*) FROM NoteLinks WHERE SourceId = '{target.Id}'"));
    }

    [Fact]
    public async Task Failed_save_rolls_back_everything()
    {
        var note = await CreateNextAsync();
        note.Title = "before";
        await _repo.SaveAsync(note);
        await _db.ExecAsync("DROP TABLE NotesFts;"); // force a failure late in the transaction

        note.Title = "after";
        note.Tags = ["x"];
        await Assert.ThrowsAnyAsync<Exception>(() => _repo.SaveAsync(note));

        var loaded = (await _repo.GetAsync(note.Id))!;
        Assert.Equal("before", loaded.Title);
        Assert.Empty(loaded.Tags);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NoteRepositoryTests|FullyQualifiedName~FtsIndexTests"`
Expected: build FAILS (types missing).

- [ ] **Step 4: Implement `FtsIndex`, `ContentHash`, `VectorIndex`**

`src/Zettelkasten.Infrastructure/Data/FtsIndex.cs`
```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Zettelkasten.Infrastructure.Data;

/// <summary>Raw-SQL access to the NotesFts FTS5 table (EF Core has no FTS5 support).</summary>
internal static class FtsIndex
{
    public static async Task UpsertAsync(AppDbContext db, string noteId, string title, string content,
        IEnumerable<string> tags, CancellationToken ct)
    {
        var tagText = string.Join(' ', tags);
        await DeleteAsync(db, noteId, ct);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO NotesFts (NoteId, Title, Content, Tags) VALUES ({noteId}, {title}, {content}, {tagText})", ct);
    }

    public static async Task DeleteAsync(AppDbContext db, string noteId, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"DELETE FROM NotesFts WHERE NoteId = {noteId}", ct);

    /// <summary>
    /// Turns free text into a safe FTS5 expression: every run of letters/digits becomes a quoted
    /// prefix term ("word"*), so user input can never contain FTS syntax. Null when nothing is left.
    /// </summary>
    public static string? BuildMatchExpression(string freeText)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in freeText.Normalize(NormalizationForm.FormC) + " ")
        {
            if (char.IsLetterOrDigit(ch)) { current.Append(ch); continue; }
            if (current.Length > 0) { terms.Add($"\"{current}\"*"); current.Clear(); }
        }
        return terms.Count == 0 ? null : string.Join(' ', terms);
    }

    public static Task<List<string>> MatchAsync(AppDbContext db, string matchExpression, int limit, CancellationToken ct) =>
        db.Database.SqlQuery<string>(
                $"SELECT NoteId AS Value FROM NotesFts WHERE NotesFts MATCH {matchExpression} ORDER BY bm25(NotesFts) LIMIT {limit}")
            .ToListAsync(ct);
}
```

`src/Zettelkasten.Infrastructure/Embeddings/ContentHash.cs`
```csharp
using System.Security.Cryptography;
using System.Text;

namespace Zettelkasten.Infrastructure.Embeddings;

public static class ContentHash
{
    /// <summary>The text that gets embedded for a note.</summary>
    public static string NoteText(string title, string content) => $"{title}\n{content}";

    /// <summary>The text that gets embedded for a tag: "machine-learning" -> "machine learning".</summary>
    public static string TagText(string tagName) => tagName.Replace('-', ' ');

    public static string Compute(string title, string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NoteText(title, content))));
}
```

`src/Zettelkasten.Infrastructure/Embeddings/VectorIndex.cs`
```csharp
using System.Collections.Concurrent;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

public sealed record VectorHit(string Id, float Score);

/// <summary>In-memory note vectors; brute-force dot product is fast enough for tens of thousands of notes.</summary>
public sealed class VectorIndex
{
    private readonly ConcurrentDictionary<string, float[]> _vectors = new();

    public int Count => _vectors.Count;
    public void Set(string id, float[] vector) => _vectors[id] = vector;
    public void Remove(string id) => _vectors.TryRemove(id, out _);
    public void Clear() => _vectors.Clear();

    public IReadOnlyList<VectorHit> Search(float[] query, float minScore, int limit) =>
        _vectors
            .Select(kv => new VectorHit(kv.Key, VectorMath.Dot(query, kv.Value)))
            .Where(h => h.Score >= minScore)
            .OrderByDescending(h => h.Score)
            .Take(limit)
            .ToList();
}
```

- [ ] **Step 5: Implement `NoteRepository`**

`src/Zettelkasten.Infrastructure/Notes/NoteRepository.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Ids;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Core.Tags;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Notes;

public sealed class NoteRepository(
    IDbContextFactory<AppDbContext> factory,
    IEmbeddingService embeddings,
    VectorIndex index,
    TimeProvider time) : INoteRepository
{
    public async Task<Note> CreateAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = time.GetLocalNow();
        var candidate = NoteIdGenerator.Generate(now, _ => false);
        var taken = (await db.Notes
                .Where(n => string.Compare(n.Id, candidate) >= 0)
                .OrderBy(n => n.Id).Select(n => n.Id).Take(10_000)
                .ToListAsync(ct))
            .ToHashSet();
        var id = NoteIdGenerator.Generate(now, taken.Contains);
        var ms = now.ToUnixTimeMilliseconds();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Notes.Add(new NoteEntity { Id = id, CreatedAtMs = ms, UpdatedAtMs = ms });
        await db.SaveChangesAsync(ct);
        await FtsIndex.UpsertAsync(db, id, "", "", [], ct);
        await tx.CommitAsync(ct);

        return new Note { Id = id, CreatedAt = now, UpdatedAt = now };
    }

    public async Task<Note?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.Notes.AsNoTracking()
            .Include(n => n.NoteTags).ThenInclude(nt => nt.Tag)
            .SingleOrDefaultAsync(n => n.Id == id, ct);
        if (e is null) return null;

        var manual = await db.NoteLinks.AsNoTracking()
            .Where(l => l.SourceId == id && l.Kind == LinkKind.Manual)
            .OrderBy(l => l.TargetId).Select(l => l.TargetId)
            .ToListAsync(ct);

        return new Note
        {
            Id = e.Id,
            Title = e.Title,
            Content = e.Content,
            CreatedAt = FromMs(e.CreatedAtMs),
            UpdatedAt = FromMs(e.UpdatedAtMs),
            Tags = e.NoteTags.Select(nt => nt.Tag!.Name).Order().ToList(),
            ManualLinks = manual,
        };
    }

    public async Task SaveAsync(Note note, CancellationToken ct = default)
    {
        var tags = note.Tags.Select(TagNormalizer.Normalize).Where(t => t.Length > 0).Distinct().ToList();
        var manual = note.ManualLinks.Where(id => NoteIdGenerator.IsValid(id) && id != note.Id).Distinct().ToList();
        var inline = LinkParser.ExtractTargets(note.Content).Where(id => id != note.Id).ToList();
        var hash = ContentHash.Compute(note.Title, note.Content);

        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Notes
                         .Include(n => n.NoteTags)
                         .Include(n => n.Embedding)
                         .SingleOrDefaultAsync(n => n.Id == note.Id, ct)
                     ?? throw new InvalidOperationException($"Note {note.Id} does not exist.");

        float[]? vector = null;
        if (embeddings.IsAvailable && entity.Embedding?.ContentHash != hash)
            vector = await Task.Run(() => embeddings.Embed(ContentHash.NoteText(note.Title, note.Content)), ct);

        var now = time.GetLocalNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        entity.Title = note.Title;
        entity.Content = note.Content;
        entity.UpdatedAtMs = now.ToUnixTimeMilliseconds();

        // Tags: reuse existing (case-insensitive), create missing with an embedding when possible
        var tagEntities = await db.Tags.Where(t => tags.Contains(t.Name)).ToListAsync(ct);
        foreach (var name in tags.Where(n => tagEntities.All(t => !string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var created = new TagEntity
            {
                Name = name,
                Embedding = embeddings.IsAvailable ? VectorMath.ToBytes(embeddings.Embed(ContentHash.TagText(name))) : null,
            };
            db.Tags.Add(created);
            tagEntities.Add(created);
        }
        var keepIds = tagEntities.Where(t => t.Id != 0).Select(t => t.Id).ToHashSet();
        entity.NoteTags.RemoveAll(nt => !keepIds.Contains(nt.TagId));
        foreach (var tag in tagEntities.Where(t => t.Id == 0 || entity.NoteTags.All(nt => nt.TagId != t.Id)))
            entity.NoteTags.Add(new NoteTagEntity { NoteId = entity.Id, Tag = tag });

        if (vector is not null)
        {
            entity.Embedding ??= new NoteEmbeddingEntity { NoteId = entity.Id, Vector = [], ContentHash = "" };
            entity.Embedding.Vector = VectorMath.ToBytes(vector);
            entity.Embedding.ContentHash = hash;
        }

        await db.SaveChangesAsync(ct);

        await db.NoteLinks.Where(l => l.SourceId == note.Id).ExecuteDeleteAsync(ct);
        db.NoteLinks.AddRange(inline.Select(t => new NoteLinkEntity { SourceId = note.Id, TargetId = t, Kind = LinkKind.Inline }));
        db.NoteLinks.AddRange(manual.Select(t => new NoteLinkEntity { SourceId = note.Id, TargetId = t, Kind = LinkKind.Manual }));
        await db.SaveChangesAsync(ct);

        await FtsIndex.UpsertAsync(db, note.Id, note.Title, note.Content, tags, ct);
        await tx.CommitAsync(ct);

        if (vector is not null) index.Set(note.Id, vector);
        note.UpdatedAt = now;
        note.Tags = tags.Order().ToList();
        note.ManualLinks = manual;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // ON DELETE CASCADE removes NoteTags, outgoing NoteLinks and the embedding
        await db.Notes.Where(n => n.Id == id).ExecuteDeleteAsync(ct);
        await FtsIndex.DeleteAsync(db, id, ct);
        await tx.CommitAsync(ct);
        index.Remove(id);
    }

    public async Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().OrderByDescending(n => n.UpdatedAtMs).Take(count)
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        return rows.Select(r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs))).ToList();
    }

    public async Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().OrderBy(n => n.Title)
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        return rows.Select(r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs))).ToList();
    }

    public async Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Notes.AsNoTracking().Where(n => ids.Contains(n.Id))
            .Select(n => new { n.Id, n.Title, n.UpdatedAtMs }).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id, r => new NoteSummary(r.Id, r.Title, FromMs(r.UpdatedAtMs)));
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (
                from l in db.NoteLinks.AsNoTracking()
                where l.SourceId == id
                join n in db.Notes on l.TargetId equals n.Id into targets
                from n in targets.DefaultIfEmpty()
                orderby l.Kind, l.TargetId
                select new { l.TargetId, Title = n != null ? n.Title : null, l.Kind })
            .ToListAsync(ct);
        return rows.Select(r => new LinkInfo(r.TargetId, r.Title, r.Kind)).ToList();
    }

    public async Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await (
                from l in db.NoteLinks.AsNoTracking()
                where l.TargetId == id
                join n in db.Notes on l.SourceId equals n.Id
                orderby n.Title, l.Kind
                select new { l.SourceId, n.Title, l.Kind })
            .ToListAsync(ct);
        return rows.Select(r => new LinkInfo(r.SourceId, r.Title, r.Kind)).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Notes.CountAsync(ct);
    }

    private static DateTimeOffset FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
}
```

Tag sync: `keepIds` holds already-persisted tags that stay on the note; links to any other tag are removed. New tags (`Id == 0`) and persisted tags not yet linked are then added.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NoteRepositoryTests|FullyQualifiedName~FtsIndexTests"`
Expected: all PASS.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(infra): transactional note repository with links, tags, FTS and embeddings" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 9: Infrastructure – tag repository

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Tags/TagRepository.cs`
- Test: `tests/Zettelkasten.Tests/Infrastructure/TagRepositoryTests.cs`

**Interfaces:**
- Consumes: `ITagRepository`, `Tag`, `TagWithEmbedding` (Task 6); `AppDbContext`, `TestDb` (Task 7); `FtsIndex`, `NoteRepository`, `FakeEmbeddingService`, `VectorIndex` (Task 8); `VectorMath` (Task 4)
- Produces: `sealed class TagRepository(IDbContextFactory<AppDbContext> factory) : ITagRepository`

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/TagRepositoryTests.cs`
```csharp
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class TagRepositoryTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;
    private TagRepository _tags = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, new VectorIndex(), _time);
        _tags = new TagRepository(_db.Factory);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<string> NoteWithTagsAsync(params string[] tags)
    {
        _time.Advance(TimeSpan.FromSeconds(5));
        var n = await _notes.CreateAsync();
        n.Title = "note";
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
        return n.Id;
    }

    [Fact]
    public async Task GetAll_returns_tags_sorted_by_name()
    {
        await NoteWithTagsAsync("zeta", "alpha");
        var all = await _tags.GetAllAsync();
        Assert.Equal(new[] { "alpha", "zeta" }, all.Select(t => t.Name));
    }

    [Fact]
    public async Task GetAllWithEmbeddings_returns_vectors_when_model_was_available()
    {
        await NoteWithTagsAsync("memory");
        _embeddings.IsAvailable = false;
        await NoteWithTagsAsync("offline-tag");

        var all = await _tags.GetAllWithEmbeddingsAsync();
        Assert.Equal(384, all.Single(t => t.Tag.Name == "memory").Embedding!.Length);
        Assert.Null(all.Single(t => t.Tag.Name == "offline-tag").Embedding);
    }

    [Fact]
    public async Task Delete_removes_tag_from_notes_and_search_index()
    {
        var id = await NoteWithTagsAsync("memory", "keep");
        var memory = (await _tags.GetAllAsync()).Single(t => t.Name == "memory");

        await _tags.DeleteAsync(memory.Id);

        Assert.Equal(new[] { "keep" }, (await _notes.GetAsync(id))!.Tags);
        Assert.DoesNotContain(await _tags.GetAllAsync(), t => t.Name == "memory");
        await using var ctx = _db.Factory.CreateDbContext();
        Assert.Empty(await FtsIndex.MatchAsync(ctx, "\"memory\"*", 10, default));
        Assert.Equal(new[] { id }, await FtsIndex.MatchAsync(ctx, "\"keep\"*", 10, default));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TagRepositoryTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Infrastructure/Tags/TagRepository.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Tags;

public sealed class TagRepository(IDbContextFactory<AppDbContext> factory) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new Tag(t.Id, t.Name)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return rows.Select(t => new TagWithEmbedding(
            new Tag(t.Id, t.Name),
            t.Embedding is null ? null : VectorMath.FromBytes(t.Embedding))).ToList();
    }

    public async Task DeleteAsync(int tagId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var affected = await db.NoteTags.Where(nt => nt.TagId == tagId).Select(nt => nt.NoteId).ToListAsync(ct);
        await db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync(ct); // cascades NoteTags

        // Tag names are part of the FTS row, so refresh it for every affected note
        var notes = await db.Notes.AsNoTracking()
            .Where(n => affected.Contains(n.Id))
            .Select(n => new { n.Id, n.Title, n.Content, Tags = n.NoteTags.Select(nt => nt.Tag!.Name).ToList() })
            .ToListAsync(ct);
        foreach (var n in notes)
            await FtsIndex.UpsertAsync(db, n.Id, n.Title, n.Content, n.Tags, ct);

        await tx.CommitAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TagRepositoryTests"` → Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(infra): tag repository with global tag deletion" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Infrastructure – WordPiece tokenizer and model download script

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Embeddings/WordPieceTokenizer.cs`, `scripts/fetch-model.ps1`
- Test: `tests/Zettelkasten.Tests/Infrastructure/WordPieceTokenizerTests.cs`

**Interfaces:**
- Consumes: `SemanticSettings.MaxTokens` (Task 4)
- Produces: `sealed class WordPieceTokenizer(IReadOnlyList<string> vocab)` with `static WordPieceTokenizer FromFile(string path)` and `long[] Encode(string text, int maxTokens = SemanticSettings.MaxTokens)` (starts with `[CLS]`, ends with `[SEP]`); `internal static IEnumerable<string> BasicTokenize(string text)`

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/WordPieceTokenizerTests.cs`
```csharp
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class WordPieceTokenizerTests
{
    // index = token id
    private static readonly WordPieceTokenizer Tok = new(
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "hello", "world", "play", "##ing", "!", "cafe", ","]);

    [Fact]
    public void Encodes_words_and_punctuation_with_special_tokens()
    {
        Assert.Equal(new long[] { 2, 4, 10, 5, 8, 3 }, Tok.Encode("Hello, world!"));
    }

    [Fact]
    public void Splits_into_word_pieces()
    {
        Assert.Equal(new long[] { 2, 6, 7, 3 }, Tok.Encode("playing"));
    }

    [Fact]
    public void Strips_accents_and_lowercases()
    {
        Assert.Equal(new long[] { 2, 9, 3 }, Tok.Encode("CAFÉ"));
    }

    [Fact]
    public void Unknown_words_become_unk()
    {
        Assert.Equal(new long[] { 2, 1, 4, 3 }, Tok.Encode("xyz hello"));
    }

    [Fact]
    public void Truncates_to_max_tokens_keeping_sep()
    {
        var ids = Tok.Encode(string.Concat(Enumerable.Repeat("hello ", 300)), maxTokens: 10);
        Assert.Equal(10, ids.Length);
        Assert.Equal(2, ids[0]);
        Assert.Equal(3, ids[^1]);
    }

    [Fact]
    public void Empty_text_is_cls_sep()
    {
        Assert.Equal(new long[] { 2, 3 }, Tok.Encode("  \t\n "));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~WordPieceTokenizerTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement the tokenizer**

`src/Zettelkasten.Infrastructure/Embeddings/WordPieceTokenizer.cs`
```csharp
using System.Globalization;
using System.Text;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>Minimal uncased BERT tokenizer (basic tokenization + greedy WordPiece) for all-MiniLM-L6-v2.</summary>
public sealed class WordPieceTokenizer
{
    private const int MaxCharsPerWord = 100;
    private readonly Dictionary<string, int> _vocab;
    private readonly int _cls, _sep, _unk;

    public WordPieceTokenizer(IReadOnlyList<string> vocab)
    {
        _vocab = new Dictionary<string, int>(vocab.Count, StringComparer.Ordinal);
        for (var i = 0; i < vocab.Count; i++) _vocab.TryAdd(vocab[i], i);
        _cls = _vocab["[CLS]"];
        _sep = _vocab["[SEP]"];
        _unk = _vocab["[UNK]"];
    }

    public static WordPieceTokenizer FromFile(string path) => new(File.ReadAllLines(path));

    public long[] Encode(string text, int maxTokens = SemanticSettings.MaxTokens)
    {
        var ids = new List<long>(Math.Min(maxTokens, 64)) { _cls };
        foreach (var word in BasicTokenize(text))
        {
            foreach (var id in WordPiece(word))
            {
                if (ids.Count == maxTokens - 1) goto done;
                ids.Add(id);
            }
        }
        done:
        ids.Add(_sep);
        return ids.ToArray();
    }

    internal static IEnumerable<string> BasicTokenize(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var word = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // strip accents
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                continue;
            }
            if (IsPunctuation(ch))
            {
                if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                yield return ch.ToString();
                continue;
            }
            word.Append(ch);
        }
        if (word.Length > 0) yield return word.ToString();
    }

    private IEnumerable<int> WordPiece(string word)
    {
        if (word.Length > MaxCharsPerWord) return [_unk];
        var pieces = new List<int>();
        var start = 0;
        while (start < word.Length)
        {
            var end = word.Length;
            var found = -1;
            while (start < end)
            {
                var sub = word[start..end];
                if (start > 0) sub = "##" + sub;
                if (_vocab.TryGetValue(sub, out var id)) { found = id; break; }
                end--;
            }
            if (found < 0) return [_unk];
            pieces.Add(found);
            start = end;
        }
        return pieces;
    }

    private static bool IsPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~'
        || char.IsPunctuation(c);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~WordPieceTokenizerTests"` → Expected: all PASS.

- [ ] **Step 5: Write `scripts/fetch-model.ps1`**

```powershell
# Downloads the all-MiniLM-L6-v2 embedding model once. The app itself never touches the network.
$ErrorActionPreference = 'Stop'
$models = Join-Path (Split-Path -Parent $PSScriptRoot) 'models'
New-Item -ItemType Directory -Force $models | Out-Null
$base = 'https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main'

$vocab = Join-Path $models 'vocab.txt'
if (-not (Test-Path $vocab)) { Invoke-WebRequest "$base/vocab.txt" -OutFile $vocab }

$model = Join-Path $models 'model.onnx'
if (-not (Test-Path $model)) {
    try {
        # ~23 MB int8-quantized variant
        Invoke-WebRequest "$base/onnx/model_quint8_avx2.onnx" -OutFile $model
    } catch {
        Write-Warning "Quantized model unavailable ($($_.Exception.Message)); downloading full model (~90 MB)."
        Invoke-WebRequest "$base/onnx/model.onnx" -OutFile $model
    }
}
Get-ChildItem $models | Select-Object Name, Length
```

Run: `powershell -ExecutionPolicy Bypass -File scripts/fetch-model.ps1`
Expected: lists `model.onnx` (roughly 20–90 MB) and `vocab.txt` (~230 KB). Confirm `git status` does **not** show `models/` (it is in `.gitignore`).

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(infra): WordPiece tokenizer and one-time model download script" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Infrastructure – ONNX embedding service

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Embeddings/OnnxEmbeddingService.cs`
- Modify: `tests/Zettelkasten.Tests/Zettelkasten.Tests.csproj` (add `Xunit.SkippableFact`)
- Test: `tests/Zettelkasten.Tests/Infrastructure/OnnxEmbeddingServiceTests.cs`

**Interfaces:**
- Consumes: `IEmbeddingService` (Task 6); `AppPaths` (Task 7); `WordPieceTokenizer` (Task 10); `VectorMath`, `SemanticSettings` (Task 4)
- Produces: `sealed class OnnxEmbeddingService(AppPaths paths, ILogger<OnnxEmbeddingService> logger) : IEmbeddingService, IDisposable`

- [ ] **Step 1: Add packages**

```powershell
dotnet add src/Zettelkasten.Infrastructure package Microsoft.ML.OnnxRuntime --version 1.22.0
dotnet add tests/Zettelkasten.Tests package Xunit.SkippableFact --version 1.5.23
```

- [ ] **Step 2: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/OnnxEmbeddingServiceTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class OnnxEmbeddingServiceTests
{
    private static readonly AppPaths RealPaths = new(Path.GetTempPath(), Path.Combine(AppContext.BaseDirectory, "models"));
    private static bool ModelPresent => File.Exists(RealPaths.ModelPath) && File.Exists(RealPaths.VocabPath);

    private static async Task<OnnxEmbeddingService> LoadAsync()
    {
        var s = new OnnxEmbeddingService(RealPaths, NullLogger<OnnxEmbeddingService>.Instance);
        await s.InitializeAsync();
        return s;
    }

    [Fact]
    public async Task Missing_model_means_unavailable_without_throwing()
    {
        var paths = new AppPaths(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "no-model-here"));
        using var s = new OnnxEmbeddingService(paths, NullLogger<OnnxEmbeddingService>.Instance);
        var raised = false;
        s.AvailabilityChanged += (_, _) => raised = true;

        await s.InitializeAsync();

        Assert.False(s.IsAvailable);
        Assert.False(raised);
        Assert.Throws<InvalidOperationException>(() => s.Embed("x"));
    }

    [SkippableFact]
    public async Task Real_model_produces_normalized_384_dim_vectors()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        Assert.True(s.IsAvailable);
        var v = s.Embed("Spaced repetition helps memory.");
        Assert.Equal(SemanticSettings.Dimensions, v.Length);
        Assert.Equal(1f, VectorMath.Dot(v, v), 3);
    }

    [SkippableFact]
    public async Task Related_meanings_are_closer_than_unrelated()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        var ml = s.Embed("machine learning");
        Assert.True(VectorMath.Dot(ml, s.Embed("neural networks")) > VectorMath.Dot(ml, s.Embed("cooking recipes")));
        var memory = s.Embed("how memory works");
        Assert.True(VectorMath.Dot(memory, s.Embed("spaced repetition and the forgetting curve"))
                    > VectorMath.Dot(memory, s.Embed("tax return deadline")));
    }

    [SkippableFact]
    public async Task Long_text_is_truncated_not_rejected()
    {
        Skip.IfNot(ModelPresent, "Run scripts/fetch-model.ps1 first.");
        using var s = await LoadAsync();
        var v = s.Embed(string.Concat(Enumerable.Repeat("long note text ", 2000)));
        Assert.Equal(SemanticSettings.Dimensions, v.Length);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OnnxEmbeddingServiceTests"` → Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Zettelkasten.Infrastructure/Embeddings/OnnxEmbeddingService.cs`
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>all-MiniLM-L6-v2 via ONNX Runtime on CPU: mean pooling + L2 normalization.</summary>
public sealed class OnnxEmbeddingService(AppPaths paths, ILogger<OnnxEmbeddingService> logger)
    : IEmbeddingService, IDisposable
{
    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private volatile bool _isAvailable;

    public bool IsAvailable => _isAvailable;
    public event EventHandler? AvailabilityChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_isAvailable) return;
        try
        {
            await Task.Run(() =>
            {
                if (!File.Exists(paths.ModelPath) || !File.Exists(paths.VocabPath))
                    throw new FileNotFoundException($"Embedding model not found in {paths.ModelDirectory}");
                _tokenizer = WordPieceTokenizer.FromFile(paths.VocabPath);
                _session = new InferenceSession(paths.ModelPath);
                var probe = EmbedCore("warm up");
                if (probe.Length != SemanticSettings.Dimensions)
                    throw new InvalidDataException($"Model returned {probe.Length} dimensions, expected {SemanticSettings.Dimensions}.");
            }, ct);
            _isAvailable = true;
            logger.LogInformation("Embedding model loaded from {Path}", paths.ModelPath);
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _session?.Dispose();
            _session = null;
            logger.LogWarning(ex, "Semantic search unavailable");
        }
    }

    public float[] Embed(string text)
    {
        if (!_isAvailable) throw new InvalidOperationException("Embedding model is not available.");
        return EmbedCore(text);
    }

    private float[] EmbedCore(string text)
    {
        var ids = _tokenizer!.Encode(text);
        var n = ids.Length;
        int[] shape = [1, n];
        var mask = new long[n];
        Array.Fill(mask, 1L);

        var inputs = new List<NamedOnnxValue>();
        foreach (var name in _session!.InputMetadata.Keys)
        {
            inputs.Add(name switch
            {
                "input_ids" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(ids, shape)),
                "attention_mask" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(mask, shape)),
                "token_type_ids" => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(new long[n], shape)),
                _ => throw new InvalidDataException($"Unexpected model input '{name}'."),
            });
        }

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == "last_hidden_state") ?? results.First();
        var hidden = output.AsTensor<float>(); // [1, tokens, dims]
        var dims = hidden.Dimensions[2];

        var v = new float[dims];
        for (var t = 0; t < n; t++)
            for (var d = 0; d < dims; d++)
                v[d] += hidden[0, t, d];
        for (var d = 0; d < dims; d++) v[d] /= n;
        VectorMath.Normalize(v);
        return v;
    }

    public void Dispose() => _session?.Dispose();
}
```

If the build reports `CS0618` (obsolete) for `NamedOnnxValue`/`DenseTensor` in this ONNX Runtime version, switch to the `OrtValue` API: create inputs with `OrtValue.CreateTensorValueFromMemory(ids, new long[] { 1, n })`, run with `_session.Run(new RunOptions(), inputNames, inputValues, _session.OutputNames)`, and read `GetTensorDataAsSpan<float>()` (index `t * dims + d`). Behavior must stay identical.

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~OnnxEmbeddingServiceTests"`
Expected: all PASS (model fetched in Task 10). If any real-model test shows as skipped, `models/` is not being copied — check the `<None Include="..\..\models\**" ...>` item in the test csproj.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(infra): offline ONNX embedding service for all-MiniLM-L6-v2" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Infrastructure – vector index loading and embedding backfill

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Embeddings/EmbeddingBackfillService.cs`
- Test: `tests/Zettelkasten.Tests/Infrastructure/EmbeddingBackfillServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 7); `VectorIndex`, `ContentHash`, `NoteRepository`, `FakeEmbeddingService` (Task 8); `TagRepository` (Task 9)
- Produces: `sealed class EmbeddingBackfillService(IDbContextFactory<AppDbContext>, IEmbeddingService, VectorIndex, ILogger<EmbeddingBackfillService>)` with `Task LoadIndexAsync(CancellationToken ct = default)` and `Task<int> BackfillAsync(CancellationToken ct = default)` (returns number of notes + tags embedded)

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/EmbeddingBackfillServiceTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class EmbeddingBackfillServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new() { IsAvailable = false };
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, _index, _time);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private EmbeddingBackfillService Service(VectorIndex index) =>
        new(_db.Factory, _embeddings, index, NullLogger<EmbeddingBackfillService>.Instance);

    private async Task AddNoteAsync(string title, params string[] tags)
    {
        _time.Advance(TimeSpan.FromSeconds(5));
        var n = await _notes.CreateAsync();
        n.Title = title;
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
    }

    [Fact]
    public async Task Backfills_notes_and_tags_saved_while_model_was_unavailable()
    {
        await AddNoteAsync("one", "memory");
        await AddNoteAsync("two");
        Assert.Equal(0, _index.Count);

        _embeddings.IsAvailable = true;
        var count = await Service(_index).BackfillAsync();

        Assert.Equal(3, count); // 2 notes + 1 tag
        Assert.Equal(2, _index.Count);
        Assert.NotNull((await new TagRepository(_db.Factory).GetAllWithEmbeddingsAsync()).Single().Embedding);
        Assert.Equal(0, await Service(_index).BackfillAsync()); // nothing stale anymore
    }

    [Fact]
    public async Task Backfill_does_nothing_without_model()
    {
        await AddNoteAsync("one");
        Assert.Equal(0, await Service(_index).BackfillAsync());
    }

    [Fact]
    public async Task LoadIndex_restores_vectors_from_database()
    {
        _embeddings.IsAvailable = true;
        await AddNoteAsync("one");
        await AddNoteAsync("two");

        var fresh = new VectorIndex();
        await Service(fresh).LoadIndexAsync();

        Assert.Equal(2, fresh.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EmbeddingBackfillServiceTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Infrastructure/Embeddings/EmbeddingBackfillService.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Embeddings;

/// <summary>Loads stored note vectors into memory and embeds anything saved while the model was unavailable.</summary>
public sealed class EmbeddingBackfillService(
    IDbContextFactory<AppDbContext> factory,
    IEmbeddingService embeddings,
    VectorIndex index,
    ILogger<EmbeddingBackfillService> logger)
{
    private const int BatchSize = 50;

    public async Task LoadIndexAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        index.Clear();
        await foreach (var e in db.NoteEmbeddings.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
            index.Set(e.NoteId, VectorMath.FromBytes(e.Vector));
        logger.LogInformation("Loaded {Count} note vectors", index.Count);
    }

    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable) return 0;
        var updated = 0;

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var notes = await db.Notes.AsNoTracking()
                .Select(n => new { n.Id, n.Title, n.Content, Hash = n.Embedding != null ? n.Embedding.ContentHash : null })
                .ToListAsync(ct);
            var stale = notes.Where(n => ContentHash.Compute(n.Title, n.Content) != n.Hash).ToList();

            foreach (var batch in stale.Chunk(BatchSize))
            {
                var vectors = new Dictionary<string, float[]>();
                foreach (var n in batch)
                {
                    var vector = await Task.Run(() => embeddings.Embed(ContentHash.NoteText(n.Title, n.Content)), ct);
                    vectors[n.Id] = vector;
                    var hash = ContentHash.Compute(n.Title, n.Content);
                    var existing = await db.NoteEmbeddings.FindAsync([n.Id], ct);
                    if (existing is null)
                        db.NoteEmbeddings.Add(new NoteEmbeddingEntity { NoteId = n.Id, Vector = VectorMath.ToBytes(vector), ContentHash = hash });
                    else
                    {
                        existing.Vector = VectorMath.ToBytes(vector);
                        existing.ContentHash = hash;
                    }
                }
                try
                {
                    await db.SaveChangesAsync(ct);
                    foreach (var (id, v) in vectors) index.Set(id, v);
                    updated += batch.Length;
                }
                catch (DbUpdateException ex)
                {
                    // A concurrent save of the same note won; the next backfill catches up.
                    logger.LogWarning(ex, "Embedding backfill batch failed");
                    db.ChangeTracker.Clear();
                }
            }

            var tags = await db.Tags.Where(t => t.Embedding == null).ToListAsync(ct);
            foreach (var t in tags)
                t.Embedding = VectorMath.ToBytes(embeddings.Embed(ContentHash.TagText(t.Name)));
            await db.SaveChangesAsync(ct);
            updated += tags.Count;
        }

        if (updated > 0) logger.LogInformation("Backfilled {Count} embeddings", updated);
        return updated;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~EmbeddingBackfillServiceTests"` → Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(infra): vector index loading and embedding backfill" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 13: Infrastructure – hybrid search service

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Search/SearchService.cs`
- Test: `tests/Zettelkasten.Tests/Infrastructure/SearchServiceTests.cs`

**Interfaces:**
- Consumes: `ISearchService`, `INoteRepository`, `IEmbeddingService`, `SearchResult` (Task 6); `SearchQuery`, `RankFusion`, `SemanticSettings` (Task 4); `FtsIndex`, `VectorIndex`, `NoteRepository`, `FakeEmbeddingService` (Task 8)
- Produces: `sealed class SearchService(IDbContextFactory<AppDbContext>, INoteRepository, IEmbeddingService, VectorIndex, ILogger<SearchService>) : ISearchService`

Search semantics:
- Empty query → 50 most recently updated notes (no badges).
- Free text present → candidates = RRF fusion of keyword hits (FTS) and semantic hits (vector index, score ≥ 0.35).
- No free text, `id:` present → notes whose ID starts with the prefix, newest ID first.
- Only `#tags` → notes having all tags, most recently updated first.
- `id:` and `#tags` are then applied as **filters** (intersection) to the candidates. Results capped at 50.
- Keyword search errors are logged and treated as "no keyword hits"; search never throws for user input.

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/SearchServiceTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public sealed class SearchServiceTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly VectorIndex _index = new();
    private TestDb _db = null!;
    private NoteRepository _notes = null!;
    private SearchService _search = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDb.CreateAsync(_time);
        _notes = new NoteRepository(_db.Factory, _embeddings, _index, _time);
        _search = new SearchService(_db.Factory, _notes, _embeddings, _index, NullLogger<SearchService>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Note> AddAsync(string title, string content = "", params string[] tags)
    {
        _time.Advance(TimeSpan.FromMinutes(1));
        var n = await _notes.CreateAsync();
        n.Title = title;
        n.Content = content;
        n.Tags = [.. tags];
        await _notes.SaveAsync(n);
        return n;
    }

    [Fact]
    public async Task Empty_query_returns_most_recent_first()
    {
        var older = await AddAsync("older");
        var newer = await AddAsync("newer");
        var results = await _search.SearchAsync("   ");
        Assert.Equal(new[] { newer.Id, older.Id }, results.Select(r => r.Note.Id));
        Assert.All(results, r => Assert.False(r.MatchedKeyword || r.MatchedSemantic));
    }

    [Fact]
    public async Task Keyword_prefix_match_is_flagged()
    {
        var hit = await AddAsync("Spaced repetition", "review intervals");
        await AddAsync("Cooking", "pasta");
        var r = Assert.Single(await _search.SearchAsync("repet"));
        Assert.Equal(hit.Id, r.Note.Id);
        Assert.True(r.MatchedKeyword);
    }

    [Fact]
    public async Task Keyword_search_ignores_diacritics()
    {
        var accented = await AddAsync("Café culture");
        var plain = await AddAsync("Naive cafe list");
        Assert.Equal(2, (await _search.SearchAsync("cafe")).Count);
        Assert.Contains(await _search.SearchAsync("CAFÉ"), r => r.Note.Id == plain.Id);
        Assert.Contains(await _search.SearchAsync("café"), r => r.Note.Id == accented.Id);
    }

    [Fact]
    public async Task Semantic_only_match_is_found_and_flagged()
    {
        _embeddings.Synonyms["memory"] = "recall";
        var n = await AddAsync("Total recall");
        var r = Assert.Single(await _search.SearchAsync("memory"));
        Assert.Equal(n.Id, r.Note.Id);
        Assert.True(r.MatchedSemantic);
        Assert.False(r.MatchedKeyword);
    }

    [Fact]
    public async Task Note_matching_both_ways_ranks_first()
    {
        _embeddings.Synonyms["memory"] = "recall";
        var semanticOnly = await AddAsync("Total recall");
        var both = await AddAsync("Memory palace");
        var results = await _search.SearchAsync("memory");
        Assert.Equal(both.Id, results[0].Note.Id);
        Assert.True(results[0].MatchedKeyword && results[0].MatchedSemantic);
        Assert.Contains(results, r => r.Note.Id == semanticOnly.Id);
    }

    [Fact]
    public async Task Tag_filter_restricts_results_and_works_alone()
    {
        var tagged = await AddAsync("memory one", "", "learning", "psych");
        await AddAsync("memory two", "", "learning");
        Assert.Equal(new[] { tagged.Id }, (await _search.SearchAsync("memory #learning #Psych")).Select(r => r.Note.Id));
        Assert.Equal(2, (await _search.SearchAsync("#learning")).Count);
        Assert.Empty(await _search.SearchAsync("#nonexistent"));
    }

    [Fact]
    public async Task Id_prefix_lists_and_filters()
    {
        var a = await AddAsync("alpha");
        var b = await AddAsync("beta");
        Assert.Equal(new[] { b.Id, a.Id }, (await _search.SearchAsync("id:2026")).Select(r => r.Note.Id));
        Assert.Equal(new[] { a.Id }, (await _search.SearchAsync($"id:{a.Id}")).Select(r => r.Note.Id));
        Assert.Empty(await _search.SearchAsync("alpha id:1999"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"unbalanced")]
    [InlineData("*")]
    [InlineData("a*b")]
    [InlineData("-memory")]
    [InlineData("title:memory")]
    [InlineData("(memory")]
    [InlineData("memory AND")]
    [InlineData("NOT")]
    [InlineData("NEAR(a b)")]
    [InlineData("^")]
    [InlineData("!!!???")]
    [InlineData("🧠")]
    public async Task Search_never_throws_on_fts_syntax(string query)
    {
        await AddAsync("memory", "content");
        var ex = await Record.ExceptionAsync(() => _search.SearchAsync(query));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Works_keyword_only_without_model()
    {
        var n = await AddAsync("Zettel");
        _embeddings.IsAvailable = false;
        var r = Assert.Single(await _search.SearchAsync("zettel"));
        Assert.Equal(n.Id, r.Note.Id);
        Assert.False(r.MatchedSemantic);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchServiceTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Infrastructure/Search/SearchService.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Search;

public sealed class SearchService(
    IDbContextFactory<AppDbContext> factory,
    INoteRepository notes,
    IEmbeddingService embeddings,
    VectorIndex index,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = SearchQuery.Parse(query);
        if (q.IsEmpty)
        {
            var recent = await notes.GetRecentAsync(SemanticSettings.MaxResults, ct);
            return recent.Select(n => new SearchResult(n, false, false)).ToList();
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var tags = q.Tags.Distinct().ToList();
        var keyword = new HashSet<string>();
        var semantic = new HashSet<string>();
        List<string> ordered;

        if (q.FreeText.Length > 0)
        {
            var rankings = new List<IReadOnlyList<string>>();
            var keywordIds = await KeywordAsync(db, q.FreeText, ct);
            keyword.UnionWith(keywordIds);
            rankings.Add(keywordIds);

            if (embeddings.IsAvailable)
            {
                var vector = await Task.Run(() => embeddings.Embed(q.FreeText), ct);
                var semanticIds = index.Search(vector, SemanticSettings.SearchMinScore, SemanticSettings.CandidateLimit)
                    .Select(h => h.Id).ToList();
                semantic.UnionWith(semanticIds);
                rankings.Add(semanticIds);
            }
            ordered = RankFusion.Fuse(rankings).ToList();
        }
        else if (q.IdPrefix is not null)
        {
            ordered = await db.Notes.AsNoTracking()
                .Where(n => n.Id.StartsWith(q.IdPrefix))
                .OrderByDescending(n => n.Id).Select(n => n.Id)
                .Take(SemanticSettings.CandidateLimit).ToListAsync(ct);
        }
        else
        {
            var withTags = await NotesWithAllTagsAsync(db, tags, ct);
            ordered = await db.Notes.AsNoTracking()
                .Where(n => withTags.Contains(n.Id))
                .OrderByDescending(n => n.UpdatedAtMs).Select(n => n.Id)
                .Take(SemanticSettings.MaxResults).ToListAsync(ct);
        }

        if (q.IdPrefix is not null)
            ordered = ordered.Where(id => id.StartsWith(q.IdPrefix, StringComparison.Ordinal)).ToList();
        if (tags.Count > 0)
        {
            var allowed = await NotesWithAllTagsAsync(db, tags, ct);
            ordered = ordered.Where(allowed.Contains).ToList();
        }

        var summaries = await notes.GetSummariesAsync(ordered.Take(SemanticSettings.MaxResults).ToList(), ct);
        return summaries.Select(n => new SearchResult(n, keyword.Contains(n.Id), semantic.Contains(n.Id))).ToList();
    }

    private async Task<List<string>> KeywordAsync(AppDbContext db, string freeText, CancellationToken ct)
    {
        var expression = FtsIndex.BuildMatchExpression(freeText);
        if (expression is null) return [];
        try
        {
            return await FtsIndex.MatchAsync(db, expression, SemanticSettings.CandidateLimit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Keyword search failed for {Expression}", expression);
            return [];
        }
    }

    private static async Task<HashSet<string>> NotesWithAllTagsAsync(AppDbContext db, List<string> tags, CancellationToken ct)
    {
        var ids = await db.NoteTags.AsNoTracking()
            .Where(nt => tags.Contains(nt.Tag!.Name))
            .GroupBy(nt => nt.NoteId)
            .Where(g => g.Count() == tags.Count)
            .Select(g => g.Key)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchServiceTests"` → Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(infra): hybrid keyword + semantic search with tag and id filters" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Infrastructure – tag suggestion service

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Tags/TagSuggestionService.cs`
- Test: `tests/Zettelkasten.Tests/Infrastructure/TagSuggestionServiceTests.cs`

**Interfaces:**
- Consumes: `ITagSuggestionService`, `ITagRepository`, `IEmbeddingService`, `TagSuggestion(s)` (Task 6); `TagNormalizer`, `InitialsMatcher` (Task 3); `SemanticSettings`, `VectorMath` (Task 4); `ContentHash.TagText` (Task 8)
- Produces: `sealed class TagSuggestionService(ITagRepository tags, IEmbeddingService embeddings) : ITagSuggestionService`

Rules: empty normalized input → no exact, no suggestions. Exact normalized match (case-insensitive) → `ExactMatch` set, no suggestions. Otherwise suggest tags with similarity ≥ 0.75 **or** an initials match; initials matches first, then by score descending; at most 5.

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/TagSuggestionServiceTests.cs`
```csharp
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Tags;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class TagSuggestionServiceTests
{
    private readonly FakeEmbeddingService _embeddings = new();

    private sealed class StubTags(IReadOnlyList<TagWithEmbedding> all) : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Tag>>(all.Select(t => t.Tag).ToList());
        public Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult(all);
        public Task DeleteAsync(int tagId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private TagSuggestionService Service(params string[] names) =>
        new(new StubTags(names.Select((n, i) =>
            new TagWithEmbedding(new Tag(i + 1, n), _embeddings.Embed(ContentHash.TagText(n)))).ToList()), _embeddings);

    [Fact]
    public async Task Exact_normalized_match_is_reused()
    {
        var r = await Service("machine-learning").SuggestAsync("  Machine   Learning ");
        Assert.Equal("machine-learning", r.ExactMatch?.Name);
        Assert.Empty(r.Similar);
    }

    [Fact]
    public async Task Similar_tags_are_suggested()
    {
        var r = await Service("machine-learning", "cooking").SuggestAsync("machine learning ops");
        Assert.Null(r.ExactMatch);
        var s = Assert.Single(r.Similar);
        Assert.Equal("machine-learning", s.Tag.Name);
        Assert.True(s.Score >= 0.75f);
    }

    [Fact]
    public async Task Initials_match_is_suggested_first()
    {
        var r = await Service("machine-learning-ops", "machine-learning").SuggestAsync("ML");
        Assert.Equal("machine-learning", r.Similar[0].Tag.Name);
        Assert.True(r.Similar[0].IsInitialsMatch);
    }

    [Fact]
    public async Task Initials_still_work_without_model()
    {
        var service = Service("machine-learning", "machine-learning-ops");
        _embeddings.IsAvailable = false;
        var r = await service.SuggestAsync("ml");
        Assert.Equal("machine-learning", Assert.Single(r.Similar).Tag.Name);
    }

    [Fact]
    public async Task Unrelated_and_empty_inputs_suggest_nothing()
    {
        var service = Service("machine-learning");
        Assert.Empty((await service.SuggestAsync("gardening")).Similar);
        var empty = await service.SuggestAsync("  # ");
        Assert.Null(empty.ExactMatch);
        Assert.Empty(empty.Similar);
    }

    [Fact]
    public async Task At_most_five_suggestions()
    {
        // fake cosine = 3 / sqrt(3 * 4) ≈ 0.866 for every tag
        var r = await Service("long-term-memory-a", "long-term-memory-b", "long-term-memory-c",
                "long-term-memory-d", "long-term-memory-e", "long-term-memory-f")
            .SuggestAsync("long term memory");
        Assert.Equal(5, r.Similar.Count);
    }
}
```

The fake embedding is bag-of-words, so `"machine learning ops"` vs `"machine learning"` scores 2/√6 ≈ 0.816 — above the 0.75 threshold.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TagSuggestionServiceTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.Infrastructure/Tags/TagSuggestionService.cs`
```csharp
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Search;
using Zettelkasten.Core.Tags;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Infrastructure.Tags;

public sealed class TagSuggestionService(ITagRepository tags, IEmbeddingService embeddings) : ITagSuggestionService
{
    public async Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default)
    {
        var name = TagNormalizer.Normalize(input);
        if (name.Length == 0) return new TagSuggestions(null, []);

        var all = await tags.GetAllWithEmbeddingsAsync(ct);
        var exact = all.FirstOrDefault(t => string.Equals(t.Tag.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new TagSuggestions(exact.Tag, []);

        var query = embeddings.IsAvailable
            ? await Task.Run(() => embeddings.Embed(ContentHash.TagText(name)), ct)
            : null;

        var similar = all
            .Select(t => new TagSuggestion(
                t.Tag,
                query is not null && t.Embedding is not null ? VectorMath.Dot(query, t.Embedding) : 0f,
                InitialsMatcher.IsMatch(name, t.Tag.Name)))
            .Where(s => s.IsInitialsMatch || s.Score >= SemanticSettings.TagSuggestionMinScore)
            .OrderByDescending(s => s.IsInitialsMatch)
            .ThenByDescending(s => s.Score)
            .Take(SemanticSettings.MaxTagSuggestions)
            .ToList();

        return new TagSuggestions(null, similar);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TagSuggestionServiceTests"` → Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(infra): semantic tag suggestions with initials matching" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Infrastructure – backup service and DI registration

**Files:**
- Create: `src/Zettelkasten.Infrastructure/Backup/BackupService.cs`, `src/Zettelkasten.Infrastructure/DependencyInjection.cs`
- Test: `tests/Zettelkasten.Tests/Infrastructure/BackupServiceTests.cs`, `tests/Zettelkasten.Tests/Infrastructure/DependencyInjectionTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 7–14
- Produces:
  - `sealed class BackupService(AppPaths paths) : IBackupService`
  - `static IServiceCollection DependencyInjection.AddZettelkastenInfrastructure(this IServiceCollection services, AppPaths paths)` registering: `AppPaths`, `TimeProvider` (System), `IDbContextFactory<AppDbContext>`, `VectorIndex`, `OnnxEmbeddingService` + `IEmbeddingService`, `INoteRepository`, `ITagRepository`, `ISearchService`, `ITagSuggestionService`, `IBackupService`, `DbInitializer`, `EmbeddingBackfillService` — all singletons.

- [ ] **Step 1: Add the DI abstractions package reference (explicit, though EF brings it transitively)**

```powershell
dotnet add src/Zettelkasten.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions --version 10.0.0
```

- [ ] **Step 2: Write failing tests**

`tests/Zettelkasten.Tests/Infrastructure/BackupServiceTests.cs`
```csharp
using Microsoft.Data.Sqlite;
using Zettelkasten.Infrastructure.Backup;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.Infrastructure;

public class BackupServiceTests
{
    [Fact]
    public async Task Export_writes_a_consistent_copy_while_db_is_open()
    {
        await using var db = await TestDb.CreateAsync();
        var notes = new NoteRepository(db.Factory, new FakeEmbeddingService(), new VectorIndex(), TimeProvider.System);
        var n = await notes.CreateAsync();
        n.Title = "keep me";
        await notes.SaveAsync(n);

        await using var open = new SqliteConnection(db.Paths.ConnectionString); // simulate the app holding a connection
        await open.OpenAsync();

        var target = Path.Combine(db.Paths.DataDirectory, "export", "backup.db");
        await new BackupService(db.Paths).ExportAsync(target);

        await using var c = new SqliteConnection($"Data Source={target};Pooling=False");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Title FROM Notes";
        Assert.Equal("keep me", await cmd.ExecuteScalarAsync());
    }
}
```

`tests/Zettelkasten.Tests/Infrastructure/DependencyInjectionTests.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.Tests.Infrastructure;

public class DependencyInjectionTests
{
    [Fact]
    public void All_services_resolve()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddZettelkastenInfrastructure(new AppPaths(Path.GetTempPath(), Path.GetTempPath()));
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(sp.GetRequiredService<INoteRepository>());
        Assert.NotNull(sp.GetRequiredService<ITagRepository>());
        Assert.NotNull(sp.GetRequiredService<ISearchService>());
        Assert.NotNull(sp.GetRequiredService<ITagSuggestionService>());
        Assert.NotNull(sp.GetRequiredService<IBackupService>());
        Assert.NotNull(sp.GetRequiredService<DbInitializer>());
        Assert.NotNull(sp.GetRequiredService<EmbeddingBackfillService>());
        Assert.Same(sp.GetRequiredService<OnnxEmbeddingService>(), sp.GetRequiredService<IEmbeddingService>());
    }
}
```

`AddLogging()` needs `Microsoft.Extensions.Logging` — it is available to the tests transitively via EF Core; if not, add `dotnet add tests/Zettelkasten.Tests package Microsoft.Extensions.Logging --version 10.0.0`.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BackupServiceTests|FullyQualifiedName~DependencyInjectionTests"` → Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Zettelkasten.Infrastructure/Backup/BackupService.cs`
```csharp
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure.Data;

namespace Zettelkasten.Infrastructure.Backup;

public sealed class BackupService(AppPaths paths) : IBackupService
{
    public Task ExportAsync(string destinationPath, CancellationToken ct = default) =>
        Task.Run(() => SqliteBackup.Copy(paths.ConnectionString, destinationPath), ct);
}
```

`src/Zettelkasten.Infrastructure/DependencyInjection.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure.Backup;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;
using Zettelkasten.Infrastructure.Notes;
using Zettelkasten.Infrastructure.Search;
using Zettelkasten.Infrastructure.Tags;

namespace Zettelkasten.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddZettelkastenInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(paths.ConnectionString));

        services.AddSingleton<VectorIndex>();
        services.AddSingleton<OnnxEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<OnnxEmbeddingService>());

        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ITagSuggestionService, TagSuggestionService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<DbInitializer>();
        services.AddSingleton<EmbeddingBackfillService>();
        return services;
    }
}
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test` → Expected: all tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(infra): backup export and DI registration" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 16: App – note editor view model (auto-save, flush, failure handling)

**Files:**
- Create: `src/Zettelkasten.App/ViewModels/NoteEditorViewModel.cs`
- Test: `tests/Zettelkasten.Tests/Support/FakeNoteRepository.cs`, `tests/Zettelkasten.Tests/App/NoteEditorViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository`, `Note`, `NoteSummary` (Task 6); `Debouncer` (Task 5); `LinkParser` (Task 2)
- Produces:
  - `sealed partial class NoteEditorViewModel(INoteRepository, TimeProvider, ILogger<NoteEditorViewModel>) : ObservableObject, IDisposable` with observable `Title`, `Content`, `IsPreview`, `SaveError`, `LastSavedAt`; read-only `CurrentNote`, `NoteId`, `HasNote`, `IsDirty`; `internal Task PendingSave`; `SaveCommand` (`IAsyncRelayCommand`); methods `Task LoadAsync(Note? note)`, `Task FlushAsync()`, `Task UpdateAndSaveAsync(Action<Note> mutate)`, `void RequestOpen(string noteId)`, `Task<IReadOnlyList<NoteSummary>> GetLinkCandidatesAsync()`, `Task<IReadOnlyDictionary<string, string>> GetLinkTitlesAsync()`; events `NoteLoaded`, `Saved` (`EventHandler<Note>`), `OpenRequested` (`EventHandler<string>`); const `SaveFailedMessage`
  - Test support `FakeNoteRepository(TimeProvider? time = null) : INoteRepository` with `Note Add(string title, string content = "", params string[] tags)`, `int SaveCount`, `List<Note> Saved`, `bool FailNextSave`

- [ ] **Step 1: Add the MVVM package to the App**

```powershell
dotnet add src/Zettelkasten.App package CommunityToolkit.Mvvm --version 8.4.0
dotnet add src/Zettelkasten.App package Microsoft.Extensions.Hosting --version 10.0.0
```

- [ ] **Step 2: Write the fake repository (test support)**

`tests/Zettelkasten.Tests/Support/FakeNoteRepository.cs`
```csharp
using System.Globalization;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Support;

/// <summary>In-memory INoteRepository for view model tests.</summary>
public sealed class FakeNoteRepository(TimeProvider? time = null) : INoteRepository
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, Note> _notes = [];
    private long _counter;

    public int SaveCount { get; private set; }
    public List<Note> Saved { get; } = [];
    public bool FailNextSave { get; set; }

    public Note Add(string title, string content = "", params string[] tags)
    {
        var id = (20261102000000 + ++_counter).ToString(CultureInfo.InvariantCulture);
        var at = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero).AddSeconds(_counter);
        var note = new Note { Id = id, Title = title, Content = content, Tags = [.. tags], CreatedAt = at, UpdatedAt = at };
        _notes[id] = note;
        return note.Clone();
    }

    public Task<Note> CreateAsync(CancellationToken ct = default) => Task.FromResult(Add(""));

    public Task<Note?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_notes.TryGetValue(id, out var n) ? n.Clone() : null);

    public Task SaveAsync(Note note, CancellationToken ct = default)
    {
        if (FailNextSave) { FailNextSave = false; throw new IOException("disk full"); }
        if (!_notes.ContainsKey(note.Id)) throw new InvalidOperationException("missing");
        SaveCount++;
        note.UpdatedAt = _time.GetLocalNow();
        _notes[note.Id] = note.Clone();
        Saved.Add(note.Clone());
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) { _notes.Remove(id); return Task.CompletedTask; }

    public Task<IReadOnlyList<NoteSummary>> GetRecentAsync(int count, CancellationToken ct = default) =>
        Result(_notes.Values.OrderByDescending(n => n.UpdatedAt).ThenByDescending(n => n.Id).Take(count).Select(Summary));

    public Task<IReadOnlyList<NoteSummary>> GetAllSummariesAsync(CancellationToken ct = default) =>
        Result(_notes.Values.OrderBy(n => n.Title).Select(Summary));

    public Task<IReadOnlyList<NoteSummary>> GetSummariesAsync(IReadOnlyList<string> ids, CancellationToken ct = default) =>
        Result(ids.Where(_notes.ContainsKey).Select(id => Summary(_notes[id])));

    public Task<IReadOnlyList<LinkInfo>> GetOutgoingLinksAsync(string id, CancellationToken ct = default)
    {
        if (!_notes.TryGetValue(id, out var n)) return Result<LinkInfo>([]);
        var inline = LinkParser.ExtractTargets(n.Content).Where(t => t != id)
            .Select(t => new LinkInfo(t, _notes.GetValueOrDefault(t)?.Title, LinkKind.Inline));
        var manual = n.ManualLinks.Distinct()
            .Select(t => new LinkInfo(t, _notes.GetValueOrDefault(t)?.Title, LinkKind.Manual));
        return Result(inline.Concat(manual));
    }

    public Task<IReadOnlyList<LinkInfo>> GetBacklinksAsync(string id, CancellationToken ct = default)
    {
        var inline = _notes.Values.Where(n => n.Id != id && LinkParser.ExtractTargets(n.Content).Contains(id))
            .Select(n => new LinkInfo(n.Id, n.Title, LinkKind.Inline));
        var manual = _notes.Values.Where(n => n.ManualLinks.Contains(id))
            .Select(n => new LinkInfo(n.Id, n.Title, LinkKind.Manual));
        return Result(inline.Concat(manual));
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_notes.Count);

    private static NoteSummary Summary(Note n) => new(n.Id, n.Title, n.UpdatedAt);
    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToList());
}
```

- [ ] **Step 3: Write failing tests**

`tests/Zettelkasten.Tests/App/NoteEditorViewModelTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class NoteEditorViewModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly NoteEditorViewModel _vm;

    public NoteEditorViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        _vm = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
    }

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task Edits_are_saved_after_one_second_of_inactivity()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        _vm.Content = "a";
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _vm.Content = "ab";
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, _repo.SaveCount);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await _vm.PendingSave;

        Assert.Equal(1, _repo.SaveCount);
        Assert.Equal("ab", _repo.Saved[^1].Content);
        Assert.False(_vm.IsDirty);
        Assert.NotNull(_vm.LastSavedAt);
    }

    [Fact]
    public async Task Loading_a_note_does_not_save()
    {
        await _vm.LoadAsync(_repo.Add("t", "body"));
        _time.Advance(TimeSpan.FromSeconds(5));
        await _vm.PendingSave;
        Assert.Equal(0, _repo.SaveCount);
        Assert.Equal("body", _vm.Content);
    }

    [Fact]
    public async Task Loading_another_note_flushes_pending_edits_to_previous_note()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.LoadAsync(a);
        _vm.Title = "edited";
        _time.Advance(TimeSpan.FromMilliseconds(300));

        await _vm.LoadAsync(b);
        _time.Advance(TimeSpan.FromSeconds(2));
        await _vm.PendingSave;

        var saved = Assert.Single(_repo.Saved);
        Assert.Equal(a.Id, saved.Id);
        Assert.Equal("edited", saved.Title);
        Assert.Equal("b", (await _repo.GetAsync(b.Id))!.Title);
        Assert.Equal("b", _vm.Title);
        Assert.Equal(b.Id, _vm.NoteId);
    }

    [Fact]
    public async Task Failed_save_keeps_text_reports_error_and_retries()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        _repo.FailNextSave = true;
        _vm.Content = "important";
        _time.Advance(TimeSpan.FromSeconds(1));
        await _vm.PendingSave;

        Assert.Equal(NoteEditorViewModel.SaveFailedMessage, _vm.SaveError);
        Assert.True(_vm.IsDirty);
        Assert.Equal("important", _vm.Content);

        await _vm.SaveCommand.ExecuteAsync(null);   // Ctrl+S
        Assert.Null(_vm.SaveError);
        Assert.Equal("important", _repo.Saved[^1].Content);
    }

    [Fact]
    public async Task UpdateAndSave_saves_immediately()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        await _vm.UpdateAndSaveAsync(n => n.Tags.Add("x"));
        Assert.Equal(1, _repo.SaveCount);
        Assert.Contains("x", _repo.Saved[^1].Tags);
    }

    [Fact]
    public async Task Flush_without_changes_does_nothing()
    {
        await _vm.LoadAsync(_repo.Add("t"));
        await _vm.FlushAsync();
        Assert.Equal(0, _repo.SaveCount);
    }

    [Fact]
    public async Task Link_titles_only_include_existing_notes()
    {
        var target = _repo.Add("Target");
        await _vm.LoadAsync(_repo.Add("t", $"[[{target.Id}]] [[20990101000000]]"));
        var titles = await _vm.GetLinkTitlesAsync();
        Assert.Equal("Target", Assert.Single(titles).Value);
    }

    [Fact]
    public async Task Link_candidates_exclude_current_note()
    {
        var other = _repo.Add("Other");
        var self = _repo.Add("Self");
        await _vm.LoadAsync(self);
        Assert.Equal(new[] { other.Id }, (await _vm.GetLinkCandidatesAsync()).Select(s => s.Id));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NoteEditorViewModelTests"` → Expected: build FAILS.

- [ ] **Step 5: Implement**

`src/Zettelkasten.App/ViewModels/NoteEditorViewModel.cs`
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Links;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.ViewModels;

public sealed partial class NoteEditorViewModel : ObservableObject, IDisposable
{
    public const string SaveFailedMessage = "Save failed – your text is still in the editor";

    private readonly INoteRepository _repo;
    private readonly ILogger<NoteEditorViewModel> _logger;
    private readonly Debouncer _autoSave;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Note? _note;
    private int _version;
    private int _savedVersion;
    private bool _loading;

    public NoteEditorViewModel(INoteRepository repo, TimeProvider time, ILogger<NoteEditorViewModel> logger)
    {
        _repo = repo;
        _logger = logger;
        _autoSave = new Debouncer(TimeSpan.FromSeconds(1), time);
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _isPreview;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private DateTimeOffset? _lastSavedAt;

    public Note? CurrentNote => _note;
    public string? NoteId => _note?.Id;
    public bool HasNote => _note is not null;
    public bool IsDirty => _version != _savedVersion;

    /// <summary>The most recently scheduled auto-save (exposed for tests).</summary>
    internal Task PendingSave { get; private set; } = Task.CompletedTask;

    public event EventHandler? NoteLoaded;
    public event EventHandler<Note>? Saved;
    public event EventHandler<string>? OpenRequested;

    /// <summary>Saves pending edits of the current note first, then shows <paramref name="note"/>.</summary>
    public async Task LoadAsync(Note? note)
    {
        await FlushAsync();
        _loading = true;
        try
        {
            _note = note;
            Title = note?.Title ?? "";
            Content = note?.Content ?? "";
        }
        finally
        {
            _loading = false;
        }
        _version = _savedVersion = 0;
        SaveError = null;
        OnPropertyChanged(nameof(CurrentNote));
        OnPropertyChanged(nameof(NoteId));
        OnPropertyChanged(nameof(HasNote));
        NoteLoaded?.Invoke(this, EventArgs.Empty);
    }

    public async Task FlushAsync()
    {
        _autoSave.Cancel();
        await SaveCoreAsync();
    }

    [RelayCommand]
    private Task Save() => FlushAsync();

    /// <summary>Applies a change to tags/links and saves right away.</summary>
    public async Task UpdateAndSaveAsync(Action<Note> mutate)
    {
        if (_note is null) return;
        mutate(_note);
        _version++;
        await FlushAsync();
    }

    public void RequestOpen(string noteId) => OpenRequested?.Invoke(this, noteId);

    public async Task<IReadOnlyList<NoteSummary>> GetLinkCandidatesAsync() =>
        (await _repo.GetAllSummariesAsync()).Where(s => s.Id != NoteId).ToList();

    public async Task<IReadOnlyDictionary<string, string>> GetLinkTitlesAsync()
    {
        var summaries = await _repo.GetSummariesAsync(LinkParser.ExtractTargets(Content));
        return summaries.ToDictionary(s => s.Id, s => s.Title);
    }

    partial void OnTitleChanged(string value) => MarkDirty();
    partial void OnContentChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || _note is null) return;
        _version++;
        PendingSave = _autoSave.Trigger(SaveCoreAsync);
    }

    private async Task SaveCoreAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var note = _note;
            if (note is null || !IsDirty) return;

            var version = _version;
            note.Title = Title;
            note.Content = Content;
            var snapshot = note.Clone();
            try
            {
                await _repo.SaveAsync(snapshot);
                note.UpdatedAt = snapshot.UpdatedAt;
                note.Tags = snapshot.Tags;               // repository normalizes these
                note.ManualLinks = snapshot.ManualLinks;
                _savedVersion = version;
                SaveError = null;
                LastSavedAt = snapshot.UpdatedAt;
                Saved?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving note {NoteId} failed", note.Id);
                SaveError = SaveFailedMessage;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose()
    {
        _autoSave.Dispose();
        _saveLock.Dispose();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NoteEditorViewModelTests"` → Expected: all PASS.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(app): note editor view model with debounced auto-save" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: App – search view model

**Files:**
- Create: `src/Zettelkasten.App/ViewModels/SearchViewModel.cs`
- Test: `tests/Zettelkasten.Tests/App/SearchViewModelTests.cs`

**Interfaces:**
- Consumes: `ISearchService`, `SearchResult`, `NoteSummary` (Task 6); `Debouncer` (Task 5)
- Produces: `sealed partial class SearchViewModel(ISearchService, TimeProvider, ILogger<SearchViewModel>) : ObservableObject, IDisposable` with observable `Query`, `Selected` (`SearchResult?`); `ObservableCollection<SearchResult> Results`; `internal Task PendingSearch`; `Task RefreshAsync()`; `void SelectById(string? id)`; `ClearCommand`; event `OpenRequested` (`EventHandler<string>`)

- [ ] **Step 1: Write failing tests**

`tests/Zettelkasten.Tests/App/SearchViewModelTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.App;

public sealed class SearchViewModelTests : IDisposable
{
    private sealed class StubSearch : ISearchService
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, TaskCompletionSource<IReadOnlyList<SearchResult>>> Pending { get; } = [];
        public bool Manual { get; set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            Calls.Add(query);
            if (!Manual) return Task.FromResult<IReadOnlyList<SearchResult>>([Result(query)]);
            var tcs = new TaskCompletionSource<IReadOnlyList<SearchResult>>();
            Pending[query] = tcs;
            return tcs.Task;
        }
    }

    private static SearchResult Result(string id) =>
        new(new NoteSummary(id, $"title {id}", DateTimeOffset.UnixEpoch), true, false);

    private readonly FakeTimeProvider _time = new();
    private readonly StubSearch _search = new();
    private readonly SearchViewModel _vm;

    public SearchViewModelTests() =>
        _vm = new SearchViewModel(_search, _time, NullLogger<SearchViewModel>.Instance);

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task Query_changes_are_debounced_250ms()
    {
        _vm.Query = "m";
        _vm.Query = "mem";
        _time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Empty(_search.Calls);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await _vm.PendingSearch;

        Assert.Equal(new[] { "mem" }, _search.Calls);
        Assert.Equal("mem", Assert.Single(_vm.Results).Note.Id);
    }

    [Fact]
    public async Task Stale_results_are_discarded()
    {
        _search.Manual = true;
        _vm.Query = "a";
        var first = _vm.RefreshAsync();
        _vm.Query = "ab";
        var second = _vm.RefreshAsync();

        _search.Pending["ab"].SetResult([Result("x")]);
        await second;
        _search.Pending["a"].SetResult([Result("y")]);
        await first;

        Assert.Equal("x", Assert.Single(_vm.Results).Note.Id);
    }

    [Fact]
    public async Task User_selection_requests_open_but_refresh_and_SelectById_do_not()
    {
        var opened = new List<string>();
        _vm.OpenRequested += (_, id) => opened.Add(id);
        await _vm.RefreshAsync();             // query "" -> result id ""
        _vm.SelectById("");
        await _vm.RefreshAsync();             // selection restored silently
        Assert.Empty(opened);

        _vm.Selected = null;
        _vm.Selected = _vm.Results[0];
        Assert.Equal(new[] { "" }, opened);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchViewModelTests"` → Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Zettelkasten.App/ViewModels/SearchViewModel.cs`
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly ISearchService _search;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly Debouncer _debounce;
    private int _requestVersion;
    private bool _updating;

    public SearchViewModel(ISearchService search, TimeProvider time, ILogger<SearchViewModel> logger)
    {
        _search = search;
        _logger = logger;
        _debounce = new Debouncer(TimeSpan.FromMilliseconds(250), time);
    }

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SearchResult? _selected;

    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>The most recently scheduled search (exposed for tests).</summary>
    internal Task PendingSearch { get; private set; } = Task.CompletedTask;

    public event EventHandler<string>? OpenRequested;

    partial void OnQueryChanged(string value) => PendingSearch = _debounce.Trigger(RefreshAsync);

    partial void OnSelectedChanged(SearchResult? value)
    {
        if (!_updating && value is not null) OpenRequested?.Invoke(this, value.Note.Id);
    }

    public async Task RefreshAsync()
    {
        var version = ++_requestVersion;
        IReadOnlyList<SearchResult> results;
        try
        {
            results = await _search.SearchAsync(Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for {Query}", Query);
            return;
        }
        if (version != _requestVersion) return; // a newer search is running

        _updating = true;
        try
        {
            var selectedId = Selected?.Note.Id;
            Results.Clear();
            foreach (var r in results) Results.Add(r);
            Selected = Results.FirstOrDefault(r => r.Note.Id == selectedId);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Highlights the open note without triggering navigation.</summary>
    public void SelectById(string? id)
    {
        _updating = true;
        try { Selected = Results.FirstOrDefault(r => r.Note.Id == id); }
        finally { _updating = false; }
    }

    [RelayCommand]
    private void Clear() => Query = "";

    public void Dispose() => _debounce.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchViewModelTests"` → Expected: all PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(app): debounced search view model" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 18: App – side panel view model (tags with suggestions, manual links, backlinks)

**Files:**
- Create: `src/Zettelkasten.App/Services/IDialogService.cs`, `src/Zettelkasten.App/ViewModels/NotePanelViewModel.cs`
- Test: `tests/Zettelkasten.Tests/Support/FakeDialogService.cs`, `tests/Zettelkasten.Tests/App/NotePanelViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository`, `ITagRepository`, `ITagSuggestionService`, `LinkInfo`, `TagSuggestion(s)` (Task 6); `TagNormalizer` (Task 3); `Debouncer` (Task 5); `NoteEditorViewModel` (Task 16); `FakeNoteRepository` (Task 16)
- Produces:
  - `interface IDialogService { bool Confirm(string title, string message); string? PickNote(string title, string? excludeId); string? PickSaveFile(string title, string defaultFileName, string filter); void ShowError(string title, string message); }`
  - `sealed partial class NotePanelViewModel(INoteRepository, ITagRepository, ITagSuggestionService, NoteEditorViewModel, IDialogService, TimeProvider) : ObservableObject, IDisposable` with collections `Tags` (`string`), `Related`, `Outgoing`, `Backlinks` (`LinkInfo`), `Suggestions` (`TagSuggestion`); observable `TagInput`, `CanCreateTag`; `internal Task PendingSuggest`; `Task RefreshAsync()`; commands `AddTagFromInputCommand`, `AcceptSuggestionCommand(TagSuggestion)`, `CreateTagAnywayCommand`, `RemoveTagCommand(string)`, `DeleteTagEverywhereCommand(string)`, `AddManualLinkCommand`, `RemoveManualLinkCommand(LinkInfo)`, `OpenLinkCommand(LinkInfo)`; event `OpenRequested`
  - Test support `FakeDialogService : IDialogService` with `bool ConfirmResult`, `List<string> Confirmations`, `string? PickNoteResult`, `string? SaveFileResult`, `List<string> Errors`

- [ ] **Step 1: Write the dialog abstraction**

`src/Zettelkasten.App/Services/IDialogService.cs`
```csharp
namespace Zettelkasten.App.Services;

public interface IDialogService
{
    bool Confirm(string title, string message);
    /// <summary>Lets the user search and pick a note. Returns its ID or null when cancelled.</summary>
    string? PickNote(string title, string? excludeId);
    string? PickSaveFile(string title, string defaultFileName, string filter);
    void ShowError(string title, string message);
}
```

`tests/Zettelkasten.Tests/Support/FakeDialogService.cs`
```csharp
using Zettelkasten.App.Services;

namespace Zettelkasten.Tests.Support;

public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public List<string> Confirmations { get; } = [];
    public string? PickNoteResult { get; set; }
    public string? SaveFileResult { get; set; }
    public List<string> Errors { get; } = [];

    public bool Confirm(string title, string message) { Confirmations.Add(message); return ConfirmResult; }
    public string? PickNote(string title, string? excludeId) => PickNoteResult;
    public string? PickSaveFile(string title, string defaultFileName, string filter) => SaveFileResult;
    public void ShowError(string title, string message) => Errors.Add(message);
}
```

- [ ] **Step 2: Write failing tests**

`tests/Zettelkasten.Tests/App/NotePanelViewModelTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class NotePanelViewModelTests : IDisposable
{
    private sealed class StubSuggestions : ITagSuggestionService
    {
        public TagSuggestions Result { get; set; } = new(null, []);
        public Task<TagSuggestions> SuggestAsync(string input, CancellationToken ct = default) => Task.FromResult(Result);
    }

    private sealed class StubTags : ITagRepository
    {
        public List<Tag> All { get; } = [];
        public List<int> Deleted { get; } = [];
        public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Tag>>(All);
        public Task<IReadOnlyList<TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TagWithEmbedding>>(All.Select(t => new TagWithEmbedding(t, null)).ToList());
        public Task DeleteAsync(int tagId, CancellationToken ct = default) { Deleted.Add(tagId); return Task.CompletedTask; }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly StubSuggestions _suggest = new();
    private readonly StubTags _tags = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly NoteEditorViewModel _editor;
    private readonly NotePanelViewModel _vm;

    public NotePanelViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        _editor = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
        _vm = new NotePanelViewModel(_repo, _tags, _suggest, _editor, _dialogs, _time);
    }

    public void Dispose() { _vm.Dispose(); _editor.Dispose(); }

    private async Task<Note> OpenAsync(Note note)
    {
        await _editor.LoadAsync(note);
        await _vm.RefreshAsync();
        return note;
    }

    [Fact]
    public async Task Refresh_splits_related_outgoing_and_backlinks()
    {
        var target = _repo.Add("Target");
        var related = _repo.Add("Related");
        var note = _repo.Add("Note", $"[[{target.Id}]] [[20990101000000]]", "memory");
        await _editor.LoadAsync(note);
        await _editor.UpdateAndSaveAsync(n => n.ManualLinks.Add(related.Id));
        _repo.Add("Fan", $"[[{note.Id}]]");

        await _vm.RefreshAsync();

        Assert.Equal(new[] { "memory" }, _vm.Tags);
        Assert.Equal(related.Id, Assert.Single(_vm.Related).NoteId);
        Assert.Equal(2, _vm.Outgoing.Count);
        Assert.Contains(_vm.Outgoing, l => l.IsBroken);
        Assert.Equal("Fan", Assert.Single(_vm.Backlinks).Title);
    }

    [Fact]
    public async Task Enter_with_similar_tags_shows_suggestions_instead_of_creating()
    {
        await OpenAsync(_repo.Add("n"));
        var existing = new Tag(1, "machine-learning");
        _suggest.Result = new TagSuggestions(null, [new TagSuggestion(existing, 0.9f, false)]);
        _vm.TagInput = "deep learning";

        await _vm.AddTagFromInputCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Tags);
        Assert.Single(_vm.Suggestions);
        Assert.True(_vm.CanCreateTag);

        await _vm.AcceptSuggestionCommand.ExecuteAsync(_vm.Suggestions[0]);
        Assert.Equal(new[] { "machine-learning" }, _vm.Tags);
        Assert.Equal("", _vm.TagInput);
        Assert.Empty(_vm.Suggestions);
    }

    [Fact]
    public async Task Enter_with_exact_match_reuses_existing_tag()
    {
        await OpenAsync(_repo.Add("n"));
        _suggest.Result = new TagSuggestions(new Tag(1, "machine-learning"), []);
        _vm.TagInput = "Machine Learning";
        await _vm.AddTagFromInputCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "machine-learning" }, _vm.Tags);
    }

    [Fact]
    public async Task Create_anyway_adds_normalized_input()
    {
        await OpenAsync(_repo.Add("n"));
        _vm.TagInput = "Neural Nets";
        await _vm.CreateTagAnywayCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "neural-nets" }, _vm.Tags);
        Assert.Equal("neural-nets", Assert.Single(_repo.Saved[^1].Tags));
    }

    [Fact]
    public async Task Typing_updates_suggestions_after_debounce()
    {
        await OpenAsync(_repo.Add("n"));
        _suggest.Result = new TagSuggestions(null, [new TagSuggestion(new Tag(1, "memory"), 0.8f, false)]);
        _vm.TagInput = "memroy";
        Assert.Empty(_vm.Suggestions);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        await _vm.PendingSuggest;
        Assert.Single(_vm.Suggestions);
    }

    [Fact]
    public async Task Manual_link_uses_picker_and_can_be_removed()
    {
        var other = _repo.Add("Other");
        await OpenAsync(_repo.Add("n"));
        _dialogs.PickNoteResult = other.Id;

        await _vm.AddManualLinkCommand.ExecuteAsync(null);
        var link = Assert.Single(_vm.Related);
        Assert.Equal(other.Id, link.NoteId);

        await _vm.RemoveManualLinkCommand.ExecuteAsync(link);
        Assert.Empty(_vm.Related);
    }

    [Fact]
    public async Task Delete_tag_everywhere_requires_confirmation()
    {
        _tags.All.Add(new Tag(7, "memory"));
        await OpenAsync(_repo.Add("n", "", "memory"));

        _dialogs.ConfirmResult = false;
        await _vm.DeleteTagEverywhereCommand.ExecuteAsync("memory");
        Assert.Empty(_tags.Deleted);

        _dialogs.ConfirmResult = true;
        await _vm.DeleteTagEverywhereCommand.ExecuteAsync("memory");
        Assert.Equal(new[] { 7 }, _tags.Deleted);
        Assert.Empty(_vm.Tags);
    }

    [Fact]
    public void Open_link_ignores_broken_links()
    {
        var opened = new List<string>();
        _vm.OpenRequested += (_, id) => opened.Add(id);
        _vm.OpenLinkCommand.Execute(new LinkInfo("20990101000000", null, LinkKind.Inline));
        _vm.OpenLinkCommand.Execute(new LinkInfo("20261102000001", "x", LinkKind.Inline));
        Assert.Equal(new[] { "20261102000001" }, opened);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NotePanelViewModelTests"` → Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Zettelkasten.App/ViewModels/NotePanelViewModel.cs`
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zettelkasten.App.Services;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;
using Zettelkasten.Core.Model;
using Zettelkasten.Core.Tags;

namespace Zettelkasten.App.ViewModels;

public sealed partial class NotePanelViewModel : ObservableObject, IDisposable
{
    private readonly INoteRepository _notes;
    private readonly ITagRepository _tags;
    private readonly ITagSuggestionService _suggestions;
    private readonly NoteEditorViewModel _editor;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _suggestDebounce;

    public NotePanelViewModel(INoteRepository notes, ITagRepository tags, ITagSuggestionService suggestions,
        NoteEditorViewModel editor, IDialogService dialogs, TimeProvider time)
    {
        _notes = notes;
        _tags = tags;
        _suggestions = suggestions;
        _editor = editor;
        _dialogs = dialogs;
        _suggestDebounce = new Debouncer(TimeSpan.FromMilliseconds(200), time);
    }

    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<LinkInfo> Related { get; } = [];
    public ObservableCollection<LinkInfo> Outgoing { get; } = [];
    public ObservableCollection<LinkInfo> Backlinks { get; } = [];
    public ObservableCollection<TagSuggestion> Suggestions { get; } = [];

    [ObservableProperty] private string _tagInput = "";
    [ObservableProperty] private bool _canCreateTag;

    internal Task PendingSuggest { get; private set; } = Task.CompletedTask;

    public event EventHandler<string>? OpenRequested;

    private string NormalizedInput => TagNormalizer.Normalize(TagInput);

    public async Task RefreshAsync()
    {
        var note = _editor.CurrentNote;
        Replace(Tags, note?.Tags ?? []);
        if (note is null)
        {
            Related.Clear(); Outgoing.Clear(); Backlinks.Clear();
            return;
        }
        var outgoing = await _notes.GetOutgoingLinksAsync(note.Id);
        var backlinks = await _notes.GetBacklinksAsync(note.Id);
        Replace(Related, outgoing.Where(l => l.Kind == LinkKind.Manual));
        Replace(Outgoing, outgoing.Where(l => l.Kind == LinkKind.Inline));
        Replace(Backlinks, backlinks);
    }

    partial void OnTagInputChanged(string value)
    {
        CanCreateTag = false;
        PendingSuggest = _suggestDebounce.Trigger(async () => ShowSuggestions(await _suggestions.SuggestAsync(TagInput)));
    }

    [RelayCommand]
    private async Task AddTagFromInput()
    {
        var name = NormalizedInput;
        if (name.Length == 0) return;
        var result = await _suggestions.SuggestAsync(TagInput);
        if (result.ExactMatch is not null) { await AddTagAsync(result.ExactMatch.Name); return; }
        if (result.Similar.Count > 0) { ShowSuggestions(result); return; } // let the user decide
        await AddTagAsync(name);
    }

    [RelayCommand]
    private Task AcceptSuggestion(TagSuggestion suggestion) => AddTagAsync(suggestion.Tag.Name);

    [RelayCommand]
    private Task CreateTagAnyway() => AddTagAsync(NormalizedInput);

    [RelayCommand]
    private async Task RemoveTag(string name)
    {
        await _editor.UpdateAndSaveAsync(n => n.Tags.Remove(name));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteTagEverywhere(string name)
    {
        if (!_dialogs.Confirm("Delete tag", $"Remove the tag \"{name}\" from all notes and delete it?\n\nThis cannot be undone."))
            return;
        var tag = (await _tags.GetAllAsync()).FirstOrDefault(t => t.Name == name);
        if (tag is null) return;
        await _editor.FlushAsync();
        await _tags.DeleteAsync(tag.Id);
        _editor.CurrentNote?.Tags.Remove(name);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddManualLink()
    {
        var note = _editor.CurrentNote;
        if (note is null) return;
        var id = _dialogs.PickNote("Link to note", note.Id);
        if (id is null || id == note.Id) return;
        await _editor.UpdateAndSaveAsync(n => { if (!n.ManualLinks.Contains(id)) n.ManualLinks.Add(id); });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveManualLink(LinkInfo link)
    {
        await _editor.UpdateAndSaveAsync(n => n.ManualLinks.Remove(link.NoteId));
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenLink(LinkInfo link)
    {
        if (!link.IsBroken) OpenRequested?.Invoke(this, link.NoteId);
    }

    private async Task AddTagAsync(string name)
    {
        if (name.Length == 0 || _editor.CurrentNote is null) return;
        await _editor.UpdateAndSaveAsync(n => { if (!n.Tags.Contains(name)) n.Tags.Add(name); });
        TagInput = "";
        _suggestDebounce.Cancel();
        Suggestions.Clear();
        CanCreateTag = false;
        await RefreshAsync();
    }

    private void ShowSuggestions(TagSuggestions result)
    {
        Suggestions.Clear();
        if (result.ExactMatch is not null)
            Suggestions.Add(new TagSuggestion(result.ExactMatch, 1f, false));
        else
            foreach (var s in result.Similar) Suggestions.Add(s);
        var name = NormalizedInput;
        CanCreateTag = result.ExactMatch is null && name.Length > 0 && !Tags.Contains(name);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public void Dispose() => _suggestDebounce.Dispose();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NotePanelViewModelTests"` → Expected: all PASS.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(app): side panel view model for tags, manual links and backlinks" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 19: App – main view model (navigation, new/delete note, backup, status)

**Files:**
- Create: `src/Zettelkasten.App/ViewModels/MainViewModel.cs`
- Test: `tests/Zettelkasten.Tests/Support/FakeSearchService.cs`, `tests/Zettelkasten.Tests/App/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteRepository`, `IEmbeddingService`, `IBackupService`, `ISearchService` (Task 6); `NavigationHistory` (Task 5); `SearchViewModel` (Task 17); `NoteEditorViewModel` (Task 16); `NotePanelViewModel`, `IDialogService`, `FakeDialogService` (Task 18); `FakeNoteRepository` (Task 16); `FakeEmbeddingService` (Task 8)
- Produces: `sealed partial class MainViewModel(INoteRepository, IEmbeddingService, IBackupService, IDialogService, SearchViewModel, NoteEditorViewModel, NotePanelViewModel, TimeProvider, ILogger<MainViewModel>) : ObservableObject` with `Search`, `Editor`, `Panel`; observable `NoteCount`, `ModelStatus`, `StatusMessage`; `Task InitializeAsync()`, `Task OpenAsync(string id)`, `Task ShutdownAsync()`, `void OnModelLoadCompleted()`; commands `NewNoteCommand`, `BackCommand`, `ForwardCommand`, `DeleteNoteCommand`, `ExportBackupCommand`, `TogglePreviewCommand`; event `FocusTitleRequested`

- [ ] **Step 1: Write the fake search service (test support)**

`tests/Zettelkasten.Tests/Support/FakeSearchService.cs`
```csharp
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.Tests.Support;

/// <summary>Returns the repository's most recent notes for any query.</summary>
public sealed class FakeSearchService(INoteRepository notes) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct = default) =>
        (await notes.GetRecentAsync(50, ct)).Select(n => new SearchResult(n, false, false)).ToList();
}
```

- [ ] **Step 2: Write failing tests**

`tests/Zettelkasten.Tests/App/MainViewModelTests.cs`
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public sealed class MainViewModelTests
{
    private sealed class StubBackup : IBackupService
    {
        public List<string> Exports { get; } = [];
        public Task ExportAsync(string destinationPath, CancellationToken ct = default)
        {
            Exports.Add(destinationPath);
            return Task.CompletedTask;
        }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly FakeNoteRepository _repo;
    private readonly FakeDialogService _dialogs = new();
    private readonly StubBackup _backup = new();
    private readonly FakeEmbeddingService _embeddings = new() { IsAvailable = false };
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _repo = new FakeNoteRepository(_time);
        var editor = new NoteEditorViewModel(_repo, _time, NullLogger<NoteEditorViewModel>.Instance);
        var search = new SearchViewModel(new FakeSearchService(_repo), _time, NullLogger<SearchViewModel>.Instance);
        var panel = new NotePanelViewModel(_repo, new TagRepositoryStub(), new SuggestionStub(), editor, _dialogs, _time);
        _vm = new MainViewModel(_repo, _embeddings, _backup, _dialogs, search, editor, panel, _time,
            NullLogger<MainViewModel>.Instance);
    }

    private sealed class TagRepositoryStub : ITagRepository
    {
        public Task<IReadOnlyList<Zettelkasten.Core.Model.Tag>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Zettelkasten.Core.Model.Tag>>([]);
        public Task<IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding>> GetAllWithEmbeddingsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Zettelkasten.Core.Model.TagWithEmbedding>>([]);
        public Task DeleteAsync(int tagId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SuggestionStub : ITagSuggestionService
    {
        public Task<Zettelkasten.Core.Model.TagSuggestions> SuggestAsync(string input, CancellationToken ct = default) =>
            Task.FromResult(new Zettelkasten.Core.Model.TagSuggestions(null, []));
    }

    [Fact]
    public async Task Initialize_opens_most_recent_note_and_counts()
    {
        _repo.Add("old");
        var recent = _repo.Add("recent");
        await _vm.InitializeAsync();
        Assert.Equal(recent.Id, _vm.Editor.NoteId);
        Assert.Equal(2, _vm.NoteCount);
        Assert.Equal(recent.Id, _vm.Search.Selected?.Note.Id);
    }

    [Fact]
    public async Task New_note_is_created_opened_and_back_returns()
    {
        var first = _repo.Add("first");
        await _vm.InitializeAsync();
        var focused = false;
        _vm.FocusTitleRequested += (_, _) => focused = true;

        await _vm.NewNoteCommand.ExecuteAsync(null);

        Assert.NotEqual(first.Id, _vm.Editor.NoteId);
        Assert.True(focused);
        Assert.Equal(2, _vm.NoteCount);
        Assert.True(_vm.BackCommand.CanExecute(null));

        await _vm.BackCommand.ExecuteAsync(null);
        Assert.Equal(first.Id, _vm.Editor.NoteId);
        Assert.True(_vm.ForwardCommand.CanExecute(null));
    }

    [Fact]
    public async Task Opening_missing_note_keeps_current_and_reports()
    {
        var a = _repo.Add("a");
        await _vm.InitializeAsync();
        await _vm.OpenAsync("20990101000000");
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        Assert.Contains("20990101000000", _vm.StatusMessage);
    }

    [Fact]
    public async Task Panel_and_editor_link_requests_navigate()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        await _vm.InitializeAsync();          // opens b
        _vm.Editor.RequestOpen(a.Id);
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        _vm.Panel.OpenLinkCommand.Execute(new Zettelkasten.Core.Model.LinkInfo(b.Id, "b", Zettelkasten.Core.Model.LinkKind.Inline));
        Assert.Equal(b.Id, _vm.Editor.NoteId);
    }

    [Fact]
    public async Task Delete_lists_backlinks_and_falls_back_to_previous_note()
    {
        var a = _repo.Add("a");
        var b = _repo.Add("b");
        _repo.Add("linker", $"[[{b.Id}]]");
        await _vm.InitializeAsync();          // opens linker (most recent)
        await _vm.OpenAsync(a.Id);
        await _vm.OpenAsync(b.Id);

        await _vm.DeleteNoteCommand.ExecuteAsync(null);

        Assert.Contains("linker", Assert.Single(_dialogs.Confirmations));
        Assert.Null(await _repo.GetAsync(b.Id));
        Assert.Equal(a.Id, _vm.Editor.NoteId);
        Assert.Equal(2, _vm.NoteCount);
    }

    [Fact]
    public async Task Delete_cancelled_keeps_note()
    {
        var a = _repo.Add("a");
        await _vm.InitializeAsync();
        _dialogs.ConfirmResult = false;
        await _vm.DeleteNoteCommand.ExecuteAsync(null);
        Assert.NotNull(await _repo.GetAsync(a.Id));
        Assert.Equal(a.Id, _vm.Editor.NoteId);
    }

    [Fact]
    public async Task Export_backup_uses_chosen_path_and_cancel_does_nothing()
    {
        await _vm.ExportBackupCommand.ExecuteAsync(null);
        Assert.Empty(_backup.Exports);

        _dialogs.SaveFileResult = @"C:\backups\zk.db";
        await _vm.ExportBackupCommand.ExecuteAsync(null);
        Assert.Equal(new[] { @"C:\backups\zk.db" }, _backup.Exports);
        Assert.Contains(@"C:\backups\zk.db", _vm.StatusMessage);
    }

    [Fact]
    public void Model_status_reflects_availability()
    {
        Assert.Contains("loading", _vm.ModelStatus);
        _vm.OnModelLoadCompleted();
        Assert.Contains("unavailable", _vm.ModelStatus);
        _embeddings.IsAvailable = true;
        Assert.Contains("ready", _vm.ModelStatus);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"` → Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Zettelkasten.App/ViewModels/MainViewModel.cs`
```csharp
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zettelkasten.App.Services;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Common;

namespace Zettelkasten.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly INoteRepository _notes;
    private readonly IEmbeddingService _embeddings;
    private readonly IBackupService _backup;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly ILogger<MainViewModel> _logger;
    private readonly NavigationHistory _history = new();
    private bool _modelLoadCompleted;

    public MainViewModel(INoteRepository notes, IEmbeddingService embeddings, IBackupService backup,
        IDialogService dialogs, SearchViewModel search, NoteEditorViewModel editor, NotePanelViewModel panel,
        TimeProvider time, ILogger<MainViewModel> logger)
    {
        _notes = notes;
        _embeddings = embeddings;
        _backup = backup;
        _dialogs = dialogs;
        _time = time;
        _logger = logger;
        Search = search;
        Editor = editor;
        Panel = panel;

        Search.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Panel.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Editor.OpenRequested += (_, id) => Run(() => OpenAsync(id));
        Editor.Saved += (_, _) => Run(OnSavedAsync);
        _embeddings.AvailabilityChanged += (_, _) => UpdateModelStatus();
        UpdateModelStatus();
    }

    public SearchViewModel Search { get; }
    public NoteEditorViewModel Editor { get; }
    public NotePanelViewModel Panel { get; }

    [ObservableProperty] private int _noteCount;
    [ObservableProperty] private string _modelStatus = "";
    [ObservableProperty] private string? _statusMessage;

    public event EventHandler? FocusTitleRequested;

    public async Task InitializeAsync()
    {
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();
        if (Search.Results.FirstOrDefault() is { } first)
            await OpenAsync(first.Note.Id);
    }

    public Task OpenAsync(string id) => OpenCoreAsync(id, recordHistory: true);

    public Task ShutdownAsync() => Editor.FlushAsync();

    public void OnModelLoadCompleted()
    {
        _modelLoadCompleted = true;
        UpdateModelStatus();
    }

    [RelayCommand]
    private async Task NewNote()
    {
        await Editor.FlushAsync();
        var note = await _notes.CreateAsync();
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();
        await OpenAsync(note.Id);
        FocusTitleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task Back()
    {
        if (_history.GoBack() is { } id) await OpenCoreAsync(id, recordHistory: false);
        NotifyNavigation();
    }

    private bool CanGoBack() => _history.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private async Task Forward()
    {
        if (_history.GoForward() is { } id) await OpenCoreAsync(id, recordHistory: false);
        NotifyNavigation();
    }

    private bool CanGoForward() => _history.CanGoForward;

    [RelayCommand]
    private async Task DeleteNote()
    {
        var note = Editor.CurrentNote;
        if (note is null) return;

        var backlinks = await _notes.GetBacklinksAsync(note.Id);
        var title = string.IsNullOrWhiteSpace(Editor.Title) ? "(untitled)" : Editor.Title;
        var message = $"Delete \"{title}\" ({note.Id})?";
        if (backlinks.Count > 0)
            message += "\n\nThese notes link to it and will show a broken link:\n" +
                       string.Join("\n", backlinks.Select(b => $"• {b.DisplayTitle}").Distinct());
        if (!_dialogs.Confirm("Delete note", message)) return;

        await Editor.LoadAsync(null); // flushes pending edits first
        await _notes.DeleteAsync(note.Id);
        var previous = _history.Remove(note.Id);
        NoteCount = await _notes.CountAsync();
        await Search.RefreshAsync();

        if (previous is not null)
            await OpenCoreAsync(previous, recordHistory: false);
        else if (Search.Results.FirstOrDefault() is { } first)
            await OpenCoreAsync(first.Note.Id, recordHistory: true);
        else
            await Panel.RefreshAsync();
        NotifyNavigation();
    }

    [RelayCommand]
    private async Task ExportBackup()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var path = _dialogs.PickSaveFile("Export backup", $"zettelkasten-backup-{stamp}.db", "SQLite database (*.db)|*.db");
        if (path is null) return;
        try
        {
            await Editor.FlushAsync();
            await _backup.ExportAsync(path);
            StatusMessage = $"Backup saved to {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup export to {Path} failed", path);
            _dialogs.ShowError("Backup failed", ex.Message);
        }
    }

    [RelayCommand]
    private void TogglePreview() => Editor.IsPreview = !Editor.IsPreview;

    private async Task OpenCoreAsync(string id, bool recordHistory)
    {
        if (Editor.NoteId == id) return;
        var note = await _notes.GetAsync(id);
        if (note is null)
        {
            StatusMessage = $"Note {id} does not exist.";
            return;
        }
        await Editor.LoadAsync(note);
        if (recordHistory) _history.Visit(id);
        Search.SelectById(id);
        StatusMessage = null;
        await Panel.RefreshAsync();
        NotifyNavigation();
    }

    private async Task OnSavedAsync()
    {
        await Panel.RefreshAsync();
        await Search.RefreshAsync();
    }

    private void NotifyNavigation()
    {
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    private void UpdateModelStatus() =>
        ModelStatus = _embeddings.IsAvailable ? "semantic search ready"
            : _modelLoadCompleted ? "semantic search unavailable"
            : "loading semantic model…";

    /// <summary>Runs event-triggered work; failures are logged and shown instead of crashing.</summary>
    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed");
            _dialogs.ShowError("Something went wrong", ex.Message);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"` → Expected: all PASS. (The fakes complete synchronously, so event-driven navigation like `Editor.RequestOpen` finishes before the assertion.)

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(app): main view model with navigation history, delete, backup and status" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 20: App – shell, composition root, theme, dialogs and views

**Files:**
- Create: `src/Zettelkasten.App/AppServices.cs`, `Themes/Theme.xaml`, `Converters/InverseBooleanToVisibilityConverter.cs`, `Services/DialogService.cs`, `Views/MainWindow.xaml(.cs)`, `Views/NoteEditorView.xaml(.cs)`, `Views/NotePanelView.xaml(.cs)`, `Views/NotePickerWindow.xaml(.cs)`
- Modify: `src/Zettelkasten.App/App.xaml`, `src/Zettelkasten.App/App.xaml.cs`
- Delete: `src/Zettelkasten.App/MainWindow.xaml`, `src/Zettelkasten.App/MainWindow.xaml.cs` (template versions)
- Test: `tests/Zettelkasten.Tests/App/AppServicesTests.cs`

**Interfaces:**
- Consumes: all view models (Tasks 16–19); `AddZettelkastenInfrastructure`, `DbInitializer`, `EmbeddingBackfillService`, `AppPaths` (Tasks 7, 12, 15); `ISearchService`, `IEmbeddingService` (Task 6)
- Produces: `static IServiceCollection AppServices.AddZettelkastenApp(this IServiceCollection, AppPaths)`; `DialogService : IDialogService`; `MainWindow(MainViewModel)`; `NoteEditorView` with `TextEditor Editor`, `FlowDocumentScrollViewer Preview`, `void FocusTitle()`; `NotePickerWindow(ISearchService, string? excludeId)` with `string? SelectedId`

- [ ] **Step 1: Add packages, delete template window**

```powershell
dotnet add src/Zettelkasten.App package Serilog.Extensions.Hosting --version 9.0.0
dotnet add src/Zettelkasten.App package Serilog.Sinks.File --version 6.0.0
dotnet add src/Zettelkasten.App package AvalonEdit --version 6.3.0.90
Remove-Item src/Zettelkasten.App/MainWindow.xaml, src/Zettelkasten.App/MainWindow.xaml.cs
```

Add to `src/Zettelkasten.App/Zettelkasten.App.csproj` so the model ships next to the exe (build and publish):

```xml
  <ItemGroup>
    <None Include="..\..\models\**" Link="models\%(RecursiveDir)%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing DI test**

`tests/Zettelkasten.Tests/App/AppServicesTests.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.App;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Infrastructure;

namespace Zettelkasten.Tests.App;

public class AppServicesTests
{
    [Fact]
    public void Main_view_model_resolves_with_shared_children()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddZettelkastenApp(new AppPaths(Path.GetTempPath(), Path.GetTempPath()));
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var main = sp.GetRequiredService<MainViewModel>();
        Assert.Same(main.Editor, sp.GetRequiredService<NoteEditorViewModel>());
        Assert.Same(main.Panel, sp.GetRequiredService<NotePanelViewModel>());
        Assert.Same(main.Search, sp.GetRequiredService<SearchViewModel>());
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~AppServicesTests"` → Expected: build FAILS.

- [ ] **Step 3: Composition root, converter, theme**

`src/Zettelkasten.App/AppServices.cs`
```csharp
using Microsoft.Extensions.DependencyInjection;
using Zettelkasten.App.Services;
using Zettelkasten.App.ViewModels;
using Zettelkasten.App.Views;
using Zettelkasten.Infrastructure;

namespace Zettelkasten.App;

public static class AppServices
{
    public static IServiceCollection AddZettelkastenApp(this IServiceCollection services, AppPaths paths)
    {
        services.AddZettelkastenInfrastructure(paths);
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<NoteEditorViewModel>();
        services.AddSingleton<NotePanelViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
```

`src/Zettelkasten.App/Converters/InverseBooleanToVisibilityConverter.cs`
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zettelkasten.App.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

`src/Zettelkasten.App/Themes/Theme.xaml`
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- All colors live here so a dark theme can be added later -->
    <SolidColorBrush x:Key="WindowBrush" Color="#FAFAF7" />
    <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="LineBrush" Color="#E3E1DA" />
    <SolidColorBrush x:Key="TextBrush" Color="#1F2328" />
    <SolidColorBrush x:Key="MutedBrush" Color="#6B6F76" />
    <SolidColorBrush x:Key="AccentBrush" Color="#2F6FDF" />
    <SolidColorBrush x:Key="AccentSoftBrush" Color="#E8F0FD" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#C62828" />
    <SolidColorBrush x:Key="CodeBackgroundBrush" Color="#F3F2EE" />

    <Style x:Key="SectionHeader" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        <Setter Property="Margin" Value="0,16,0,6" />
    </Style>

    <Style x:Key="InputBox" TargetType="TextBox">
        <Setter Property="Padding" Value="8,5" />
        <Setter Property="BorderBrush" Value="{StaticResource LineBrush}" />
        <Setter Property="Background" Value="{StaticResource SurfaceBrush}" />
    </Style>

    <Style x:Key="LinkButton" TargetType="Button">
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <ContentPresenter Margin="0,2" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="IconButton" TargetType="Button">
        <Setter Property="Foreground" Value="{StaticResource MutedBrush}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Padding" Value="6,2" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.35" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key="Chip" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource AccentSoftBrush}" />
        <Setter Property="CornerRadius" Value="10" />
        <Setter Property="Padding" Value="8,1,2,1" />
        <Setter Property="Margin" Value="0,0,4,4" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: App startup, logging and global exception handling**

`src/Zettelkasten.App/App.xaml` (replace contents)
```xml
<Application x:Class="Zettelkasten.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:Zettelkasten.App.Converters"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Theme.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
            <conv:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVisibility" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/Zettelkasten.App/App.xaml.cs` (replace contents)
```csharp
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Zettelkasten.App.ViewModels;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Infrastructure;
using Zettelkasten.Infrastructure.Data;
using Zettelkasten.Infrastructure.Embeddings;

namespace Zettelkasten.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.Default();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogDirectory, "zettelkasten-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        RegisterGlobalExceptionHandlers();

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSerilog();
            builder.Services.AddZettelkastenApp(paths);
            _host = builder.Build();
            var services = _host.Services;

            await services.GetRequiredService<DbInitializer>().InitializeAsync();
            var backfill = services.GetRequiredService<EmbeddingBackfillService>();
            await backfill.LoadIndexAsync();

            var vm = services.GetRequiredService<MainViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await vm.InitializeAsync();

            // Load the model in the background; the app works keyword-only until it's ready.
            _ = Task.Run(async () =>
            {
                try
                {
                    await services.GetRequiredService<IEmbeddingService>().InitializeAsync();
                    await backfill.BackfillAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background model initialization failed");
                }
                finally
                {
                    Dispatcher.Invoke(vm.OnModelLoadCompleted);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"Zettelkasten could not start:\n\n{ex.Message}\n\nDetails are in {paths.LogDirectory}",
                "Zettelkasten", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}\n\nThe app keeps running. Details are in the log.",
                "Zettelkasten", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Fatal unhandled exception");
    }
}
```

- [ ] **Step 5: Dialogs and the note picker**

`src/Zettelkasten.App/Services/DialogService.cs`
```csharp
using System.Windows;
using Microsoft.Win32;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Abstractions;

namespace Zettelkasten.App.Services;

public sealed class DialogService(ISearchService search) : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? PickNote(string title, string? excludeId)
    {
        var picker = new NotePickerWindow(search, excludeId) { Title = title, Owner = Owner };
        return picker.ShowDialog() == true ? picker.SelectedId : null;
    }

    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = defaultFileName, Filter = filter, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
```

`src/Zettelkasten.App/Views/NotePickerWindow.xaml`
```xml
<Window x:Class="Zettelkasten.App.Views.NotePickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="480" Height="420" WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{StaticResource SurfaceBrush}" FontFamily="Segoe UI" FontSize="13">
    <DockPanel Margin="12">
        <TextBox x:Name="QueryBox" DockPanel.Dock="Top" Style="{StaticResource InputBox}"
                 TextChanged="QueryBox_TextChanged" PreviewKeyDown="QueryBox_PreviewKeyDown" />
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button Content="Link" IsDefault="True" Click="Ok_Click" Padding="16,4" Margin="0,0,8,0" />
            <Button Content="Cancel" IsCancel="True" Padding="16,4" />
        </StackPanel>
        <ListBox x:Name="ResultsList" Margin="0,8,0,0" MouseDoubleClick="Ok_Click" HorizontalContentAlignment="Stretch">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <DockPanel Margin="2,3">
                        <TextBlock DockPanel.Dock="Right" Text="{Binding Note.Id}" Foreground="{StaticResource MutedBrush}" FontSize="11" />
                        <TextBlock Text="{Binding Note.DisplayTitle}" TextTrimming="CharacterEllipsis" />
                    </DockPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </DockPanel>
</Window>
```

`src/Zettelkasten.App/Views/NotePickerWindow.xaml.cs`
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zettelkasten.Core.Abstractions;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.Views;

public partial class NotePickerWindow : Window
{
    private readonly ISearchService _search;
    private readonly string? _excludeId;
    private int _version;

    public NotePickerWindow(ISearchService search, string? excludeId)
    {
        _search = search;
        _excludeId = excludeId;
        InitializeComponent();
        Loaded += async (_, _) => { QueryBox.Focus(); await RefreshAsync(); };
    }

    public string? SelectedId { get; private set; }

    private async void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++_version;
        var results = await _search.SearchAsync(QueryBox.Text);
        if (version != _version) return;
        ResultsList.ItemsSource = results.Where(r => r.Note.Id != _excludeId).ToList();
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var delta = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (delta == 0 || ResultsList.Items.Count == 0) return;
        ResultsList.SelectedIndex = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        e.Handled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        SelectedId = result.Note.Id;
        DialogResult = true;
    }
}
```

- [ ] **Step 6: Editor view (plain editing; highlighting, autocomplete and preview come in Task 21)**

`src/Zettelkasten.App/Views/NoteEditorView.xaml`
```xml
<UserControl x:Class="Zettelkasten.App.Views.NoteEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ae="http://icsharpcode.net/sharpdevelop/avalonedit"
             Background="{StaticResource SurfaceBrush}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <DockPanel Grid.Row="0" Margin="20,14,20,0" Visibility="{Binding HasNote, Converter={StaticResource BoolToVisibility}}">
            <ToggleButton DockPanel.Dock="Right" IsChecked="{Binding IsPreview}" Content="Preview" Padding="10,2"
                          ToolTip="Switch between editing and preview (Ctrl+E)" />
            <TextBlock Text="{Binding NoteId}" Foreground="{StaticResource MutedBrush}" FontFamily="Consolas"
                       VerticalAlignment="Center" />
        </DockPanel>

        <TextBox x:Name="TitleBox" Grid.Row="1" Margin="20,8,20,8" Padding="0,4"
                 Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}"
                 FontSize="22" FontWeight="SemiBold" BorderThickness="0,0,0,1"
                 BorderBrush="{StaticResource LineBrush}"
                 Visibility="{Binding HasNote, Converter={StaticResource BoolToVisibility}}" />

        <ae:TextEditor x:Name="Editor" Grid.Row="2" Padding="20,4" WordWrap="True"
                       FontFamily="Cascadia Mono, Consolas" FontSize="14"
                       Foreground="{StaticResource TextBrush}" IsEnabled="{Binding HasNote}"
                       Visibility="{Binding IsPreview, Converter={StaticResource InverseBoolToVisibility}}" />

        <FlowDocumentScrollViewer x:Name="Preview" Grid.Row="2" Margin="4,0"
                                  VerticalScrollBarVisibility="Auto"
                                  Visibility="{Binding IsPreview, Converter={StaticResource BoolToVisibility}}" />

        <TextBlock Grid.Row="0" Grid.RowSpan="3" HorizontalAlignment="Center" VerticalAlignment="Center"
                   Foreground="{StaticResource MutedBrush}" FontSize="14"
                   Text="No note open – press Ctrl+N to create one."
                   Visibility="{Binding HasNote, Converter={StaticResource InverseBoolToVisibility}}" />
    </Grid>
</UserControl>
```

`src/Zettelkasten.App/Views/NoteEditorView.xaml.cs`
```csharp
using System.Windows;
using System.Windows.Controls;
using Zettelkasten.App.ViewModels;

namespace Zettelkasten.App.Views;

public partial class NoteEditorView : UserControl
{
    private NoteEditorViewModel? _vm;
    private bool _syncing;

    public NoteEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Editor.TextChanged += (_, _) =>
        {
            if (!_syncing && _vm is not null) _vm.Content = Editor.Text;
        };
    }

    public void FocusTitle()
    {
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.NoteLoaded -= OnNoteLoaded;
        _vm = e.NewValue as NoteEditorViewModel;
        if (_vm is not null) _vm.NoteLoaded += OnNoteLoaded;
        OnNoteLoaded(this, EventArgs.Empty);
    }

    private void OnNoteLoaded(object? sender, EventArgs e)
    {
        _syncing = true;
        try
        {
            Editor.Text = _vm?.Content ?? "";
            Editor.Document.UndoStack.ClearAll(); // Ctrl+Z must not bring back the previous note
            Editor.ScrollToHome();
        }
        finally
        {
            _syncing = false;
        }
    }
}
```

- [ ] **Step 7: Side panel view**

`src/Zettelkasten.App/Views/NotePanelView.xaml`
```xml
<UserControl x:Class="Zettelkasten.App.Views.NotePanelView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:model="clr-namespace:Zettelkasten.Core.Model;assembly=Zettelkasten.Core"
             Background="{StaticResource WindowBrush}">
    <UserControl.Resources>
        <!-- One template for every link list; broken links are red with a warning sign -->
        <DataTemplate x:Key="LinkTemplate" DataType="{x:Type model:LinkInfo}">
            <Button Style="{StaticResource LinkButton}" ToolTip="{Binding NoteId}"
                    Command="{Binding DataContext.OpenLinkCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                    CommandParameter="{Binding}">
                <StackPanel Orientation="Horizontal">
                    <TextBlock x:Name="Warn" Text="⚠ " Visibility="Collapsed" />
                    <TextBlock x:Name="Label" Text="{Binding DisplayTitle}" TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="Kind" Text="  · related" Foreground="{StaticResource MutedBrush}" Visibility="Collapsed" />
                </StackPanel>
            </Button>
            <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding IsBroken}" Value="True">
                    <Setter TargetName="Warn" Property="Visibility" Value="Visible" />
                    <Setter TargetName="Warn" Property="Foreground" Value="{StaticResource ErrorBrush}" />
                    <Setter TargetName="Label" Property="Foreground" Value="{StaticResource ErrorBrush}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Kind}" Value="Manual">
                    <Setter TargetName="Kind" Property="Visibility" Value="Visible" />
                </DataTrigger>
            </DataTemplate.Triggers>
        </DataTemplate>
    </UserControl.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="14,0,14,14">
            <TextBlock Text="TAGS" Style="{StaticResource SectionHeader}" />
            <ItemsControl ItemsSource="{Binding Tags}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource Chip}"
                                Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=ItemsControl}}">
                            <Border.ContextMenu>
                                <ContextMenu>
                                    <MenuItem Header="Delete tag everywhere…"
                                              Command="{Binding PlacementTarget.Tag.DeleteTagEverywhereCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                              CommandParameter="{Binding}" />
                                </ContextMenu>
                            </Border.ContextMenu>
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding StringFormat='#{0}'}" VerticalAlignment="Center" />
                                <Button Content="✕" Style="{StaticResource IconButton}" ToolTip="Remove from this note"
                                        Command="{Binding DataContext.RemoveTagCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                        CommandParameter="{Binding}" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBox Style="{StaticResource InputBox}" Margin="0,4,0,0"
                     Text="{Binding TagInput, UpdateSourceTrigger=PropertyChanged}"
                     ToolTip="Type a tag and press Enter">
                <TextBox.InputBindings>
                    <KeyBinding Key="Enter" Command="{Binding AddTagFromInputCommand}" />
                </TextBox.InputBindings>
            </TextBox>

            <TextBlock Text="Similar existing tags:" Foreground="{StaticResource MutedBrush}" Margin="0,6,0,2"
                       Visibility="{Binding HasItems, ElementName=SuggestionList, Converter={StaticResource BoolToVisibility}}" />
            <ItemsControl x:Name="SuggestionList" ItemsSource="{Binding Suggestions}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button Style="{StaticResource LinkButton}"
                                Command="{Binding DataContext.AcceptSuggestionCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                CommandParameter="{Binding}">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding Tag.Name, StringFormat='#{0}'}" FontWeight="SemiBold" />
                                <TextBlock x:Name="Score" Text="{Binding Score, StringFormat='  ({0:0.00})'}" Foreground="{StaticResource MutedBrush}" />
                                <TextBlock x:Name="Abbrev" Text="  (abbreviation)" Foreground="{StaticResource MutedBrush}" Visibility="Collapsed" />
                            </StackPanel>
                        </Button>
                        <DataTemplate.Triggers>
                            <DataTrigger Binding="{Binding IsInitialsMatch}" Value="True">
                                <Setter TargetName="Score" Property="Visibility" Value="Collapsed" />
                                <Setter TargetName="Abbrev" Property="Visibility" Value="Visible" />
                            </DataTrigger>
                        </DataTemplate.Triggers>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <Button Style="{StaticResource LinkButton}" Command="{Binding CreateTagAnywayCommand}"
                    Visibility="{Binding CanCreateTag, Converter={StaticResource BoolToVisibility}}">
                <TextBlock>
                    <Run Text="Create '" /><Run Text="{Binding TagInput, Mode=OneWay}" /><Run Text="' anyway" />
                </TextBlock>
            </Button>

            <DockPanel Margin="0,16,0,6">
                <Button DockPanel.Dock="Right" Content="+ link note" Style="{StaticResource LinkButton}"
                        Command="{Binding AddManualLinkCommand}" ToolTip="Add a related note (Ctrl+L)" />
                <TextBlock Text="RELATED" Style="{StaticResource SectionHeader}" Margin="0" />
            </DockPanel>
            <ItemsControl ItemsSource="{Binding Related}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel>
                            <Button DockPanel.Dock="Right" Content="✕" Style="{StaticResource IconButton}" ToolTip="Remove link"
                                    Command="{Binding DataContext.RemoveManualLinkCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                    CommandParameter="{Binding}" />
                            <ContentControl Content="{Binding}" ContentTemplate="{StaticResource LinkTemplate}" />
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="OUTGOING" Style="{StaticResource SectionHeader}" />
            <ItemsControl ItemsSource="{Binding Outgoing}" ItemTemplate="{StaticResource LinkTemplate}" />

            <TextBlock Text="BACKLINKS" Style="{StaticResource SectionHeader}" />
            <ItemsControl ItemsSource="{Binding Backlinks}" ItemTemplate="{StaticResource LinkTemplate}" />
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`src/Zettelkasten.App/Views/NotePanelView.xaml.cs`
```csharp
using System.Windows.Controls;

namespace Zettelkasten.App.Views;

public partial class NotePanelView : UserControl
{
    public NotePanelView() => InitializeComponent();
}
```

- [ ] **Step 8: Main window**

`src/Zettelkasten.App/Views/MainWindow.xaml`
```xml
<Window x:Class="Zettelkasten.App.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:Zettelkasten.App.Views"
        Title="Zettelkasten" Height="820" Width="1320" MinWidth="900" MinHeight="500"
        Background="{StaticResource WindowBrush}" Foreground="{StaticResource TextBrush}"
        FontFamily="Segoe UI" FontSize="13">
    <Window.InputBindings>
        <KeyBinding Key="N" Modifiers="Control" Command="{Binding NewNoteCommand}" />
        <KeyBinding Key="S" Modifiers="Control" Command="{Binding Editor.SaveCommand}" />
        <KeyBinding Key="E" Modifiers="Control" Command="{Binding TogglePreviewCommand}" />
        <KeyBinding Key="L" Modifiers="Control" Command="{Binding Panel.AddManualLinkCommand}" />
        <KeyBinding Key="Left" Modifiers="Alt" Command="{Binding BackCommand}" />
        <KeyBinding Key="Right" Modifiers="Alt" Command="{Binding ForwardCommand}" />
    </Window.InputBindings>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource LineBrush}"
                BorderThickness="0,0,0,1" Padding="10,8">
            <DockPanel>
                <Button DockPanel.Dock="Left" Content="←" FontSize="16" Style="{StaticResource IconButton}"
                        Command="{Binding BackCommand}" ToolTip="Back (Alt+←)" />
                <Button DockPanel.Dock="Left" Content="→" FontSize="16" Style="{StaticResource IconButton}"
                        Command="{Binding ForwardCommand}" ToolTip="Forward (Alt+→)" Margin="0,0,8,0" />
                <Menu DockPanel.Dock="Right" Background="Transparent" VerticalAlignment="Center" Margin="6,0,0,0">
                    <MenuItem Header="⋯" FontSize="16" ToolTip="More">
                        <MenuItem Header="Delete note…" Command="{Binding DeleteNoteCommand}" />
                        <MenuItem Header="Export backup…" Command="{Binding ExportBackupCommand}" />
                    </MenuItem>
                </Menu>
                <Button DockPanel.Dock="Right" Content="+ New" Padding="14,4" Margin="8,0,0,0"
                        Command="{Binding NewNoteCommand}" ToolTip="New note (Ctrl+N)" />
                <TextBox x:Name="SearchBox" Style="{StaticResource InputBox}" FontSize="14"
                         Text="{Binding Search.Query, UpdateSourceTrigger=PropertyChanged}"
                         PreviewKeyDown="SearchBox_PreviewKeyDown"
                         ToolTip="Search notes (Ctrl+K) – #tag filters by tag, id:2026… finds IDs" />
            </DockPanel>
        </Border>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="290" MinWidth="180" />
                <ColumnDefinition Width="4" />
                <ColumnDefinition Width="*" MinWidth="320" />
                <ColumnDefinition Width="4" />
                <ColumnDefinition Width="290" MinWidth="200" />
            </Grid.ColumnDefinitions>

            <ListBox x:Name="ResultsList" Grid.Column="0" BorderThickness="0" Background="{StaticResource WindowBrush}"
                     ItemsSource="{Binding Search.Results}" SelectedItem="{Binding Search.Selected}"
                     HorizontalContentAlignment="Stretch" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="6,6">
                            <DockPanel>
                                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                                    <TextBlock Text="🔤" ToolTip="Keyword match"
                                               Visibility="{Binding MatchedKeyword, Converter={StaticResource BoolToVisibility}}" />
                                    <TextBlock Text="🧠" ToolTip="Semantic match" Margin="3,0,0,0"
                                               Visibility="{Binding MatchedSemantic, Converter={StaticResource BoolToVisibility}}" />
                                </StackPanel>
                                <TextBlock Text="{Binding Note.DisplayTitle}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                            </DockPanel>
                            <TextBlock Text="{Binding Note.Id}" Foreground="{StaticResource MutedBrush}" FontSize="11" />
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" Background="{StaticResource LineBrush}" />
            <views:NoteEditorView x:Name="EditorView" Grid.Column="2" DataContext="{Binding Editor}" />
            <GridSplitter Grid.Column="3" HorizontalAlignment="Stretch" Background="{StaticResource LineBrush}" />
            <views:NotePanelView Grid.Column="4" DataContext="{Binding Panel}" />
        </Grid>

        <Border Grid.Row="2" Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource LineBrush}"
                BorderThickness="0,1,0,0" Padding="12,4">
            <DockPanel>
                <TextBlock DockPanel.Dock="Right" Text="{Binding ModelStatus}" Foreground="{StaticResource MutedBrush}" />
                <TextBlock DockPanel.Dock="Right" Foreground="{StaticResource MutedBrush}"
                           Text="{Binding NoteCount, StringFormat='{}{0} notes  ·  '}" />
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{Binding Editor.SaveError}" Foreground="{StaticResource ErrorBrush}" FontWeight="SemiBold" Margin="0,0,12,0" />
                    <TextBlock Text="{Binding Editor.LastSavedAt, StringFormat='Saved {0:HH:mm}'}" Foreground="{StaticResource MutedBrush}" Margin="0,0,12,0" />
                    <TextBlock Text="{Binding StatusMessage}" Foreground="{StaticResource MutedBrush}" />
                </StackPanel>
            </DockPanel>
        </Border>
    </Grid>
</Window>
```

`src/Zettelkasten.App/Views/MainWindow.xaml.cs`
```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Zettelkasten.App.ViewModels;

namespace Zettelkasten.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.FocusTitleRequested += (_, _) => EditorView.FocusTitle();
        PreviewKeyDown += OnPreviewKeyDown;
        MouseDown += OnMouseDown;
        Closing += OnClosing;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.K or Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var command = e.ChangedButton switch
        {
            MouseButton.XButton1 => _vm.BackCommand,
            MouseButton.XButton2 => _vm.ForwardCommand,
            _ => null,
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm.Search.ClearCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when _vm.Search.Results.Count > 0:
                _ = _vm.OpenAsync(_vm.Search.Results[0].Note.Id);
                EditorView.Editor.Focus();
                e.Handled = true;
                break;
            case Key.Down when ResultsList.Items.Count > 0:
                ResultsList.Focus();
                ResultsList.SelectedIndex = 0;
                (ResultsList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
                e.Handled = true;
                break;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        await _vm.ShutdownAsync();
        if (_vm.Editor.IsDirty &&
            MessageBox.Show(this, "Your latest changes could not be saved. Close anyway and lose them?",
                "Zettelkasten", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _closeConfirmed = true;
        Close();
    }
}
```

- [ ] **Step 9: Build and run all tests**

Run: `dotnet build Zettelkasten.slnx` → Expected: 0 errors. Fix any XAML errors before continuing.
Run: `dotnet test` → Expected: all PASS (incl. `AppServicesTests`).

- [ ] **Step 10: Manual smoke test**

Run: `dotnet run --project src/Zettelkasten.App`
Check by hand:
1. Window opens; status bar shows `0 notes · loading semantic model…`, then `semantic search ready` within a few seconds.
2. Ctrl+N creates a note, the cursor lands in the title; typing in title and body shows `Saved HH:mm` about 1 s after you stop.
3. Ctrl+K focuses search; typing filters the list; badges 🔤/🧠 appear.
4. Add a tag via the tag box + Enter; it appears as a chip; ✕ removes it.
5. Ctrl+L opens the picker; choosing a note adds it under RELATED; the other note shows it under BACKLINKS.
6. Alt+← / Alt+→ and mouse side buttons navigate history.
7. ⋯ → Export backup… writes a `.db` file; ⋯ → Delete note… asks for confirmation.
8. Close and reopen: everything is still there. `%LocalAppData%\Zettelkasten\logs\` contains a log file.

- [ ] **Step 11: Commit**

```powershell
git add -A
git commit -m "feat(app): WPF shell with search, editor, side panel, dialogs and startup" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---


### Task 21: App – Markdown highlighting, `[[` autocomplete, Ctrl+click links and preview

**Files:**
- Create: `src/Zettelkasten.App/Editor/Markdown.xshd`, `src/Zettelkasten.App/Editor/WikiLinkCompletionData.cs`, `src/Zettelkasten.App/Preview/FlowDocumentRenderer.cs`
- Modify: `src/Zettelkasten.App/Zettelkasten.App.csproj` (embedded resource), `src/Zettelkasten.App/Views/NoteEditorView.xaml.cs`
- Test: `tests/Zettelkasten.Tests/Support/StaRunner.cs`, `tests/Zettelkasten.Tests/App/FlowDocumentRendererTests.cs`, `tests/Zettelkasten.Tests/App/EditorSupportTests.cs`

**Interfaces:**
- Consumes: `WikiLinkRewriter`, `LinkParser` (Task 2); `NoteSummary` (Task 6); `NoteEditorViewModel.GetLinkCandidatesAsync / GetLinkTitlesAsync / RequestOpen / NoteLoaded / IsPreview / Content` (Task 16); `NoteEditorView` (Task 20)
- Produces:
  - `sealed class FlowDocumentRenderer(Brush linkBrush, Brush brokenBrush, Brush codeBackground)` (+ parameterless ctor with default colors), `FlowDocument Render(string markdown, Func<string, string?> titleLookup, Action<string> onLink)`, const `BrokenToolTip`
  - `sealed class WikiLinkCompletionData(NoteSummary note) : ICompletionData`
  - `internal static IHighlightingDefinition NoteEditorView.LoadHighlighting()`
  - Test support `static class StaRunner { static void Run(Action action); }`

- [ ] **Step 1: Add Markdig and register the highlighting resource**

```powershell
dotnet add src/Zettelkasten.App package Markdig --version 0.40.0
```

Add to `src/Zettelkasten.App/Zettelkasten.App.csproj`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Editor\Markdown.xshd" LogicalName="Markdown.xshd" />
  </ItemGroup>
```

- [ ] **Step 2: Write test support and failing tests**

`tests/Zettelkasten.Tests/Support/StaRunner.cs`
```csharp
using System.Runtime.ExceptionServices;

namespace Zettelkasten.Tests.Support;

/// <summary>WPF objects need an STA thread.</summary>
public static class StaRunner
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
```

`tests/Zettelkasten.Tests/App/FlowDocumentRendererTests.cs`
```csharp
using System.Windows;
using System.Windows.Documents;
using Zettelkasten.App.Preview;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public class FlowDocumentRendererTests
{
    private static string TextOf(TextElement e) => new TextRange(e.ContentStart, e.ContentEnd).Text;

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    [Fact]
    public void Renders_heading_and_emphasis() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("# Title\n\nHello *world* and **bold**", _ => null, _ => { });

        var heading = Assert.IsType<Paragraph>(doc.Blocks.FirstBlock);
        Assert.Equal(FontWeights.Bold, heading.FontWeight);
        Assert.Equal("Title", TextOf(heading));
        Assert.Single(FindAll<Italic>(doc));
        Assert.Single(FindAll<Bold>(doc));
    });

    [Fact]
    public void Wiki_link_shows_title_and_reports_click() => StaRunner.Run(() =>
    {
        string? clicked = null;
        var doc = new FlowDocumentRenderer().Render(
            "see [[20261102143015]]", id => id == "20261102143015" ? "Target note" : null, url => clicked = url);

        var link = Assert.Single(FindAll<Hyperlink>(doc));
        Assert.Equal("Target note", TextOf(link));
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Assert.Equal("zk:20261102143015", clicked);
    });

    [Fact]
    public void Broken_link_is_marked() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("[[20261102143015]]", _ => null, _ => { });
        var link = Assert.Single(FindAll<Hyperlink>(doc));
        Assert.Equal(FlowDocumentRenderer.BrokenToolTip, link.ToolTip);
    });

    [Fact]
    public void Renders_lists_quotes_and_code() => StaRunner.Run(() =>
    {
        var doc = new FlowDocumentRenderer().Render("- a\n- b\n\n> quoted\n\n```\nvar x = 1;\n```\n\n---", _ => null, _ => { });
        var blocks = doc.Blocks.ToList();
        Assert.Equal(2, Assert.IsType<List>(blocks[0]).ListItems.Count);
        Assert.IsType<Section>(blocks[1]);
        Assert.Contains("var x = 1;", TextOf(Assert.IsType<Paragraph>(blocks[2])));
        Assert.IsType<BlockUIContainer>(blocks[3]);
    });

    [Fact]
    public void Plain_urls_become_links() => StaRunner.Run(() =>
    {
        string? clicked = null;
        var doc = new FlowDocumentRenderer().Render("visit https://example.org now", _ => null, u => clicked = u);
        var link = Assert.Single(FindAll<Hyperlink>(doc));
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Assert.Equal("https://example.org", clicked);
    });
}
```

`tests/Zettelkasten.Tests/App/EditorSupportTests.cs`
```csharp
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Zettelkasten.App.Editor;
using Zettelkasten.App.Views;
using Zettelkasten.Core.Model;
using Zettelkasten.Tests.Support;

namespace Zettelkasten.Tests.App;

public class EditorSupportTests
{
    [Fact]
    public void Completion_inserts_id_and_closing_brackets() => StaRunner.Run(() =>
    {
        var editor = new TextEditor { Text = "see [[mem" };
        var data = new WikiLinkCompletionData(new NoteSummary("20261102143015", "Memory", DateTimeOffset.Now));

        data.Complete(editor.TextArea, new TextSegment { StartOffset = 6, Length = 3 }, EventArgs.Empty);

        Assert.Equal("see [[20261102143015]]", editor.Text);
        Assert.Contains("Memory", data.Text);
        Assert.Contains("20261102143015", data.Text);
    });

    [Fact]
    public void Markdown_highlighting_loads() => StaRunner.Run(() =>
    {
        var definition = NoteEditorView.LoadHighlighting();
        Assert.Equal("Markdown", definition.Name);
        Assert.NotNull(definition.GetNamedColor("WikiLink"));
    });
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~FlowDocumentRendererTests|FullyQualifiedName~EditorSupportTests"` → Expected: build FAILS.

- [ ] **Step 4: Write the highlighting definition**

`src/Zettelkasten.App/Editor/Markdown.xshd`
```xml
<?xml version="1.0"?>
<SyntaxDefinition name="Markdown" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
    <Color name="Heading" foreground="#1F4E79" fontWeight="bold" />
    <Color name="Strong" fontWeight="bold" />
    <Color name="Emphasis" fontStyle="italic" />
    <Color name="Code" foreground="#8A3B12" />
    <Color name="WikiLink" foreground="#2F6FDF" fontWeight="bold" />
    <Color name="Link" foreground="#2F6FDF" />
    <Color name="Quote" foreground="#6B6F76" fontStyle="italic" />
    <Color name="ListMarker" foreground="#9A6700" fontWeight="bold" />

    <RuleSet>
        <Span color="Code" multiline="true">
            <Begin>^```</Begin>
            <End>^```</End>
        </Span>
        <Rule color="Heading">^\#{1,6}\s.*$</Rule>
        <Rule color="Quote">^&gt;.*$</Rule>
        <Rule color="ListMarker">^\s*([-*+]|\d+\.)\s</Rule>
        <Rule color="WikiLink">\[\[\d{14}\]\]</Rule>
        <Rule color="Code">`[^`]+`</Rule>
        <Rule color="Strong">\*\*[^*]+\*\*</Rule>
        <Rule color="Emphasis">\*[^*\s][^*]*\*|_[^_\s][^_]*_</Rule>
        <Rule color="Link">\[[^\]]+\]\([^)]+\)</Rule>
    </RuleSet>
</SyntaxDefinition>
```

- [ ] **Step 5: Implement completion data and the renderer**

`src/Zettelkasten.App/Editor/WikiLinkCompletionData.cs`
```csharp
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Zettelkasten.Core.Model;

namespace Zettelkasten.App.Editor;

/// <summary>An entry in the [[ autocomplete list; typing filters on title or ID.</summary>
public sealed class WikiLinkCompletionData(NoteSummary note) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => $"{note.DisplayTitle} {note.Id}";
    public object Content => $"{note.DisplayTitle}   ·   {note.Id}";
    public object Description => note.Id;
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, $"{note.Id}]]");
}
```

`src/Zettelkasten.App/Preview/FlowDocumentRenderer.cs`
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zettelkasten.Core.Links;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace Zettelkasten.App.Preview;

/// <summary>Renders Markdown as a native WPF FlowDocument; [[id]] links become clickable note links.</summary>
public sealed class FlowDocumentRenderer(Brush linkBrush, Brush brokenBrush, Brush codeBackground)
{
    public const string BrokenToolTip = "No note with this ID";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public FlowDocumentRenderer()
        : this(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xDF)),
               new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
               new SolidColorBrush(Color.FromRgb(0xF3, 0xF2, 0xEE)))
    {
    }

    public FlowDocument Render(string markdown, Func<string, string?> titleLookup, Action<string> onLink)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(20, 8, 20, 20),
            TextAlignment = TextAlignment.Left,
        };
        foreach (var block in Markdown.Parse(WikiLinkRewriter.Rewrite(markdown, titleLookup), Pipeline))
            if (RenderBlock(block, onLink) is { } rendered) doc.Blocks.Add(rendered);
        return doc;
    }

    private WpfBlock? RenderBlock(MdBlock block, Action<string> onLink) => block switch
    {
        HeadingBlock h => WithInlines(new Paragraph
        {
            FontWeight = FontWeights.Bold,
            FontSize = h.Level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 },
            Margin = new Thickness(0, h.Level <= 2 ? 14 : 10, 0, 6),
        }, h.Inline, onLink),
        ParagraphBlock p => WithInlines(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }, p.Inline, onLink),
        QuoteBlock q => Quote(q, onLink),
        ListBlock l => List(l, onLink),
        CodeBlock c => new Paragraph(new Run(c.Lines.ToString().TrimEnd()))
        {
            FontFamily = Mono, FontSize = 13, Background = codeBackground,
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10),
        },
        ThematicBreakBlock => new BlockUIContainer(new Separator { Margin = new Thickness(0, 6, 0, 12) }),
        HtmlBlock html => new Paragraph(new Run(html.Lines.ToString())),
        LeafBlock leaf when leaf.Inline is not null => WithInlines(new Paragraph(), leaf.Inline, onLink),
        _ => null,
    };

    private Section Quote(QuoteBlock quote, Action<string> onLink)
    {
        var section = new Section
        {
            BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0), Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 10),
        };
        foreach (var child in quote)
            if (RenderBlock(child, onLink) is { } rendered) section.Blocks.Add(rendered);
        return section;
    }

    private List List(ListBlock list, Action<string> onLink)
    {
        var result = new List
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(24, 0, 0, 0),
        };
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
            {
                if (RenderBlock(child, onLink) is not { } rendered) continue;
                if (rendered is Paragraph para) para.Margin = new Thickness(0, 0, 0, 2);
                listItem.Blocks.Add(rendered);
            }
            result.ListItems.Add(listItem);
        }
        return result;
    }

    private Paragraph WithInlines(Paragraph paragraph, ContainerInline? inlines, Action<string> onLink)
    {
        AddInlines(paragraph.Inlines, inlines, onLink);
        return paragraph;
    }

    private void AddInlines(InlineCollection target, ContainerInline? container, Action<string> onLink)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis, onLink);
                    target.Add(span);
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = Mono, Background = codeBackground });
                    break;
                case LinkInline { IsImage: true } image:
                    target.Add(new Run($"[image: {image.Url}]"));
                    break;
                case LinkInline link:
                    target.Add(Link(link.Url ?? "", h => AddInlines(h.Inlines, link, onLink), onLink));
                    break;
                case AutolinkInline auto:
                    target.Add(Link(auto.Url, h => h.Inlines.Add(new Run(auto.Url)), onLink));
                    break;
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()));
                    break;
                case HtmlInline html:
                    target.Add(new Run(html.Tag));
                    break;
                case ContainerInline other:
                    AddInlines(target, other, onLink);
                    break;
            }
        }
    }

    private WpfInline Link(string url, Action<Hyperlink> fill, Action<string> onLink)
    {
        var hyperlink = new Hyperlink { Foreground = linkBrush, Cursor = System.Windows.Input.Cursors.Hand };
        fill(hyperlink);
        if (url.StartsWith(WikiLinkRewriter.BrokenScheme, StringComparison.Ordinal))
        {
            hyperlink.Foreground = brokenBrush;
            hyperlink.ToolTip = BrokenToolTip;
        }
        else if (!url.StartsWith(WikiLinkRewriter.NoteScheme, StringComparison.Ordinal))
        {
            hyperlink.ToolTip = url;
        }
        hyperlink.Click += (_, _) => onLink(url);
        return hyperlink;
    }
}
```

- [ ] **Step 6: Wire everything into the editor view**

Replace `src/Zettelkasten.App/Views/NoteEditorView.xaml.cs` with:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Zettelkasten.App.Editor;
using Zettelkasten.App.Preview;
using Zettelkasten.App.ViewModels;
using Zettelkasten.Core.Links;

namespace Zettelkasten.App.Views;

public partial class NoteEditorView : UserControl
{
    private readonly FlowDocumentRenderer _renderer;
    private NoteEditorViewModel? _vm;
    private CompletionWindow? _completion;
    private bool _syncing;

    public NoteEditorView()
    {
        InitializeComponent();
        _renderer = new FlowDocumentRenderer(
            (Brush)FindResource("AccentBrush"), (Brush)FindResource("ErrorBrush"), (Brush)FindResource("CodeBackgroundBrush"));
        Editor.SyntaxHighlighting = LoadHighlighting();
        Editor.TextChanged += (_, _) =>
        {
            if (!_syncing && _vm is not null) _vm.Content = Editor.Text;
        };
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.PreviewMouseLeftButtonDown += OnEditorMouseDown;
        DataContextChanged += OnDataContextChanged;
    }

    internal static IHighlightingDefinition LoadHighlighting()
    {
        using var stream = typeof(NoteEditorView).Assembly.GetManifestResourceStream("Markdown.xshd")
                           ?? throw new InvalidOperationException("Markdown.xshd resource is missing.");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    public void FocusTitle()
    {
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.NoteLoaded -= OnNoteLoaded;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _vm = e.NewValue as NoteEditorViewModel;
        if (_vm is not null)
        {
            _vm.NoteLoaded += OnNoteLoaded;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        OnNoteLoaded(this, EventArgs.Empty);
    }

    private void OnNoteLoaded(object? sender, EventArgs e)
    {
        _completion?.Close();
        _syncing = true;
        try
        {
            Editor.Text = _vm?.Content ?? "";
            Editor.Document.UndoStack.ClearAll(); // Ctrl+Z must not bring back the previous note
            Editor.ScrollToHome();
        }
        finally
        {
            _syncing = false;
        }
        if (_vm?.IsPreview == true) RenderPreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteEditorViewModel.IsPreview) && _vm?.IsPreview == true) RenderPreview();
    }

    private async void RenderPreview()
    {
        if (_vm is null) return;
        var titles = await _vm.GetLinkTitlesAsync();
        Preview.Document = _renderer.Render(_vm.Content, id => titles.GetValueOrDefault(id), OnPreviewLink);
    }

    private void OnPreviewLink(string url)
    {
        if (url.StartsWith(WikiLinkRewriter.NoteScheme, StringComparison.Ordinal))
            _vm?.RequestOpen(url[WikiLinkRewriter.NoteScheme.Length..]);
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text != "[" || _vm is null) return;
        var offset = Editor.CaretOffset;
        if (offset < 2 || Editor.Document.GetText(offset - 2, 2) != "[[") return;

        var candidates = await _vm.GetLinkCandidatesAsync();
        if (candidates.Count == 0) return;

        _completion = new CompletionWindow(Editor.TextArea) { Width = 380 };
        foreach (var note in candidates)
            _completion.CompletionList.CompletionData.Add(new WikiLinkCompletionData(note));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null) return;
        var offset = Editor.Document.GetOffset(position.Value.Location);
        if (LinkParser.FindAt(Editor.Text, offset) is { } link)
        {
            _vm.RequestOpen(link.TargetId);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FlowDocumentRendererTests|FullyQualifiedName~EditorSupportTests"` → Expected: all PASS.
Run: `dotnet test` → Expected: whole suite PASS.

- [ ] **Step 8: Manual check**

Run: `dotnet run --project src/Zettelkasten.App`
1. Headings, `**bold**`, `` `code` `` and `[[2026…]]` are colored in the editor.
2. Typing `[[` opens a list of notes; typing filters it; Enter inserts `[[id]]`.
3. Ctrl+click on `[[id]]` opens that note; a link to a non-existent ID does nothing.
4. Ctrl+E shows the preview: link shows the target's title; clicking it navigates; a broken link is red with a tooltip.
5. After switching notes, Ctrl+Z does not restore the previous note's text.

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "feat(app): markdown highlighting, wiki-link autocomplete, ctrl+click and preview" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 22: Distribution, README and spec update

**Files:**
- Create: `README.md`
- Modify: `.gitignore`, `docs/superpowers/specs/2026-11-02-zettelkasten-design.md`

**Interfaces:**
- Consumes: the finished application
- Produces: a self-contained `publish/` folder and up-to-date docs

- [ ] **Step 1: Ignore publish output and test leftovers**

Append to `.gitignore`:

```
# Local build output
publish/
design.db
```

- [ ] **Step 2: Update the spec to record the planning deviations**

In `docs/superpowers/specs/2026-11-02-zettelkasten-design.md`:
- In section 2, replace `tokenizer via Microsoft.ML.Tokenizers` with `tokenizer: small built-in BERT WordPiece implementation (WordPieceTokenizer)`.
- In section 3 (Core key types), replace `` `NoteIdGenerator` – `yyyyMMddHHmmss` from an injectable clock`` with `` `NoteIdGenerator` – `yyyyMMddHHmmss` from the local time supplied by `TimeProvider` ``, and remove `IClock` from the interface list.
- Add a line under section 7: `Closing the app with unsaved changes that could not be saved asks for confirmation before discarding them.`

- [ ] **Step 3: Write `README.md`**

````markdown
# Zettelkasten

An offline note-taking app for the Zettelkasten method: notes with unique IDs, inline `[[id]]` links and
manual "related" links, backlinks, tags with duplicate detection, and search that combines keywords with
meaning. Everything is stored in one local SQLite file. Nothing is sent over the network.

## Setup (once)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fetch-model.ps1   # downloads the ~23 MB embedding model
dotnet build
dotnet test
dotnet run --project src/Zettelkasten.App
```

Without the model the app still works, but search is keyword-only and tags get no "similar tag" suggestions.

## Publish a standalone copy

```powershell
dotnet publish src/Zettelkasten.App -c Release -r win-x64 --self-contained -o publish
```

Run `publish\Zettelkasten.exe`. The folder can be copied anywhere; no .NET installation is needed.

## Where data lives

- Notes: `%LocalAppData%\Zettelkasten\zettelkasten.db` (back it up via ⋯ → Export backup…)
- Logs: `%LocalAppData%\Zettelkasten\logs\`
- Before a schema upgrade the app copies the database to `zettelkasten.db.bak-<timestamp>`.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+K / Ctrl+F | Focus search |
| Ctrl+N | New note |
| Ctrl+S | Save now (auto-save runs 1 s after you stop typing) |
| Ctrl+E | Toggle edit / preview |
| Ctrl+L | Add a related (manual) link |
| Alt+← / Alt+→, mouse side buttons | Back / forward |
| `[[` | Link autocomplete |
| Ctrl+click on `[[id]]` | Open linked note |

## Search syntax

`spaced repetition #learning id:2026` – free text (keyword + semantic), `#tag` filters by tag,
`id:` filters by ID prefix. Results show 🔤 for keyword and 🧠 for semantic matches.

## Project layout

- `src/Zettelkasten.Core` – domain logic, no dependencies
- `src/Zettelkasten.Infrastructure` – SQLite/EF Core, FTS5, ONNX embeddings, search
- `src/Zettelkasten.App` – WPF UI (MVVM)
- `tests/Zettelkasten.Tests` – xUnit tests
- Tuning knobs for search and tag suggestions: `src/Zettelkasten.Core/Search/SemanticSettings.cs`
````

- [ ] **Step 4: Publish and verify the standalone build**

```powershell
dotnet publish src/Zettelkasten.App -c Release -r win-x64 --self-contained -o publish
Test-Path publish/Zettelkasten.exe, publish/models/model.onnx, publish/models/vocab.txt
```

Expected: `True True True`. Start `publish\Zettelkasten.exe` and confirm the status bar reaches `semantic search ready`.

- [ ] **Step 5: Final verification against the spec**

Run: `dotnet test` → Expected: all PASS, 0 skipped (model present).

Manual checklist with the published app (use a few real notes):
1. Create ~5 notes on different topics with tags; search a phrase that only matches by meaning (e.g. note "Spaced repetition and the forgetting curve", query `how memory works`) → result shows 🧠.
2. Add tag `machine-learning` to one note, then type `ML` in another note's tag box → suggestion `#machine-learning (abbreviation)`. Type `deep learning` → check whether `machine-learning` is suggested. **If obvious duplicates are not suggested or unrelated tags are, adjust `TagSuggestionMinScore` in `SemanticSettings.cs` (try 0.65–0.80) and record the chosen value in the commit message.** Do the same for `SearchMinScore` if semantic results are too noisy or too sparse.
3. Delete a note that others link to → the dialog lists them; afterwards they show the link in red under OUTGOING.
4. Rename the `models` folder in `publish/`, start the app → it works keyword-only and the status bar says `semantic search unavailable`. Restore the folder.
5. Export a backup, then open it with any SQLite viewer or by temporarily copying it over the live DB (app closed) → notes are intact.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "docs: README, publish setup and spec updates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
