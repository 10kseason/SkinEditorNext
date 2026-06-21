# SkinEditorNext Maintenance Notes

## Where to edit

- New skin generation and LR2 command output: `Services/Lr2SkinWriter.cs`
- LR2 parsing, include flattening, image/font slot discovery, and diagnostics:
  `Services/Lr2SkinParser.cs`
- Main editor UI behavior: `MainWindow.xaml.cs`
- Main editor layout: `MainWindow.xaml`
- Preview image loading: `Services/Lr2BitmapFactory.cs`
- Help-tab data shape: `Models/SkinHelpEntry.cs`, `skinHelper.txt`,
  `skinObjGroup.txt`
- New skin dialog: `NewSkinDialog.xaml` and `NewSkinDialog.xaml.cs`

## Rules for future edits

- Keep generated `#SRC_*` and `#DST_*` fields in the same order as LR2's
  `ReadSRC` and `ReadDST`.
- Treat `#IMAGE` index as command order. Do not add a numeric slot field to
  `#IMAGE`.
- When moving preview objects, preserve the owning file. Include-file objects
  must be written back to the include file, not silently copied into the main
  `.lr2skin`.
- Keep preview overlay controls non-interactive so arrow keys and mouse drags
  continue to target the preview canvas.
- Prefer LR2-loadable asset paths. When an asset is under an `LR2files` tree,
  write `LR2files\...` instead of a skin-local relative path.
- Add short comments when changing LR2 field order, asset path rules, include
  write-back behavior, or preview assumptions.

## Verification

After behavior changes, run the smallest relevant checks first:

```powershell
dotnet build .\SkinEditorNext.csproj
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
dotnet run --project .\SkinEditorNext.csproj -- --create-skin-smoke ".build\new-image.lr2skin" "path\to\image.png"
```

Before a release package, run `dotnet publish` in Release mode and smoke the
published executable with `--parse` or `--render-info`.
