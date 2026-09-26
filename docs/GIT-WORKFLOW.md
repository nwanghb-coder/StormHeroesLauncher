# Local Git and release workflow

Frozen Stage 1 source: annotated tag `friend-0.2`, commit
`9dda358225aa0a6df1b4ee9f78664f741381ecd9` (Initial project import).
This is the imported source snapshot, not a reconstruction of pre-import development history.
Never move/delete/overwrite the tag or rewrite its history. Git tags are technically movable;
this workflow treats the release tag as permanent. Remote protection is future work.

The original binary release is identified by [its hash record](releases/friend-0.2.txt).
That file is an unchanged copy of the release-time record; its developer-test caveat is historical.
The ignored `artifacts/` directory is not backed up by Git. Preserve the original ZIP separately;
rebuilding the tag does not reproduce the frozen artifact by definition.

## Stage 2 work

`main` is the stable/frozen milestone line. `stage2-feature-complete` is the current
Stage 2 integration branch. Future substantial features may use `feature/<short-name>`:
feature branch -> offline tests/build -> commit -> merge/PR into `stage2-feature-complete`.
Do not merge Stage 2 into `main` until the milestone is intentionally frozen.
Tags are immutable milestone references.

Work on `stage2-feature-complete`. Inspect status/diffs before edits and commits.
Stage specific related files; use focused commits with a concrete problem/result description.
Run the relevant offline checks and record limitations. Never include local settings, logs,
credentials, CLI binaries or build output in commits. Increment development identity for the
next development build; use new artifact directories such as `artifacts/Stage2-0.3.0-dev.1/`.

The baseline update changes version identity and docs only. The existing portable publish
profiles remain authoritative for self-contained x64 packaging.

## GitHub synchronization and releases

Remote: https://github.com/nwanghb-coder/StormHeroesLauncher.git

At initial inspection, GitHub was unreachable. On 2026-09-26, a successful normal fetch
and remote-ref inspection confirmed remote main at the imported commit, with no remote
Stage 2 branch or release tag yet. Synchronization publishes only the Stage 2 branch and
the existing frozen tag; it does not merge into main or create a GitHub Release.
Before an explicitly requested push, inspect remote branches/tags, check for divergence,
and stop and report any unexpected divergence or tag mismatch without changing history.
Push only the requested branch/tag; never force-push the frozen baseline.

GitHub issue creation, project setup, branch/tag protection and release uploads remain future
explicitly authorized actions. Suggested issue content: scope, acceptance criteria, safety
boundaries and validation evidence. Release records should include source commit/tag, version,
Build ID, SDK/runtime, hashes, package layout and manual acceptance status.
