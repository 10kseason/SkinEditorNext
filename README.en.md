# SkinEditorNext (English)

Languages: [English](README.en.md) | [Korean](README.ko.md) | [Japanese](README.ja.md)

SkinEditorNext is a WPF editor for LR2 `.lr2skin` files. It focuses on safe inspection, command-level editing, and a preview surface that can be switched between read-only and edit mode.

## Features

- Open and parse `.lr2skin` files, including `#INCLUDE` files.
- When launched from an LR2 folder, scan `LR2files\Theme` for `.lr2skin` import candidates, optionally apply Keep/SD/HD/FHD/4K presets on import, and apply SD/HD/FHD/4K `#RESOLUTION,width,height` directly to the current editor buffer.
- Create a beginner-friendly LR2 skin file with `#INFORMATION`, `#RESOLUTION`, runtime timing commands, comments, and visible note-lane/key-area/gauge/BGA guide placeholders.
- Edit raw skin text and save the main skin file.
- Add PNG/BMP/TGA image assets. Assets under an `LR2files` tree are written as LR2-loadable `LR2files\...` paths.
- Preview the selected `#IMAGE` asset in the left panel with source line, path, and image size.
- Preview LR2 objects with image loading, custom options, basic timer modes, interpolation, crop/div/cycle frame selection, alpha/RGB, angle, blend, and sort order handling.
- Inspect the selected preview object in the right overlay, including source file, line, image slot, SRC/DST data, resolved image path, and a thumbnail.
- Edit the main skin and loaded include/CSV files beside the preview, with live preview refresh while editing.
- Size the preview from `#RESOLUTION`, or from `#INFORMATION` x/y when a skin omits `#RESOLUTION`.
- In read-only mode, drag the preview to pan; zoom with the mouse wheel or `-`/`=` keys.
- Toggle preview read-only/edit mode with `L`.
- In edit mode, move the selected preview object by 1 px with arrow keys or by dragging it with the mouse. Main skin and include-file objects are both written back to their owning file.
- Show LR2 command help from `skinHelper.txt` and object groups from `skinObjGroup.txt` in the Help tab.

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

```powershell
dotnet publish .\SkinEditorNext.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .build\release\SkinEditorNext-win-x64
```

Zip the output folder for distribution. The release package includes the executable, helper tables, and English/Korean/Japanese README files.

## Current Limits

- Preview is an editor approximation of LR2 loading/drawing behavior. It does not fully emulate every gameplay state, option, BGA/video/DXA, text renderer, or LR2 runtime side effect.
- Unsupported image resources are shown as placeholders in preview.
- Object creation UI is still intentionally basic; advanced command authoring may require raw text editing.
- LR2 Theme import applies resolution presets to the editor buffer only. Use Save when you want to overwrite the imported skin file.

Derived from source code by [ GOMazk](https://github.com/GOMazk).