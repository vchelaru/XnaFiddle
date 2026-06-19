# Issue Workflow

Intentionally minimal. Grows only when a real, repeated gap warrants it.

## Open the PR as soon as work is finished

When work on an issue is finished — whether or not it needs manual testing — open a PR on GitHub right away so CI runs in parallel with the user's manual testing. If on `main`, create a branch; commit; push; open the PR with `gh pr create`. Don't wait for manual-test sign-off. The repo owner has made this the standing workflow, so open the PR without asking for per-task approval.

## Bundle incidental skill-file improvements into the PR

While working an issue we sometimes make skill-file improvements. This is normal and good — these incremental changes are important for moving faster. Include those skill-file changes in the same PR unless something obvious indicates we shouldn't.
