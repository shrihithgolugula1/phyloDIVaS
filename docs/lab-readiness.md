# Supervised lab readiness — 10 November 2026

**Verdict: blocked for an independently verified end-to-end analysis stress test.**
Suitable for supervised UI exploration with the limitations below. Do not infer
end-to-end readiness from successful builds. No existing scientific history was
modified, cancelled, deleted, or resubmitted during this audit.

| Severity | Finding and evidence | Repair / status |
| --- | --- | --- |
| P0 | Status endpoint declares completion when any HyPhy dataset is OK, even with missing methods | Fixed: requires all three method record types, all available records OK, and no active downstream evidence. Three new regression assertions pass. Collection gene coverage remains a separate viewer check. |
| P0 | Existing history retrieval previously timed out; complete replay not established | Subsequent authorized read-only audit reached the eight-bat history and full BUSTED/aBSREL collections. UI replay still required. |
| P0 | Multi-user cache/request isolation not demonstrated | Inspect credential scopes and run focused tests; no load claim |
| P1 | Fresh users need SDK/Node/certificate and separate account/database setup | Existing launcher tests; verify clean source, document lab path |
| P1 | Search stale-query/download behavior | Fixed stale success/error/loading overwrites in genome and gene searches; organism changes invalidate old gene requests. Download errors preserve results; ZIP signature checked before saving. 23 isolated browser tests pass. |
| P1 | Record counts displayed without units; obsolete continuation label | Labels now explicitly say dataset/collection records and recover interrupted preparation |
| P1 | Optional external tree context not validated; divergence-time flag could imply a universal saturation cutoff | External context disabled for lab beta; reference subtree remains. Flag now explicitly says time alone cannot establish saturation or invalidate an analysis. |

Architecture: React/Vite; seven .NET 8 APIs; PostgreSQL/Cognito for account
features; NCBI search/download; request-scoped Galaxy integration. Original
archive preserved. Work is in the authoritative modern source checkout; published
source is exported separately, without credentials or private audit data.

## Executed checks

| Type | Evidence | Limit |
| --- | --- | --- |
| Browser/live | Analysis first-use screen renders; missing species/key disables submission. Home → Learn More → About → Go to Search navigation works. Live Explore `human` returned Homo sapiens GCF_000001405.40 plus five related primates. | Not every link exercised; no narrow-viewport signoff; no real download verified. |
| Automated fixtures | 87 pipeline assertions, including strict N0 filtering, protein/CDS pairs, workflow wiring, completion evidence. 38 frontend results tests; 12 launcher/export tests. API build passes (0 errors). 23 additional Chrome browser tests cover search races/grouping/download errors, including HTTP-200 HTML rejection. | Fixture-based, not Galaxy job execution. |
| Real local output parsing | Three native pilot JSON files, eight sequences/34 codons; read+parse+summarize = 9.1 ms on maintainer Mac, Node 22. | Local pilot, not the older bat history; no network latency included. |
| Synthetic performance | 30,000 method cards / 10,000 genes, normalization plus BH = 195.1 ms on maintainer Mac. | Repeated pilot summaries with unique HOG IDs; not real large-history retrieval or browser rendering timing. |
| Completed-history replay | Subsequent read-only Galaxy API inspection succeeded using the user-specified credential file, with no key printed or persisted. Latest BUSTED/aBSREL JSON and Markdown collections each contain 9,978 elements: 9,952 OK and 26 paused. An actual aBSREL JSON sample parses: 8 sequences, 739 codons, 13 tested branches, minimum corrected p=1. | This is API replay, not full authenticated browser recovery or all-file parsing. MEME/unique affected-gene reconciliation pending final audit completion. |
| Live smoke submission | Not executed. | No current authenticated Galaxy session; no genomes submitted. |

## Remaining P0/P1 reproduction and next actions

The observed localhost:3000 frontend serves a separate user clone at the prior
published commit. Source edits are NOT automatically hot-reloaded into that clone.
Its working tree has an untracked nested clone directory; preserve it. In the
installation actually used for the session, run `git pull --ff-only`, then stop
its launcher with Ctrl+C only when no orchestration request is active, and run
`python3 scripts/local_app.py --setup`. New builds must be loaded before claiming
live verification of this patch. Do not repeatedly clone inside an existing clone.

1. **History recovery/coverage (P0):** Resume Existing → enter an owning user's
   key and a known completed history → Load Existing History. Reproduce the
   previous long wait; compare exact per-method element states and unique HOG
   IDs against Galaxy. Repeat for an older and a partially failed history. Reload
   and reconnect using a fresh browser session. Exact success/failure counts remain
   unknown; neither 9,978 discovered genes nor a workflow badge proves success.
2. **New submission/automatic transitions (P0):** after authentication, use a
   distinct clearly named small run with three eligible Drosophila proteomes
   (minimum accepted by UI), and select only one or two strict 1:1 groups once
   OrthoFinder completes. Verify a new history ID, an invocation, active collection
   elements, and transitions through MSA, back-translation, tree inference, all
   three HyPhy tools. If a suitable completed preparation already exists, replay
   its downstream inputs rather than rerunning full genomes. Do not use All Groups.
   Submission is not end-to-end completion; mark each observed stage separately.
3. **Isolation (P0):** inspect two independent authenticated sessions using distinct
   histories. Attempt each other's private history and repeated submission. Current
   code uses AsyncLocal request API-key/header scopes, but the fallback configured
   key, process-local per-history submission locks, and persistent browser result
   cache have not received an executed cross-user isolation test. Do not advertise
   concurrent production readiness.
4. **Fresh clone (P1):** test SDK/Node/certificate setup on a lab machine without
   inherited developer secrets. Database/Cognito account features remain separately
   configured; a successful local listener does not verify these dependencies.
5. **Downloads/layout (P1):** verify exact assembly and gene filenames/content
   against NCBI, test ambiguous/common/clade queries and rapid edits, and check
   desktop plus 390px viewport. NCBI's official assembly page hit a browser-check
   gate in this audit; its contents were not verified.
   Explore exact unique species queries now show one best-ranked assembly with
   additional assemblies/related species collapsed. Ambiguous/clade queries stay
   uncollapsed rather than guessing a species. Equivalent canonical grouping in
   the analysis picker, all clade pagination boundaries, and narrow layouts have
   not been verified. Test console still reports an uncontrolled-to-controlled
   input warning; this is a remaining minor frontend issue.
6. Backend progress fixes are compiled but require a controlled API restart before
   live validation. Do not interrupt an active orchestration request to load them.

### Subsequent live collection-state fix

The real Galaxy collection metadata distinguishes `populated_state=ok` from
successful element states. Progress snapshot queries now retain `elements_states`
and derive status from those counts, rather than treating a populated collection
as a complete analysis. The observed 9,952 OK / 26 paused case is a regression
assertion; 88 pipeline checks now pass and the API compiles with zero errors.
History-wide counts of errors/paused datasets include upstream steps and old
attempts and must not be reported as failed genes. Targeted collection discovery
took 14.23 seconds in this live audit; this does not include subsequent collection
expansion/sample downloads or browser rendering.

### Focused reproduction commands

From the authoritative source checkout:

```bash
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet run --project PipelineLogicTests/PipelineLogicTests.csproj
dotnet build AnalysisAPI/AnalysisAPI.csproj
cd ClientApp
npm run test:hyphy-results
npx vitest run --config vitest.explore.config.js
npm run build
```

The isolated browser suite requires installed Chrome and permission to start its
local test server. Its first sandboxed attempt failed EPERM before any tests;
rerunning with local-server permission passed all 23. All browser API responses
in that suite are fixtures, separate from the live human-search baseline.

## Tree and instructions provenance

The integration is the web application at https://bigtreeviewer.net/, with the
bundled `ClientApp/public/bigtree-reference.btvsession` and a custom session/metadata
handoff. Official homepage retrieval supplied no readable documentation in this
audit. Full taxonomic coverage, including nonvertebrates, is unverified; the UI
integration is disabled rather than represented as a complete tree of life.
No author contact occurred. The local reference tree is an NCBI-taxonomy topology
with TimeTree estimates, distinct from OrthoFinder inference. No universal Ma
cutoff proves dS saturation.

API-key directions agree with the current official Galaxy training FAQ:
https://training.galaxyproject.org/training-material/faqs/galaxy/preferences_admin_api_key.html
(User → Preferences → Manage API key). Keys belong only in the app's temporary
input, not URLs, screenshots, reports, fixtures, or command arguments.

## Supervised session / fallback

Before participants arrive, update one verified checkout rather than repeatedly
cloning nested directories. Launch the app once, confirm all services, and verify
one completed history using the steps above. Use that history for the result demo;
then start at most one small smoke run and retain its history ID. Until replay is
verified, show the results component as under validation, not as a finished demo.

If Galaxy is slow/unavailable, use Home/About/Explore and the local tree; explain
that new submissions and private result retrieval depend on Galaxy. Existing
downloaded native JSON/Markdown can be discussed offline, explicitly identified
as previously generated files. Do not repeatedly resubmit, fabricate progress, or
interpret missing results as no selection. Record failures with time, page/action,
history/dataset IDs (never keys), and a non-sensitive screenshot.
