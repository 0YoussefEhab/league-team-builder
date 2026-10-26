# Desktop edition (Windows)

The desktop app is a thin window around the normal web app. It has **no League code of its own**: the views, controllers, services and migrations are the files in `src/LeagueTeamBuilder`, so both editions behave the same.

## How it works
- `LeagueTeamBuilderDesktop.exe` (this project) starts `LeagueTeamBuilder.exe` (the web app) hidden, on `127.0.0.1:47821` (a free port if that one is taken), waits for `/healthz`, then shows it in a WebView2 window.
- Closing the window stops the server. A Windows job object also stops it if the window process is killed.
- Data lives in `%LocalAppData%\LeagueTeamBuilder\` (`leagueteambuilder.db`, `server.log`, `desktop.log`, `webview\`), never in the install folder, so updates cannot touch it.
- First start: a small form asks for the first Admin e-mail and password (the web edition reads them from user-secrets / environment variables). They are passed to the server once, through its environment.
- Links to other sites open in the normal browser.

## Build
```powershell
.\scripts\build-desktop.ps1                          # artifacts\desktop\ (run LeagueTeamBuilderDesktop.exe)
.\scripts\build-desktop.ps1 -Version 0.1.0 -Pack     # also artifacts\installer\ (needs: dotnet tool install -g vpk)
```
Add the project to the solution once: `dotnet sln add src/LeagueTeamBuilder.Desktop`.

## Not yet verified (written without a .NET SDK)
- The WebView2 package version in the csproj. If restore says it does not exist, use the latest 1.0.x.
- That publishing both projects into one folder gives no file clash (expected: identical runtime files overwrite each other).
- Window icon (`<ApplicationIcon>`), code signing (unsigned installers trigger SmartScreen), and the Velopack release/update feed.
