# SkinEditorNext

SkinEditorNext is a WPF editor for LR2 `.lr2skin` files. It focuses on safe
inspection, command-level editing, and a preview surface that can be switched
between read-only and edit mode.

## Features

- Open and parse `.lr2skin` files, including `#INCLUDE` files.
- Create a basic LR2 skin file with `#INFORMATION`, `#RESOLUTION`, runtime
  timing commands, and optional starter objects.
- Edit raw skin text and save the main skin file.
- Add PNG/BMP/TGA image assets. Assets under an `LR2files` tree are written as
  LR2-loadable `LR2files\...` paths.
- Preview LR2 objects with image loading, custom options, basic timer modes,
  interpolation, crop/div/cycle frame selection, alpha/RGB, angle, blend, and
  sort order handling.
- Inspect the selected preview object in the right overlay, including source
  file, line, image slot, SRC/DST data, resolved image path, and a thumbnail.
- Toggle preview read-only/edit mode with `L`.
- In edit mode, move the selected preview object by 1 px with arrow keys or by
  dragging it with the mouse. Main skin and include-file objects are both
  written back to their owning file.
- Show LR2 command help from `skinHelper.txt` and object groups from
  `skinObjGroup.txt` in the Help tab.

## Command-Line Checks

```powershell
dotnet build .\SkinEditorNext.csproj
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
dotnet run --project .\SkinEditorNext.csproj -- --render-png "path\to\skin.lr2skin" 1000 ".build\preview.png" playing
dotnet run --project .\SkinEditorNext.csproj -- --create-skin ".build\new.lr2skin" 0 1280 720 "New LR2 Skin"
dotnet run --project .\SkinEditorNext.csproj -- --create-skin-smoke ".build\new-image.lr2skin" "path\to\image.png"
```

## Release Build

The project can be published as a self-contained Windows x64 build:

```powershell
dotnet publish .\SkinEditorNext.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .build\release\SkinEditorNext-win-x64
```

Zip the output folder for distribution.

## Current Limits

- Preview is an editor approximation of LR2 loading/drawing behavior. It does
  not fully emulate every gameplay state, option, BGA/video/DXA, text renderer,
  or LR2 runtime side effect.
- Unsupported image resources are shown as placeholders in preview.
- Object creation UI is still intentionally basic; advanced command authoring
  may require raw text editing.
