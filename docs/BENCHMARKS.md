# Benchmarks & load testing

Three BenchmarkDotNet suites in [`tests/Ordinis.Benchmarks`](../tests/Ordinis.Benchmarks) put
numbers on design decisions this codebase otherwise only argues for. A k6 script in
[`tests/load`](../tests/load) checks the optimistic-concurrency write path under contention.

| Suite | Question it answers |
| --- | --- |
| `GetTasksFilteredBenchmarks` | What does EF Core cost compared with hand-written Dapper on the busiest read query, and how much of that gap comes from the query's *shape* rather than from EF itself? |
| `TaskMappingBenchmarks` | Is manual mapping (`TaskMapper`) still the right call, or would Mapster save effort at no cost? |
| `MiddlewarePipelineBenchmarks` | What does the observability and error-handling middleware stack cost per request? |
| `tests/load/task-update-contention.js` | Under 50 concurrent writers, does `PUT /tasks/{id}` with `If-Match` only ever answer `204` or `409`, and how fast? |

---

## Running the benchmarks

Prerequisites: the .NET 10 SDK, and Docker for the database suite.

```bash
# Interactive picker
dotnet run -c Release --project tests/Ordinis.Benchmarks

# One suite, or all of them
dotnet run -c Release --project tests/Ordinis.Benchmarks -- --filter "*TaskMapping*"
dotnet run -c Release --project tests/Ordinis.Benchmarks -- --filter "*"

# Quick smoke run (one iteration per case, numbers are meaningless)
dotnet run -c Release --project tests/Ordinis.Benchmarks -- --filter "*" --job dry
```

Always use `-c Release`. BenchmarkDotNet refuses to measure Debug builds. Reports go to
`BenchmarkDotNet.Artifacts/results/` (gitignored); add `--exporters github` to get
Markdown tables.

### The benchmark database

`GetTasksFilteredBenchmarks` and the load test share one seeded SQL Server database:

- 1 organization, 50 users and 1 project with 5 boards.
- 10,000 tasks, with about 15,000 comments and about 3,300 attachments.
- About 80% of tasks have an assignee, and statuses are spread across the workflow.

The data is written through the domain model and `AppDbContext`, the same path production
writes take, so outbox rows, timestamps and concurrency tokens look exactly as the API would
produce them. Every attribute is drawn from a fixed-seed `Random`, which makes runs
reproducible without correlating columns. An earlier `i % n` scheme made every "Billing" task
the same priority, so combined filters returned nothing.

Where it lives:

- **Default.** A SQL Server Testcontainer named `ordinis-benchmarks-mssql`, started with
  `WithReuse(true)`. BenchmarkDotNet runs every case in its own process, and reuse lets all of
  them attach to one container that was seeded once (about 2 minutes on first use). The
  container is left running afterwards. Remove it with `docker rm -f ordinis-benchmarks-mssql`.
- **Override.** Set `ORDINIS_BENCHMARK_CONNECTION` to any SQL Server connection string, for
  example the docker-compose `db-sqlserver` service, to benchmark a specific environment.

`dotnet run -c Release --project tests/Ordinis.Benchmarks -- seed` creates and seeds the
database without benchmarking. It prints the connection string and the login credentials
the load test uses.

---

## Methodology

**Parity before measurement.** Every suite's `[GlobalSetup]` checks that the variants it
compares produce the same output, and throws if they don't. That way no variant can win by
doing less work:

- **Queries:** identical `TotalCount`, page membership and order across all three variants,
  and DTOs that match field for field (see the known defect below for the one exemption).
- **Mapping:** Mapster and `TaskMapper` must serialize to identical JSON, `_links` included.
  Mapster reuses `TaskMapper.BuildLinks`, which is `internal` for this reason and exposed via
  `InternalsVisibleTo`.

**Per-operation latency for database calls.** BenchmarkDotNet's statistics are computed over
iterations, and by default each iteration averages many invocations. That smooths away the
tail a P99 is supposed to show. `PerOperationLatencyConfig` pins one invocation per iteration,
so the 200 measurements per case are 200 individual queries and P50/P95/P99 describe real
per-call latency. BenchmarkDotNet has no built-in P99 column, so `PercentileColumn` adds one.
The `MinIterationTime` warning this triggers is expected: that heuristic protects
sub-microsecond benchmarks from timer resolution and doesn't apply at millisecond latencies.

**A fresh DI scope per database operation**, the same as one HTTP request. All variants pay
for `AppDbContext` creation and connection-pool checkout equally.

**Middleware is measured in memory.** `TestServer` bypasses Kestrel and the network, so what
remains is the ASP.NET Core pipeline itself. Logging goes through real Serilog, with the same
enrichment and `Microsoft.AspNetCore` override as `appsettings.json`, into a sink that
discards events. That measures template binding and `LogContext` enrichment but not console
I/O, which depends on the terminal rather than the code.

---

## Results

Baseline captured on 2026-11-21. BenchmarkDotNet v0.15.8, .NET 10.0.14, Windows 11, Intel Core
i9-12900KS, SQL Server 2022 in Docker Desktop (WSL2) on the same machine. Absolute numbers
depend on hardware; compare ratios.

### EF Core vs Dapper: `GetTasksFiltered`, 10,000 tasks

| Variant | Scenario | Median (P50) | P95 | P99 | Allocated | Ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| EF Core (production handler) | FirstPage | 19.03 ms | 23.41 ms | 24.75 ms | 439 KB | 1.00 |
| EF Core (projection) | FirstPage | 10.32 ms | 12.25 ms | 12.68 ms | 194 KB | 0.56 |
| Dapper | FirstPage | 8.72 ms | 9.60 ms | 10.04 ms | 126 KB | 0.46 |
| EF Core (production handler) | SearchAndFilter | 22.34 ms | 25.64 ms | 26.99 ms | 464 KB | 1.00 |
| EF Core (projection) | SearchAndFilter | 19.21 ms | 20.88 ms | 21.62 ms | 201 KB | 0.87 |
| Dapper | SearchAndFilter | 17.68 ms | 19.16 ms | 19.51 ms | 126 KB | 0.80 |
| EF Core (production handler) | DeepPage | 33.19 ms | 42.52 ms | 45.57 ms | 1,265 KB | 1.00 |
| EF Core (projection) | DeepPage | 17.35 ms | 19.39 ms | 19.79 ms | 250 KB | 0.52 |
| Dapper | DeepPage | 19.40 ms | 21.09 ms | 21.66 ms | 190 KB | 0.58 |

Scenarios:

- **FirstPage:** no filters, 20 rows, default sort.
- **SearchAndFilter:** `search=billing` plus `priority=High`, sorted by title.
- **DeepPage:** page 100 of 100-row pages, which is offset 9,900.

The three variants:

- **EF Core (production handler):** the real `GetTasksFilteredHandler`. It loads tracked
  aggregates with `Include(Comments)` and `Include(Assignee)`, counts comments in memory, and
  then makes a second round-trip for display names.
- **EF Core (projection):** the same filters and ordering, but it does a `Select` into
  `TaskSummaryDto` with SQL `COUNT` subqueries, uses no tracking, and finishes in two
  round-trips.
- **Dapper:** hand-written SQL that returns the count and the page in one `QueryMultiple`
  round-trip.

What this shows:

- **Most of the gap is query shape, not EF Core.** Rewriting the handler as a projection
  recovers 13–48% of latency and 56–80% of allocations. After that, Dapper's remaining edge
  is 1.5–1.6 ms (one round-trip fewer, no expression-tree translation), and on the deep page
  the projection is actually faster. That supports the codebase's rule of EF Core by default
  and Dapper for reporting queries: on a filtered list, the right LINQ gets you most of the
  way.
- **The production handler's tail is the widest.** P99 is about 1.4× the median on the deep
  page. That comes from change-tracking 100 aggregates and materializing every comment row
  through the `Include` join, just to count them.
- **Offset pagination needs a deferred join once subqueries are involved.** The first Dapper
  draft put the `COUNT` subqueries directly in a `SELECT … OFFSET 9900`, and SQL Server
  evaluated them for every row it walked past: 134 ms, 4× slower than the production
  handler. Paging the IDs first in a CTE (or a `Contains` subquery in EF) and computing counts
  only for those 100 rows brought it down to 19 ms. That's a useful lesson for any future
  Dapper query here.

**Known defect, found by the parity check.** The production handler never loads
`Attachments`, so `TaskSummaryDto.AttachmentCount` is always `0` in `GET /api/v1/tasks`.
`GetBoardTasks`, `GetProjectTasks`, `GetUserTasks` and `GetBoardById` load neither
collection, so their `CommentCount` is also always `0`. The projection variant is the fix and
also the faster query. Because of this defect, the handler is exempt from the field-level
check on those two count fields only.

### Manual mapping vs Mapster: `TaskMapper.ToDto()`

Each task has an assignee, 3 comments, 2 attachments and a concurrency token, so every mapping
rule runs.

| Tasks | Manual (`TaskMapper`) | Mapster | Ratio | Allocated (manual / Mapster) |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 0.60 µs | 1.84 µs | 3.05× | 3.67 KB / 4.28 KB |
| 100 | 62.9 µs | 191.8 µs | 3.05× | 362 KB / 364 KB |
| 10,000 | 42.0 ms | 55.5 ms | 1.33× | 36.2 MB / 36.3 MB |

Manual mapping is about 3× faster at realistic page sizes. At 10,000 items both are
dominated by allocating the DTO graph (Gen2 collections), which narrows the gap. Mapster's
per-object overhead comes from the `MapContext` parameter lookups. It needs them because the
per-request display-name dictionary can't be baked into a compiled, shared mapping.

The speed gap isn't the deciding argument; configuration is. Mapster still needed 8 explicit
`Map` rules across the three types (two display-name lookups, the Base64 ETag, comment
filtering and ordering, attachment ordering, the `StorageUrl` rename, the edited-only
`UpdatedAt`, and `_links`), which covers every member where `TaskMapper` does more than copy a
value. It also moved compile-time errors
to run-time configuration errors, because a misspelled property fails when the mapping is
compiled at startup rather than at build time. This keeps the "Mapster only if boilerplate
becomes excessive" decision where it is: it doesn't remove much boilerplate here, and it's
slower.

### Middleware pipeline overhead

`GET /ping` through `TestServer`, cumulative in `Program.cs` order, 3 launches × 50
iterations:

| Stage | Median | Mean ± StdDev | Allocated | Δ allocated |
| --- | ---: | ---: | ---: | ---: |
| Bare (routing + endpoint) | 9.47 µs | 10.83 ± 3.92 µs | 11.62 KB | — |
| + `CorrelationIdMiddleware` | 11.43 µs | 12.61 ± 4.71 µs | 12.56 KB | +0.94 KB |
| + `RequestLoggingMiddleware` | 15.04 µs | 16.26 ± 4.60 µs | 14.63 KB | +2.07 KB |
| + `GlobalExceptionMiddleware` (full) | 13.67 µs | 15.02 ± 4.34 µs | 14.66 KB | +0.03 KB |

The full stack costs about 4–5 µs and 3 KB per request. Request logging, meaning Serilog
template binding and property capture, is most of that. Correlation ID adds a logger-scope
dictionary and an ID string, and exception handling is just a `try` block on the happy path.
Treat the timings as an upper bound: the standard deviation is about as large as the effect,
and the last two stages overlap within noise. The allocation deltas are deterministic, so
they're the reliable signal. Against the 9–20 ms database queries above, the overhead is
about 0.05% of a typical request.

---

## Load test

`tests/load/task-update-contention.js` runs 50 virtual users for 60 seconds. Each one reads a
task (capturing its `ETag`) and writes it back with `PUT /api/v1/tasks/{id}` and `If-Match`.
All writes target a pool of 5 `InProgress` tasks, so they collide constantly.

Thresholds (the run fails if any is crossed):

| Threshold | Why |
| --- | --- |
| `task_update_unexpected_outcome` rate == 0 | Every write must be a `204`, or a `409` with an `application/problem+json` body. Anything else (5xx, a 2xx on a stale ETag, 412, 429) fails. |
| `task_updates_conflicted` count > 0 | Proves contention actually happened, so a pass isn't vacuous. |
| `PUT` p95 < 500 ms, p99 < 1 s | Latency budget for the write path under contention. |
| `checks` rate > 0.99 | Reads return `200` with an `ETag`. |

### Running it

1. Seed the database. This prints the connection string and credentials:

   ```bash
   dotnet run -c Release --project tests/Ordinis.Benchmarks -- seed
   ```

2. Start the API against that database. The rate limits are raised because every virtual
   user shares one login, and the per-user default of 500 requests a minute would turn this
   into a rate-limiter test:

   ```powershell
   $env:ConnectionStrings__DefaultConnection = "<printed by step 1>"
   $env:DatabaseProvider = "SqlServer"
   $env:ASPNETCORE_ENVIRONMENT = "Production"
   $env:ASPNETCORE_URLS = "http://localhost:5080"
   $env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   $env:RateLimiting__SlidingWindow__PermitLimit = "1000000"
   $env:RateLimiting__FixedWindow__PermitLimit = "1000000"
   dotnet run -c Release --project src/Ordinis.Api --no-launch-profile
   ```

3. Run k6 from its Docker image. No local install is needed:

   ```bash
   docker run --rm -i -e BASE_URL=http://host.docker.internal:5080 \
     grafana/k6:2.4.0 run - < tests/load/task-update-contention.js
   ```

   Tunables, passed with `-e`: `VUS` (default 50), `DURATION` (`60s`), `HOT_TASKS` (5; fewer
   means more contention), and `ORG_SLUG`/`EMAIL`/`PASSWORD` to target a different dataset.

### Results

Same machine and date as above, with the API in Release on Kestrel:

| Metric | Value |
| --- | ---: |
| Read + write cycles | 91,619 (1,520/s) |
| Writes accepted (`204`) | 9,651 |
| Writes rejected as stale (`409` Problem Details) | 81,968 (89.5%) |
| Unexpected outcomes (5xx, stale write accepted, …) | **0** |
| `PUT` latency p50 / p95 / p99 | 16.8 / 29.7 / 40.5 ms |

The 89.5% conflict rate is the point: with 50 writers on 5 rows, about 10 writers race for
each version, and exactly one can win. Every loser got a clean `409` and nobody silently
overwrote anyone. The ETag / `If-Match` / `RowVersion` chain described in
[CONCURRENCY.md](CONCURRENCY.md) holds end to end under load. A real client would re-fetch
and retry on `409`; this script deliberately doesn't, so it measures raw contention.

While the script was being built, an early run reported about 20% unexpected outcomes. They
were `422 task.terminal-state` responses: one of the first five tasks was `Done`, and the
domain correctly refuses to edit terminal tasks. The hot pool is now filtered to `InProgress`
tasks. The script logs the first few unexpected responses per virtual user, so a failing run
explains itself.
