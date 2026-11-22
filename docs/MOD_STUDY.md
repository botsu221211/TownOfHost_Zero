# 他MODの調査メモ (TOH_Zero 開発用)

TOH_Zero に機能を足すときの参考として、ほかの Among Us MOD のコードを調べた結果をまとめたもの。
調査日: 2026-11-23。各MODのソースは `~/Downloads/` にある。

## 1. 調べたMODの一覧

| MOD | 種類 | 規模(.cs) | ライセンス | 場所 |
|---|---|---|---|---|
| TownOfHost_L 0.0.1 | ホストMOD (Yのフォーク) | 570ファイル / 約10万行 | GPL-3.0 | `~/Downloads/TownOfHost_L-0.0.1` |
| TownOfHost-K 51.14.33.1 | ホストMOD | 459 / 約9.4万行 | GPL-3.0 | `~/Downloads/TownOfHost-K-51.14.33.1` |
| TownOfHost-Pko 5.38.25.97 | ホストMOD (Kのフォーク) | 617 / 約15万行 | GPL-3.0 | `~/Downloads/TownOfHost-Pko-5.38.25.97` |
| TownOfHost-hamo 4.40.02 | ホストMOD (K系) | 755 / 約24万行 | GPL-3.0 | `~/Downloads/TownOfHost-hamo-4.40.02-` |
| TownOfHost_Y 2026.11.15 | ホストMOD (Zeroの元) | 338 / 約4.8万行 | GPL-3.0 | `~/Downloads/TownOfHost_Y-2026.11.15` |
| SuperNewRoles 3.3.0.1 | クライアントMOD | 724 / 約13万行 | GPL-3.0 | `~/Downloads/SuperNewRoles-3.3.0.1` |
| More-Roles-Plus 0.1.4 | クライアントMOD | 613 / 約16万行 | GPL-3.0 | `~/Downloads/More-Roles-Plus-0.1.4` |
| Nebula on the Ship | クライアントMOD | **ソースなし** (お知らせ・翻訳データのみ) | - | `~/Downloads/Nebula-v-v3.7.2.1-110-1670` |

### ライセンスの注意
- Nebula 以外はすべて GPL-3.0。TOH_Zero も GPL-3.0 なので、**コードを取り込んでよい**。
  取り込んだ時は、ファイルの先頭のコメントと README の「移植・参考クレジット」に出典(MOD名とファイル)を書く。
- Nebula はソースが無いので、アイデアの参考のみ。
- **クライアントMOD (SNR・More-Roles-Plus・Nebula) の仕組みは、そのままでは使えない。**
  全員が MOD を入れている前提で、独自のボタン・演出・通信を使っているため。TOH_Zero はホストだけが入れるので、
  「バニラの客に何を送れば同じことが見えるか」に置き換えて考える必要がある。役職・ルールのアイデアとしては使える。

---

## 2. TOH_Zero に入れると良さそうな機能 (おすすめ順)

| # | 機能 | 参考元 | 何がうれしいか |
|---|---|---|---|
| 1 | **名前表示の送信をまとめる** | hamo `Modules/NameRpcBatch.cs`, L `Core/NotifyRolesQueue.cs` | 名前の更新1回で「見る人×見られる人」通(10人で約100通)送っていたのを、送り先ごとに1通にまとめる。Switch・スマホの人の ping が安定し、切断が減る |
| 2 | **大きいパケットの分割** | hamo `Modules/NetworkSafety.cs` | 1000バイトを超える通信を分けて送る。スマホ・Switch で届かない問題の対策 |
| 3 | **幽霊役職** | hamo `Roles/Ghost/` (`GhostRoleAssign.cs`, `Role/*.cs`) | 死んだ人に役職を付ける。死んだ人をバニラの守護天使 (GuardianAngel) にして、守る(プロテクト)ボタンを押した時にホストが能力を発動する。例: 幽霊が緊急会議を起こす(Ghostbuttoner)、サボタージュする(GhostSaboteur) |
| 4 | **ホスト操作盤** | L `Modules/HostPanel.cs`, `HostPanelButton.cs` | 画面左下のボタンから開く窓で、キック・BAN・廃村などをコマンド無しで操作できる |
| 5 | **放置(AFK)検出** | L `Modules/AfkDetector.cs`, hamo `Modules/AfkDetector.cs` | 動かない人を見つけ、ロビーならキック、試合中は警告→設定に応じて処理 |
| 6 | **デバイスの使用時間制限** | L `Modules/DeviceTimer.cs` | アドミン・カメラ・バイタルを使える時間を全員で共有の持ち時間にする |
| 7 | **ロビー投票** | L `Modules/LobbyPoll.cs` (/poll, /pv), hamo `Modules/LobbyVote.cs` | 次のルールやプリセットをロビーで投票で決める |
| 8 | **チャットで設定変更** | L `Modules/SettingCommand.cs` (/cs) | 設定画面を開かずに「/cs キルクール 20」のように変える |
| 9 | **戦績** | L `Modules/PlayerStats.cs` (/me), K `Modules/Statistics.cs` | 部屋で遊んだ人の試合数・勝ち・キルなどをホストの手元に記録 |
| 10 | **荒らし対策** | L `Modules/ChatGuard.cs`, `ChatMute.cs`, `JoinGuard.cs` | 禁止語で自動キック、一定時間チャット禁止、入室の振り分け |
| 11 | **ホストの強制操作コマンド** | L `Modules/ChatCommandTable.cs` | /kick /ban /mute /exile(強制追放) /kill(強制キル) /start /say など |
| 12 | **チーム内チャット** | L `Modules/TeamChat.cs` (/tc /lc /jc) | インポスター同士・ラバーズ同士だけで話せる(秘匿メッセージの仕組みと同じ) |
| 13 | **役職辞典** | L `Modules/RoleDictionary.cs` | 全役職を陣営ごと・五十音順で一覧できる画面 |
| 14 | **最初の会議のチャット停止** | hamo `Modules/FirstMeetingChatBlock.cs` | 最初の会議で喋れないルール。「〇〇が投票しました」も隠して、投票順からの推理を防ぐ |
| 15 | **MVP 発表** | hamo `Modules/MvpAnnouncer.cs` | 試合後のロビーで MVP を発表 |
| 16 | **役職割り当ての公平化** | SNR `Roles/RoleAssignmentFairness.cs`, More-Roles-Plus `src/Roles/AssignFairness` | 同じ人に同じ役職・陣営が続かないようにする。ホストMODでもそのまま使える考え方 |
| 17 | **ホストの自動復帰** | L `Modules/AutoRehost.cs`, hamo `Modules/AutoRehost.cs` | ホストが落ちた時に部屋を立て直す |
| 18 | **実績** | K/Pko/hamo `Modules/Achievement/` | 条件を満たすと実績が解除される |
| 19 | **カスタムスポーン** | K系 `Modules/CustomSpawn/`, L `Modules/SpawnEditor` | 会議後のスポーン位置をマップごとに自分で決める |

---

## 3. 仕組みの調べ方メモ (どうやって作っているか)

### 偽オブジェクト(バニラの客にも見える物を置く)
- L `Core/CustomNetObject.cs` / Pko・hamo `Modules/Customnetobject.cs`
- 偽の PlayerControl を作り、**名前の文字で絵を描く**。ポータル・罠・爆弾など、客が MOD を入れていなくても見える物を置ける。
- 会議の UI が崩れないよう、GameData.AllPlayers から外す処理が要る(hamo のコメント参照)。

### 見る人ごとに役職を変える
- L `Modules/RoleView.cs`: 見る人ごとのバニラ役職(RoleTypes)を1か所で管理。
  TOH_Zero の isDesyncImpostor(フレンジー・マッドアンノウン等)や AntiBlackout の身代わりを整理する時に参考になる。
- L `Modules/AntiBlackout.cs`: 暗転対策の作り込み(fillCrew、ジャッカルとサイドキックの2対2、デシンク役職の画面)。
  TOH_Zero の AntiBlackout で残っている心配(ジャッカル+サイドキック)の対処はここが参考になる。

### 一時的に幽霊にする・生き返らせる
- L `Modules/TempGhost.cs`: 生きたまま、決めた時間だけ幽霊にする。
- L `Modules/Revive.cs`, `DeadBodyEraser.cs`: 本当に死んだ人を生き返らせ、残った死体を消す。

### 演出
- L `Modules/DeathBurst.cs`: 死んだ場所に、死因ごとの一瞬の演出(爆発・狙撃・炎・毒など)。
- Pko・hamo `Modules/CustomSound.cs`, `LobbyBGM.cs`: 効果音・ロビーBGM。

### 透明化
- Pko・hamo `Modules/InvisiblePatch.cs`, `InvisibleRPC.cs.cs`: 本当に見えなくする仕組み。

### チャット
- L `Modules/ChatPushOut.cs`: 空白で押し出す(TOH_Zero では「邪魔」との声で不採用)。
- L `Modules/Whisper.cs`: 耳打ち。TOH_Zero の /c に近い。

---

## 4. 役職のアイデア集

hamo が一番多い(インポスター102・マッド33・クルー103・ニュートラル92・幽霊13・属性約60)。
ファイル名から一部を抜き出したもの。中身は `~/Downloads/TownOfHost-hamo-4.40.02-/Roles/` などを見る。

- **インポスター**: Abductor(誘拐), Bomber, Camouflager, Clockworker, DoubleKiller, Eraser, EvilMaker, Evolver, Jumper, Magician, Mole, ProBowler, QuickKiller, Reverser, Rocket, Samurai, Spider, Swooper, Teleporter, Trauma …
- **マッド**: BlackCat, MadAvenger, MadBait, MadBetrayer, MadChanger, MadInvisible, MadJester, MadParasite, MadScream, MadSuicide, MadTeller, MadTracker, Nue …
- **クルー**: Android, Arcana, Balancer, Cakeshop, Comebacker, Coroner, Inspector, Jailer, JekyllandHyde, King, Locksmith, MagicalGirl, Merlin, Police, President, Sage, Snowman, Typewriter, VillageChief, WolfBoy …
- **ニュートラル**: AntiHero, Banker, Catalyst, Chef, Cupid, Dancer, DoppelGanger, Dracula, Eater, Mermaid, Monika, PhantomThief, PlagueDoctor, RaccoonParent/Child, Romeo, SantaClaus, TheThreeLittlePigs, Tuna, Vulture, Zombie …
- **幽霊**: AsistingAngel, DemonicCrusher, DemonicTracker, DemonicVenter, GhostFloodlight, GhostNoiseSender, GhostRumour, GhostSaboteur, Ghostbuttoner, Influencer
- **属性**: Amanojaku(天邪鬼), Absorb, Chain, ColorLovers, Jumbo, MagicHand, Moon, Powerful, Slacker, SlowStarter, Stamina, Transparent, Triplets, Twins, VoteTracker …
- **SNR(クライアント)**: WaveCannon, Kunoichi, Pteranodon, Squid, Stuntman, Busker, HamburgerShop, DyingMessenger, Sauner など。ホストMODで再現するには工夫が要る。
- **Nebula**: ソースは無いが、`Announcement_Japanese.json` に役職・ルールの紹介が多数ある。

---

## 5. 使えない(使いにくい)もの

- More-Roles-Plus の演出(カットイン・刀・時間巻き戻し・ドローン等)、SNR の独自ボタン・独自オブジェクト:
  全員が MOD を入れている前提。ホストMODでは、バニラの客に見せる手段(名前の文字・偽オブジェクト・バニラの RPC)で置き換えられる物だけ。
- Pko・hamo の `ChatServer`, `web`, `GlobalChatClient`, `Presetshareclient`: 外部のサーバーを使う機能。運用するサーバーが別に要る。
