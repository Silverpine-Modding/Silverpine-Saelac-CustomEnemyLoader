# Enemy Loader regression checks

Run from the game workspace:

```powershell
dotnet run --project "Plugin Development/CustomEnemyLoader/Tests/RegressionChecks.csproj" -c Release
```

The checks exercise text-box sizing and newline preservation, production offset math/serialization, direction interchange,
the ModdingTools shared art file protocol, pack independence from authoring files,
model deduplication and collision handling, safe embedded-source removal,
traversal rejection, and transactional cache replacement/cancellation.
All fixtures use a fresh GUID temporary directory.
The host links the framework's pure file implementations for testing; the actual
plugin references the installed ModdingTools assembly and does not embed them.

Manual text checks: paste a long paragraph into the scene description, add blank
lines with Enter, and delete text again. Confirm the box grows/shrinks and pushes
the clone/HP controls down without horizontal overflow. Test a long asset path
beside Browse too; it must wrap without gaining stored line breaks. Discard all
test changes.

Manual in-game checks: open through ModdingTools, visit every tab, switch preview
directions, drag/nudge/reset offsets, cancel a drag/picker with Escape, switch away
from unsaved edits, import an existing shared entry, and build a GLB while changing
selection to confirm cancellation. Do not save test edits into a released pack.
