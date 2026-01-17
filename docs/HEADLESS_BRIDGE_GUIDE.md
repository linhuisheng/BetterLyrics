# Headless Host + NativeAOT Bridge Guide

This guide explains how to run a **no-UI host** for BetterLyrics services and how to build a **NativeAOT bridge DLL** that Electron can call via `ffi-napi`. The host and bridge communicate over a local named pipe, so Electron does not need to load WinUI/WinAppSDK directly.

## 1) Build the NativeAOT Bridge DLL

```bash
dotnet publish BetterLyrics.Bridge/BetterLyrics.Bridge.csproj -c Release -r win-x64
```

The output is a **native DLL** (AOT) under:

```
BetterLyrics.Bridge/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/BetterLyrics.Bridge.dll
```

> For ARM64, use `-r win-arm64`.

## 2) Run the Headless Host EXE

```bash
dotnet run --project BetterLyrics.HeadlessHost/BetterLyrics.HeadlessHost.csproj -c Release
```

This process initializes BetterLyrics services, starts the GSMTC watcher, and opens the IPC pipe for the NativeAOT bridge.

> If you run the headless host as an **unpackaged exe**, make sure the Windows App SDK runtime is available. The project uses the Windows App SDK bootstrapper to initialize the runtime without package identity.

## 3) Electron FFI bindings (example)

Callback signature (C ABI):

```c
typedef void (*BL_Callback)(const char* jsonUtf8, int length);
```

### Example (Node.js with ffi-napi)

```javascript
const ffi = require("ffi-napi");
const ref = require("ref-napi");

const callbackType = ffi.Function("void", ["pointer", "int"]);

const bridge = ffi.Library("BetterLyrics.Bridge.dll", {
  BL_Initialize: ["int", []],
  BL_StartMonitoring: ["int", []],
  BL_RegisterCallback: ["int", [callbackType]],
  BL_UnregisterCallback: ["int", []],
  BL_SearchLyrics: ["pointer", ["string", "string", "string"]],
  BL_FreeBuffer: ["void", ["pointer"]],
});

const onUpdate = callbackType.toPointer((jsonPtr, length) => {
  const json = ref.readCString(jsonPtr, 0);
  bridge.BL_FreeBuffer(jsonPtr);
  const payload = JSON.parse(json);
  console.log("Playback update:", payload);
});

bridge.BL_Initialize();
bridge.BL_RegisterCallback(onUpdate);
bridge.BL_StartMonitoring();

const resultPtr = bridge.BL_SearchLyrics("Song", "Artist", "Album");
const resultJson = ref.readCString(resultPtr, 0);
bridge.BL_FreeBuffer(resultPtr);
console.log("Search result:", JSON.parse(resultJson));
```

## 4) JSON payloads

### PlaybackSnapshot

```json
{
  "playerId": "Spotify",
  "title": "Track",
  "artist": "Artist",
  "album": "Album",
  "isPlaying": true,
  "positionSeconds": 12.3,
  "durationSeconds": 215.0,
  "lyricsText": "full lyrics...",
  "lyricsRaw": "...",
  "translation": null,
  "transliteration": null,
  "currentLineIndex": 4,
  "currentLineText": "current line",
  "currentLineStartMs": 12000,
  "currentLineEndMs": 15000
}
```

### LyricsSearchSnapshot

```json
{
  "found": true,
  "provider": "LRCLIB",
  "matchPercentage": 92,
  "title": "Track",
  "artist": "Artist",
  "album": "Album",
  "durationSeconds": 215.0,
  "raw": "...",
  "translation": "...",
  "transliteration": "..."
}
```
