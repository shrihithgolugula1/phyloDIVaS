# Supervised lab readiness — 10 November 2026

Verdict in progress. Do not infer readiness from successful builds.

| Severity | Finding and evidence | Repair / status |
| --- | --- | --- |
| P0 | Status endpoint declares completion when any HyPhy dataset is OK, even with missing methods | Tighten completion evidence; test/build pending |
| P0 | Existing history retrieval previously timed out; complete replay not established | Fresh authenticated history replay required |
| P0 | Multi-user cache/request isolation not demonstrated | Inspect credential scopes and run focused tests; no load claim |
| P1 | Fresh users need SDK/Node/certificate and separate account/database setup | Existing launcher tests; verify clean source, document lab path |
| P1 | Search stale-query/download behavior | Targeted parallel audit in progress |

Architecture: React/Vite; seven .NET 8 APIs; PostgreSQL/Cognito for account
features; NCBI search/download; request-scoped Galaxy integration. Original
archive preserved. Work is in the authoritative modern source checkout; published
source is exported separately, without credentials or private audit data.

Verification and exact remaining steps will be updated before handoff.
