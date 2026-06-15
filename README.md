# LR2 Skin Editor Next

새로 재구축 중인 LR2 `.lr2skin` 편집기입니다.

## 현재 기능

- `.lr2skin` 열기
- 새 `.lr2skin` 생성
  - `#INFORMATION`
  - `#RESOLUTION,width,height`
  - `#ENDOFHEADER`
  - 기본 runtime timing 명령
- 메인 스킨 파일 원문 코드 편집
- 메인 스킨 파일 저장 / 다른 이름 저장
- `#INCLUDE` 재귀 파싱
- `#CUSTOMFILE` 기본값을 사용한 `*` include 경로 해석
- `#RESOLUTION,width,height` 읽기 및 삽입/수정
- 이미지 자산 추가
  - PNG/BMP/TGA 지원
  - 스킨 파일 폴더의 `assets` 하위로 복사
  - 기존 파일명 충돌 시 `_1`, `_2` suffix 사용
- 간단 제작 모드
  - `#SRC_IMAGE/#DST_IMAGE` 이미지 오브젝트 추가
  - `#SRC_NUMBER/#DST_NUMBER` 숫자 오브젝트 추가
  - `#FONT` 및 `#SRC_TEXT/#DST_TEXT` 텍스트 오브젝트 추가
  - DST time/position/size/alpha/RGB/blend/filter/angle/center/loop/timer/op 필드 입력
- 간단 모드에서 선택 오브젝트의 SRC/DST 좌표 수정
- LR2 로더 참조 기반 미리보기
  - `#CUSTOMOPTION` 기본 선택과 `#IF/#ELSEIF/#ELSE/#ENDIF` 반영
  - DST 위치/크기/alpha/밝기/회전/loop/acc 보간
  - SRC div/cycle 프레임 선택 및 crop
  - sortID 순서 렌더링
  - `.\LR2files\...` / `LR2files\...` 루트 상대 경로 해석
  - PNG/BMP 등 WPF 지원 이미지와 24/32bit TGA 로딩
  - `Playing`, `Ready`, `Scene`, `All timers` 프리뷰 모드
- UI 없이 파서 검증:

```powershell
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
dotnet run --project .\SkinEditorNext.csproj -- --render-png "path\to\skin.lr2skin" 1000 ".build\preview.png" playing
dotnet run --project .\SkinEditorNext.csproj -- --create-skin ".build\new.lr2skin" 0 1280 720 "New LR2 Skin"
dotnet run --project .\SkinEditorNext.csproj -- --create-skin-smoke ".build\new-image.lr2skin" "path\to\image.png"
```

## 아직 제한

- include 파일은 읽기/미리보기 대상이며, 저장은 현재 연 메인 `.lr2skin`만 수행합니다.
- 제작 UI가 새로 추가하는 명령은 현재 메인 파일 끝에 append합니다.
- 미리보기는 LR2의 로딩/배치 규칙을 포팅한 시뮬레이션입니다. 실제 LR2 런타임의 모든 게임 상태, 모든 내장 option, BGA/video/DXA, 텍스트 렌더링까지 완전 재현하지는 않습니다.
- WPF가 직접 읽지 못하는 리소스는 미리보기에서 박스 표시로 대체됩니다.
- 노트/게이지/버튼/슬라이더 전용 제작 UI와 include 파일 편집기는 아직 없습니다.
