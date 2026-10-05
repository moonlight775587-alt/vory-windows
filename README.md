# Vory for Windows

A Windows desktop remote for a **self-hosted Hermes Agent gateway** — a community
Windows port of [matt0975/vory](https://github.com/matt0975/vory) (MIT), the
iPhone/iPad remote for Hermes Agent. The agent runs on **your** machine; this app
is only the remote. Nothing is hardcoded: on first launch you enter the URL of
your own `hermes serve` / `hermes dashboard` and how you authenticate to it.

## Requirements

- **Windows 10 / 11 or Windows Server 2016+** (Server 2016 fully supported)
- **.NET Framework 4.8 runtime**
- To compile: **Visual Studio 2019+** or **Build Tools for Visual Studio 2019+**
  (MSBuild 16+) with the ".NET desktop development" workload
- A Hermes Agent install with the dashboard running, reachable from this machine

## Build

```powershell
# restore NuGet packages, then build
msbuild VoryWindows.sln /t:restore /p:Configuration=Release
msbuild VoryWindows.sln /p:Configuration=Release
```

The binary lands in `src\VoryWindows\bin\Release\VoryWindows.exe`.
The only NuGet dependency is **Newtonsoft.Json 13.x** (restored automatically);
everything else comes from .NET Framework 4.8 itself.

## Where credentials live

`%APPDATA%\VoryWindows\gateways.dat` — all gateway URLs, tokens and
Cloudflare Access secrets, encrypted with **DPAPI (CurrentUser)**. The app log
(`%APPDATA%\VoryWindows\logs\app.log`) redacts token-shaped values. Passwords
are used once during sign-in and never stored.

## Connecting

Settings → Gateways → Add gateway (also the first-launch screen):

1. **Name** — anything, e.g. `Home`.
2. **Gateway URL** — the dashboard base URL exactly as you reach it, e.g.
   `https://hermes.example.com`, `https://gateway.example.com/hermes`
   (reverse-proxy prefix) or `http://192.168.1.20:9119` on the LAN. Pasted
   `/api/...` suffixes are stripped. Plain `http://` is allowed; the form warns
   when the host is not on a private network.
3. **Authentication**
   - **Session token** — paste `HERMES_DASHBOARD_SESSION_TOKEN`. Sent as
     `X-Hermes-Session-Token` and as `?token=` on the WebSocket.
   - **Username & password** — RFC 8252 native flow
     (`/auth/native/authorize` → `/auth/password-login` → `/auth/native/token`).
     Only the resulting access + refresh tokens are kept; refresh goes through
     `/auth/native/refresh`, and a dead refresh token asks you to sign in again.
   - **Sign in with browser** — Nous Portal / OIDC via PKCE + loopback redirect
     (an `HttpListener` on 127.0.0.1), with a **manual code-paste fallback**
     if the listener cannot bind.
4. **Cloudflare Access (optional)** — `CF-Access-Client-Id` +
   `CF-Access-Client-Secret` are sent on every HTTP request **and** the
   WebSocket upgrade when both are set.
5. **Test connection** must pass all 3 legs before Save: `/api/status` JSON,
   credential accepted (`/api/auth/me`), and `wss://…/api/ws` opening with
   `gateway.ready`.

## Feature checklist vs the original

| Feature | Status |
|---|---|
| Chat list (search, pin, archive, delete, Needs-you badge, project filter) | ✅ pin/archive are device-local; delete hits the server |
| Streaming threads (`message.delta`), per-turn stats (`tokens · tok/s · seconds`) | ✅ |
| Tool cards (capped monospace output), todo checklist | ✅ |
| Approval cards (Once/Session/Always/Deny, incl. queued via `approval.respond`) | ✅ |
| Clarify / sudo / secret prompts (secrets never logged) | ✅ |
| Model picker grouped by provider, session-scoped `config.set` | ✅ |
| Slash autocomplete (`commands.catalog` + local `/approve /deny /stop /new /title /model /reasoning`) | ✅ (`/title`, `/stop`, `/reasoning` are best-effort; `-32601` is reported) |
| Attachments (drag-drop / paste / picker → `image.attach_bytes`, `pdf.attach`, `file.attach`, `@file:` refs) | ✅ |
| Bot-to-bot notices ("Messaging X…" → "Messaged X" → "Message from X") | ✅ |
| Home: greeting, Overview (`/api/analytics/usage?days=` 7/30/90, activity blocks, cost estimate), bots row, pick-up list | ✅ |
| Bots: profiles, drawn avatar with calm blink (no bounce/scale pops), editor (description, default model, SOUL.md), group rooms | ✅ |
| Files: paged browser, download, multipart upload, drag-drop | ✅ |
| Settings: Gateways, Model, Config, Env/API keys, Tools, Skills, MCP, Approvals, Cron, Sessions, Channels (ro), System, Maintenance, Plugins (ro), Appearance | ✅ |
| 503 "Restart required" banner (probes `/api/model/options`) with Update/Restart | ✅ |
| System tray: minimize-to-tray, approval balloons + Approve/Deny menu | ✅ |
| Live Activities / APNs push | ❌ not on Windows — tray balloons cover foreground approvals |
| Face ID lock | ❌ DPAPI-encrypted store instead |

## Protocol

Implemented exactly per `builds/PROTOCOL.md` (REST + `/api/ws` JSON-RPC 2.0 +
auth): integer-id client requests, string-id server requests, event
notifications, exponential-backoff reconnect with per-attempt
`/api/auth/ws-ticket` re-mint, `?profile=` on every scoped call, 401 →
refresh → retry, HTML-body detection, unknown event types ignored.

## Project layout

```
VoryWindows.sln
src/VoryWindows/
  App.xaml(.cs), MainWindow.xaml(.cs)
  Mvvm/            ViewModelBase, RelayCommand, AsyncRelayCommand
  Models/          gateway/session/profile/message DTOs, UrlUtil
  Networking/      JsonRpc, HermesRestClient, HermesSocketClient
  Auth/            GatewayStore (DPAPI), NativeAuthFlow, OidcLoopback
  Services/        AppState, TrayService, Log
  ViewModels/      Main, Home, Chats, ChatThread, Bots, Files, Settings (+sections)
  Views/
    Controls/      AvatarControl, ChatTemplateSelector
    Pages/         Home, Chats, ChatThreadView, Bots, Files, Settings
    Dialogs/       GatewayDialog, ProfileEditorWindow, InputDialog, CronJobDialog
  Resources/       DarkTheme.xaml, LightTheme.xaml
```

C# 7.3 max, hand-rolled MVVM, WPF on .NET Framework 4.8 — so it builds with
older toolchains and runs on Windows Server 2016.

## Attribution

Community port of [matt0975/vory](https://github.com/matt0975/vory) by
matt0975, MIT licensed. See also https://vory.dev/.
