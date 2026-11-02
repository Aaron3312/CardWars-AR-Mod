# Card Wars AR

**Adventure Time: Card Wars battles on a real table, in augmented reality, and two phones playing each other over Wi-Fi.**

This is a mod for [CardWars-Revival](https://github.com/Lukas-Simonson/CardWars-Revival) (Unity 6), which builds on [shishkabob27's PC port](https://github.com/shishkabob27/CardWars) of the discontinued mobile game. It adds:

- **AR battle board**: with AR Foundation (ARCore on Android, ARKit on iOS), the battle board stands on a table you point your phone at. You place it with a tap, move, rotate and resize it in an **Adjust** mode, and play by touching it as usual: cards, lanes, floop, the battle ring.
- **Versus on the table** ("Versus en la mesa"): two phones on the same Wi-Fi play the same battle against each other, each player with their own deck. The two games run in lockstep with a shared seed, so only the moves travel over the network. Optionally, a **printed marker** makes both phones see the same board in the same spot.
- **A test bot** that plays a whole versus match by itself on two desktop instances and checks every turn for desyncs.
- **An `infinite_currency` setting** (coins and gems stay at 999,999) and iOS settings for a free Apple developer account.

> **This repository contains only my code: no game assets, no game code dump, no builds.** It is applied on top of a CardWars-Revival checkout that you get yourself. Card Wars and Adventure Time belong to Cartoon Network; this is a non-commercial fan project, not affiliated with or endorsed by them.

The design notes and the in-game texts are in **Spanish**: [docs/ar-plan.md](docs/ar-plan.md) covers the AR mode and [docs/ar-versus.md](docs/ar-versus.md) covers versus. This README is the English guide.

## Contents

- [What's in this repo](#whats-in-this-repo)
- [Requirements](#requirements)
- [Install](#install)
- [Build for Android and iOS](#build-for-android-and-ios)
- [Playing in AR](#playing-in-ar)
- [Versus on the table](#versus-on-the-table)
- [Settings](#settings)
- [Testing without phones](#testing-without-phones)
- [How it works](#how-it-works)
- [Known limits](#known-limits)
- [Glossary of in-game texts](#glossary-of-in-game-texts)
- [License and credits](#license-and-credits)

## What's in this repo

| Path | What it is |
|---|---|
| `overlay/` | New files, copied into the Unity project as they are: `ARBattleRig.cs`, the `Versus*.cs` scripts, the XR loader settings, the AR overlay shader, the marker image and its reference library, and two editor menus |
| `patches/upstream-changes.patch` | Changes to existing CardWars-Revival files: the AR Foundation packages, settings, the battle camera and input hooks, a seeded random generator in the card abilities, and iOS build settings |
| `apply.sh` | Applies both of the above to your checkout |
| `docs/` | Design notes (Spanish) and the printable marker (`docs/marcador/marcador-versus.pdf`) |
| `tools/` | The Python scripts that generate the marker and its printable sheet (Pillow) |

## Requirements

- **Unity 6000.6.7f1**, with Android Build Support and/or iOS Build Support.
- **A phone with AR:** Android with [ARCore](https://developers.google.com/ar/devices) (Google Play Services for AR), or an iPhone or iPad with ARKit (iOS 15+).
- For iOS, Xcode on a Mac. A free Apple ID is enough to install on your own device.
- The packages are added by the patch and resolved by Unity on first open: AR Foundation, ARCore XR Plugin and ARKit XR Plugin, all 6.6.2.

## Install

```sh
git clone https://github.com/Lukas-Simonson/CardWars-Revival.git
cd CardWars-Revival
git checkout -b ar 246cef8e86ee74e49f1fe1b4a9f86447c4ff7a1e   # the commit this mod was made on
cd ..
git clone https://github.com/Aaron3312/CardWars-AR-Mod.git
CardWars-AR-Mod/apply.sh CardWars-Revival
```

`apply.sh` applies the patch and copies `overlay/`. The result is staged in git but not committed, so `git diff --cached` shows exactly what changed.

- **On a newer CardWars-Revival commit**, the script tries a 3-way merge, and conflicts are marked in the usual git way.
- **By hand:** `git apply --index patches/upstream-changes.patch`, then `cp -R overlay/. /path/to/CardWars-Revival/`.

Then open the folder in Unity Hub. The first import takes a while.

The XR loaders and the marker library are already configured in `overlay/`. The editor menus **Card Wars → AR → Configure XR loaders** and **Card Wars → AR → Create versus marker library** only regenerate them.

## Build for Android and iOS

You can build from the editor (File → Build Profiles) or headless with the project's `PlatformBuild` script.

```sh
# Android (IL2CPP, arm64): an APK you can sideload
Unity -batchmode -quit -projectPath . -buildTarget Android \
  -executeMethod PlatformBuild.Build -buildPath Build/Android/CardWars.apk

# iOS: an Xcode project; open it, pick your team, run on the device
Unity -batchmode -quit -projectPath . -buildTarget iOS \
  -executeMethod PlatformBuild.Build -buildPath Build/iOS
```

- Add `-development` for a development build.
- **Android:** `PlatformBuild` sets IL2CPP and arm64. ARCore is marked as required by the AR packages.
- **iOS:**
  - The bundle id is `com.aaron3312.cardwarsar`, because a free Apple team can't sign the original `app.recompile.cardwars`. Change `IosBundleId` in `Assets/Editor/PlatformBuild.cs` to one of your own.
  - The build adds the camera and local-network usage texts to `Info.plist`, locks the game to landscape and marks ARKit as required.

## Playing in AR

AR is **on by default on Android and iOS** (`"ar_mode"` in [Settings](#settings)). Desktop builds and the editor keep the normal camera.

When a battle starts, the board stays hidden until you place it.

1. **Find the table.** Move the phone slowly over a table until dots appear. Usable tables (horizontal, at least 0.12 m² and 20 cm on each side) are highlighted in light blue; patches that are still too small stay faint white.
2. **Place.** With the center of the screen over a usable table, the outline of the board appears exactly where it will go: blue half for you, red half for the opponent, and an arrow toward your side. Tap the table to put the board there. The game ignores touches while you place it.
3. **Play.** The board is anchored to the table, so it stays put while you walk around, and you play as usual. If tracking is lost (low light, moving fast), the board stays where it was until the phone sees the table again.
4. **Adjust** (the **Ajustar** button at the bottom left):
   - One finger drags the board across the table.
   - Two fingers rotate it, and pinching resizes it between 0.6× and 1.6× (about 30 to 80 cm).
   - **▲ / ▼** raise or lower it in 1 cm steps.
   - **Fijar** (lock) anchors it again and gives touches back to the game.
   - **Recolocar** (re-place) starts over; **Cancelar** returns to where it was.

The chosen size and height are remembered for the next battle.

## Versus on the table

Two phones play the same battle against each other. Each player uses their current deck and sees the board on their own table, or on the same one.

1. **Both phones on the same Wi-Fi.** No internet is needed.
2. In the main menu (the Tree Fort), tap **Versus en la mesa**, under your player panel on the left.
3. **Pick the board mode for your phone:** **Con marcador** (with the printed marker) or **Sin marcador** (without it). Each player picks their own, and the choice is remembered.
4. **One player taps Crear partida** (create match). Their phone shows a **big number**, the last part of their IP address (for example `45` for 192.168.1.45), with the full IP underneath.
5. **The other taps Unirse** (join), types that number and taps **Conectar**. On networks that aren't a typical home /24, type the full IP instead.
6. **Both enter the battle** (the Candy Kingdom board, normal rules):
   - Each player places the board, then their 4 landscapes.
   - Each player spins the ketchup bottle. Who starts is decided by the shared random seed, the same on both phones.
7. **Turns work as usual.** On the opponent's turn, their moves play out on your board. The player who attacks plays the battle ring; the defender's phone waits for that roll and shows it.
8. At the end you get **Revancha** (rematch, the loser starts) or **Volver al menú**.

**Don't lock the phone or switch apps during a match.** A quick look at another app is fine, but after about 35 s without an answer the other phone shows that the connection was lost.

### Board modes

- **With the printed marker:** both players see one shared board.
  1. Print `docs/marcador/marcador-versus.pdf` at **100 % scale** (page 1 is A4, page 2 is US Letter). The marker square must measure **15 × 15 cm**; check it with the 5 cm ruler on the sheet.
  2. Lay the sheet flat in the middle of the table. The player who **creates** the match sits at the edge with the white notch; the other sits across.
  3. Point the phone at the sheet. The board snaps to it at full size (about 50 cm, larger than the sheet), and each player sees their own side facing them. When the sheet goes out of view, the board stays anchored where it was.
  - **Adjust** still works, but it detaches the board from the marker. **Re-place** looks for the marker again, and **Sin marcador** ignores it.
- **Without the marker:** each player places their own board by tapping the table, as in single-player.
  - To share a table, put something in the middle (a coin, a glass) and both tap on it from their side, then fine-tune with Adjust.
  - It also works on different tables or in different rooms, because only the moves travel over the network.
- **Without AR** (desktop builds, or `"ar_mode": false`): versus plays with the normal camera.

## Settings

The settings live in `Assets/StreamingAssets/server_settings.json`, in the build's StreamingAssets.

| Key | Default | Meaning |
|---|---|---|
| `ar_mode` | `true` on Android and iOS, `false` elsewhere | Puts the battle board on a real table with AR Foundation |
| `infinite_currency` | `true` (set by this mod) | Coins and gems start at 999,999 and never go down. Set it to `false` for normal progression |
| `offline_mode`, `skip_tutorial` | from CardWars-Revival | See its README |

## Testing without phones

A desktop build (where AR is off) has a versus test mode: each instance plays by itself through the same inputs a person would use, and logs a hash of the game state every turn.

```sh
Unity -batchmode -quit -projectPath . -buildTarget StandaloneOSX \
  -executeMethod PlatformBuild.Build -buildPath Build/Mac/CardWars.app
APP="Build/Mac/CardWars.app/Contents/MacOS/Card Wars"
mkdir -p /tmp/vs
CW_VERSUS=host CW_VERSUS_GAMES=2 CW_VERSUS_LOG=/tmp/vs/match.log \
  "$APP" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile /tmp/vs/host.log &
CW_VERSUS=join CW_VERSUS_ADDR=127.0.0.1 CW_VERSUS_GAMES=2 CW_VERSUS_LOG=/tmp/vs/match.log \
  "$APP" -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile /tmp/vs/join.log &
```

The log says `turn N in sync (hash)` or `DESYNC` on every turn, and `GAME n OVER: won/lost, desyncs 0` at the end.

| Variable | Meaning |
|---|---|
| `CW_VERSUS` | `host` or `join` |
| `CW_VERSUS_ADDR`, `CW_VERSUS_PORT` | Where to connect (default 127.0.0.1) |
| `CW_VERSUS_GAMES` | Number of games, with automatic rematches |
| `CW_VERSUS_LOG` | Match log |
| `CW_TEST_SHOTS`, `CW_TEST_INTERVAL` | Screenshot folder and interval |
| `CW_VERSUS_BOTSEED`, `CW_VERSUS_TIMEOUT`, `CW_TEST_CLICKS` | Bot seed, timeout in minutes, click logging |

For AR in the editor, AR Foundation's **XR Simulation** works once the session is created after the XR Origin (already done in `ARBattleRig`). Its default room only has a floor and walls, so the board lands on the floor.

## How it works

- **`ARBattleRig`** (`overlay/Assets/Scripts/Assembly-CSharp/ARBattleRig.cs`) runs only with `ar_mode`. It turns the battle into a three-state flow: Place, then Locked, which you can toggle with Adjust.
  - It creates the XR Origin and session, draws the detected planes with a dot grid (`AROverlay.shader`, in `Resources` so builds always include it) and raycasts taps against tables only.
  - The board is attached to an `ARAnchor` on the plane, and its pivot is measured so the base sits exactly on the table.
  - It keeps the battle camera on the phone, and picks the drop lane with a ray from your finger.
  - While placing or adjusting, it blocks the game's input (NGUI's `LockInput`, FingerGestures, raw mouse emulation).
- **Versus:**
  - **`VersusRandom`** replaces `UnityEngine.Random` in the battle code, which is why the patch touches many small ability scripts. Both phones share a seed, so they compute the same battle.
  - **`VersusNet`** is the LAN link: one TCP connection between the two phones (the host listens, the guest connects).
  - **`VersusMatch`** sends each local move and replays the remote ones through the same code paths a touch would use, then checks a hash of the game state every turn.
  - **`VersusMenu`** and **`VersusGui`** are the menu and the in-battle texts.
  - **`VersusTestBot`** is the self-playing tester.
- **The marker**: `tools/make_versus_marker.py` generates a high-contrast pattern from scratch (no game art), and `make_versus_marker_print.py` lays it out on A4 and Letter with the seating marks. `VersusMarkerLibrary.asset` is the AR Foundation reference image library for it, at 15 cm.

The full audit of every random call in the battle code, and why each one is or isn't synced, is in [docs/ar-versus.md](docs/ar-versus.md) under "Auditoría del azar".

## Known limits

- **The first placement blocks the whole game**, including the menu button. If ARCore or ARKit never finds a plane (low light, a plain glossy table), you can only place the board on the floor or close the app.
- **Tapping Adjust during the battle ring** also stops the ring for that turn.
- Versus is LAN only: there's no online play, no matchmaking, and no reconnection after a dropped link.
- The plane-size thresholds were tuned on one Android phone, so other devices may need different values.

## Glossary of in-game texts

| In the game | Meaning |
|---|---|
| Versus en la mesa | Versus on the table |
| Con marcador / Sin marcador / Usar marcador | With marker / Without marker / Use marker |
| Crear partida / Unirse / Conectar | Create match / Join / Connect |
| Ajustar / Fijar / Recolocar / Cancelar | Adjust / Lock / Re-place / Cancel |
| Mueve el teléfono despacio sobre la mesa… | Move the phone slowly over the table… |
| Toca la mesa para poner el tablero | Tap the table to place the board |
| Tablero sobre el marcador | Board on the marker |
| Turno del rival… / El rival está atacando… | Opponent's turn… / The opponent is attacking… |
| ¡Ganaste! / Perdiste / Revancha / Volver al menú | You won! / You lost / Rematch / Back to menu |
| Se perdió la conexión con el rival | Lost the connection to the opponent |

## License and credits

- **My code** (everything in `overlay/`, `tools/`, `docs/` and `apply.sh`, plus the lines the patch adds) is under the [MIT License](LICENSE).
- The patch also carries context lines from CardWars-Revival files. Those, the rest of CardWars-Revival and every game asset are **not** covered by this license and stay with their owners.
- **[CardWars-Revival](https://github.com/Lukas-Simonson/CardWars-Revival)** by Lukas Simonson, and **[CardWars](https://github.com/shishkabob27/CardWars)** by shishkabob27, who brought the game back to life.
- **[AR Foundation](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/manual/index.html)**, with ARCore and ARKit, by Unity.
- Adventure Time and Card Wars are trademarks of Cartoon Network. No game assets are included or distributed.
