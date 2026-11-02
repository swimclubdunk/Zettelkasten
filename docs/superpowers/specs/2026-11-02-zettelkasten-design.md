# Zettelkasten – Design Spec

Date: 2026-11-02
Status: Approved in conversation, awaiting written review

## 1. Purpose

A fully offline, single-user Zettelkasten desktop application for personal daily use and as a .NET practice project. It must be robust enough to live in every day and cleanly structured enough to learn from.

**Success criteria**
- Create, edit, browse and delete notes, each with a unique ID.
- Link notes inline (`[[id]]`) and manually; see outgoing links and backlinks.
- Find notes quickly via a top search bar that combines keyword and semantic matching.
- Tag notes; creating a tag suggests semantically similar existing tags to prevent duplicates.
- No network access at runtime. No separate server process. All data in one local file.

**Non-goals (for now):** graph view, images/attachments, multiple windows/tabs, Markdown file import/export, sync, multi-user, cross-platform.

## 2. Technology

- .NET 10, C#, WPF, MVVM via CommunityToolkit.Mvvm
- DI / hosting: Microsoft.Extensions.Hosting
- Storage: SQLite (embedded, single file) via EF Core (Microsoft.EntityFrameworkCore.Sqlite); raw SQL for FTS5
- Embeddings: all-MiniLM-L6-v2 (quantized ONNX) via Microsoft.ML.OnnxRuntime (CPU), tokenizer: small built-in BERT WordPiece implementation (`WordPieceTokenizer`)
- Editor: AvalonEdit; Markdown rendering: Markdig → WPF FlowDocument
- Logging: Serilog (rolling file)
- Tests: xUnit

## 3. Solution structure

```
Zettelkasten.slnx
Directory.Build.props            (Nullable enable, TreatWarningsAsErrors)
models/                          (all-MiniLM-L6-v2 ONNX + vocab.txt; fetched once by scripts/fetch-model.ps1)
src/
  Zettelkasten.Core/             domain + interfaces + pure logic; no package dependencies
  Zettelkasten.Infrastructure/   EF Core/SQLite, FTS5, ONNX embedding, vector index, search, backup
  Zettelkasten.App/              WPF views, view models, DI composition root
tests/
  Zettelkasten.Tests/            xUnit (unit + integration)
```

Dependency direction: App → Infrastructure → Core; App → Core. Core depends on nothing.

### Core (key types)
- `Note` (Id, Content, CreatedAt, UpdatedAt, Tags, ManualLinks). Notes have no title of their own: the read-only `Title` is the content's first meaningful line (`NoteTitle.FromContent`: Markdown markers stripped, max 80 chars; empty note → "(empty note)").
- `Tag` (Id, Name)
- `NoteLink` (SourceId, TargetId, Kind: Inline | Manual)
- `NoteIdGenerator` – `yyyyMMddHHmmss` from the local time supplied by `TimeProvider`; on collision increments by one second until free
- `LinkParser` – extracts distinct `[[id]]` targets (14-digit IDs) from Markdown
- `TagNormalizer` – trim, lowercase, collapse whitespace to `-`
- `InitialsMatcher` – true if one normalized tag equals the initials of the other's `-`-separated words (e.g. `ml` ↔ `machine-learning`)
- `RankFusion` – Reciprocal Rank Fusion (k = 60) over N ranked lists
- `SearchQueryParser` – splits input into free text, `#tag` filters and `id:` prefix
- `SemanticSettings` – thresholds (search ≥ 0.25, tag suggestion ≥ 0.60; tuned against the real model, see RealModelThresholdTests)
- Interfaces: `INoteRepository`, `ITagRepository`, `ISearchService`, `IEmbeddingService`, `ITagSuggestionService`, `IBackupService` (time comes from the BCL `TimeProvider`)

## 4. Data model & storage

Database file: `%LocalAppData%\Zettelkasten\zettelkasten.db`, WAL mode, schema via EF Core migrations.

| Table | Columns | Notes |
|---|---|---|
| `Notes` | `Id` TEXT PK, `Title`, `Content`, `CreatedAt`, `UpdatedAt` | Id = creation timestamp `yyyyMMddHHmmss`; `Title` is a cached first line of `Content`, set on every save (migration `MergeTitlesIntoContent` moved former titles into the content as a `# heading`) |
| `Tags` | `Id` INT PK, `Name` UNIQUE (NOCASE), `Embedding` BLOB | Name stored normalized |
| `NoteTags` | `NoteId`, `TagId` | composite PK; cascade on delete |
| `NoteLinks` | `SourceId`, `TargetId`, `Kind` | PK (SourceId, TargetId, Kind); TargetId not a FK (allows broken links) |
| `NoteEmbeddings` | `NoteId` PK, `Vector` BLOB (384 × float32), `ContentHash` | Recomputed only when the hash of Content changes |
| `NotesFts` | FTS5 virtual table (`NoteId` UNINDEXED, `Title`, `Content`, `Tags`) | Created in a migration via raw SQL |

**Rules**
- Saving a note is one transaction: note row, inline links rebuilt from `LinkParser` (manual links untouched), tags, FTS row, embedding (if model available and hash changed).
- Links to non-existent IDs are stored and displayed as broken; they resolve automatically when such a note exists.
- Deleting a note (after confirmation) removes the note, its tags, outgoing links, FTS row and embedding. Incoming links remain and become broken; the confirmation dialog lists the linking notes.
- Deleting a tag (after confirmation) removes it from all notes.
- "Export backup…" uses the SQLite online backup API to write a consistent copy to a user-chosen path.

## 5. Search & semantic layer

**Embedding service**
- Model + vocab ship in the app output folder; loaded once on a background thread at startup.
- BERT WordPiece tokenization (max 256 tokens), ONNX inference, mean pooling with attention mask, L2 normalization → cosine = dot product.
- Until loaded, or if loading fails, `IsAvailable = false` and callers fall back to keyword-only behavior.
- Notes saved while unavailable get embedded once the model becomes available (backfill of notes with missing/stale embeddings at startup).

**Vector index**
- All note vectors held in memory (`Dictionary<string, float[]>`), loaded at startup, updated on save/delete. Brute-force dot product.

**Note search**
- Debounce 250 ms in the view model.
- Keyword: FTS5 `MATCH` with each term prefix-matched (`term*`), ranked by `bm25`. User input is escaped/quoted to avoid FTS syntax errors.
- Semantic: embed query text, score against the index, keep scores ≥ 0.25.
- Merge with RRF; each result carries flags `MatchedKeyword` / `MatchedSemantic` shown as badges.
- `#tag` filters restrict results to notes with all given tags; `id:` prefix matches note IDs directly.
- Empty query → 50 most recently updated notes.

**Tag suggestions**
- Input normalized; exact normalized match → reuse existing tag silently.
- Otherwise suggest existing tags with embedding similarity ≥ 0.60 or an initials match, sorted by score, max 5.
- User picks a suggestion or chooses "Create '<name>' anyway". Never blocks.
- Tag embeddings computed on creation (and backfilled if missing).

## 6. User interface

Three-column main window: results list (left), note editor (center), side panel (right); search bar + "New" button on top; status bar at bottom.

- **Results list:** title, ID, match badges (🔤 keyword, 🧠 semantic).
- **Editor:** ID (read-only, faint), Edit/Preview toggle, then the content directly (no title box; Ctrl+N focuses the body). A one-line Markdown cheat sheet sits under the editor while editing. Edit = AvalonEdit with light Markdown highlighting and `[[` autocomplete (title search, inserts `[[id]]`). Preview = Markdig → FlowDocument; `[[id]]` rendered as clickable link labelled with the target title (broken ones in red).
- **Side panel:** tags (remove ✕, add box with suggestion dropdown), related/manual links (remove ✕, "+ link note" picker), outgoing links (broken marked ⚠), backlinks (inline + manual).
- **Status bar:** last saved time, note count, model status, save errors.
- **Auto-save:** 1 s after last edit, on note switch, and on window close; `Ctrl+S` forces save.
- **Navigation:** clicking links opens notes; back/forward history (`Alt+←/→`, mouse XButtons).
- **Shortcuts:** `Ctrl+K`/`Ctrl+F` focus search, `Ctrl+N` new note, `Ctrl+E` toggle Edit/Preview, `Ctrl+L` add manual link, `Ctrl+S` save.
- **Theme:** light; all colors in one resource dictionary.

## 7. Error handling

- Save failures roll back the transaction; status bar shows "Save failed – your text is still in the editor"; next auto-save retries. Editor content is never discarded on failure.
- Embedding model missing/corrupt → keyword-only mode, status "semantic search unavailable", details logged.
- Before applying pending migrations, copy the DB to `zettelkasten.db.bak-yyyyMMddHHmmss`.
- Global handlers (`DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException`) log and show a friendly message; continue when possible.
- Closing the app with unsaved changes that could not be saved asks for confirmation before discarding them.
- Serilog rolling daily log in `%LocalAppData%\Zettelkasten\logs\`, retained 14 days.

## 8. Testing

- **Core unit tests:** ID generation + collision, link parsing (duplicates, invalid IDs, edge cases), tag normalization, initials matching, RRF, search query parsing.
- **Infrastructure integration tests** (temp SQLite file per test): save/load round trip, inline link rebuild preserving manual links, backlinks, broken links, FTS sync after update/delete, transaction rollback on failure, tag exact-match reuse, note delete semantics.
- **Embedding sanity tests** (skipped if model files absent): "machine learning" closer to "neural networks" than to "cooking"; tag suggestion finds `machine-learning` for `deep-learning`.
- **View model tests** with fake services: auto-save debounce, navigation history, search debounce.
- No automated UI tests.

## 9. Build & distribution

- `scripts/fetch-model.ps1` downloads the model and vocab into `models/` once (the only network use, at setup time). Model files are git-ignored.
- `dotnet publish -c Release -r win-x64 --self-contained` produces a runnable folder.
- Git repository initialized in the project root with a .NET `.gitignore`.
