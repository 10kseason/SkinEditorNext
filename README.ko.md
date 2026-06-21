# SkinEditorNext (한국어)

언어: [English](README.en.md) | [한국어](README.ko.md) | [日本語](README.ja.md)

SkinEditorNext는 LR2 `.lr2skin` 파일을 위한 WPF 편집기입니다. 원본 텍스트를 안전하게 확인하고, 명령 단위 편집과 프리뷰 기반 위치 조정을 할 수 있게 만드는 것을 목표로 합니다.

## 주요 기능

- `.lr2skin` 파일과 `#INCLUDE` 파일을 열고 파싱합니다.
- LR2 폴더에서 실행하면 `LR2files\Theme` 안의 `.lr2skin`을 스캔해서 Import 목록을 만들고, Keep/SD/HD/FHD/4K 해상도 프리셋을 import 또는 현재 편집 버퍼에 바로 적용할 수 있습니다.
- New 생성 시 `#INFORMATION`, `#RESOLUTION`, 런타임 타이밍 명령, 주석, 노트 라인/키 영역/게이지/BGA 가이드 placeholder가 들어간 입문용 기본 스킨을 만듭니다.
- 원본 스킨 텍스트를 직접 편집하고 저장할 수 있습니다.
- PNG/BMP/TGA 이미지 에셋을 추가할 수 있습니다. `LR2files` 아래 에셋은 LR2에서 읽기 좋은 `LR2files\...` 경로로 기록됩니다.
- 왼쪽 패널에서 선택한 `#IMAGE` 에셋의 미리보기, 원본 라인, 경로, 이미지 크기를 확인할 수 있습니다.
- 이미지 로딩, 커스텀 옵션, 기본 타이머 모드, 보간, crop/div/cycle 프레임, alpha/RGB, angle, blend, sort order를 반영한 LR2 오브젝트 프리뷰를 제공합니다.
- 오른쪽 오버레이에서 선택한 프리뷰 오브젝트의 파일, 라인, 이미지 슬롯, SRC/DST 값, 실제 이미지 경로, 썸네일을 확인할 수 있습니다.
- `L` 키로 프리뷰 read-only/edit mode를 전환합니다.
- edit mode에서는 방향키로 1px 이동하거나 마우스로 드래그해서 위치를 바꿀 수 있습니다. main 파일과 include 파일 오브젝트 모두 자기 파일에 반영됩니다.
- Help 탭에서 `skinHelper.txt` 명령 도움말과 `skinObjGroup.txt` 오브젝트 그룹을 확인할 수 있습니다.

## 명령줄 확인

```powershell
dotnet build .\SkinEditorNext.csproj
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
dotnet run --project .\SkinEditorNext.csproj -- --render-png "path\to\skin.lr2skin" 1000 ".build\preview.png" playing
dotnet run --project .\SkinEditorNext.csproj -- --create-skin ".build\new.lr2skin" 0 1280 720 "New LR2 Skin"
dotnet run --project .\SkinEditorNext.csproj -- --create-skin-smoke ".build\new-image.lr2skin" "path\to\image.png"
```

## 릴리즈 빌드

```powershell
dotnet publish .\SkinEditorNext.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .build\release\SkinEditorNext-win-x64
```

배포 zip에는 실행 파일, helper 테이블, 영어/한국어/일본어 README가 포함됩니다.

## 현재 한계

- 프리뷰는 LR2 로딩/그리기 동작의 편집기용 근사치입니다. 모든 게임 상태, 옵션, BGA/video/DXA, 텍스트 렌더러, LR2 런타임 부작용을 완전히 에뮬레이션하지는 않습니다.
- 지원하지 않는 이미지 리소스는 프리뷰에서 placeholder로 표시됩니다.
- 오브젝트 생성 UI는 아직 기본 기능 위주입니다. 고급 명령 작성은 raw text 편집이 필요할 수 있습니다.
- LR2 Theme import의 해상도 프리셋은 편집 버퍼에만 적용됩니다. 실제 파일을 덮어쓰려면 Save를 눌러야 합니다.

[ GOMazk](https://github.com/GOMazk) 님의 소스코드에서 파생된 프로젝트입니다.