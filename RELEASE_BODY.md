v1.58 — no changes; a guard against deleting working code

**Nothing in this release changes what Kronos does.** 1.57 is correct and stays correct. This exists to stop a mistake that has not been made yet.

## What happened

The audit of 1.57's fixes reported two statements in the database migration as dead code:

```sql
DROP INDEX IF EXISTS GameAsset_id
DROP INDEX IF EXISTS GameHistory_game_id
```

Its reasoning was that `sqlite-net` names its indexes `{table}_{hash}` rather than `{table}_{column}` — so those names could never match, and the statements could be deleted.

**They are load-bearing.** Acting on that report would have removed working migration code on the strength of a grep.

- `sqlite-net-e` 1.11.0 builds index names from the table and column name, so an install from before the snake_case migration really does have indexes called `GameAsset_id` and `GameHistory_game_id`.
- `ALTER TABLE ... RENAME TO` retargets an index at the renamed table but **leaves the index's own name alone**. By that point in the migration, `game_asset` carries an index still called `GameAsset_id`.
- `CreateTable` then adds `game_asset_id` alongside it. That succeeds either way.

So without those two statements, every upgraded database ends up with two indexes on the same column, one of them stale.

## What this release adds

Six tests that build a real legacy schema in a real SQLite file and demonstrate each of those steps, rather than trusting anyone's reading of the source. Both statements are pinned, so deleting either now fails a test.

The comment above them said only *"Delete old indexes if they exist"* — which is exactly why the audit concluded they were removable. It now explains the mechanism and points at the tests.

Verified by deleting one statement and watching the test fail.

## Testing

497 tests, up from 491. Three consecutive runs green.

## Downloading

`Kronos-1.58.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.