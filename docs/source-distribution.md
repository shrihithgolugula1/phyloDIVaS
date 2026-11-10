# Source distribution and backups

The public application source is available at
https://github.com/shrihithgolugula1/phyloDIVaS.
The repository root is the current application, not a collection of old versions.
Generated builds, development credentials, private result audits, and local
runtime data are excluded from publication. Settings containing connection
strings or API secrets are exported with empty values; users configure their
own external services as described in the README.

On the maintainer's configured Mac, a user-level source backup watcher checks
saved edits every 60 seconds and requires 30 seconds of stability before pushing
a source snapshot. It starts at login and uses existing Git authentication.
Network or authentication failures are retried; a sleeping/offline computer cannot
push until it is awake and connected again. This watcher is not installed on
users' computers by cloning or launching the application.

The development directory remains authoritative; the separate publication
checkout is generated. Edit application source in the development directory.
Do not edit the publication checkout directly, because later exports replace its
tracked source. An export never edits the development source or archival copies.

Cloning the default branch gets the latest successfully pushed snapshot.
Existing clones require `git pull --ff-only` and dependency setup when needed.
Automatic backup commits represent saved source, not verified releases. Use a
tested release tag for a reproducible demonstration or deployment.

The watcher is registered as `org.phylodivas.sourcebackup` on the maintainer's
Mac. Logs are kept in the development directory's ignored
`.local-run/source-backup/` folder. Automatic backups never force-push; if another
machine advances the public branch, reconcile it explicitly rather than rewriting
remote history. Local configuration is never a substitute for a tested,
independently provisioned database or Cognito setup.
