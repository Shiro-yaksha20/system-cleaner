# XAML Resource Errors - December 3, 2025

## Current Error

**Exception**: `System.Windows.Markup.XamlParseException`

**Message**: `A 'DynamicResourceExtension' cannot be set on the 'BasedOn' property of type 'Style'. A 'DynamicResourceExtension' can only be set on a DependencyProperty of a DependencyObject.`

**Cause**: In WPF, the `BasedOn` property of a `Style` does NOT support `DynamicResource`. It only accepts `StaticResource`.

---

## Error Location

**File**: `SystemCleaner.App/Views/CleaningPage.xaml`  
**Line**: 44 (approximately)

```xaml
<ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem" BasedOn="{DynamicResource ModuleListItemStyle}">
        <Setter Property="ToolTip" Value="{Binding Description}" />
    </Style>
</ListBox.ItemContainerStyle>
```

---

## Root Cause Analysis

1. **Original Issue**: `ModuleListItemStyle` was defined only in `MainWindow.xaml` resources
2. **Problem**: `CleaningPage.xaml` (a UserControl) cannot access resources defined in `MainWindow.xaml` directly
3. **Attempted Fix**: Changed `StaticResource` to `DynamicResource` - this doesn't work because `BasedOn` doesn't support `DynamicResource`
4. **Style also added to App.xaml**: The style was added to `App.xaml` but it's being loaded after the page tries to use it, OR the `BasedOn="{DynamicResource ...}"` syntax is simply invalid

---

## WPF Resource Resolution Rules

| Property Type | StaticResource | DynamicResource |
|---------------|----------------|-----------------|
| Regular DependencyProperty | ✅ | ✅ |
| Style.BasedOn | ✅ | ❌ NOT SUPPORTED |
| ControlTemplate.TargetType | ✅ | ❌ |

---

## Fix Options

### Option 1: Use StaticResource (Recommended)
Since `ModuleListItemStyle` is now in `App.xaml`, change back to `StaticResource`:

```xaml
<Style TargetType="ListBoxItem" BasedOn="{StaticResource ModuleListItemStyle}">
```

**Note**: Make sure the style is defined in `App.xaml` BEFORE any page tries to use it.

### Option 2: Inline the Full Style
Remove `BasedOn` entirely and define the complete style inline in `CleaningPage.xaml`:

```xaml
<ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem">
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="8,0" />
        <!-- ... all other setters ... -->
        <Setter Property="ToolTip" Value="{Binding Description}" />
    </Style>
</ListBox.ItemContainerStyle>
```

### Option 3: Remove BasedOn and Add Only Custom Setters
If only adding `ToolTip`, just use the base style directly from App.xaml and don't extend it:

```xaml
<ListBox ItemContainerStyle="{StaticResource ModuleListItemStyle}" ...>
```

Then add `ToolTip` via `ItemTemplate` instead.

---

## Additional Issue: Duplicate Style Definitions

The style `ModuleListItemStyle` is currently defined in TWO places:
- `MainWindow.xaml` line 236
- `App.xaml` line 116

**Action Required**: Remove the duplicate from `MainWindow.xaml` to avoid confusion and potential conflicts.

---

## Files to Modify

| File | Action |
|------|--------|
| `Views/CleaningPage.xaml` | Change `DynamicResource` to `StaticResource` for `BasedOn` |
| `MainWindow.xaml` | Remove duplicate `ModuleListItemStyle` definition |

---

## Verification Steps

1. Ensure `ModuleListItemStyle` is defined in `App.xaml` (already done)
2. Change `BasedOn="{DynamicResource ...}"` to `BasedOn="{StaticResource ...}"` in `CleaningPage.xaml`
3. Remove duplicate style from `MainWindow.xaml`
4. Rebuild and test

---

## Log Entries Reference

```
Timestamp: 2025-12-03T00:21:21.4095767+05:30
Context: Dispatcher
Exception:
System.Windows.Markup.XamlParseException: A 'DynamicResourceExtension' cannot be set on the 'BasedOn' property of type 'Style'. A 'DynamicResourceExtension' can only be set on a DependencyProperty of a DependencyObject.
```
