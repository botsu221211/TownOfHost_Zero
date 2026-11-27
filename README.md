# Town Of Host_Zero

## TOH_Zeroへようこそ！
TOH_Zeroは [Town Of Host_Y (TOH_Y)](https://github.com/Yumenopai/TownOfHost_Y) をフォークして作られた、Among Us のホストMODです。<br>
TOH_Y は [Town Of Host (TOH)](https://github.com/tukasa0001/TownOfHost) のフォークです。<br>

**TOH_Zero は TOH_Y・本家TOH の公式バージョンではありません。**<br>
TOH_Zeroに関する不具合・質問を TOH_Y や本家TOH の開発者・Discordへ問い合わせないでください。<br>

## ★この Mod について
この Mod は非公式のものであり、この Mod の開発に関して Among Us の開発元である"Innersloth"は一切関与していません。<br>
この Mod の問題などに関して公式に問い合わせないでください。<br>

- **TOH_Y・本家TOHを含む**、別MODとの共存はできません。必ず1つのMODを共有して使用してください。
- ホストのみの導入でプレイできます。ホスト以外の導入はサポートしていません。
- Innersloth の [Among Us Mod Policy](https://www.innersloth.com/among-us-mod-policy/) に従ってご利用ください。

### よくある質問
- 公開ルームは使えないの？
  - innersloth公式からホストMOD各種へのアナウンスに基づいて、TOH系列は**公開ルーム(野良)で使用出来ないように**MODで制限をかけています。
  - 非公開ルームでコードを共有して遊ぶのには問題ありませんが、公開ルームでのMOD部屋への誘導を促すための部屋作成はおやめ下さい。
- 役職の説明はどこを見ればいい？
  - ゲーム内のヘルプコマンドや、[WIKIWIKI](https://wikiwiki.jp/amonguas/TOH-Zero)をご覧ください。

## ★リリース
**最新バージョンは[こちらからダウンロード](https://github.com/botsu221211/TownOfHost_Zero/releases/latest)**<br>

### 導入方法
#### はじめて導入する場合(おすすめ)
1. リリースから、自分の Among Us に合う zip をダウンロードします。BepInEx と TOH_Zero が入っています。
   - Steam版: `TownOfHost_Zero_steam_v(バージョン).zip`
   - Epic版: `TownOfHost_Zero_epic_v(バージョン).zip`
2. zip の中身を全部、Among Us のフォルダ(`Among Us.exe` があるフォルダ)にそのまま入れます。

#### BepInEx を自分で入れている場合
1. [BepInEx (IL2CPP版)](https://builds.bepinex.dev/projects/bepinex_be) を Among Us のフォルダに導入します。
   - 動作確認しているのは `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.735` です。
2. リリースから `TownOfHost_Zero-v(バージョン).dll` (例: `TownOfHost_Zero-v1.0.0.dll`)をダウンロードし、`Among Us/BepInEx/plugins/` に入れます。

※TOH_Y など他のTOH系MODのdllや、古いバージョンの TOH_Zero の dll が plugins フォルダに残っている場合は削除してください(2つ入れると動きません)。

※[TownOfHost_Y](https://github.com/Yumenopai/TownOfHost_Y) Fork / [TownOfHost](https://github.com/tukasa0001/TownOfHost) Fork<br>

## TOH_Zero外部サイト
[WIKIWIKI](https://wikiwiki.jp/amonguas/TOH-Zero)・[Discord](https://discord.gg/tX2Ue4sgHm)

## 開発者
### TOH_Zero
- [botsu221211](https://github.com/botsu221211)

### TOH_Y (フォーク元)
- [ゆめの/Yumenopai](https://github.com/Yumenopai)
- TownOfHostの開発者様は省略。


## 移植・参考クレジット (TOH_Yより継承)
- [本家TOH](https://github.com/tukasa0001/TownOfHost)・[TOH_H](https://github.com/Hyz-sui/TownOfHost-H)
  - Fork元であり、様々な機能の参考にしています。
- [TownOfPlus](https://github.com/tugaru1975/TownOfPlus)
  - MODクライアント視点霊界での視界ズーム
- [SuperNewRoles](https://github.com/ykundesu/SuperNewRoles)
  - 猫又系役職の道連れ・純愛者・神・自爆魔・マッドジェスター/マッドてるてる・パン屋レアコメント・にじいろスター/スター・ロビーBGMのミュート
- [TheOtherRoles](https://github.com/TheOtherRolesAU/TheOtherRoles)
  - サングラス・弁護士・VIP・タイブレーカー・ゲッサー・チェインシフター・ゴッドファーザー＆ジャニター/マフィア・ジャッカルサイドキック
- [Nebula on the Ship](https://github.com/Dolly1016/Nebula-Public)
  - タイトル画面から見れるInfoアナウンス画面
- [TownOfHostEdited](https://github.com/KARPED1EM/TownOfHostEdited)
  - スカベンジャー
- [ExtremeRoles](https://github.com/yukieiji/ExtremeRoles)
  - トトカルチョ
- (Skeld.net)
  - エスカレーショナー/Escalation Gamemode・シンクロカラーモード/twin,50-50,clone Gamemode

## 移植クレジット (TOH_Zero)
- TownOfHost-hamo
  - モデレーター
  - GM の詳細設定・自動スタート・試合後に自動で部屋に戻る
- TownOfHost-Pko
  - ペットを撫でて能力を使う仕組み・ペットの自動付与
  - /h r の役職説明の見せ方
- TownOfHost-K
  - 勝利優先度の仕組み
- [TownOfHost_L](https://github.com/haru03741-glitch/TownOfHost_L)
  - ダミー(CustomNetObject)の仕組み(白狐のお皿)
- [Nebula on the Ship](https://github.com/Dolly1016/Nebula-Public)
  - 白狐(妖狐を参考)・スカーレット
- [ExtremeRoles](https://github.com/yukieiji/ExtremeRoles)
  - ニート
- [SuperNewRoles](https://github.com/ykundesu/SuperNewRoles)
  - ステファインダー
