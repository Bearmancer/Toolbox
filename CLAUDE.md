# Formatting

Run `dprint fmt .` after any set of file edits, before reporting work as done or ready.

Before every `git commit` or `git push`, run `dprint fmt .` first. A pre-commit hook
(`.git/hooks/pre-commit`) also formats staged files automatically and a pre-push hook
(`.git/hooks/pre-push`) blocks pushing unformatted code — but do not rely on the hooks
alone; run it yourself first so formatting diffs land in the commit you intend, not a
surprise amend.

Note: `.git/hooks/` is not tracked by git and will not survive a fresh clone.
