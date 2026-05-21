# Contributing

Thanks for considering a contribution.

## Quick start

```sh
git clone https://github.com/touhidalam69/DataAccess_SqlServer
cd DataAccess_SqlServer
dotnet restore src/DataAccess_SqlServer.sln
dotnet build src/DataAccess_SqlServer.sln -c Release
dotnet test src/TA.DataAccess.SqlServer.Tests/TA.DataAccess.SqlServer.Tests.csproj
```

## Branching

- `main` is always shippable.
- Feature branches: `feat/<short-topic>`.
- Bug fixes: `fix/<short-topic>`.
- Squash-merge into `main` with a Conventional Commit subject.

## Commits

Use Conventional Commits:

- `feat: add X` — new public API or behavior.
- `fix: correct X` — bug fix.
- `perf: speed up X` — performance.
- `refactor: tidy X` — no behavior change.
- `docs: update X` — README/comments.
- `test: cover X`.
- `build: bump X` — deps/CI/csproj.
- Breaking change: append `!` and a `BREAKING CHANGE:` footer.

## Versioning (SemVer)

`MAJOR.MINOR.PATCH`:

- **MAJOR** — any change visible to consumers that requires source/binary changes: removed/renamed public types or members, signature changes, dropped TFMs, behavior changes that callers can observe.
- **MINOR** — backward-compatible additions: new public APIs, new optional parameters via overloads, new TFMs, new attributes.
- **PATCH** — backward-compatible fixes and internal improvements: bug fixes, perf, doc-only changes, dependency patch bumps.

Pre-release tags use `-preview.N` or `-rc.N`.

## Pull requests

- Open against `main`.
- Include a short rationale, before/after if a fix.
- Add tests under `src/TA.DataAccess.SqlServer.Tests/` for any logic change.
- Run `dotnet build -c Release` and `dotnet test` locally before pushing.
- Keep PRs focused — one concern per PR.

## Release

1. Bump `<Version>` in `src/TA.DataAccess.SqlServer/TA.DataAccess.SqlServer.csproj` per SemVer rules above.
2. Update `<PackageReleaseNotes>`.
3. Tag: `git tag v2.0.0 && git push --tags`.
4. CI workflow publishes to NuGet on tag push.

## Code style

- `LangVersion` = latest, `Nullable` enabled, `ImplicitUsings` enabled — set in [Directory.Build.props](Directory.Build.props).
- `.NET` analyzers run on every build at `latest-recommended`.
- Public APIs need XML doc comments.
- No reflection on a hot path without caching.
- All async methods take `CancellationToken` and call `ConfigureAwait(false)`.
- Identifiers in dynamic SQL go through `Identifier.Quote`. Never interpolate raw.

## Security

If you find a security issue, do not open a public issue. Email the maintainer instead.
