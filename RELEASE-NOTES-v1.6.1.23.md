# v1.6.1.23 — OutputCodec Save-Validation Fix (P0 User-Impact Bug)

**Release date:** 2026-05-09
**Type:** Hotfix (high user-impact silent bug)
**Tests:** 102/102 (was 85, +17 new)
**Build:** 0 warnings, 0 errors

## What changed

The Settings `#OutputCodec` dropdown offers **12 codec choices** across 4 optgroups (Software / NVIDIA NVENC / Intel QSV / Stream-Copy). v22's deep-audit found that four code paths each carried their own inline allowlist of *different sizes*:

| Site | Allowlist |
|---|---|
| `UpscalerController.cs:1437` (Save endpoint) | **3** entries (libx264, libx265, copy) |
| `VideoFrameProcessor.cs:400` (Reconstruct) | 7 entries |
| `ProcessingMethodExecutor.cs:477` (Realtime) | 6 entries (no "copy") |
| `ProcessingMethodExecutor.cs:803` (Batch) | 12 entries (correct) |

**User-impact of the worst case:** a user on NVIDIA RTX 40 hardware picks "AV1 NVENC (RTX 40+)" in the dropdown, clicks Save -- the value is silently discarded by the 3-entry Save allowlist. `config.OutputCodec` stays at `libx264`. The next time they open Settings, the dropdown shows `libx264` (their previous value) -- many users assume "I must have clicked the wrong one" rather than diagnosing a silent rejection. Subsequent encoding runs at **5-20x slower** than the GPU could deliver, with zero error indication.

This is the worst silent-validation bug since v21 wired `RestrictToUnwatchedContent`.

## Fix

New `Services/CodecRegistry.cs` consolidates the allowlist into two HashSets:

```csharp
internal static class CodecRegistry
{
    // 12 entries -- mirrors the #OutputCodec dropdown exactly
    internal static readonly HashSet<string> OutputCodecs = new(...)
    {
        "libx264", "libx265", "libsvtav1", "libaom-av1", "libvpx-vp9",
        "h264_nvenc", "hevc_nvenc", "av1_nvenc",
        "h264_qsv", "hevc_qsv", "av1_qsv",
        "copy"
    };

    // 6 entries -- realtime pipe path (no copy, no software AV1/VP9, no AV1-HW pending validation)
    internal static readonly HashSet<string> RealtimeOutputCodecs = new(...)
    {
        "libx264", "libx265",
        "h264_nvenc", "hevc_nvenc",
        "h264_qsv", "hevc_qsv"
    };
}
```

All 4 sites now reference one of these sets. No more inline lists.

## Drift-lock regression test

The audit's lesson was that grep-based dead-config scans miss this bug class -- you need to *read the validation lambda body*, not just check that a Save endpoint exists. To prevent future drift between the UI dropdown and the registry, `CodecRegistryTests.HtmlDropdown_ListsExactlyTheCodecsInRegistry` parses the embedded `configurationpage.html` at test time, extracts every `<option value="X">` inside `<select id="OutputCodec">`, and asserts set-equality with `CodecRegistry.OutputCodecs`.

Adding a UI option without updating the registry (or removing one without removing the matching `<option>`) fails the build. Same lock principle as v19's `ModelAvailability HaveCount(5)` for the model-availability HashSet.

## Tests added (+17)

| Test | What it locks |
|---|---|
| `OutputCodecs_HasExactly12Entries_LockingDriftAgainstUI` | Set size cardinality |
| `OutputCodecs_ContainsEachUIDropdownOption [Theory, 12 InlineData]` | Each codec individually present |
| `OutputCodecs_IsCaseInsensitive_SoUIOrUserCannotBreakSaveByCasing` | StringComparer.OrdinalIgnoreCase |
| `RealtimeOutputCodecs_IsStrictSubsetOfOutputCodecs` | Realtime subset of Output, strict |
| `RealtimeOutputCodecs_DoesNotContainCopyOrSlowSoftwareCodecs` | Specific exclusions documented |
| `HtmlDropdown_ListsExactlyTheCodecsInRegistry` | THE drift-lock |

Tests grew 85 -> 102. All pass.

## What this release deliberately does NOT do

- **No realtime allowlist expansion.** `av1_nvenc` and `av1_qsv` remain excluded from the realtime pipe path because their realtime stability on RTX 40 / Arc hasn't been validated. Users who pick AV1-HW for batch still get it (full `OutputCodecs` set); only the realtime streaming path falls back. Conservative, reversible later.
- **No new properties, no new endpoints, no schema changes.** Existing v22 saved configs are bit-for-bit compatible.

## Files touched

| File | Change |
|---|---|
| `Services/CodecRegistry.cs` (new) | Single source of truth |
| `Controllers/UpscalerController.cs:1437` | Save uses CodecRegistry.OutputCodecs |
| `Services/VideoFrameProcessor.cs:400` | Reconstruct uses CodecRegistry.OutputCodecs |
| `Services/ProcessingMethodExecutor.cs:477` | Realtime uses CodecRegistry.RealtimeOutputCodecs |
| `Services/ProcessingMethodExecutor.cs:803` | Batch uses CodecRegistry.OutputCodecs |
| `JellyfinUpscalerPlugin.Tests/Services/CodecRegistryTests.cs` (new) | 17 tests, drift-lock |
| `JellyfinUpscalerPlugin.csproj`, `meta.json`, `PluginConfiguration.cs:538` | Version 1.6.1.22 -> 1.6.1.23 |
| `manifest.json`, `repository-jellyfin.json` | New v1.6.1.23 entry |
| `site/index.html`, `site/changelog.html`, `README.md` | v23 surfaced |

## Methodology lesson

This bug existed since at least v1.6.1.15 (when the codec dropdown was expanded from 3 to 12 options) but escaped 6 audits. The audit class that catches it: scan for multi-line `TryApply` lambdas in the controller -- each one with a non-trivial body can contain validation that silently discards user choices. Add to pre-release CI gate.
