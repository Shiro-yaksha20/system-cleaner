# Startup Tab Issues Analysis

## Overview

This document analyzes the Startup tab functionality and identifies potential issues causing it to not function properly.

**Last Updated:** December 3, 2025

---

## Critical Issue: "Awaiting" Status Never Updates ✅ FIXED

### What Changed

- `StartupManagerViewModel` now constructs each row through `CreateEntryViewModelAsync`, which eagerly calls `_discoveryService.GetStartupEntryApprovalStateAsync`.
- `StartupEntryViewModel` exposes `HasToggleMetadata` and `UpdateToggleAvailability` so toggles reflect whether Windows provided approval data.
- `VerificationStatus` is populated during refresh, allowing the column to display "Confirmed" or "Mismatch" immediately. Entries lacking approval metadata automatically disable their toggle and surface a warning.

---

## Identified Issues (Updated)

### 1. ~~CheckBox and ToggleButton Dual Binding~~ ✅ FIXED

Removed redundant CheckBox control. Now only ToggleButton exists.

### 2. ~~Location Column Shows Wrong Data~~ ✅ FIXED

Changed to "Scope" column with correct binding.

### 3. ~~No Error Handling in UI~~ ✅ FIXED

Added Issues panel, loading overlay, and empty state.

### 4. **Confirmation Column Design Flaw** ✅ FIXED

- Implemented Option B: the refresh pipeline now verifies every entry against `_discoveryService.GetStartupEntryApprovalStateAsync`.
- The column shows "Confirmed" when Windows reports the same state, "Mismatch" when divergent, and toggles are disabled (with issues logged) when approval metadata is missing.
- See `StartupManagerViewModel.InitializeVerificationAsync` and `StartupEntryViewModel.UpdateToggleAvailability` for details.

### 5. **GetApprovalState Logic May Return Unexpected Values** ✅ FIXED

**Location:** `StartupDiscoveryService.cs` line 341-354

```csharp
private static bool? GetApprovalState(RegistryKey key, string valueName)
{
    try
    {
        if (key.GetValue(valueName) is not byte[] data || data.Length == 0)
        {
            return null;  // <-- No approval entry = null (not enabled/disabled)
        }

        return data[0] switch
        {
            2 => true,   // Enabled
            3 => false,  // Disabled
            var other => other != 0  // <-- Anything else non-zero = enabled?
        };
    }
    catch
    {
        return null;
    }
}
```

**Fix:** Only `0x02` and `0x03` now map to enabled/disabled. Any other value (including `0x00`) returns `null`, ensuring corrupted payloads don't masquerade as "Enabled".

### 6. **Approval State Lookup Uses Wrong Key for Some Entries** ✅ FIXED

When loading approval states, the code uses `$"{location.Scope}|{name}"` as the lookup key:

```csharp
// In LoadApprovalStates:
states[$"{location.Scope}|{name}"] = state.Value;

// In GetStartupEntriesAsync:
var stateKey = $"{location.Scope}|{name}";
var isEnabled = approvalStates.TryGetValue(stateKey, out var enabled) ? enabled : true;
```

**Fix:** Approval state lookups now include hive, registry view, sub-key, scope, and value name via `BuildApprovalStateKey`, eliminating collisions between Run, Run32, RunOnce, etc.

### 7. **SupportsToggle May Be True Even When Toggle Will Fail** ✅ FIXED

- `StartupEntryViewModel` now exposes `HasToggleMetadata` and `_hasApprovalState`; the binding evaluates to `false` whenever Windows withholds approval metadata.
- During refresh we disable toggles (and log an issue) if `_discoveryService.GetStartupEntryApprovalStateAsync` can't read the approval data, so users no longer interact with entries destined to fail.

### 8. **desktop.ini and System Files Showing as Startup Entries** ✅ FIXED

**Root Cause:**
The `ReadStartupFolderEntries` method used `Directory.EnumerateFiles(path, "*")` which returns ALL files in the Startup folder, including:
- `desktop.ini` - Windows folder configuration file (hidden/system)
- `thumbs.db` - Windows thumbnail cache (hidden/system)
- Any other non-executable files

These files:
1. Are not actual startup programs
2. Don't have entries in `StartupApproved` registry (so verification returns `null`)
3. Should never be shown to users

**Fix Applied:**
Added `IsSystemOrHiddenFile` helper that filters out:
- Files with Hidden or System attributes
- Known system files (`desktop.ini`, `thumbs.db`)
- Files without valid startup extensions (`.exe`, `.bat`, `.cmd`, `.lnk`, `.vbs`, `.ps1`)

**Code Location:** `StartupDiscoveryService.ReadStartupFolderEntries` now calls `IsSystemOrHiddenFile` before adding entries.

---

## Recommended Fix for "Awaiting" Issue

Implemented Option B (verification on load). The pseudocode above now lives in `StartupManagerViewModel.CreateEntryViewModelAsync` and `InitializeVerificationAsync`.

---

## Code References

| File | Purpose |
|------|---------|
| `StartupPage.xaml` | UI layout, data bindings |
| `StartupEntryViewModel.cs` | Entry wrapper, toggle logic, VerificationStatus |
| `StartupManagerViewModel.cs` | List management, refresh, toggle operations |
| `StartupDiscoveryService.cs` | Registry reading, approval state management |
| `StartupEntry.cs` | Data model for startup items |

---

## Testing Checklist

- [x] Verify startup entries load on app start
- [ ] Test toggle with admin rights
- [ ] Test toggle without admin rights (should show permission error)
- [ ] Test with empty startup registry keys
- [ ] Test startup folder items separately from registry items
- [x] Check if refresh button works
- [x] Verify status message updates correctly
- [x] **NEW:** Fix "Awaiting" to show meaningful status
