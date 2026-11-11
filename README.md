# phyloDIVaS

## Portable installation from GitHub

Install Git, Python 3.10+, the **.NET 8 SDK**, and **Node.js 22 with npm** first.
The launcher checks prerequisites; it does not install system software.

```bash
git clone https://github.com/shrihithgolugula1/phyloDIVaS.git
cd phyloDIVaS
dotnet dev-certs https --trust
python3 scripts/local_app.py --setup
```

On Windows use `py -3` instead of `python3`. Certificate trust may require an
operating-system prompt. On Linux, follow Microsoft's certificate-store guidance
if browser trust is not configured automatically. Keep certificate validation enabled.

`--setup` restores/builds the seven APIs and installs frontend dependencies with
`npm ci` from the committed lockfile. It then starts everything in one terminal
and opens **http://localhost:3000** after the frontend responds and all API ports
are listening. This verifies startup, not database/cloud connectivity.

Subsequent starts need only:

```bash
python3 scripts/local_app.py
```

Press **Ctrl+C** in that terminal to stop the services it started. Occupied ports
produce an error; the launcher never kills a pre-existing application.
Per-service logs are under `.local-run/` (ignored by Git).
Use `--check` to check prerequisites and configuration format without launching,
or `--no-browser` to suppress automatic browser opening.

### Configuration and limitations

| Feature | External dependency |
| --- | --- |
| Analysis/private results | User's Galaxy Europe account/API key, entered in the app |
| Genome search/retrieval | NCBI network access; optional NCBI key for applicable requests |
| Login/registration/account | Configured AWS Cognito resources and PostgreSQL schema/connection |
| Saved search history and feedback | Configured database/service dependencies |

Copy `local-config.example.json` to `local-config.json` for private environment
overrides, replacing the database placeholders. Double underscores represent
.NET configuration nesting, such as `ConnectionStrings__DefaultConnection`.
The portable launcher reads this file; it is ignored by Git. AWS/Cognito settings
must match those consumed by each API's Program.cs/appsettings. This setup does
not provision independent database schemas or Cognito tenants automatically.

**Enter Galaxy keys only in the app.** Do not save them in configuration or
launcher arguments. The launcher never stores a Galaxy key or uses a personal
filesystem path. Keep database/cloud credentials private.

GitHub backs up source code; it does not host this running application, back up
Galaxy histories, or copy browser result caches. Each user runs their own localhost.
Before sharing the repository, inspect tracked configuration and Git history for
private values: adding a file to `.gitignore` does not remove previous commits.

### Updating and distributing

Stop the app before updating:

```bash
git pull --ff-only
python3 scripts/local_app.py --setup
```

For demos, distribute a tested commit/release tag. Frontend dependencies use the
npm lockfile; transitive NuGet dependencies are not yet fully locked. The launcher
supports macOS/Linux and Windows conventions, but fresh-machine end-to-end tests
on all operating systems remain necessary before claiming full portability.

These new files must be committed and pushed before users cloning GitHub receive
them. Launcher unit checks require no running services:

```bash
python3 -m unittest discover -s scripts -p 'test_local_app.py'
```

## Original macOS startup

On macOS, start the full local app in one terminal with:

```bash
./start-phylodivas-single-terminal.command
```

The script starts each backend API and the React/Vite frontend, prefixes each service's logs, and stops every service when you press `Ctrl+C`.

When the frontend log reports that Vite is ready, open:

```text
http://localhost:3000
```

The script also creates missing `ClientApp/.env.local` API URL defaults. Full login, search, account, and analysis functionality still requires valid local configuration for database, Cognito, Galaxy, and NCBI credentials.

For separate service windows while debugging, run:

```bash
./start-phylodivas.command
```

### Saved results demo

On Run Analysis, choose **View demo results**, or open `/Demo` on the local
application. The demo opens separately and requires no Galaxy key. It contains
one verified eight-bat orthogroup (739 codons), with original BUSTED, aBSREL and
MEME statistics and Markdown reports. Private Galaxy identifiers and working-file
paths are omitted. This is a teaching sample, not a genome-wide biological
conclusion. Figures, per-tool downloads and a combined demo JSON download work
locally; the larger demo data is loaded only when the demo page is opened.
