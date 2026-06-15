# SkinEditorNext Maintenance Notes

## Where to edit

- New skin generation and LR2 command output: `Services/Lr2SkinWriter.cs`
- LR2 parsing, include flattening, image/font slot discovery: `Services/Lr2SkinParser.cs`
- Main editor UI behavior: `MainWindow.xaml.cs`
- Main editor layout: `MainWindow.xaml`
- New skin dialog: `NewSkinDialog.xaml` and `NewSkinDialog.xaml.cs`

## Rules for future edits

- Keep generated `#SRC_*` and `#DST_*` fields in the same order as LR2's `ReadSRC` and `ReadDST`.
- Treat `#IMAGE` index as command order. Do not add a numeric slot field to `#IMAGE`.
- Do not edit include files from the simple creator UI until there is a clear include save policy.
- Add short comments when changing LR2 field order, asset copying, or preview assumptions.
- After changes, run:

```powershell
dotnet build .\SkinEditorNext.csproj
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
```
