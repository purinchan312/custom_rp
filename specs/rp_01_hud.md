# リソースパック仕様 #01: HUD常時チャンク座標（相対1〜16・端判定）＆日本語バイオーム名表示

本仕様書は `rp_specs/00_RP_ARCHITECTURE.md` の共通規約に基づき、オフハンドに何も持たなくても常時画面左上の座標エリア直下に「チャンク内相対座標（$1 \sim 16$）」「チャンク番号座標」「チャンク境界・四隅判定」「現在の日本語バイオーム名」を表示する機能を定義する。

---

## 1. 機能概要と表示フォーマット

* **機能プレフィックス**: `rp01`
* **発動条件**: オフハンドアイテム不要（常時表示）。バニラのHUD画面（`hud_screen`）表示中のみ描画し、インベントリ画面や三人称視点のプレイヤーモデル上には一切干渉させない。
* **配置場所**: バニラの左上座標表示（`player_position`）のすぐ下の行に縦並びで追従配置する（半透明の黒背景プレート付きでバニラ座標表示と統一感のあるデザインにする）。
* **表示レイアウト（2段構成）**:
  * **1段目（チャンク座標＆境界ステータス）**:
    * 書式: `<相対X>, <相対Z> (<チャンクX>, <チャンクZ>)` ＋ 境界上にいる場合のみ末尾に ` : <境界種別>` を付与
  * **2段目（日本語バイオーム名）**:
    * 書式: `<日本語バイオーム名>`（例: `平原`, `吹きさらしの丘`, `桜の林`, `ディープダーク` 等）

---

## 2. チャンク座標・相対位置・端判定の厳密な数式仕様

### 2.1 基準座標とチャンク計算式（Molang）
プレイヤーの現在座標からブロック整数座標 $B_x, B_z$ を求め、チャンク番号 $C_x, C_z$ およびチャンク内相対位置 $R_x, R_z$ を算出する。高さ（Y軸）は表示に含めない **X, Z の2軸表記** とする。

* **ブロック整数座標**:
  $$B_x = \lfloor \text{query.position}(0) \rfloor, \quad B_z = \lfloor \text{query.position}(2) \rfloor$$
* **チャンク番号座標（$0$ 始まり・負数対応の符号付き整数）**:
  $$C_x = \lfloor B_x / 16 \rfloor, \quad C_z = \lfloor B_z / 16 \rfloor$$
  * 座標 $0 \sim 15$ はチャンク $0$、座標 $16 \sim 31$ はチャンク $1$、座標 $-16 \sim -1$ はチャンク $-1$、座標 $-32 \sim -17$ はチャンク $-2$ となる。
  * 対応桁数は少なくとも $\pm 9999$ チャンク（ワールド座標 $\pm 159999$ ブロック）以上をサポートすること。
* **チャンク内相対位置（必ず $1 \sim 16$ の整数のみ・$0$ は含まない）**:
  $$R_x = (B_x - C_x \times 16) + 1, \quad R_z = (B_z - C_z \times 16) + 1$$
  * 正の座標域でも負の座標域でも、常に西端が $R_x = 1$、東端が $R_x = 16$、北端が $R_z = 1$、南端が $R_z = 16$ となる。

### 2.2 末尾のチャンク縁・四隅ラベル判定規則
北（$-Z$ 方向）を「上」、南（$+Z$ 方向）を「下」、西（$-X$ 方向）を「左」、東（$+X$ 方向）を「右」と定義し、四隅の判定を外周線の判定より優先する。

| 条件（$R_x, R_z \in \{1 \dots 16\}$） | 末尾に追加する文字列 | 意味 |
| :--- | :--- | :--- |
| $R_x = 1$ かつ $R_z = 1$ | ` : 左上` | チャンク北西角の1マス |
| $R_x = 16$ かつ $R_z = 1$ | ` : 右上` | チャンク北東角の1マス |
| $R_x = 1$ かつ $R_z = 16$ | ` : 左下` | チャンク南西角の1マス |
| $R_x = 16$ かつ $R_z = 16$ | ` : 右下` | チャンク南東角の1マス |
| 上記4角以外で、$R_x \in \{1, 16\}$ または $R_z \in \{1, 16\}$ | ` : 線` | チャンク境界の外周縁1マス |
| $2 \le R_x \le 15$ かつ $2 \le R_z \le 15$ | （何も表示しない） | チャンク内側（コロン `:` 自体も非表示） |

### 2.3 検算用テストケース（実装時に必ず一致させること）
* ワールド座標 $(25, 77, 132)$ の場合 $\implies$ `10, 5 (1, 8)`
* ワールド座標 $(31, 37, 132)$ の場合 $\implies$ `16, 5 (1, 8) : 線`
* ワールド座標 $(64, 157, 159)$ の場合 $\implies$ `1, 16 (4, 9) : 左下`
* ワールド座標 $(0, 64, 0)$ の場合 $\implies$ `1, 1 (0, 0) : 左上`
* ワールド座標 $(-1, 64, -16)$ の場合 $\implies$ `16, 1 (-1, -1) : 右上`

---

## 3. リソースパック単体での描画実装アーキテクチャ

バニラJSON UIのテキストバインディング単体では割り算や `query.has_biome_tag` が評価できないため、**クリッピング済み `hud_player_renderer` ＋ 動的UVスプライトボーン制御** によって描画する。

### 3.1 他画面・マルチプレイへの非干渉分離（重要）
1. **`v.is_paperdoll` によるワールド描画遮断**:
   * `entity/player.entity.json` に登録する本機能のレンダーコントローラー `controller.render.rp01_hud_readout` の発動条件を `"v.is_paperdoll"` に限定する。これにより、三人称視点やマルチプレイ時の他プレイヤー視点では一切描画されない。
2. **インベントリ画面ドールとの分離（モデル空間オフセット ＋ UIクリッピング）**:
   * `models/entity/rp01_hud_readout.geo.json` 内のHUD表示用ルートボーンは、通常のプレイヤーモデル領域（$Y = 0 \sim 32$）から大きく離した高所オフセット座標（例: $Y = +1600$）に配置し、回転追従を打ち消して常にカメラ正面を向く平面クアッド群として構成する。
   * `ui/modules/rp01_hud_chunk_panel.json` 側では `"clips_children": true` を持つ高さ約 $24\text{px}$ のパネル内に `hud_player_renderer` を置き、オフセット調整によって $Y = +1600$ のHUDクアッド領域だけを小窓に切り抜いて表示する。これにより、インベントリ画面の装備プレビュー用プレイヤー表示には一切映り込まない。
3. **`ui/hud_screen.json` への非破壊挿入**:
   * 左上の `player_position` を含むスタックパネル直下に `"modifications"`（`insert_after` または `insert_back`）で `rp01_hud_chunk_panel` を挿入する。バニラの「座標を表示」ON時はその真下に隙間なく並び、OFF時も左上に綺麗に収まる配置とする。

### 3.2 可変桁数の動的カーソル詰め配置（空白ギャップ防止）
固定幅で桁枠を並べると `1, 5 (0, 0)` のような少ない桁数のときに不自然な空白が空くため、アニメーション `animations/rp01_hud_readout.animation.json` 内で水平カーソル変数 `v.cursor_x` を左から順に加算して各文字ボーンのX位置を動的に詰めること。

* **文字スロット構成（1段目）**:
  * `rx_d1`（十の位: $R_x \ge 10$ のときのみ表示、未満は `scale = 0`）、`rx_d0`（一の位）
  * `sep_comma1`（`, `）
  * `rz_d1`（$R_z \ge 10$ のときのみ表示）、`rz_d0`
  * `sep_open`（` (`）
  * `cx_sign`（$C_x < 0$ のとき `-` を表示、それ以外は `scale = 0`）、`cx_d4` 〜 `cx_d0`（上位の先行ゼロは `scale = 0` でスキップし、`0` のときは一の位 `cx_d0` のみ `0` を表示）
  * `sep_comma2`（`, `）
  * `cz_sign`、`cz_d4` 〜 `cz_d0`（同上）
  * `sep_close`（`)`）
  * `edge_label`（境界・角にいるときのみ ` : 線` / ` : 左上` / ` : 右上` / ` : 左下` / ` : 右下` のいずれかのスプライトを表示してカーソル位置に配置、内側のときは `scale = 0`）
* **UV切替**:
  * `textures/rp_custom/rp01_glyph_atlas.png` に `0`〜`9`, `-`, `, `, `(`, `)`, および5種類の境界文字列を等間隔グリッドで焼き込み、レンダーコントローラーの `uv_anim.offset` で各ボーンの表示グリフを切り替える（複数レンダーコントローラーまたはボーン別マテリアル/UVオフセットで制御）。

---

## 4. 日本語バイオーム名判定とスプライトアトラス仕様

### 4.1 バイオームアトラス画像の自動生成 (`tools/generate_rp_atlases.py`)
`textures/rp_custom/rp01_biome_atlas.png` には、Minecraft統合版の公式日本語翻訳（`ja_JP`）に準拠したバイオーム名を縦1列の行インデックス形式で鮮明にレンダリングする（白文字＋1pxドロップシャドウ、視認性の高いゴシック/ドット調日本語フォント使用）。

### 4.2 `query.has_biome_tag` 優先判定ツリー
Molangの `q.has_biome_tag('<tag>')` を用い、**特殊な複合タグを持つバイオームから順に（具体的 $\rightarrow$ 汎用的の優先順で）** 行インデックス `v.biome_idx` を決定する。最低限、以下の主要バイオームを網羅すること（同一タグ構成で判別不能な一部亜種は代表名に統合してよい）。

1. **ジ・エンド系**: `the_end` タグ $\rightarrow$ `ジ・エンド`
2. **ネザー系**:
   * `soulsand_valley` $\rightarrow$ `ソウルサンドの谷`
   * `crimson_forest` $\rightarrow$ `真紅の森`
   * `warped_forest` $\rightarrow$ `歪んだ森`
   * `basalt_deltas` $\rightarrow$ `玄武岩のデルタ`
   * `nether_wastes` または `hell` $\rightarrow$ `ネザーの荒地`
3. **地下バイオーム系**:
   * `deep_dark` $\rightarrow$ `ディープダーク`
   * `lush_caves` $\rightarrow$ `繁茂した洞窟`
   * `dripstone_caves` $\rightarrow$ `鍾乳洞`
4. **オーバーワールド主要バイオーム系（タグ組み合わせで細分化）**:
   * `pale_garden` $\rightarrow$ `ペールガーデン`
   * `cherry_grove` $\rightarrow$ `桜の林`
   * `mangrove_swamp` $\rightarrow$ `マングローブの沼地`
   * `mushroom_island` $\rightarrow$ `キノコ島`
   * `bamboo` かつ `jungle` $\rightarrow$ `竹林`
   * `jungle` かつ `edge` $\rightarrow$ `ジャングルの端` / `jungle` $\rightarrow$ `ジャングル`
   * `mesa` かつ `plateau_stone` $\rightarrow$ `森のある荒野` / `mesa` かつ `bryce` $\rightarrow$ `浸食された荒野` / `mesa` $\rightarrow$ `荒野`
   * `savanna` かつ `mutated` $\rightarrow$ `吹きさらしのサバンナ` / `savanna` かつ `plateau` $\rightarrow$ `サバンナの高原` / `savanna` $\rightarrow$ `サバンナ`
   * `swamp` $\rightarrow$ `沼地`
   * `desert` $\rightarrow$ `砂漠`
   * `ice_plains` かつ `mutated` $\rightarrow$ `樹氷` / `ice_plains` $\rightarrow$ `雪原`
   * `grove` $\rightarrow$ `雪の林`
   * `snowy_slopes` $\rightarrow$ `雪の斜面`
   * `jagged_peaks` $\rightarrow$ `尖った山頂` / `frozen_peaks` $\rightarrow$ `凍った山頂` / `stony_peaks` $\rightarrow$ `石だらけの山頂`
   * `meadow` $\rightarrow$ `草地`
   * `extreme_hills`（`mountains`）かつ `plus_trees` $\rightarrow$ `吹きさらしの森` / `extreme_hills` かつ `mutated` $\rightarrow$ `吹きさらしの砂利の丘` / `extreme_hills` $\rightarrow$ `吹きさらしの丘`
   * `roofed` かつ `forest` $\rightarrow$ `暗い森`
   * `flower_forest` $\rightarrow$ `花の森`
   * `birch` かつ `mutated` $\rightarrow$ `原生林のシラカバの森` / `birch` $\rightarrow$ `シラカバの森`
   * `mega` かつ `taiga` $\rightarrow$ `原生林のタイガ` / `taiga` かつ `cold` $\rightarrow$ `雪のタイガ` / `taiga` $\rightarrow$ `タイガ`
   * `forest` $\rightarrow$ `森`
   * `sunflower_plains`（`plains` かつ `mutated`） $\rightarrow$ `ヒマワリ平原` / `plains` $\rightarrow$ `平原`
   * `deep` かつ `warm` かつ `ocean` $\rightarrow$ `ぬるい深海` / `warm` かつ `ocean` $\rightarrow$ `暖かい海` / `lukewarm` かつ `ocean` $\rightarrow$ `ぬるい海` / `frozen` かつ `ocean` $\rightarrow$ `凍った海` / `cold` かつ `ocean` $\rightarrow$ `冷たい海` / `deep` かつ `ocean` $\rightarrow$ `深海` / `ocean` $\rightarrow$ `海`
   * `frozen` かつ `river` $\rightarrow$ `凍った川` / `river` $\rightarrow$ `川`
   * `stony_shore`（`stone_beach`） $\rightarrow$ `石だらけの海岸` / `beach` かつ `cold` $\rightarrow$ `雪の砂浜` / `beach` $\rightarrow$ `砂浜`
5. **フォールバック（未知の新規バイオーム等）**: `その他のバイオーム`

---

## 5. 受け入れテスト確認項目

- [ ] オフハンドに何も持っていない状態で、HUD左上に1段目（チャンク座標）と2段目（日本語バイオーム名）が鮮明に表示されること。
- [ ] 検算テストケース5件すべて（正座標・負座標・境界線・四隅・チャンク内側）で表示文字列が1文字の狂いもなく一致すること（相対座標に `0` が一切出ず $1 \sim 16$ であること）。
- [ ] 三人称視点（後方・前方）やインベントリ画面を開いた際、プレイヤーモデルの周囲にHUD用の文字モデルが一切映り込まないこと。
- [ ] ワールド設定で実績が有効（チートOFF）なまま適用・動作すること。