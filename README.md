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
