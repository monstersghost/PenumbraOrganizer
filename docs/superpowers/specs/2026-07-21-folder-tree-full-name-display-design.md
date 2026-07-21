# Folder tree: display full folder names

**Date:** 2026-07-21
**Status:** Approved, ready for implementation plan

## Problem

On the "Current Mods" tab, the "Current folder tree" panel is a fixed 330px-wide column
containing a `DataGrid`. Its "Current folder" column (`MainWindow.xaml`) uses a plain
`DataGridTextColumn`, which clips long folder paths with no way to see the full name.

## Scope

UI-only change, confined to `PenumbraOrganizer.App/MainWindow.xaml`'s "Current Mods" tab. No
ViewModel or data-layer changes — `VirtualFolderNode.Path` (`DomainModels.cs`) already holds the
full path string; the grid just doesn't display it in full.

## Design

1. **Wrap text.** Replace the "Current folder" `DataGridTextColumn` with a
   `DataGridTemplateColumn` whose cell template is a `TextBlock` with `TextWrapping="Wrap"`. The
   `DataGrid` has no fixed `RowHeight` today, so rows already auto-size to content — wrapped text
   just makes rows taller, no other change needed.
2. **Tooltip.** The same `TextBlock` gets `ToolTip="{Binding Path}"`, so hovering any row shows
   the full path in a single-line tooltip regardless of wrapping.
3. **Resizable pane.** The folder-tree column (`Width="330"`) and the mods-pane column (`Width="*"`)
   sit either side of a spacer column (`Width="14"`) that currently renders nothing interactive.
   Add `MinWidth="200"` to the folder-tree column and `MinWidth="320"` to the mods-pane column,
   and replace the spacer with a `GridSplitter` so the user can drag to widen the tree panel.

Not in scope: persisting the resized pane width across app restarts (session-only for now, per
user's explicit choice).

## Testing

Purely visual/WPF-layout change with no unit-testable behavior. Verified manually: run the app,
open "Current Mods", confirm long folder names wrap in full, hovering shows a tooltip, and the
splitter drags to resize the pane.
