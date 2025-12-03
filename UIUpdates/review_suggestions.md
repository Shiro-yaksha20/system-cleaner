# UI Review & Suggestions for Uninstaller Page

## 1. Layout & Resizing
- **GridSplitter Visibility**: The `GridSplitter` is currently transparent (`Background="Transparent"`). Users may not realize the layout is resizable.
  - *Suggestion*: Add a visible handle or change the background color to `{DynamicResource BorderBrush}` or a subtle gray.
- **Residual Items Section**: The "Detected Residual Items" DataGrid is constrained to a fixed height of `180` pixels inside a `Grid` row. Even if the user resizes the bottom pane to be larger, this list will not expand vertically.
  - *Suggestion*: Change the `RowDefinition` to `*` or allow it to expand to fill available space in the left column.
- **Browser Extensions List**: The `ListBox` for extensions has no height constraint. If a user has many extensions, this list will grow indefinitely, pushing the "Remove" button and subsequent content far down.
  - *Suggestion*: Set a `MaxHeight` (e.g., `200`) or wrap it in a container that shares space with the Residual Items if possible.

## 2. Visual Consistency
- **Search Box**: The search box has a fixed width of `220`.
  - *Suggestion*: Consider making it flexible (e.g., `Width="*"`) or wider to accommodate longer queries.
- **Button Widths**: Many buttons have hardcoded widths (e.g., `Width="140"`).
  - *Suggestion*: Use `MinWidth` instead of fixed `Width` to allow buttons to grow with their content (important for localization or font scaling).
- **DataGrid Columns**: The "Name" column in the main application list has a fixed width of `220`. Long application names might be truncated.
  - *Suggestion*: Use `Width="*"` for the Name column and fixed widths for columns with predictable content (like Version or Size).

## 3. User Feedback (Empty States)
- **Empty Lists**: There is currently no visual feedback if the "Applications", "Residual Items", or "Browser Extensions" lists are empty. The DataGrids/ListBoxes simply appear blank.
  - *Suggestion*: Add an overlay or a visibility toggle to show a "No items found" message when the collections are empty.

## 4. Code Organization
- **Resource Location**: The styles `ToolbarButtonStyle` and `ToolbarAccentButtonStyle` appear to be defined in `MainWindow.xaml` rather than `App.xaml`.
  - *Suggestion*: Move these shared styles to `App.xaml` (or a merged dictionary) so they are accessible to all views at design time and runtime without relying on the visual tree hierarchy.

## 5. Accessibility
- **Images**: The icon image in the header lacks an `AutomationProperties.Name`.
- **Contrast**: Ensure the `GridSplitter` (if made visible) has enough contrast against the background.

## 6. Interaction
- **Nested Scrolling**: The "Residual Items" DataGrid has its own scrollbar, but it is placed inside a parent `ScrollViewer` (the bottom pane). This can lead to "scroll trapping" where the user tries to scroll the page but gets stuck in the DataGrid, or vice versa.
  - *Suggestion*: Ensure the inner DataGrid handles mouse wheel events correctly or consider a layout that avoids nested scrolling (e.g., making the bottom pane a Grid with independent scrollable areas).

## 7. Threading Issues (FIXED)
- **Cross-thread Access on ICollectionView**: Binding directly to `ApplicationsView.IsEmpty` caused the error "The calling thread cannot access this object because a different thread owns it."
  - *Root Cause*: The `ICollectionView` is created on the UI thread, but the underlying `ObservableCollection` is modified from background threads (even with `EnableCollectionSynchronization`). Accessing properties like `IsEmpty` on the `ICollectionView` from the UI thread during a background update triggers the cross-thread exception.
  - *Solution*: Added a dedicated `HasApplications` property on the ViewModel that is explicitly raised via `RaisePropertyChanged` on the UI thread after collection updates. The XAML now binds to this property instead of `ApplicationsView.IsEmpty`.
  - *New Converter*: Created `InverseBoolToVisibilityConverter` to show "No applications found" message when `HasApplications` is `false`.
