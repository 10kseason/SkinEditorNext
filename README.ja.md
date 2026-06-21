# SkinEditorNext (日本語)

言語: [English](README.en.md) | [한국어](README.ko.md) | [日本語](README.ja.md)

SkinEditorNext は LR2 の `.lr2skin` ファイル向け WPF エディターです。スキン本文を安全に確認しながら、コマンド単位の編集とプレビュー上での位置調整を行うためのツールです。

## 主な機能

- `.lr2skin` ファイルと `#INCLUDE` ファイルを開いて解析できます。
- LR2 フォルダーから起動した場合、`LR2files\Theme` 内の `.lr2skin` をスキャンして Import 一覧を作成し、Keep/SD/HD/FHD/4K の解像度プリセットを import 時または現在の編集バッファーに直接適用できます。
- New 作成時に `#INFORMATION`、`#RESOLUTION`、ランタイム用タイミング命令、コメント、ノートレーン/キー領域/ゲージ/BGA のガイド placeholder を含む初心者向け基本スキンを生成します。
- スキンの raw text を直接編集して保存できます。
- PNG/BMP/TGA 画像アセットを追加できます。`LR2files` 配下のアセットは LR2 が読み込みやすい `LR2files\...` 形式のパスで記録されます。
- 左パネルで選択した `#IMAGE` アセットのプレビュー、元の行、パス、画像サイズを確認できます。
- 画像読み込み、カスタムオプション、基本タイマーモード、補間、crop/div/cycle フレーム、alpha/RGB、angle、blend、sort order を反映した LR2 オブジェクトプレビューを表示します。
- 右オーバーレイで選択中のプレビューオブジェクトのファイル、行、画像スロット、SRC/DST 値、解決済み画像パス、サムネイルを確認できます。
- `L` キーでプレビューの read-only/edit mode を切り替えます。
- edit mode では矢印キーで 1px 移動、またはマウスドラッグで位置を移動できます。main ファイルと include ファイルのオブジェクトは、それぞれの所有ファイルへ書き戻されます。
- Help タブで `skinHelper.txt` のコマンドヘルプと `skinObjGroup.txt` のオブジェクトグループを確認できます。

## コマンドライン確認

```powershell
dotnet build .\SkinEditorNext.csproj
dotnet run --project .\SkinEditorNext.csproj -- --parse "path\to\skin.lr2skin"
dotnet run --project .\SkinEditorNext.csproj -- --render-info "path\to\skin.lr2skin" 1000 playing
dotnet run --project .\SkinEditorNext.csproj -- --render-png "path\to\skin.lr2skin" 1000 ".build\preview.png" playing
dotnet run --project .\SkinEditorNext.csproj -- --create-skin ".build\new.lr2skin" 0 1280 720 "New LR2 Skin"
dotnet run --project .\SkinEditorNext.csproj -- --create-skin-smoke ".build\new-image.lr2skin" "path\to\image.png"
```

## リリースビルド

```powershell
dotnet publish .\SkinEditorNext.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .build\release\SkinEditorNext-win-x64
```

配布 zip には実行ファイル、helper テーブル、英語/韓国語/日本語 README が含まれます。

## 現在の制限

- プレビューは LR2 の読み込み/描画動作をエディター向けに近似したものです。すべてのゲーム状態、オプション、BGA/video/DXA、テキストレンダラー、LR2 ランタイム副作用を完全に再現するものではありません。
- 未対応の画像リソースはプレビュー上で placeholder として表示されます。
- オブジェクト作成 UI はまだ基本機能中心です。高度なコマンド作成には raw text 編集が必要になる場合があります。
- LR2 Theme import の解像度プリセットは編集バッファーにのみ適用されます。実際のファイルへ反映するには Save を押してください。

[ GOMazk](https://github.com/GOMazk) 氏のソースコードから派生したプロジェクトです。