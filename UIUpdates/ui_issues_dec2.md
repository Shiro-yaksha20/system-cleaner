# UI Issues & Improvements - December 2, 2025

## Issues Identified

### 1. Tab Navigation - No Active Tab Indicator
**Problem**: Users cannot visually identify which tab is currently selected. The navigation tabs in the title bar lack a clear active state indicator (glow, underline, or background highlight).

**Current State**: 
- The `MainTabButtonStyle` has `MultiDataTrigger` setters that should apply:
  - `AccentSoftBrush` background
  - `AccentBrush` underline indicator  
  - Full opacity on the indicator
- However, the indicator `Border` has `Opacity="0"` and `Background="Transparent"` as defaults

**Root Cause**: The triggers appear to set properties but the visual difference is too subtle or not rendering properly.

**Fix Required**:
1. Increase indicator height from `3` to `4` pixels
2. Add stronger background tint on selected state
3. Consider adding a left border accent or glow effect
4. Remove the default `Opacity="0"` on indicator - let triggers control it

**Files**: `MainWindow.xaml` (lines 201-315)

---

### 2. Bottom Bar (Scan/Clean) Not Visible in Full Screen
**Problem**: When maximizing the window, the bottom status bar containing "Scan" and "Clean" buttons becomes hidden or clipped.

**Current State**:
- Bottom bar is in `Grid.Row="3"` with `Height="56"`
- The main content area (`Grid.Row="2"`) uses `Height="*"` which should flex
- `RowDefinition` for Row 3 is fixed at `Height="56"`

**Root Cause**: 
- The window might have min-height constraints conflicting with maximized state
- The overlay busy indicator spans `Grid.RowSpan="4"` and uses `Panel.ZIndex="50"`, possibly interfering
- The rounded corners on the outer Border (`CornerRadius="12"`) combined with `WindowStyle="None"` may clip content at window edges

**Fix Required**:
1. Add padding to the main Grid to prevent edge clipping
2. Ensure bottom bar has minimum visibility even when content overflows
3. Test with `MinHeight` adjustments on the bottom border
4. Consider making the bottom bar sticky/floating if content overflows

**Files**: `MainWindow.xaml` (lines 598-634)

---

### 3. Day/Night Theme Toggle Not Working Properly
**Problem**: The theme toggle button in the top-right does not clearly indicate current state or may not be switching themes correctly.

**Current State**:
- `ThemeToggleStyle` uses a sliding thumb design
- Bound to `IsDarkTheme` property
- Triggers swap thumb position and icon based on `IsChecked`
- Icons use Segoe MDL2 Assets font codes: `` (sun) and `` (moon)

**Potential Issues**:
1. The icon font codes may not render correctly on all systems
2. The track background change (`AccentSoftBrush` vs `SurfaceAltBrush`) is subtle
3. No animation on toggle - feels unresponsive
4. Thumb and icons overlap, causing visual clutter

**Fix Required**:
1. Add transition animation for thumb movement
2. Use more distinct background colors for light vs dark states
3. Ensure icons are mutually exclusive (hide one when showing other)
4. Add border glow or shadow to indicate interactive element
5. Consider using standard Unicode symbols if MDL2 fails: ☀ and ☾

**Files**: `MainWindow.xaml` (lines 100-170), Theme binding in `MainViewModel.cs`

---

### 4. VirusTotal Quota Display Not Showing After API Key Added
**Problem**: After entering a VirusTotal API key in Settings, the quota badge in the bottom-right corner still shows "Add your API key" or doesn't update with actual quota data.

**Current State**:
- `HasQuotaInfo` controls visibility of the quota badge
- `QuotaSummary` and `QuotaDetails` display quota text
- Quota is refreshed via `RequestQuotaRefresh()` after successful scans
- `ApplyQuota()` method updates display based on `VirusTotalQuotaInfo`

**Root Causes**:
1. `ResetQuotaInfo()` sets generic messages but doesn't trigger immediate refresh
2. `HasApiKey` check in `ApplyQuota()` may run before key is fully propagated
3. The `_service.LatestQuota` may be null until first API call completes
4. No automatic refresh on API key change - quota only updates after a scan

**Fix Required**:
1. Add `RequestQuotaRefresh()` call in `OnApiKeyChanged` event handler
2. Show a "Fetching quota..." intermediate state
3. Add retry logic if initial quota fetch fails
4. Trigger quota refresh immediately when navigating to VirusTotal page
5. Add manual "Refresh Quota" button to force update

**Files**: 
- `VirusTotalViewModel.cs` (lines 43-73, 400-440)
- `VirusTotalPage.xaml` (lines 400-449)

---

## Cleaning Page Improvements

### Current Issues

1. **Module List Lacks Visual Feedback**
   - No icon differentiation between module types
   - Selected state is subtle
   - No item count badge per module

2. **DataGrid Styling**
   - Row heights are cramped at `30px`
   - No alternating row colors effectively applied
   - Column widths are fixed, may truncate long paths

3. **Activity Log Section**
   - Fixed height `120px` is too small for useful log viewing
   - No timestamps on log entries
   - No severity coloring (all entries same color)

4. **Button Toolbar**
   - Buttons are too close together
   - No visual grouping of related actions
   - "Open Location" button placement is awkward

### Recommended Fixes

```xaml
<!-- Module list item improvements -->
- Add module icon based on type (System, Browser, User)
- Show item count and size badge on each module
- Add tooltip with module description

<!-- DataGrid improvements -->
- Increase RowHeight to 36
- Add proper alternating row background
- Make Path column width flexible with MinWidth

<!-- Activity Log -->
- Make log expandable/collapsible
- Add timestamp to log entries
- Color code by severity (Info=gray, Warning=orange, Error=red, Success=green)

<!-- Button toolbar -->
- Group buttons: [Select All | Clear] [Open Location] [More ▼]
- Add icons to buttons
- Add spacing/separator between groups
```

**Files**: `CleaningPage.xaml`

---

## General UI Recommendations

### 1. Consistent Spacing
- Standardize margin/padding values: 4, 8, 12, 16, 20, 24, 32
- Current code has arbitrary values like 6, 10, 18 mixed in

### 2. Loading States
- Add skeleton loaders for lists while data loads
- Show progress indicators consistently

### 3. Empty States
- Add illustrated empty states for all lists
- Include action hints ("Click Scan to find cleanup items")

### 4. Responsive Layout
- Test at various window sizes
- Ensure minimum usable dimensions
- Add scroll bars where content overflows

### 5. Accessibility
- Add keyboard navigation support
- Ensure sufficient color contrast
- Add tooltips to icon-only buttons

---

## Priority Order

1. **High**: Fix bottom bar visibility (blocks core functionality)
2. **High**: Fix tab active indicator (navigation confusion)
3. **Medium**: Fix theme toggle UX (user preference feature)
4. **Medium**: Fix VirusTotal quota display (feature completeness)
5. **Low**: Cleaning page polish (quality of life)

---

## Files to Modify

| File | Changes |
|------|---------|
| `MainWindow.xaml` | Tab indicator, theme toggle, bottom bar layout |
| `MainWindow.xaml.cs` | Window state handling for maximize |
| `CleaningPage.xaml` | Layout polish, log improvements |
| `VirusTotalViewModel.cs` | Quota refresh on API key change |
| `VirusTotalPage.xaml` | Quota badge visibility logic |
| `DarkTheme.xaml` | Add accent glow/selection colors |
| `LightTheme.xaml` | Add accent glow/selection colors |
