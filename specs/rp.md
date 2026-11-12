# Minecraft 統合版 実績維持・統合リソースパック 共通基盤・マージ開発規約

本ドキュメントは、複数の便利リソースパック機能（HUD拡張・3D視覚化・UI改善・テクスチャ改善等）を **単一のリソースパック（Resource Pack 1つ）** に統合し、**実績（Achievements）を100%有効に保ったまま** クライアントP2PマルチおよびRealmsで運用するための共通設計規約である。

AIエージェント（Antigravity）は、`rp_specs/` 配下のマークダウンファイルを参照して実装・機能追加・更新を行う際、必ず本規約を最優先で遵守すること。

---

## 1. プロジェクト分離と絶対ガードレール（実績ON保証）

* **アドオン（BP+RP）プロジェクトとの完全分離**:
  * 本プロジェクトは **純粋なリソースパック専用リポジトリ** として構築する。ビヘイビアーパック（`behavior_pack`）やスクリプト（`@minecraft/server` 等）を同一パック・同一構成に混在させてはならない。
* **実績維持のための絶対禁止事項**:
  * `manifest.json` の `modules` には `"type": "resources"` のみを定義すること（`"data"` や `"script"` は厳禁）。
  * ワールドの「実験的機能（Experiments）」トグルを1つでも要求する定義は使用禁止。
  * クライアント本体ファイルを直接書き換える改造シェーダー（`renderer/materials/*.material.bin` のバイナリ差し替え）は使用禁止（通常RPとして読み込めず、Realms自動配布やSwitch/スマホ参加が不可能になるため）。標準RP仕様で許可されている `materials/entity.material` のJSON継承定義のみ使用する。
* **クロスプレイ・Realms配慮**:
  * Switch・モバイル端末が参加してもメモリクラッシュを起こさないよう、UIスプライトや追加テクスチャは必要最小限の解像度に抑え、ジオメトリのポリゴン数も軽量に保つこと。

---

## 2. 複数パック統合時のファイル競合回避アーキテクチャ

複数のリソースパック機能を1つにまとめる際、バニラ共有ファイル（`entity/player.entity.json`, `ui/hud_screen.json`, `ui/_ui_defs.json`, `materials/entity.material`, `texts/ja_JP.lang` 等）を単純上書きすると既存機能が消失する。これを防ぐため、以下の **機能別モジュール分割 ＋ 共有ファイル加算マージ構造** を厳守する。

```text
rp_project_root/
├── rp_specs/
│   ├── 00_RP_ARCHITECTURE.md            # 本ファイル（共通規約・マージルール）
│   ├── 01_hud_chunk_and_biome.md        # 機能仕様 #01: HUDチャンク座標＆バイオーム名
│   ├── 02_armor_stand_visualizer.md     # 機能仕様 #02: 防具立てチャンク＆湧き範囲視覚化
│   ├── 03_trade_unlock_viewer.md        # 機能仕様 #03: 村人交易全レベル解放表示
│   └── 04_<next_rp_feature>.md          # 以降、随時追加されるリソースパック仕様書
├── tools/                               # ビルド補助スクリプト（パック本体には同梱しない）
│   └── generate_rp_atlases.py           # 日本語スプライトアトラスPNG自動生成スクリプト
└── resource_pack/                       # 配布・導入対象のリソースパック本体
    ├── manifest.json
    ├── pack_icon.png
    ├── entity/
    │   ├── player.entity.json           # 共有エントリ（各機能のRC/Anim/Geoを加算マージ）
    │   └── armor_stand.entity.json      # 共有エントリ（加算マージ）
    ├── materials/
    │   └── entity.material              # カスタムエンティティマテリアル定義（キー単位で追記）
    ├── models/entity/
    │   ├── rp01_hud_readout.geo.json    # 機能別にプレフィックスを付けて完全分離
    │   └── rp02_chunk_visualizer.geo.json
    ├── animations/
    │   ├── rp01_hud_readout.animation.json
    │   └── rp02_chunk_visualizer.animation.json
    ├── render_controllers/
    │   ├── rp01_hud_readout.render_controllers.json
    │   └── rp02_chunk_visualizer.render_controllers.json
    ├── textures/
    │   └── rp_custom/
    │       ├── rp01_glyph_atlas.png
    │       ├── rp01_biome_atlas.png
    │       └── rp02_visualizer_palette.png
    ├── ui/
    │   ├── _ui_defs.json                # ui/modules/*.json を配列登録
    │   ├── hud_screen.json              # $modifications による最小差分パッチのみ記述
    │   ├── trade_2_screen.json          # 最小差分オーバーライドのみ記述
    │   └── modules/
    │       └── rp01_hud_chunk_panel.json
    └── texts/
        ├── languages.json               # ["ja_JP", "en_US"]
        ├── ja_JP.lang                   # 機能別コメント区切りでキーを追記
        └── en_US.lang
```

---

## 3. JSON UI および エンティティ定義の差分実装ルール（アプデ耐性強化）

1. **JSON UI は全文コピー禁止・差分パッチ（`$modifications` / 名前空間継承）必須**:
   * バニラの `hud_screen.json` や `trade_2_screen.json` を全文コピーして書き換えると、マイクラ本体のマイナーアップデートで高確率に画面崩れが発生する。
   * 独自のUI要素は `ui/modules/rpNN_<name>.json` に切り出して `ui/_ui_defs.json` に登録し、バニラ画面ファイル側では `"modifications"`（`insert_back`, `insert_front`, `replace`）または対象コントロールの最小限のプロパティ上書きのみを行うこと。
2. **`player.entity.json` 等のマージ規則**:
   * 短縮キー名（`textures`, `geometry`, `animations`, `render_controllers`, `scripts.animate`）には必ず `rp01_*`, `rp02_*` のように機能番号プレフィックスを付与し、機能間の命名衝突をゼロにする。
   * バニラ標準のプレイヤー描画・アニメーション・一人称腕表示・マント表示を一切損なわないよう、バニラ定義をベースにキーを追加する形で記述する。
3. **日本語テキスト・スプライト画像のプログラム生成 (`tools/`)**:
   * HUD上の日本語バイオーム名やモード表示ラベルなど、テクスチャアトラスとして文字を焼き込む必要がある画像は、手書きや曖昧な画像生成ではなく `tools/` 配下に Python（Pillow）または Node.js スクリプトを作成し、日本語フォントを用いてピクセル単位で正確なUV座標と一致するPNGを確定的に生成すること。

---

## 4. サブパック (`subpacks`) によるゲーム内プリセット切替対応

ワールドやRealmsへの適用時、ホストやプレイヤーがリソースパック設定画面（歯車アイコン）から負荷や表示構成を選べるよう、`manifest.json` に以下の `subpacks` を用意することを推奨する（不要な場合はデフォルトをフル機能とする）。

* `full`（デフォルト）: 全機能有効（HUDチャンク＆バイオーム ＋ 防具立て視覚化 ＋ 交易全解放 ＋ 以降追加機能）
* `hud_and_ui_only`: 軽量モード（防具立て3D視覚化を無効化し、HUD表示と交易UI改善のみ有効化）

---

## 5. 仕様書（マークダウン）追加時のバージョンアップ手順

ユーザーが `rp_specs/` に新しい `NN_*.md` を追加、または既存仕様書を更新した際、Antigravityは以下の手順を実行すること。

1. **差分特定と非破壊マージ**:
   * 新規または変更された `rp_specs/NN_*.md` を特定し、既存の `rp01_*`, `rp02_*` 等のアセットや共有ファイル内の既存エントリを破壊せずに統合する。
2. **`manifest.json` のバージョン自動更新（必須）**:
   * クライアントおよびRealmsにリソースパックの更新を確実に認識させて再ダウンロードを促すため、`resource_pack/manifest.json` の `header.version` および `modules[0].version` の `[major, minor, patch]` を更新する。
     * 新規 `NN_*.md` 追加時: `minor` を $+1$ し、`patch` を $0$ にリセット（例: `[1, 0, 0]` から `[1, 1, 0]`）。
     * 既存仕様の調整・バグ修正時: `patch` を $+1$（例: `[1, 1, 0]` から `[1, 1, 1]`）。
   * ※ `header.uuid` および `modules[0].uuid` は初回生成後に固定し、バージョンアップ時に変更してはならない（別パック扱いになり重複適用されるのを防ぐため）。
3. **構文・整合性チェック**:
   * すべての `.json` ファイルに構文エラーが無いこと、参照しているテクスチャパス・ジオメトリ識別子・マテリアル名に欠落が無いこと、日本語翻訳ファイル（`texts/ja_JP.lang`）が UTF-8（BOMなし）で出力されていることを検証する。