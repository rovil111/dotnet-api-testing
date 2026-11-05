# .NET API Testing with MSTest

![tests](https://github.com/rovil111/dotnet-api-testing/actions/workflows/tests.yml/badge.svg)

An ASP.NET Core minimal API on .NET 10 with an MSTest suite that exercises it over real HTTP. Each test boots a fresh copy of the API on a free port, so tests share no state and need no external services.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test
dotnet test --filter TestCategory=Smoke
```

Other categories are `Contract`, `Negative` and `Concurrency`.

## What the suite demonstrates

| Technique | Example |
| --- | --- |
| Boundary value analysis | Reserving exactly all stock, one more than stock, zero and negative quantities |
| Data-driven tests | `[DataRow]` cases, each asserting the field named in the validation error |
| Contract testing | A test fails if a JSON field is added, removed or changes type |
| Concurrency testing | 50 parallel reservations against 10 units must never oversell |
| Negative testing | Duplicate SKUs, unknown IDs and invalid input |
| Test categories | Fast smoke runs through `--filter` |
| CI reporting | TRX results uploaded by GitHub Actions |

## Layout

```text
src/Inventory.Api/           API under test
tests/Inventory.Api.Tests/   ApiHost (boots the API) and the MSTest suite
```
