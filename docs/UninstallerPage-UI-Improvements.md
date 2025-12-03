# Uninstaller Page UI Improvements

This document outlines identified UI issues in `UninstallerPage.xaml` and recommended improvements for better UX and visual consistency.

---

## ✅ Implemented Changes (Round 1)

The following improvements have been applied:

| Change | Status |
|--------|--------|
| DropShadow effects on all cards | ✅ Done |
| Search placeholder text | ✅ Done |
| Selection count display | ✅ Done |
| Residual panel empty state | ✅ Done |
| Health Insights empty state | ✅ Done |
| Keyboard shortcuts (F5, Delete, Shift+Delete) | ✅ Done |
| Segoe MDL2 icon instead of external URL | ✅ Done |
| Button spacing consistency | ✅ Done |

---

## ✅ Round 2 Fixes (Current Build)

### 1. Health Insights Empty State

- **Fix:** Empty-state message now binds to `Uninstaller.HealthInsights.Count`, so it only appears when the collection is truly empty.
- **Code:** `UninstallerPage.xaml` now uses the existing `CollectionEmptyToVisibilityConverter` on the `Count` property.

### 2. Residual Panel Messaging

- **Fix:** Added `HasScannedResiduals` flag, multi-trigger visibility logic, and a dedicated "Run Powerful Scan" prompt.
- **Code:** `UninstallerViewModel` tracks `_hasScannedResiduals`; `UninstallerPage.xaml` shows mutually-exclusive overlays (no selection vs. scan prompts vs. empty state).

### 3. Browser Plug-in Remove Button

- **Fix:** Button now sits directly under the list with `HorizontalAlignment="Left"` and `Margin="0,8,0,0"` for consistent spacing.

### 4. Medium-Priority Polish

- Selection badge hides when nothing is selected.
- Column widths tightened (Size 90px, Install Date 110px).
- Row hover highlight added (`DataGrid.RowStyle`).
- Card shadows darkened to `Opacity=0.18` and toolbar icon enlarged to `26px`.

### 5. Additional Enhancements

- Added select-all checkbox in the header (with backing logic in `UninstallerViewModel`).
- Search box now includes an inline magnifier glyph.
- Status bar switches to `Uninstaller.SelectedSizeDisplay` when the Uninstaller tab is active.
- Empty residual overlay uses the broom icon (`&#xE9F5;`).
- Added total selected size tracking (`SelectedSizeDisplay`) and synced it with the footer text.

---

## 🟡 Medium Priority Suggestions

*(Kept here for reference; all items have been implemented in the current build.)*

| Area | Issue | Suggestion |
|------|-------|------------|
| **Selection Count** | "0 selected" always visible | Hide when 0, or dim it to reduce visual noise |
| **Column Widths** | "Size" and "Install Date" columns too wide | Reduce from 110/140 to 90/110 |
| **DataGrid Hover** | No visual highlight on row hover | Add subtle hover background via RowStyle |
| **Card Shadows** | Shadows very subtle (0.12 opacity) | Consider 0.18-0.2 for more visual depth |
| **Toolbar Icon** | Icon looks small/faint | Increase FontSize to 24-26 |

---

## 🟢 Polish Suggestions

*(Also completed; table remains for historical context.)*

| Suggestion | Description |
|------------|-------------|
| **Select All Checkbox** | Add checkbox in DataGrid header to select/deselect all apps |
| **Search Icon** | Add magnifying glass icon inside search TextBox |
| **Status Bar Sync** | "Selection Size: 0 B" in footer should update with selection |
| **Empty Residual Icon** | Use a more relevant icon (e.g., `&#xE9F5;` - broom) instead of search icon |

---

## 📊 Updated Priority Matrix

| Priority | Issue | Status |
|----------|-------|--------|
| 🔴 Critical | Health Insights empty state overlapping | ✅ Fixed |
| 🔴 Critical | Dual empty states in Residual panel | ✅ Fixed |
| 🟡 Medium | Remove button positioning | ✅ Fixed |
| 🟡 Medium | Hide "0 selected" when zero | ✅ Fixed |
| 🟡 Medium | DataGrid hover effect | ✅ Fixed |
| 🟢 Low | Select All checkbox | ✅ Fixed |
| 🟢 Low | Search icon | ✅ Fixed |

---

## 📝 Summary

All issues documented in the tagged review are now resolved:

1. Health Insight and Residual panels show the right empty-state copy at the right time.
2. Residual scans track their lifecycle (`HasScannedResiduals`) so guidance stays contextual.
3. Browser plug-in actions, selection badges, search UX, footer metrics, and visual polish now align with the rest of the app.

Future iterations can build on this baseline (e.g., contextual pending-items text for Uninstaller or richer hover effects), but the critical UX bugs are closed.
