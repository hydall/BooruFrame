# BooruFrame

A small Windows app that shows random pictures from booru sites (**Danbooru** and
**Gelbooru** engines) in a single frameless window, changing them on a timer.

Think of it as a digital picture frame on your desktop: type some tags, pick the sites,
and let it run. It can also paint your desktop background instead of sitting in a window.

Images are downloaded **straight into RAM** — nothing is written to disk unless you press
the download button.

## Features

**Pictures**

- Tag search across several booru sites at once (for example `rating:general cat_ears`).
- Site profiles: each one is an engine (Danbooru / Gelbooru) + a site URL + optional API
  keys. Tick the sites you want; every new picture is picked from a random one of them.
  Danbooru and Safebooru work anonymously, Gelbooru and Rule34 need keys.
- Per-site content filter — **None / Moderate / Strict / Custom** (custom lets you exclude
  the `explicit`, `questionable` and `sensitive` ratings one by one).
- Per-site extra tags: tags added to every search and tags excluded from every search,
  with a live preview of the final query.
- Orientation filter: any / landscape / portrait / square.
- Auto-change timer from 5 seconds to 10 minutes.
- No-repeat window: don't show the same picture again for the next N changes.
- Smooth crossfade between pictures.
- A 10-picture session history — go back and forward without downloading anything again.
- Download the picture you're looking at; the exact original file is saved from the
  in-memory copy.

**Window**

- Frameless window with its own buttons (minimize / maximize / close) and a vertical
  toolbar in the bottom-right corner; both appear when you move the mouse.
- Drag the picture to move the window, double-click to maximize.
- Scaling mode: fit / stretch / cover (crop).
- Runs from the system tray: closing the window hides it there, right-click the tray icon
  for play, next, previous, settings and exit.
- **Frame on the desktop (no normal window)** — an optional mode. Off by default, so
  normally you get an ordinary window with a taskbar button and an Alt+Tab entry. Turn it
  on and the frame stops behaving like a window: no taskbar button, no Alt+Tab, and it
  sinks behind every other window as soon as it loses focus. Click it to bring it back up.
- **Wallpaper mode** — the picture becomes a live desktop wallpaper, on its own layer
  behind the desktop icons, and the app itself moves to the tray. Open the window from the
  tray whenever you like: it is a full ordinary window and the wallpaper keeps running
  behind it. Your real Windows wallpaper is never changed — the app only slips its own
  window behind the icons, so leaving the mode (or closing the app) brings the original
  wallpaper straight back. Toggle it in Settings → Display, from the tray, or with
  **Ctrl+Alt+F**.

**Other**

- Interface languages: English, Russian, Polish. On first run the app follows your Windows
  language.
- One copy at a time. Starting it again does not open a second frame — it brings up the
  window of the copy that is already running, with the settings open if the wallpaper is
  running (there is nothing else to look at then). Handy when the app is in the tray with no
  window on screen and looks like it isn't running at all.
- Errors show up as red toasts in the top-left corner and stay until you close them; you
  can set them to auto-hide after N seconds instead.
- Everything (language, scaling, interval, tags, filters, site profiles) is saved to
  `%APPDATA%\BooruFrame` and restored on the next start.
- The window comes back where you left it: same position, same size, same monitor, and
  maximized again if that is how you left it. It works the same in wallpaper mode, where the
  window waits in the tray — open it and it is where it always was. If the monitor it used is
  gone when the app starts, the window moves to one that is still there instead of opening
  off-screen; if that monitor merely moved or changed resolution, the window follows it.

## Controls

- Move the mouse — the toolbar and window buttons appear; move it away and they fade out.
- **⚙** opens settings (the picture dims behind them). Close with **Esc**, the **✕**, or a
  click outside the panel.
- **Enter** in the tag box loads a new picture.
- **←** / **→** — previous / next picture, **Space** — start / stop.
- **Ctrl+Alt+F** — toggle wallpaper mode from anywhere.

## Install

Grab `BooruFrame-vX.Y.Z.exe` from the [Releases](https://github.com/hydall/BooruFrame/releases)
page and run it. It is self-contained — no .NET install needed.

## Build it yourself

You only need the **.NET 8 SDK**.

```powershell
# run from source
dotnet run --project BooruFrame

# build a single self-contained .exe
dotnet publish BooruFrame -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The result is `BooruFrame/bin/Release/net8.0-windows/win-x64/publish/BooruFrame.exe`.

## Support

If you like BooruFrame, you can buy me a coffee:
[buymeacoffee.com/hydall](https://buymeacoffee.com/hydall)
