# League Team Builder: extraction plan (out of HFECloud) + new theme

Name: **League Team Builder** (see section 4). Goal: a standalone, open-source project that showcases the League feature, with its **own look**, while HFECloud keeps its private copy untouched.

Revised 2026-10-25 after checking `HFECloud.zip` against the first version of this plan. Nothing here was compiled or run (no .NET SDK in the authoring environment). **No box below is checked until you have built and tested it.**

**How we work:** one step per reply, then stop until you say "next step". After each step this file is updated with what was done and what was not, every changed file is listed, and anything that could not be compiled or run is said plainly. No secrets in the repo. If a design turns out wrong, you hear it before it is changed.

---

## Distribution (owner decision, 2026-10-25)

- **Open source on GitHub** (MIT), fresh repo with no HFECloud history.
- **Two forms from one codebase:**
  1. **Web edition:** a normal .NET website anyone can host (Docker, Render with a disk, any server with a persistent folder). SQLite (Postgres is not supported in v0.1.0, see decision 5), Identity login, three roles.
  2. **Desktop edition:** a downloadable app that works **fully offline** with its own **local database** (SQLite file in the user's app-data folder). No server, no account, no shared data with any web instance. Windows first.
- The web edition ships first (v0.1.0). The desktop edition is Phase E (v0.2.0) and must not block it. Section 8 has the assessment and the cheap things to do now so desktop does not need a rewrite later.

## 0. What the zip check found (corrections to the first version)

| Claim in the first version | What the zip shows |
|---|---|
| Steps 2 to 5 "done in scaffold" (copy, host, seams, rank badges) | **The scaffold is not in the zip.** `HFECloud.zip` is HFECloud only: no standalone project, no `LeagueRankBadge*`, no `RankBadgeCatalog`. Steps 2 to 5 are **not done**; they are redone as A1 to A4 and Phase B. |
| League code is about 19k lines | Confirmed: models 1,992, services 7,874, controllers 1,021, JS+CSS 8,269 (the first version said 7k). 15 `League*` DbSets confirmed. |
| `HFECloud.Tests` missing | Confirmed. Not in the zip. |
| Coupling audit is "the whole list" | **Incomplete: it covered C# types only.** The League stylesheets read the site theme: about 540 `--hv-*` references (`acc` 123, `mut` 113, `line` 91, `body` 66, `disp` 38, `card` 38, `ink` 36, `bg` 18, `bg2` 8, `logo` 7, `glow` 3). Those tokens are defined in `home-v2.css` (339 lines, one block per HFE theme) and `_Layout` loads it on every page. Without a replacement the extracted app has no colours. |
| `PLAN-league-restyle.md` R1 created a shared `_LeagueAssets` partial | **That file does not exist** and nothing references it. The pages use `lg-page` markup directly. |
| (not mentioned) | R1 to R6 of the restyle are "written, not built or run". The first compile in A5 is therefore also the first compile of that work. |
| Rank badge places to change | Confirmed, plus two the list did not name: `Services/LeagueViewText.cs` (returns the file name) and `LeagueClash/Index` (`iconBase`). Full list: `_RankSelect`, `League/Index` (3), `Leaderboard` (2), `Player`, `_MatchDetail`, `Builder`, `LeagueClash/Index`, `LeagueSettings/Guests`, `LeagueViewText`, `league-builder.js`, `league-clash-board.js`. |
| .NET 8 to .NET 10 | The csproj is `net8.0`. **Checked by grep in A1:** the 97 League files use only EF Core, Identity (2 files) and SkiaSharp (1 file, `LeagueSettingsService`). Not used by League: `AWSSDK.S3`, `MailKit`, `ClosedXML`, `QRCoder`, Google sign-in, `SkiaSharp.HarfBuzz` (those serve other HFECloud features). |

## 1. What is being extracted

| Layer | Files |
|---|---|
| Models | `Models/League*.cs` (20 files) |
| Services | `League*.cs` (about 40), `ClashTeamBuilder`, `TeamBalancer`, `ChampionCatalogService` |
| Controllers | `League`, `LeagueSettings`, `LeagueMatch`, `LeagueClash`, `LeagueExport` |
| Views | `Views/League`, `LeagueClash`, `LeagueSettings`, `Shared/_League*`, `_RankSelect`, `_ChampionPicker` |
| Front end | `wwwroot/js/league-*.js` (14 files, vanilla), `wwwroot/css/league*.css` (7 files). 273 distinct `lg-*`/`lgb-*`/`lgh-*`/`lgs-*` classes; the scripts hook onto them, so **class names and `id`/`data-*` hooks do not change** |
| Data | `Data/LeagueChampionSeeder.cs`, 15 `League*` tables |

## 2. Coupling audit (C# seams)

| Depends on | Standalone replacement |
|---|---|
| `ApplicationDbContext` (Identity + 40 other tables) | Slim `ApplicationDbContext`: Identity + the League tables |
| `ApplicationUser` (`DisplayName`, `AvatarUrl`, `Email`, `UserName`) | Small `ApplicationUser` with the same two extra fields |
| **`GroupPermissions` table + `ApplicationUser.PermissionGroupId`** (new in A3; the A2 grep missed it): `LeagueGuestLinkService` (lines 117, 152, 185, 321, 558) and `LeagueClashScheduleService` (318 to 325) query the matrix directly to find users who may open League / Respond | Not copied. A4 rewrites those 6 spots to ask the role table ("which roles may do X", then users in those roles). `ApplicationUser.IsDeactivated` (also read there) is kept |
| `IPermissionChecker` + `DynamicPermissionFilter` + group matrix | `RolePermissionChecker`: Admin, Officer, Member with an action table. Same interface, views and controllers stay as they are |
| `FileUploadService` (static, R2 + disk) | Local-disk `FileUploadService`, same method names. R2 dropped |
| `MediaUploadLimits` | Trimmed copy (avatar + image limits) |
| `NotificationService.CreateForUserAsync` | `ILeagueNotifier` + in-app notification table. Discord optional |
| `AdminActivityLogger.Log` | `IActivityLog` + `ActivityLogEntry` table |
| `DiscordNotifier.IsValidWebhookUrl` | One static helper |
| `AdminNamesEdit`, `LeagueGuestNamesPageModel` | Copied (small, in `Models/`) |
| **Theme tokens `--hv-*` from `home-v2.css`** (new in this version) | `leaguehub-theme.css`, see section 4 |

**Layout contract.** League views expect `_Layout` to provide `@section Styles` and `@section Scripts`. The HFECloud layout also loads Google Fonts, Bootstrap Icons and (in League views) `html2canvas` from CDNs; all of these must be vendored locally (decision 8). The standalone `_Layout` provides both.

**Tests.** `HFECloud.Tests` (571 tests per `PLAN-league.md`) is not in the zip. Add it before publishing. The rule classes are pure, so they move over with a namespace change.

## 3. Decisions

1. Stay on .NET, move to **.NET 10 LTS** (.NET 8 support ends 10 Nov 2026).
2. **Copy first, share later.** Optional Razor Class Library at the end (D5).
3. Simple auth: Identity default UI. Roles Admin, Officer, Member. Admin seeded from config.
4. **No Riot assets in the repo.** Rank badges become uploadable (Phase B).
5. **SQLite only** (owner decision, 2026-10-26, replaces "SQLite by default, Postgres by setting"). One set of migrations cannot serve both providers, and the desktop edition needs real in-place upgrades. One migration set, one provider package. Postgres can be added later as a second context with its own migrations without touching the SQLite ones. Fresh migrations; HFECloud's are not copied.
6. MIT license. README says "Not endorsed by or affiliated with Riot Games."
7. Open source on GitHub; two editions, web first then offline desktop (see Distribution and section 8).
8. No CDN at runtime: fonts, `html2canvas` and Bootstrap Icons are self-hosted in `wwwroot/lib`. Needed for offline, and also better for privacy and speed.
9. GitHub repo `league-team-builder` exists and is **private until D4**. Nobody else is added as a collaborator, so only the owner can change the code. After it goes public, others can read, download and fork it (the MIT license allows editing their own copy) but cannot change the owner's repo. Contributions are not accepted: the README says so, and any pull request is only a proposal that changes nothing until the owner merges it.

## 4. New theme: three looks, user picks (prototype: `league-theme-prototype.html`)

Why a new theme: the current League look is HFECloud's (dark editorial, gold/crimson/glitch, Anton, logo watermark). League Team Builder should not look like a sub-page of another site.

**Decisions (owner):** default look is **Steel**. Ship **Steel, Brutal (dark) and Forest**; Tide, Neon, Chalk and Ember were tried and dropped. Each user picks their own look. The prototype has a "Look" dropdown in the header that does this and remembers the choice. All three looks are dark, so no light mode is needed.

| Look | Shape | Type | Sides (A / B) | Extra |
|---|---|---|---|---|
| **Steel** (default) | 3px corners, thick left edge on tiles | Chakra Petch + Barlow | cyan `#19d3ff` / orange `#ff7a1a` | diagonal slash background |
| **Brutal** (dark) | 0 radius, 2px off-white borders | Archivo Black + Barlow | blue `#4d8dff` / red `#ff4d42` | hard gold offset shadow on tiles, graphite `#0f0f10` background |
| **Forest** | 14px rounded, soft tiles | Outfit | green `#4ade80` / coral `#ff8a65` | contour-line background |

Shared in all looks: the palette is **two-sided** (Side A and Side B, the two teams), gold is reserved for MVP, and the memorable element is the balance scoreboard (two big numbers, the gap, a split meter). A look is **only a token block**: colours, radius, fonts, tile background/shadow/border, soft divider colour (`--rule`), page background. No look changes markup, class names or behaviour, so adding another later is one block of CSS. The old Glitch theme, `--lg-k` radius multiplier, HFE glow/grid layer and `--hv-logo` watermark are removed.

**How the picker works in the app (step C1):**
- `wwwroot/css/leaguehub-looks.css` holds one `[data-look="..."]` block per look; Steel is the `:root` default.
- The choice is stored in a cookie `lh-look` and read by `_Layout` on the server, so `<html data-look="...">` is correct on first paint (no flash). Signed-in users also get it saved on their `ApplicationUser` (one nullable string column, in A3's migration) so it follows them across devices.
- A site default comes from config `Theme:DefaultLook` (default `steel`); the allowed list is one array in code, so an admin can hide looks. An unknown saved value falls back to the default.
- The prototype uses `data-theme` and short token names; the app uses `data-look` and `--lh-*`.
- The result image (canvas, Discord) uses a fixed palette on purpose so the picture is the same for everyone; its dialog follows the look.

**Bridge so nothing breaks on day one:** `leaguehub-theme.css` defines real `--lh-*` tokens and **aliases** every `--hv-*` name the League CSS uses (`acc` to Side A, `bg`, `bg2`, `ink`, `mut`, `line`, `card`, `glow`, `body`, `disp`, `r`, `fg`). The old stylesheets then render in the new palette without edits, and C2 to C5 replace the aliases with real `--lh-*` names and use Side B where the meaning is "red side".

**Things to watch (not decided):**
- Fonts: four families across the three looks. Proposal: load only the active look's fonts (the `<link>` is picked by the server from the cookie), or self-host.
- The old `league*.css` files still contain 115 literal 6-digit hex colours (30 distinct, for example `#12161F`, `#2A3142`, `#E6B84F`, `#4FD1C9`). C2 turns them into tokens; until then Brutal and Forest would show Steel-era colours in places.
- Brutal's off-white `--line` is used for borders everywhere in the prototype, so dividers and progress tracks use the softer `--rule`. Real pages (tables, dialogs, inputs) may need the same split.
- Rank badge placeholder colours must read on all three tile backgrounds.

**Decided:** a user's chosen look also applies on public pages, via the same cookie.

**Name (owner decision): "League Team Builder"**, code namespace and project `LeagueTeamBuilder`. "LeagueHub" was rejected. Flag, not a blocker: the name contains "League", which is also the first word of Riot's trademark. It is a descriptive name and the README carries the "not endorsed by or affiliated with Riot Games" line, but this is not legal advice. Keep the name unique in code only (one namespace), so a rename later is one script. Check Riot's current community-project policy before the first public release, and check GitHub, domain and NuGet availability yourself.

## 5. Steps (one per reply)

### Phase A: get the extraction compiling and tested (old look, features only)

- [x] **A1 Skeleton (built, run, pushed; see section 9).** New `LeagueTeamBuilder` solution, `LeagueTeamBuilder.csproj` on `net10.0`, trimmed packages, minimal `Program.cs`, `.gitignore`, MIT `LICENSE`, `global.json`, README stub. No League code yet. *You run:* `dotnet restore`, `dotnet build`, `dotnet run`.
- [x] **A2 Copy League code (applied and verified; see section 9).** Copy the files in section 1 (119 files) and rename namespace `HFECloud` to `LeagueTeamBuilder` by script, and report leftover HFECloud wording. Do **not** copy: `PLAN-*.md`, uploads, avatars, `HFECloud.db`, `wwwroot/img/ranks`, `league-export/bg-*.jpg`. The project does **not** build after this step (expected).
- [ ] **A3 Data and identity (written 2026-10-26, not built; see section 9).** Slim `ApplicationDbContext` (Identity + 15 League tables + data-protection keys), `ApplicationUser` (with `IsDeactivated` and the nullable `Look`), Identity default UI, three roles (new accounts are Members), admin seed from config, SQLite only, `/Admin/Users` role editing and deactivation.
- [ ] **A4 Seams.** `RolePermissionChecker` + filter, rewrite the 6 `GroupPermissions` / `PermissionGroupId` spots (section 2), register all League services in `Program.cs` (the list has to be built from the copied classes; HFECloud's `Program.cs` is not in hand), `ILeagueNotifier`, `IActivityLog`, local `FileUploadService`, `MediaUploadLimits`, Discord URL helper, copied small models, a temporary plain `_Layout` with `Styles` and `Scripts` sections.
- [ ] **A5 First build.** *You run:* `dotnet restore && dotnet build`, paste the errors, I fix in rounds until it is clean. This is also the first compile of the HFECloud restyle R1 to R6.
- [ ] **A6 Migration.** *You run:* `dotnet tool install --global dotnet-ef` (version 10), then `dotnet ef migrations add InitialCreate`; check it creates only the Identity tables, the 15 League tables and `DataProtectionKeys`.
- [ ] **A7 Tests.** Copy `HFECloud.Tests`, change namespace, *you run* `dotnet test`.
- [ ] **A8 Click-through.** Every League page once, features only, so behaviour bugs are found **before** the theme changes and are not confused with styling bugs.
- [ ] **A9 Wording pass.** Remove or make configurable the HFECloud / HFE wording the click-through shows (list in section 9, A2). Decisions first, see A2 notes: result-image footer and subtitle, "HFECloud account/profile" texts, Discord bot username, and the `HFE-STATS` paste format name.

### Phase B: uploadable rank badges (replaces the Riot art)

- [ ] **B1 Data and service.** `LeagueRankBadges` table (`Tier` PK, nullable `ImageUrl`), `LeagueRankBadgeService` (`SetAsync`, `RemoveAsync`, `ResetAllAsync`; png/jpg/jpeg/webp only, **no SVG**, 1 MB, must decode as an image via SkiaSharp, old file deleted only after the save succeeds), `RankBadgeCatalog` singleton, permission actions `LeagueSettings/Badges`, `UploadBadge`, `RemoveBadge`, `ResetBadges`.
- [ ] **B2 Swap every hard-coded URL** (list in section 0) to `Badges.Url(rank)`, and the two JS files to read `cfg.icons[key]`. Neutral placeholder SVG per tier in `wwwroot/img/ranks/default/`, tier colours as in the prototype.
- [ ] **B3 Badges page** `LeagueSettings/Badges`: card per tier (preview, upload, remove) and "Reset all to default". Plain markup; it is styled in C5.
- [ ] **B4 Tests** for the upload rules (extension, size, decode, delete-after-save).

*Known limit:* the catalog is in memory per process; behind several instances a change shows after restart. Fine for the demo.

### Phase C: new theme (Steel default + Brutal + Forest, per-user picker)

- [ ] **C1 Tokens, looks and shell.** `leaguehub-theme.css` (tokens + `--hv-*` aliases), `leaguehub-looks.css` (three `data-look` blocks), look picker (cookie + saved on the user), final `_Layout` (top bar with the brand mark and tabs, fonts for the active look, footer disclaimer). Drop all use of `home-v2.css`. **Self-host** the look fonts, `html2canvas` and Bootstrap Icons under `wwwroot/lib` (no CDN, needed for the desktop edition).
- [ ] **C2 Components in `league.css`:** tiles, buttons, pills, inputs, tables, dialogs, nav, focus rings, banners, empty states. Remove Anton/uppercase, glow/grid layer, `--hv-logo` watermark, `--lg-k`, and every hard-coded hex so all three looks work.
- [ ] **C3 Overview, Leaderboard, Player, Profile** (+ `league-stats.css`). Balance scoreboard on the Overview.
- [ ] **C4 Builder, Clash, History, match popup** (+ `league-builder.css`, `league-clash.css`, `league-history.css`). Side A/B colours, gold for MVP, Discord result image palette.
- [ ] **C5 Settings pages and the Badges page.** Replace the remaining `--hv-*` aliases with `--lh-*` and delete the alias block.
- [ ] **C6 Verification.** Every look (Steel, Brutal, Forest), widths 360/768/1024/1280, keyboard, reduced motion, JS off, every hook the scripts use still exists. *You run* the click-through.

### Phase D: launch

- [ ] **D1 Demo seed** (fake players, matches, a clash) behind `Demo:Seed=true`.
- [ ] **D2 README** with screenshots/GIF, Riot disclaimer, run instructions, and a "not accepting contributions, fork it for your own version" line.
- [ ] **D3 Docker** (`docker compose up`, database folder on a volume) and a live demo on a host with a persistent disk (SQLite has no separate database server; D1 reseeds the demo data at start-up, so a reset is harmless).
- [ ] **D4 Fresh git repo** (no HFECloud history), review leftover HFECloud wording and the champion list, push, tag `v0.1.0`.
- [ ] **D5 Optional:** Razor Class Library so HFECloud can consume it.

### Phase E: desktop edition (offline, local database; after the web v0.1.0)

- [ ] **E1 Desktop mode switch.** Config `App:Mode = Web | Desktop`. In Desktop: SQLite file in the per-user app-data folder, migrations applied on startup, uploads in the same folder, auto sign-in as a local Admin (no register/login/roles screens), Kestrel on `127.0.0.1` only with a random free port, and a per-launch token plus host-header check so other local users or web pages cannot talk to it. Features that need internet (Discord auto-send) show "offline" instead of failing.
- [ ] **E2 Window shell.** Host the same app in-process inside a native window (Photino.NET or WebView2 to be evaluated; fallback: open the default browser). Single-instance lock, clean shutdown, window title and icon.
- [ ] **E3 Backup and move data.** Settings page to export and import the database file (with uploads), so users can back up or move machines. This is also the only way data moves between editions; there is no sync.
- [ ] **E4 Packaging.** `dotnet publish` self-contained for `win-x64`, then an installer or portable zip. Confirm SkiaSharp's Windows native files are included. An unsigned `.exe` triggers Windows SmartScreen warnings; a code-signing certificate costs money, so decide whether to sign or document the warning.
- [ ] **E5 Clean-machine test.** A Windows machine with no .NET installed and the network off: install, create a roster, build teams, save a match, export the result image, restart, back up and restore.
- [ ] **E6 Release.** GitHub Releases with the download, README download section, tag `v0.2.0`. macOS and Linux builds are optional later.

## 6. Other Riot-owned or risky assets

| Asset | League Team Builder |
|---|---|
| Rank badges `wwwroot/img/ranks/*.png` | Removed, placeholders + upload |
| Map images `wwwroot/uploads/league-maps/*` | Removed (already uploadable) |
| Export backgrounds `wwwroot/img/league-export/bg-*.jpg` | Removed; export falls back to a plain background |
| Champion list (about 170 names) | Kept (names are facts, no art); review before launch |
| User avatars, `HFECloud.db` | Removed |

Check Riot's current policy for community projects yourself; this is not legal advice.

## 7. Known rough edges

- Copied code has HFECloud wording in comments; the Discord username and result-image text say "HFE League" (make configurable or rename).
- Pages only reachable from the HFECloud sidebar (Notifications, Profile) have no equivalent; the League tabs are the navigation.
- Uploads are served from `/uploads/...` at the site root; a path base (`/league`) would need `Url.Content`.

## 8. Desktop edition: assessment

Verdict: realistic, and cheap **if** we keep a few rules from the start. Not realistic as a rewrite: the views are Razor MVC, so a Blazor/MAUI app would mean redoing the UI. The plan is the same ASP.NET app running locally inside a window.

Why it fits: SQLite is already the default provider, the front end is vanilla JS with no build step, and the League code has no outside services except optional Discord.

Cheap rules to follow now (they cost almost nothing today and save a rewrite later):
1. No CDN at runtime (decision 8). Online-only assets are the number one thing that breaks offline.
2. All file paths (database, uploads) come from config, never `wwwroot/uploads` hard-coded.
3. Everything that talks to the internet (Discord, notifications) sits behind an interface that can be turned off.
4. Migrations run on startup in Desktop mode, so users never run `dotnet ef`.
5. Do not store anything in memory per process that the user would expect to survive (the rank-badge catalog is rebuilt from the database on start, which is fine).

Real costs to accept:
- Updates are manual downloads at first (an auto-updater can come later).
- SmartScreen warning for unsigned `.exe` files.
- Bigger download (self-contained .NET + SkiaSharp), likely tens of MB.
- Two editions to test; Desktop has no multi-user roles, so it needs its own click-through (E5).
- No sync between a desktop database and a web one; export/import only.

## 9. What was actually done, per step

### A1 Skeleton (2026-10-26): DONE (built, run, pushed)

**Owner's result (from a screenshot, Windows PowerShell, `C:\Users\Yousef\source\repos\LeagueTeamBuilder`):** `git init` ok, `dotnet restore` ok, `dotnet build` succeeded with no warnings shown, `dotnet run` started ("Now listening on: http://localhost:5000", Production environment). So the .NET 10 SDK, the floating `10.0.*` packages and the hand-written `.sln` layout restored and compiled.
**Browser check (screenshots):** `/` shows "League Team Builder: skeleton (step A1)." and `/healthz` shows `"ok"`. Both pass.
**Resolved versions (`dotnet list package`):** Identity.EntityFrameworkCore, Identity.UI, EF Core Design, Sqlite and Tools all 10.0.14; Npgsql provider 10.0.3; SkiaSharp and its Linux natives 2.88.9.
**First commit (owner's terminal):** first attempt `09ac4fd` could not be pushed ("Repository not found": a typo in the GitHub username in the remote URL and in the Git email, both fixed). The commit was amended with the corrected no-reply author and **pushed: `490c0d9 "A1: skeleton"` is on `origin/main`** (9 files, no `bin`/`obj`, CRLF warnings only).
**Pinned csproj:** written with those exact versions (no `10.0.*` left). **Confirmed (owner screenshots):** the pinned csproj is what is committed in `490c0d9` (EF Core and Identity 10.0.14, Npgsql 10.0.3, SkiaSharp 2.88.9, no `10.0.*`), and `dotnet build` with it succeeds (restore 1.1s, build 6.2s, no warnings shown). A1 is ticked.

**Original authoring record (below) was written before the build:**

**Done**
- Grep of the 97 League files (models, services, controllers, views, partials, seeder) for package use. Result: only EF Core, Identity and SkiaSharp are needed, so the package list in the new csproj is eight packages instead of fifteen.
- Wrote the skeleton (9 files, listed below). Checked only that the csproj and both JSON files are well-formed and that the `.sln` has one project.

**Not done / could not be run**
- No .NET SDK in the authoring environment: nothing was restored, built or run.
- Package versions are floating (`10.0.*`, SkiaSharp pinned at 2.88.9). I did not confirm that every package has a 10.0.x release on NuGet (the Npgsql provider and Identity UI are the two to watch). Pin exact versions after the first successful restore.
- `LeagueTeamBuilder.sln` was written by hand and not opened in Visual Studio.
- `global.json` requires SDK 10.0.100 or newer (`latestFeature` roll-forward); with an older SDK installed, every `dotnet` command in the repo will refuse to run.

**You run (from the repo root)**
1. `dotnet --version` shows 10.x.
2. `dotnet restore` then `dotnet build`. Paste any error or warning.
3. `dotnet run --project src/LeagueTeamBuilder`, open the printed address: `/` shows "League Team Builder: skeleton (step A1)." and `/healthz` shows "ok".
4. If the `.sln` is rejected: delete it and run `dotnet new sln -n LeagueTeamBuilder` then `dotnet sln add src/LeagueTeamBuilder` (the .NET 10 SDK writes a `.slnx` file; that is fine).
5. After a clean build, run `dotnet list package` and pin the versions. Then A1 gets its checkmark.

**Changed / new files** (all new; delivered in `LeagueTeamBuilder-A1.zip`)
`LeagueTeamBuilder.sln`, `global.json`, `.gitignore`, `LICENSE`, `README.md`, `src/LeagueTeamBuilder/LeagueTeamBuilder.csproj`, `src/LeagueTeamBuilder/Program.cs`, `src/LeagueTeamBuilder/appsettings.json`, `src/LeagueTeamBuilder/wwwroot/.gitkeep`; plus this plan file (sections 0, 5 and 9 edited).

**Choices to confirm**
- `LICENSE` names "Youssef Ehab Alsayed" as copyright holder for 2026. Change it if you want a handle or an organisation.
- `UserSecretsId` is a fresh GUID, so this project shares no user-secrets store with HFECloud. Nothing secret is in any file.
- Left out on purpose: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` (keeps users signed in across redeploys on Docker/Render by storing keys in the database); decide in A3 when the database exists. Also not copied: the Dockerfile and `pg_dump` setup (D3), and `Resources/Fonts` (only the link-preview cards used them).
- Expect compile errors in A5 from the .NET 8 to .NET 10 move, on top of the extraction itself.

### A2 Copy League code (2026-10-26): DONE (applied, verified against the owner's zip; not built, by design)

**Done (in the authoring environment, from `HFECloud.zip`)**
- Copied **119 files** into the repo layout under `src/LeagueTeamBuilder/`: 21 models (20 `League*` + `AdminNamesEdit.cs`, which holds both small helper classes), 39 services (36 `League*` + `ClashTeamBuilder`, `TeamBalancer`, `ChampionCatalogService`), 5 controllers, `Data/LeagueChampionSeeder.cs`, 24 files in `Views/League`, `LeagueClash`, `LeagueSettings`, 8 shared partials, 14 `league-*.js`, 7 `league*.css`. Not copied: plan files, uploads, avatars, the database, rank images, export backgrounds.
- Renamed the namespace in 89 of them (`namespace`, `using`, `@using` and qualified `HFECloud.Xxx` names). Result: only `LeagueTeamBuilder.Controllers / Data / Models / Services` remain, none named `HFECloud`.
- Checked that every `~/js` and `~/css` file the views and scripts reference is in the copy (none missing). The only other local assets they use are the rank images (Phase B) and two CDNs: `html2canvas` (jsdelivr, 4 uses) and Google Fonts (3 uses), both self-hosted in C1.
- Re-checked the seam list against the code: `ApplicationDbContext` 16 files, `IPermissionChecker` 13, `ApplicationUser` 6, `FileUploadService` 2, `MediaUploadLimits` 2, `NotificationService` 2, `AdminActivityLogger` 2, `DiscordNotifier` 1, `DynamicPermission` 1. Matches section 2.

**Not done / not run:** nothing was compiled (no SDK here). The project will not build after A2: the League code needs the context, user class, permission checker and the other seams that A3 and A4 add. `Views/_ViewImports.cshtml`, `_ViewStart.cshtml` and `_Layout` are host files, so they come in A4 (the HFECloud `_ViewImports` has `@using HFECloud` and `@using HFECloud.Models`, which become `LeagueTeamBuilder` there).

**Left on purpose (renaming would change behaviour or needs your decision), full list in `A2-leftover-wording.txt`**
- User-visible "HFECloud account / profile" texts: `LeagueSettings/Index`, `Guests`, `LinkGuest`, `League/Player`, `League/Profile` (about 10 lines). In a standalone app these become "account" / "profile".
- Result image: footer `hfecloud.com` (3 places in `league-result-image.js`), subtitle "· HFECloud" in `league-builder.js`, and `HFECloud` pushed into a clash text in `league-clash-board.js`. Proposal: one configurable site name and URL.
- `LeagueDiscordSender.cs`: the Discord bot username is "HFE League". Proposal: configurable, default "League Team Builder".
- `league-stats-import.js`: the text format header **`HFE-STATS`** (written and parsed, 4 places). Renaming breaks text people already exported. Proposal: write a new name (for example `LTB-STATS`) and keep accepting `HFE-STATS` when reading.
- One comment in `LeagueSettingsController.cs` and the `league-result-image.css` header comment.

**You run (after extracting the zip into the repo root so `src\` merges):**
1. `git status --short` lists new files only (119), nothing modified.
2. `(git ls-files | Measure-Object).Count` after `git add .` is 128 (9 from A1 + 119).
3. `Get-ChildItem src -Recurse -File | Select-String "namespace HFECloud"` finds nothing.
4. Do **not** expect `dotnet build` to pass yet.

**Changed / new files:** `LeagueTeamBuilder-A2.zip` (119 files under `src/LeagueTeamBuilder/`); `A2-leftover-wording.txt` (a working note: keep it out of the repo); this plan file (A2 and A9 lines, section 9).

**A2 verification (owner uploaded the project folder as a zip, plus a build error list):**
- The zip holds 124 files: all 123 expected (4 from A1 + 119 from A2) are present and **byte-identical** to what was prepared; the only extra is `Properties/launchSettings.json`, created by Visual Studio (dev ports 51054/51055, no secrets; fine to commit). So after `git add .` the file count is 129, not 128. The zip has no `.git`, so the commit/push of A2 was not seen; confirm it.
- `LeagueTeamBuilder.csproj` still has the pinned versions; no `namespace HFECloud` / `using HFECloud` remains.
- The build errors are the expected ones and nothing is missing from the copy. They are of two kinds:
  1. **Seam types, 5 distinct:** `ApplicationDbContext`, `ApplicationUser`, `IPermissionChecker`, `NotificationService`, `AdminActivityLogger`. Provided in A3 (context, user) and A4 (the rest).
  2. **26 view-model types** (`LeagueClashPageModel`, `LeagueOverviewModel`, `LeagueRankSelectModel` and so on). All 26 are defined in the copied `Models/` files. The Razor views cannot see them only because `Views/_ViewImports.cshtml` does not exist yet (no `@using LeagueTeamBuilder.Models`). A4 adds it and they go away.
- Types the pasted list does not mention but the copy uses and A4 must provide: `FileUploadService` (2 files), `MediaUploadLimits` (2), `DiscordNotifier` (1), the dynamic-permission attribute (1). They will show up once the first two kinds are fixed.

### A3 Data and identity (2026-10-26): WRITTEN, NOT BUILT

**Owner decision taken:** SQLite only (option 1 of the question below). Reason in decision 5.

**Done (written in the authoring environment, from the A2 project zip; nothing compiled)**
- `ApplicationDbContext`: Identity + the 15 League tables + `DataProtectionKeys`. Keys, composite keys, unique indexes (`LeagueProfile.UserId`, clash team name per clash, clash answer per event and profile) and every delete rule are configured by hand from the model comments, because HFECloud's context is not in hand. Cascade and SET NULL are enforced by the database, since the League code uses `ExecuteDelete` and `ExecuteUpdate` (they skip EF's in-memory cascade). Deleting a clash event, clash team or champion that is still referenced **fails on purpose** (Restrict); nothing in the code deletes events.
- **UTC fix (found while reading, not in the plan):** SQLite stores `DateTime` as text and reads it back with Kind Unspecified, so JSON to the browser would lose its `Z` and every time would show shifted by the viewer's UTC offset. The context converts every `DateTime` and `DateTime?` to UTC on read. HFECloud was on Postgres, which forces UTC, so this was never visible there.
- `ApplicationUser`: `DisplayName` (never null, because queries compare it with `""`), `AvatarUrl`, `IsDeactivated`, `Look`. No `PermissionGroupId` (see section 2).
- Identity default UI; password rule is length 8 only; no e-mail confirmation and a no-op e-mail sender (so password reset by e-mail does nothing; an admin handles a forgotten password for now). `AppUserManager` gives everyone who registers the Member role. `AppSignInManager` refuses a deactivated account. A role change or deactivation reaches an open session within about a minute.
- Seed at start-up: the three roles, the champion list, and the first Admin from `Seed:AdminEmail` / `Seed:AdminPassword`. It acts only when there is no active Admin, never changes a password, and refuses to start if the password breaks the rule (no half-made account). If there is no Admin and the config is missing it logs a warning and the site still runs.
- `Database:Path` (default `App_Data/leagueteambuilder.db`, relative to the app folder, not the shell folder) and `Database:MigrateOnStart` (default true). Start-up stops with a clear message while no migration exists (until A6).
- Data-protection keys are stored in the database, so sign-ins survive restarts.
- `/Admin/Users` (Admin role only, not the League permission table): list, change role (one role per user), deactivate or reactivate. Refuses to remove or deactivate the last active Admin and refuses self-deactivation. Plain markup, styled in C5.
- `Program.cs`: default route is now `League/Index` (the A1 skeleton text at `/` is gone); `/healthz` stays.

**Not done / could not be run**
- Nothing compiled or run (no .NET SDK, and no access to NuGet from here). Not confirmed: that `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.14 exists (it should, it ships with Identity 10.0.14); that `Properties<DateTime?>()` and `Properties<DateTime>()` can both be set in `ConfigureConventions` without a clash; the overrides in `AppUserManager` / `AppSignInManager` (written against the .NET 10 constructors from memory).
- The app does not start until A4 (services, permission checker) and A6 (migration) exist. That is expected.
- Registration through the Identity default UI cannot ask for a display name (the pages are not copied), so new accounts have an empty `DisplayName` and show under their e-mail. A profile edit or an admin name edit sets it. Say if you want a copied Register page.
- Left out on purpose: sign-in with Google, e-mail sending, a "delete user" button (accounts are deactivated, never deleted, so league history stays).

**First build (owner, 2026-10-26):** restore clean; the `ApplicationUser` errors are gone. 30 `ApplicationDbContext` errors remained because the context was in `LeagueTeamBuilder.Data`; moved to the root namespace `LeagueTeamBuilder` (as in HFECloud). `Program.cs` got `using LeagueTeamBuilder;`. For A4: `IPermissionChecker`, `NotificationService` and `AdminActivityLogger` are used the same way (no `using` for them in the League files), so they go in the root namespace too.

**New find (section 2, row added):** the League code reads the old group table in 6 places. A4 rewrites them.

**You run, after copying the files in (one new folder: `Views/AdminUsers`)**
1. Add one line to `.gitignore`: `App_Data/` (the database file must never be committed).
2. `dotnet restore` shows no error for the new package. If `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.14 is not found, paste the message.
3. `dotnet build` still fails (A4 adds the rest). The seam errors for `ApplicationDbContext` and `ApplicationUser` should be gone; the other three seam types remain.
4. Do not run the app yet.

**Changed / new files** (send-only-the-edited-files, no zip): new `Models/ApplicationUser.cs`, `Models/AdminUsersModels.cs`, `Data/ApplicationDbContext.cs`, `Data/AppRoles.cs`, `Data/DatabaseSetup.cs`, `Data/IdentitySeeder.cs`, `Services/AppIdentity.cs`, `Controllers/AdminUsersController.cs`, `Views/AdminUsers/Index.cshtml`; changed `Program.cs`, `LeagueTeamBuilder.csproj` (Npgsql removed, data-protection package added), `appsettings.json` (`Database` section); this plan file.

**Choices to confirm**
- A profile's link to its user is Restrict (a user with a league profile cannot be deleted; deactivate instead).
- No unique index on champion names (the service checks duplicates; a database rule could clash with what it allows).
- The `Seed:*` keys are not in `appsettings.json` at all, so no secret and no empty placeholder is in the repo. Set them with `dotnet user-secrets set "Seed:AdminEmail" "you@example.com"` and `dotnet user-secrets set "Seed:AdminPassword" "..."` from the project folder.

**The question that was asked (kept for the record):** one EF migration set cannot serve SQLite and Postgres; HFECloud used `EnsureCreated` for SQLite (no upgrades) and migrations for Postgres only. Options were SQLite only, two migration sets, or Postgres for web and SQLite for desktop. Chosen: SQLite only.
