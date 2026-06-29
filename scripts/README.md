# Scripts

Place small repository maintenance scripts here when they become necessary.

Do not add one-off commands as scripts unless they are repeatable and documented.

Use `memory-refresh-all.ps1` for the full manual project-memory rebuild. It orchestrates the legacy JSON refresh, canonical SQLite refresh/stale-check, and LanceDB cleanup/rebuild/eval sequence without installing hooks or enabling background automation.
