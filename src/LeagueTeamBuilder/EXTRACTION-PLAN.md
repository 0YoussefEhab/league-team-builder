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


**Decision change (owner, 2026-10-26): map pictures in the demo.** The owner says a friend who works at Riot said a free showcase is fine. This is not a written licence. Decision: the static demo page (`site/`) MAY use the map pictures `site/img/normal-map.jpg` and `site/img/aram-map.jpg`, with the line "Map picture © Riot Games. Not endorsed by or affiliated with Riot Games." under each map. They still do NOT go in the repo's app folders, in the app zips, or in the real app. If a picture is missing, the demo shows a plain dark box. The owner should still read Riot's community-project policy before the repo is public.

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
- [x] **A3 Data and identity (written 2026-10-26, not built; see section 9).** Slim `ApplicationDbContext` (Identity + 15 League tables + data-protection keys), `ApplicationUser` (with `IsDeactivated` and the nullable `Look`), Identity default UI, three roles (new accounts are Members), admin seed from config, SQLite only, `/Admin/Users` role editing and deactivation.
- [x] **A4 Seams (written 2026-10-26, not built; see section 9).** `RolePermissionChecker` + filter, rewrite the 6 `GroupPermissions` / `PermissionGroupId` spots (section 2), register all League services in `Program.cs` (the list has to be built from the copied classes; HFECloud's `Program.cs` is not in hand), `ILeagueNotifier`, `IActivityLog`, local `FileUploadService`, `MediaUploadLimits`, Discord URL helper, copied small models, a temporary plain `_Layout` with `Styles` and `Scripts` sections.
- [x] **A5 First build.** *You run:* `dotnet restore && dotnet build`, paste the errors, I fix in rounds until it is clean. This is also the first compile of the HFECloud restyle R1 to R6.
- [x] **A6 Migration.** *You run:* `dotnet tool install --global dotnet-ef` (version 10), then `dotnet ef migrations add InitialCreate`; check it creates only the Identity tables, the 15 League tables, `DataProtectionKeys`, and the two tables A4 added (`UserNotifications`, `ActivityLogEntries`).
- [x] **A7 Tests (owner ran `dotnet test` 2026-10-27: 168 of 168 passed; see section 9).** `HFECloud.Tests` is not in the zip, so the tests were written from scratch in `tests/LeagueTeamBuilder.Tests` (created in B4): permissions and the filter, looks, game names, match-day rules, the team balancer, the champion catalog. Services that need `ILeagueNotifier` / `IActivityLog` fakes (match, guest, clash, profile, history) are NOT covered yet.
- [ ] **A8 Click-through (checklist written 2026-10-27; owner has not run it yet; see section 9).** Every League page once, features only, so behaviour bugs are found **before** the theme changes and are not confused with styling bugs. (The theme was in fact written first, C1 to C5, so styling is part of what you will see; note it, do not chase it yet.)
- [ ] **A9 Wording pass (written 2026-10-27, not built or run; see section 9).** Remove or make configurable the HFECloud / HFE wording the click-through shows (list in section 9, A2). Decisions first, see A2 notes: result-image footer and subtitle, "HFECloud account/profile" texts, Discord bot username, and the `HFE-STATS` paste format name.

### Phase B: uploadable rank badges (replaces the Riot art)

- [x] **B1 Data and service (written 2026-10-27; owner ran the `AddLeagueRankBadges` migration and the build succeeded, 2026-10-27; see section 9).** `LeagueRankBadges` table (`Tier` PK, nullable `ImageUrl`), `LeagueRankBadgeService` (`SetAsync`, `RemoveAsync`, `ResetAllAsync`; png/jpg/jpeg/webp only, **no SVG**, 1 MB, must decode as an image via SkiaSharp, old file deleted only after the save succeeds), `RankBadgeCatalog` singleton, permission actions `LeagueSettings/Badges`, `UploadBadge`, `RemoveBadge`, `ResetBadges`.
- [ ] **B2 (written 2026-10-27, not built; see section 9) Swap every hard-coded URL** (list in section 0) to `Badges.Url(rank)`, and the two JS files to read `cfg.icons[key]`. Neutral placeholder SVG per tier in `wwwroot/img/ranks/default/`, tier colours as in the prototype.
- [ ] **B3 (written 2026-10-27, not built; see section 9) Badges page** `LeagueSettings/Badges`: card per tier (preview, upload, remove) and "Reset all to default". Plain markup; it is styled in C5.
- [x] **B4 Tests** for the upload rules (owner ran `dotnet test` 2026-10-27: 54 of 54 passed; see section 9) (extension, size, decode, delete-after-save).

*Known limit:* the catalog is in memory per process; behind several instances a change shows after restart. Fine for the demo.

### Phase C: new theme (Steel default + Brutal + Forest, per-user picker)

- [x] **C1 Tokens, looks and shell (written 2026-10-26, not built; see section 9).** `leaguehub-theme.css` (tokens + `--hv-*` aliases), `leaguehub-looks.css` (three `data-look` blocks), look picker (cookie + saved on the user), final `_Layout` (top bar with the brand mark and tabs, fonts for the active look, footer disclaimer). Drop all use of `home-v2.css`. **Self-host** the look fonts, `html2canvas` and Bootstrap Icons under `wwwroot/lib` (no CDN, needed for the desktop edition). Fonts, `html2canvas` and the 4 view tags are NOT swapped yet: no network in the authoring environment, so the download list is in section 9 (C1).
- [x] **C2 Components in `league.css` (written 2026-10-26, not built; see section 9).** tiles, buttons, pills, inputs, tables, dialogs, nav, focus rings, banners, empty states. Remove Anton/uppercase, glow/grid layer, `--hv-logo` watermark, `--lg-k`, and every hard-coded hex so all three looks work.
- [x] **C3 Overview, Leaderboard, Player, Profile** (+ `league-overview.css`, `league-stats.css`) (written 2026-10-26, not built; see section 9). Scoreboard CSS is done and the scoreboard is on the Overview (C3b, 2026-10-26, owner delegated the decision; not built).
- [x] **C4 Builder, Clash, History, match popup** (+ `league-builder.css`, `league-clash.css`, `league-history.css`). Side A/B colours, gold for MVP, Discord result image palette. (3 CSS files written 2026-10-26, not built; see C4 in section 9. `league-result-image.css` and the font decisions stay open.)
- [x] **C5 Settings pages, result image, alias cleanup (written 2026-10-26, not built; see section 9).** The Badges page itself is styled when B3 builds it (plain markup on `league.css` classes).
- [ ] **C6 Verification.** Every look (Steel, Brutal, Forest), widths 360/768/1024/1280, keyboard, reduced motion, JS off, every hook the scripts use still exists. *You run* the click-through.

### Phase D: launch

- [x] **D1 Demo seed (owner ran it 2026-10-26: "seeder worked"; `dotnet test` 180 of 180 passed, 12 seeder tests included; see section 9)** (fake players, matches, a clash) behind `Demo:Seed=true`.
- [ ] **D2 README (written 2026-10-27, not run; six screenshots still to take; see section 9)** with screenshots/GIF, Riot disclaimer, run instructions, and a "not accepting contributions, fork it for your own version" line.
- [ ] **D3 Docker (written 2026-10-27, not built or run; see section 9)** (`docker compose up`, one data folder on a volume) so people can run it themselves. **No hosted demo** (owner decision, 2026-10-27: the project is for people to use in their own projects; there is no Render or other live site).
- [ ] **D4 Fresh git repo** (no HFECloud history), review leftover HFECloud wording and the champion list, push, tag `v0.1.0`.
- [ ] **D5 Interactive demo with a guided tour (redesigned 2026-10-26: the first text-only tutorial page was NOT what the owner wanted; see section 9, "D5 redo").** One self-contained `index.html`, static, no database, hosted on Vercel (owner decision). Explains the project, the Team Builder and Clash, and carries the four links.
- [ ] **D6 Optional (was D5):** Razor Class Library so HFECloud can consume it.

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
- Pages only reachable from the HFECloud sidebar had no equivalent. A4 adds a plain `/Notifications` page; the League tabs are the navigation for the rest.
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

**Builds (owner):** first build 107 errors; second, after the `ApplicationDbContext` namespace fix, 77. None of the 77 point at an A3 file; they are exactly the seams A4 provides. A3 is committed and pushed (`bb9f867 \"A3: database, identity, roles, admin users\"`, 14 files, on top of the A2 push `af99de4`). **A3 is ticked only after A6**, because login, roles and the admin seed cannot run until A4 and the migration exist. New warning found: `LeagueMatchController.Empty()` hid `ControllerBase.Empty` (new in .NET 10); fixed in A4 by renaming it.

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

### A4 Seams (2026-10-26): WRITTEN, NOT BUILT

**Done (written in the authoring environment, from the owner's current project zip; nothing compiled)**
- **Role table** (`Services/RolePermissions.cs`): one line per League action with the lowest role that may use it; a higher role may do everything a lower one may. `RolePermissionChecker` answers `IPermissionChecker` for the views (same `CanAccessAsync(User, controller, action)`, so no view or controller changed). `RolePermissionFilter` is a global filter that enforces the same table on every League controller: not signed in goes to login, a normal page gets the access-denied page, a script call (Accept or body is JSON) gets a plain 403. **Fail closed:** an action that is not in the table is denied for everybody, Admin included, and a start-up check logs a warning for every League action missing from it.
- **The 6 `GroupPermissions` / `PermissionGroupId` spots rewritten** (5 in `LeagueGuestLinkService`, 1 in `LeagueClashScheduleService`) to ask the role table: `UserIdsAllowedTo(controller, action)` returns the ids of users whose role may use the action. `LeagueClashScheduleService.GetRecipientUserIdsAsync` now also skips deactivated accounts (the old query did not; an account that cannot sign in should not get a notification).
- **`ILeagueNotifier` + `UserNotifications` table** (replaces `NotificationService`; same `CreateForUserAsync(userId, message, link)`), and a plain `/Notifications` page (your own list, newest first, "Mark all as read"). Not in the plan: without a page the notifications would be written and never seen.
- **`IActivityLog` + `ActivityLogEntries` table** (replaces `AdminActivityLogger`). `Log` only adds the line; the caller's next `SaveChangesAsync` writes it. All 6 call sites already save right after, in the same transaction. There is **no page that shows the trail yet**.
- **Local-disk `FileUploadService`** (same method names as HFECloud's: `ValidatePublicFile`, `SaveValidatedAsync`, `SaveAsync`, `DeleteIfExists`, `DeleteReplacedUpload`), trimmed `MediaUploadLimits`, and `DiscordWebhook.IsValidUrl` (replaces `DiscordNotifier.IsValidWebhookUrl`, one line changed in `LeagueDiscordSender`). Files get a random name; deletes only touch `wwwroot/uploads/...` and refuse paths that climb out.
- **`Views/_ViewImports.cshtml`** (`@using LeagueTeamBuilder`, `.Models`, `.Services`, MVC tag helpers) and `_ViewStart.cshtml`: this should clear the ~30 view-model errors. **Temporary `_Layout`** with the `Styles` and `Scripts` sections, a plain top bar, and `wwwroot/css/temp-theme.css`, which defines the `--hv-*` tokens so the old stylesheets show colours until C1 (delete both then). `Areas/Identity/Pages/_ViewStart.cshtml` points the Identity login and register pages at that layout.
- **`Program.cs`:** filter, the three seams, 15 League services (scoped; `TeamBalancer` and `ClashTeamBuilder` scoped too, because I could not confirm they hold no state), `AddHttpClient<LeagueDiscordSender>()`, the start-up check.
- **`LeagueMatchController.Empty()` renamed `EmptyRequest()`** (9 places), which removes the .NET 10 warning.

**Not done / could not be run**
- Nothing compiled or run (no .NET SDK here). Things I could not confirm without a compiler: the exact shape of HFECloud's `FileUploadService` (written from how the League code calls it; the middle value of the `SaveValidatedAsync` result is unused there), that the Identity UI package really looks for its layout through `Areas/Identity/Pages/_ViewStart.cshtml` (A8 will show: the login page renders with the top bar or it does not), and that EF translates the role subquery (it is the same shape as the old group subquery).
- `HFECloud.Tests` is not here, so nothing constructs these services yet; A7 will need `ILeagueNotifier` and `IActivityLog` fakes.
- Uploads still go to `wwwroot/uploads/...` (the League code passes `WebRootPath`). Section 8 rule 2 (paths from config) is left for Phase E.
- No admin screen for the activity trail, and no e-mail or Discord for notifications.

**Role defaults I chose (owner decision needed, one table to change)**
- **Member:** League overview, leaderboard, player pages, history, match detail, own profile; Clash page, respond to a clash.
- **Officer** (everything Member has, plus): team builder, save/start/finish/edit/delete matches, set MVP, import stats, run the Clash board, export and send result pictures, guests (add, edit, delete, names, link), player rank and names, player tier, schedule/edit/cancel a clash.
- **Admin** (everything above, plus): champion catalog, modes and maps, tier list, Discord auto-send, the guest link log, unlink and undo guest links, "Recompute MVPs".

**You run (after extracting `LeagueTeamBuilder-A4.zip` into the repo root so `src\` merges)**
1. Add `wwwroot/uploads/` to `.gitignore` (uploaded pictures must not be committed).
2. `dotnet build`. Expect the `IPermissionChecker`, `NotificationService`, `AdminActivityLogger` and view-model errors to be gone. Paste whatever is left; that is A5.
3. Do not run the app yet (no migration until A6).

**Changed / new files** (send-only-the-edited-files zip)
New: `Services/RolePermissions.cs`, `Services/RolePermissionFilter.cs`, `Services/LeagueNotifier.cs`, `Services/ActivityLog.cs`, `Services/FileUploadService.cs`, `Services/MediaUploadLimits.cs`, `Services/DiscordWebhook.cs`, `Models/UserNotification.cs`, `Controllers/NotificationsController.cs`, `Views/Notifications/Index.cshtml`, `Views/_ViewImports.cshtml`, `Views/_ViewStart.cshtml`, `Views/Shared/_Layout.cshtml`, `Areas/Identity/Pages/_ViewStart.cshtml`, `wwwroot/css/temp-theme.css`.
Changed: `Program.cs`, `Data/ApplicationDbContext.cs` (two tables), `Services/LeagueGuestLinkService.cs`, `Services/LeagueClashScheduleService.cs`, `Services/LeagueProfileService.cs`, `Services/LeagueDiscordSender.cs`, `Services/LeaguePermissions.cs` (comment), `Controllers/LeagueMatchController.cs`, `Controllers/LeagueController.cs` (comment), this plan file.

### C1 Tokens, looks and shell (2026-10-26): WRITTEN, NOT BUILT

**Owner's state at the start of C1 (reported, not seen by me):** the project builds with 0 errors, runs, and login works. The A3 to A8 boxes above are left as they were until you tick them.

**Done (written in the authoring environment, from the owner's project zip; nothing compiled or run)**
- **Tokens and looks.** `leaguehub-theme.css`: the real `--lh-*` tokens for Steel (on `:root`) plus the temporary `--hv-*` alias block (`acc` to `--lh-a`, `bg`, `bg2`, `ink`, `fg`, `mut`, `line`, `card` to `--lh-tile`, `glow`, `body`, `disp`, `r`, `logo` = a transparent picture so the old watermark stays hidden). `leaguehub-looks.css`: one `[data-look="brutal"]` block and one `[data-look="forest"]` block, copied from the prototype with the names changed. Steel is the default, as decided.
- **Look picker.** `Services/LeagueLooks.cs`: the ONE array `LeagueLooks.All` (key, name, fonts file) and `LookResolver`: cookie `lh-look`, else the signed-in user's `ApplicationUser.Look`, else `Theme:DefaultLook`, else the first listed look. A value not in the array counts as missing. `Controllers/LookController.cs` (`POST /Look/Set`, `[AllowAnonymous]`, antiforgery token, validates against the array, sets the cookie for 1 year with SameSite Lax and not HttpOnly, saves `ApplicationUser.Look` when signed in with one `ExecuteUpdate`, redirects only to a local URL via `Url.IsLocalUrl`, else to `/`). Its name does not start with "League", so the role table and its start-up check do not apply. `Program.cs`: one line registers `LookResolver`. `appsettings.json`: `Theme:DefaultLook` = `steel`.
- **Layout.** `Views/Shared/_Layout.cshtml` replaces the temporary one: `<html data-look>` written on the server (no flash), brand mark, tabs (League, Notifications with an unread count, Users for Admins; underline gradient Side A to Side B, as in the prototype), the Look dropdown, sign in / register / sign out, skip link, footer line "Not endorsed by or affiliated with Riot Games.". `@section Styles` (in `<head>`, after the shell CSS) and `@section Scripts` (end of `<body>`) are kept. `home-v2.css` is not referenced anywhere (it was never in this project; only `temp-theme.css` stood in for it).
- **Shell CSS.** `leaguehub-shell.css` (new file, not named in the plan): shell, base elements (zero specificity via `:where`, so any class rule in the League CSS wins), and shims for the Bootstrap classes the Identity pages use: `row`, `col-md-4`, `col-md-6`, `form-floating` (pure CSS floating labels, so it works with JavaScript off), `form-control`, `form-label`, `form-check-input`, `checkbox`, `mb-3`, `w-100`, `btn`, `btn-lg`, `btn-primary`, `btn-link`, `text-danger`, `text-success`, `text-muted`, `alert*`, `btn-close` (hidden: it needs Bootstrap's script), and the ASP.NET validation classes. The Identity form column is drawn as a tile (`.col-md-4 > section`). No hard-coded colour in this file; focus ring on every link, button, input, select and textarea; `prefers-reduced-motion` switches animations and transitions off site-wide; the top bar wraps and the tabs scroll sideways below 760px.
- **Picker script.** `wwwroot/js/lh-shell.js`: choosing a look applies it at once (`data-look` and the look's fonts link) and saves it in the background, so the team builder or any open form is not reloaded. If the save fails, the form is posted normally. With JavaScript off, the dropdown has an "Apply" button that posts the form and redirects back.
- **Fonts: what I chose.** One small stylesheet per look (`wwwroot/lib/fonts/steel.css`, `brutal.css`, `forest.css`, `font-display: swap`); the server links only the active look's file, and `lh-shell.js` swaps the link when the user changes look. Weights as in the prototype: Chakra Petch 600/700, Barlow 400/500/600, Archivo Black 400, Outfit 400/500/800; Latin subset only (Arabic or other scripts fall back to the system font). The `.woff2` files are NOT in the package (see below).

**Not done / could not be run**
- **No .NET SDK and no network here.** Nothing was built, run, or opened in a browser. I wrote Razor, C# and CSS without a compiler; anything below marked "unconfirmed" is exactly that.
- **No font files, no `html2canvas`.** Per your rule I did not invent them and I left the CDN tags in place: `html2canvas` (jsdelivr) in `League/Builder` line 73, `League/History` line 225, `League/MatchDetail` line 45, `LeagueClash/Index` line 161. The font `.css` files already point at `files/<name>.woff2`; until you add the files, the fallback fonts show (and the browser console shows 404s for them).
- **Not swapped, found while checking:** `League/History`, `League/MatchDetail` and `LeagueClash/Index` also load **Inter and Poppins from Google Fonts** (for the result-image dialog and the canvas). You did not list those two; they break "no CDN at runtime" and the offline desktop edition. I did not touch them; decision for you in C4 (add `Inter` 500/600/700/800 and `Poppins` 800/900 to the download list, or drop them).
- Bootstrap Icons: nothing in the views or scripts uses them (grep for `bi bi-` and `class="bi `), so nothing to host.
- `temp-theme.css` is not deleted by the zip (a zip cannot delete). Delete it yourself, command below. Until you do, nothing links it (the new layout does not), so it is dead weight only.
- The old League stylesheets are untouched (C2 to C5). Until C2, expect: the old glow and grid layer is still drawn (now in the look's colours), `--lg-k` still computes radii as 1 (so Brutal's square corners do not reach the old components), Anton does not load (it never did in this project), and in Brutal the old `--hv-line` hairlines are the bright off-white `--lh-line`. Pages whose headings have no class (Notifications, Admin users, some League partials) now get the display font from the base rules.
- No view was edited, so the hook rule holds by construction. I still generated the audit list (`docs/C1-script-hooks.txt`: every id, class, `data-*` attribute and every `querySelector`/`closest`/`matches` string in the 14 scripts, and whether each is found in a view). 44 names are not found in any view because the scripts build them (state classes `is-*`, dialog markup `data-lgx-*` / `data-lri-*`, pins and options). That is information, not a failure.

**Decisions on the open points (say if you disagree)**
- **Rank badge placeholder colours:** the prototype tier colours, with one change: **Iron `#7b8394` becomes `#8a93a5`**. Measured contrast of the 10 colours against the three tile backgrounds (Steel `#171a1f`, Brutal `#1a1a1c`, Forest tile `#152c20`): the original Iron is 3.9:1 on Forest, below 4.5:1 for the digit; `#8a93a5` is 5.6 / 5.6 / 4.8. All other colours are 4.6 or higher everywhere. Applied when the placeholders are built (B2 / C3); no badge art exists in C1.
- **Soft divider:** `--lh-rule` is used for table row lines, list dividers, the footer rule and (from C2) dialog dividers and progress tracks; `--lh-line` is used for outer borders of tiles, inputs, buttons and the top bar. The shell and the base table rule already follow this split. In Steel and Forest the two are the same colour; only Brutal differs.
- **Cookie versus saved look:** when both exist the cookie wins (it is the choice made on this device); the saved value is used only when the cookie is missing or not in the allowed list. The cookie is written only when the user picks, so a user who signs in on a new device gets their saved look (one small query per page while there is no cookie) until they pick again.

**Found while checking (for C4, not changed)**
- `league-export.js` (the map picture) reads `getComputedStyle(document.body).fontFamily` for its canvas text. In the temporary layout that was the system font; with the new shell it is the look's body font, so that picture's font now follows the look. Its colours are fixed, so only the font changes. The result-image script is separate. If you want the map picture fixed too, that is a small JS change in C4.

**You run (from the repo root, after extracting the zip so `src\` merges)**
1. `git rm src/LeagueTeamBuilder/wwwroot/css/temp-theme.css`
2. Download the files (PowerShell; the URLs are the Fontsource pattern on jsDelivr, written from memory and **not checked**; if one fails, take the same file from the `@fontsource/<name>` npm package):
   ```
   cd src\LeagueTeamBuilder
   New-Item -ItemType Directory -Force wwwroot\lib\fonts\files, wwwroot\lib\html2canvas | Out-Null
   $fs = "https://cdn.jsdelivr.net/fontsource/fonts"
   foreach ($f in @(@("chakra-petch",600),@("chakra-petch",700),@("barlow",400),@("barlow",500),@("barlow",600),@("archivo-black",400),@("outfit",400),@("outfit",500),@("outfit",800))) {
     Invoke-WebRequest "$fs/$($f[0])@latest/latin-$($f[1])-normal.woff2" -OutFile "wwwroot\lib\fonts\files\$($f[0])-latin-$($f[1])-normal.woff2" }
   Invoke-WebRequest "https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js" -OutFile wwwroot\lib\html2canvas\html2canvas.min.js
   ```
   That is 9 font files (names exactly `chakra-petch-latin-600-normal.woff2`, `chakra-petch-latin-700-normal.woff2`, `barlow-latin-400/500/600-normal.woff2`, `archivo-black-latin-400-normal.woff2`, `outfit-latin-400/500/800-normal.woff2`) in `wwwroot/lib/fonts/files/`, and `html2canvas.min.js` in `wwwroot/lib/html2canvas/`. Check each is more than a few KB (an error page saved as .woff2 would be tiny). Keep the font licences (SIL OFL) with the files if you publish.
3. When `html2canvas.min.js` is in place, change the 4 tags by hand (each is one line): `<script src="https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js"></script>` becomes `<script src="~/lib/html2canvas/html2canvas.min.js"></script>` in `Views/League/Builder.cshtml`, `Views/League/History.cshtml`, `Views/League/MatchDetail.cshtml`, `Views/LeagueClash/Index.cshtml`. Do not do this before the file exists.
4. `dotnet build`. Paste any error (the likeliest places: `LookResolver` / `LookController`, since I could not compile them).
5. `dotnet run`, then check: `/Identity/Account/Login` and `/Identity/Account/Register` (labels float inside the inputs, button is light on dark, errors in the Side B colour); `/Identity/Account/AccessDenied` (sign in as a Member and open `/League/Builder`); the Look dropdown on a public page and on a League page, in all three looks; reload and confirm the look stays; sign in, pick Forest, open the site in a private window on the same machine, sign in, and confirm Forest (saved look). Dev tools, Application, Cookies: `lh-look` exists for one year, SameSite Lax.
6. Turn JavaScript off (dev tools) on the login page: the dropdown shows an Apply button and it works; the floating labels still work.
7. Tab through the top bar: every link, the dropdown and Sign out show a visible ring; "Skip to content" appears on the first Tab. Narrow the window to 360px: the tabs scroll sideways and nothing overflows the page.
8. Set `Theme:DefaultLook` to `forest` in `appsettings.json` and confirm a visitor with no cookie gets Forest; set it to `nonsense` and confirm Steel.

**Changed / new files** (zip under `src/LeagueTeamBuilder/`)
New: `wwwroot/css/leaguehub-theme.css`, `wwwroot/css/leaguehub-looks.css`, `wwwroot/css/leaguehub-shell.css`, `wwwroot/lib/fonts/steel.css`, `wwwroot/lib/fonts/brutal.css`, `wwwroot/lib/fonts/forest.css`, `wwwroot/js/lh-shell.js`, `Services/LeagueLooks.cs`, `Controllers/LookController.cs`, `docs/C1-script-hooks.txt`.
Changed: `Views/Shared/_Layout.cshtml` (rewritten), `Program.cs` (one registration line), `appsettings.json` (`Theme` section), `EXTRACTION-PLAN.md` (C1 line in section 5, this record).
To delete by you: `wwwroot/css/temp-theme.css`.

**Choices to confirm**
- `leaguehub-shell.css` and the per-look fonts files are new files beyond the plan's list (a base layer and a per-look font link had to live somewhere).
- The Look dropdown sits in the top bar on every page, including login, as the plan says ("public pages and the login page too").
- The alias `--hv-line` maps to `--lh-line` as you specified, so old League pages show bright hairlines in Brutal until C2. If that is too loud for testing, say so and I will point the alias at `--lh-rule` for the interim.

**Unconfirmed (no compiler)**
- That `Razor` accepts `selected="@(l.Key == look.Key)"` as a boolean attribute (it should: true writes `selected="selected"`, false writes nothing), that `<a href="~/...">` is resolved in the layout for both MVC and Razor Pages (it should, by the URL tag helper), and that the Identity UI package renders its pages through `Areas/Identity/Pages/_ViewStart.cshtml` (A4 note; A8 would have shown it).
- That `Select(u => u.Look)` and `ExecuteUpdateAsync` on `ApplicationUser.Look` translate on SQLite (both are plain column operations).
- The prototype's `color-mix()` and `:has()` need a current browser (Chrome 111+, Safari 16.2+, Firefox 113+); the old League CSS already needed them.

### C1 follow-up: Notifications tab and POST /Look/Set 404 (2026-10-26): WRITTEN, NOT BUILT

**Found, from your project zip (read, not run)**
- **Notifications tab.** `Views/Shared/_Layout.cshtml` line 49 had `Notifications@if (unread > 0) {...}` (text glued to `@`, so Razor read an email address). The word is now `<span>Notifications</span>` followed by the `@if`. I searched the whole layout and every other `.cshtml` for a letter or digit directly followed by `@if`, `@foreach`, `@for`, `@while`, `@switch`, `@using` or `@{`: that line was the only hit.
- **404 on /Look/Set. Cause confirmed: `Controllers/LookController.cs` was not in the project.** The zip has 7 controllers (AdminUsers, League, LeagueMatch, LeagueSettings, LeagueExport, LeagueClash, Notifications) and no `LookController`, although the C1 record lists it as a new file. It was lost in the merge. With no controller there is no endpoint, so the default route has nothing to match and the server answers 404.
- Checked and fine, so not the cause: (c) `Program.cs` maps `{controller=League}/{action=Index}/{id?}`; there is no `UsePathBase` and no route attribute. `RolePermissionFilter` and `RolePermissions.FindUnlistedActions` act only on controllers whose name starts with "League" (`LeaguePermissions.IsLeagueController`), and `Look` does not. (d) `builder.Services.AddScoped<LookResolver>();` is present (`Program.cs` line 52). `LeagueLooks.cs`, `ApplicationUser.Look` and the `Look` column in the migration are present.

**Fix.** New `Controllers/LookController.cs`: `[AllowAnonymous]`, `POST Set(string? look, string? returnUrl)` with `[ValidateAntiForgeryToken]`. A look not in `LeagueLooks.All` gives 400 and nothing is saved. Otherwise it sets the cookie `lh-look` (1 year, SameSite Lax, not HttpOnly, Secure only on https), saves `ApplicationUser.Look` for a signed-in user with one `ExecuteUpdateAsync`, answers 204 when the header `X-LH-Ajax` is present, else 302 to `returnUrl` if `Url.IsLocalUrl`, else to `/`.

**Not run.** No .NET SDK here; nothing compiled. Please build and run.

**You run**
1. Add `Controllers/LookController.cs`, replace `Views/Shared/_Layout.cshtml`. Stop the app fully, `dotnet build`, `dotnet run` (hot reload does not add controllers).
2. Signed in with an unread notification: the tab shows "Notifications" and a count badge, no code text.
3. Dev tools, Network, pick a look: `POST /Look/Set` returns 204; Application, Cookies: `lh-look` for 1 year, SameSite Lax. Reload: the look stays.
4. Signed in, pick Forest, delete the cookie, reload: Forest (from `ApplicationUser.Look`). Check the `Look` column of `AspNetUsers` if you like.
5. JavaScript off, press Apply: 302 back to the same page, look changed.
6. Tamper test (console on any page): `fetch('/Look/Set',{method:'POST',body:new URLSearchParams({look:'nonsense'})})` gives 400 (without the antiforgery token it is 400 as well, so for a real check edit the select's option value in Elements and submit with JS off: 400, no cookie change). A form with `returnUrl=https://example.com` goes to `/`.

**Changed / new files:** New `Controllers/LookController.cs`. Changed `Views/Shared/_Layout.cshtml`, `EXTRACTION-PLAN.md`. C1 stays unticked until you confirm.

**Still open from the C1 list (not touched in this reply):** the 9 font files and `html2canvas.min.js` with the 4 script-tag changes, and your three decisions (Inter/Poppins, `--hv-line`, `league-export.js` font). C2 waits for "next step".

### C2 Components in league.css (2026-10-26): WRITTEN, NOT BUILT

**Done (one file changed: `wwwroot/css/league.css`; no view, script, controller or other CSS file touched)**
- Rewritten on the `--lh-*` tokens only. Tile = `--lh-tile` + `--lh-tb` + `--lh-tbl` (left edge) + `--lh-r` + `--lh-tsh`, used by `.lg-card`, `.lg-tile`, banners. Inputs, buttons, nav bar and dropdown lists use `--lh-tb` / `--lh-line` (borders); table lines, lane and map-card borders, slider tracks and the status pill use `--lh-rule` (dividers). Primary button = Side A fill; ghost = outlined; danger hover = Side B. Nav pill for the current page = Side A fill. Map pins: team 1 Side A, team 2 Side B (was ink). Badges: MVP is the only gold; rank badge is neutral; Clash badge is Side A.
- Removed: the glow/grid layer (`body:has(.lg-page)::before/::after`, `lg-drift`), the `body[data-home-theme="glitch"]` rules (4), `backdrop-filter`, Anton (the `.lg-h1` and `.lg-h2` fonts now use `--lh-dw` and `--lh-disp`), all forced uppercase (small labels are 12px with a little spacing instead), every hard-coded colour in this file (13 hex values and 11 `rgba()` values; shadows use `color-mix(... black ...)`), every `--hv-*` name, and `--lg-k` from this file's own rules (radii are `--lh-r` / `--lh-rs`; avatars, pins and slider thumbs use `min(50%, calc(var(--lh-r) * 4))`: round in Forest, square in Brutal).
- Focus rings: 2px `--lh-ink`, offset 2px, on links, buttons, inputs, selects, rank button, champion options, tiles, lane chips, map pins and range sliders. Reduced motion: transitions and the tile lift are switched off.
- Section 1b of the file is a TEMPORARY bridge, because 5 other stylesheets (builder, clash, history, overview, stats) still read these names from `.lg-page`: `--lg-danger` (= Side B), `--lg-ok`, `--lg-warn` (= Side B for now; gold is reserved for MVP), `--lg-k` (.15 in Steel, 0 in Brutal, .7 in Forest, so old components get roughly the right corners in each look), `--hfe-accent`, `--hfe-accent-soft`, `--hfe-font-display`. `--lg-acc-t` (small accent text) stays as a derived value. Delete 1b when the last of those files is rewritten (C4/C5).
- Checked by script: all 106 distinct `lg-*` class names of the old file are still in the new one, none added (so no hook can have gone); `data-lg-peak-wrap` and braces balance; no `#hex`, `rgba(`, `hv-`, `glitch`, `Anton`, `uppercase`, `backdrop` or `data-home-theme` left in the file. The 148 selector names the 14 scripts use were re-scanned: 118 are in the views, 30 are built by the scripts (state classes, dialog markup `data-lgx-*` / `data-lri-*`, pins, options), as in C1.

**Not done / not run**
- Nothing compiled or viewed in a browser (no .NET SDK, no browser here). Contrast, spacing and the three looks are unchecked by eye.
- The other 5 League stylesheets (and `league-result-image.css`, 99 hex values) still hold their hard-coded colours and `--hv-*` names: C3 and C4 by design. The prototype (`docs/league-theme-prototype.html`) and `docs/C1-script-hooks.txt` were not in the zip I received, so I worked from the token files and the plan; compare with the prototype when you look.
- No dialog rules live in `league.css` (the three `<dialog>` elements are styled in league-builder/history/clash CSS): they are done in C4.

**You run**
1. Copy `wwwroot/css/league.css` over the old one. No restart needed; hard-refresh (Ctrl+F5) because of the CSS cache.
2. Open `/League`, `/League/Profile`, `/LeagueSettings` (and Champions, Guests, Tiers, Map spots) in each look (Steel, Brutal, Forest). Look for: tiles with the look's edge and shadow, readable text on buttons, visible focus ring when you Tab, no leftover glow or grid layer, no uppercase headings, the sliders on a guest's lane levels, the rank dropdown and the champion list opening above the next tile.
3. Narrow to 360px: nothing sideways except the tables (they scroll inside their box).
4. Dev tools, Rendering, "Emulate prefers-reduced-motion": no tile lift.
5. Tell me what looks wrong, by page and look.

**Choices to confirm**
- Danger and error text use the Side B colour (as the shell already does), so on a page with a red/orange team an error looks like the team colour. If you want a separate error colour that is not a token, say so before C3.
- Rank badge is neutral grey-ish for now (gold only for MVP); the tier colours come with the badges (B2 / C3).
- Labels lost their capitals; if Brutal should keep capitals on small labels, that is one `text-transform` in one look block, but a look is tokens only, so it would need a new token. Say if you want it.

### C3 Overview, Leaderboard, Player, Profile (2026-10-26): WRITTEN, NOT BUILT; scoreboard NOT yet on the page

**Done (3 CSS files; no view, script, controller, service or model touched)**
- `league-overview.css` (covers the Overview, Leaderboard and Player page) and `league-stats.css` (My league stats) rewritten on the `--lh-*` tokens. Gone: every `--hv-*` name, every `--lg-k`, all hex and `rgba()` values, forced uppercase, the logo watermarks (`.lg-pod::before`, `.lg-cteam-wrap::before`, `.lg-hero::before`) and the podium glow. Kept: the per-person `--h` hue gradients behind an initial (data, not a look), the pyramid shapes, every class name.
- Decisions applied: MVP counts are gold (MVP place numbers on the Overview, the MVPs column on the Leaderboard); the winning Clash chip uses `--lh-ok`; the "L" in a player's recent form and errors use Side B; rows and stat tiles use `--lh-rule` borders, tiles and the podium cards use the look's tile (border, shadow); top-3 and the active filter use Side A.
- **Balance scoreboard, component only.** New classes `lg-board-row`, `-side`, `-a`, `-b`, `-n`, `-l`, `-gap`, `-gn`, `-gl`, `-meter`, `-foot` in `league-overview.css`: Side A big number on the left, Side B on the right, the gap in the middle, and a meter split by `--a` (a percentage the page writes inline, with `role="img"` and a text label, so it works with JavaScript off). At 560px and below the gap drops under the two numbers. Expected markup: `<section class="lg-card lg-c12 lg-board"> ... <div class="lg-board-row"><div class="lg-board-side lg-board-a"><span class="lg-board-n">41</span><span class="lg-board-l">Side A wins</span></div><div class="lg-board-gap"><span class="lg-board-gn">2</span><span class="lg-board-gl">game gap</span></div><div class="lg-board-side lg-board-b">...</div></div><div class="lg-board-meter" role="img" aria-label="Side A 51%, Side B 49%" style="--a:51.25%"><i></i></div><div class="lg-board-foot"><span>51% Side A</span><span>49% Side B</span></div></section>`.
- **Why it is not on the page:** the Overview model has no per-side numbers (`LeagueOverviewServerStats` has game counts only). Rule 2 forbids editing services and models, so I did not. The data exists: `LeagueOverviewService` already holds each finished match's `WinnerTeamIndex` (index 0 = Team 1 = Side A, 1 = Team 2 = Side B, as in the pins and the result image). Proposal, awaiting the owner: add two read-only counts to `LeagueOverviewServerStats` (`SideAWins`, `SideBWins`: finished custom games with two teams, winner index 0 / 1), count them in `LeagueOverviewService` next to the existing counts, and add the scoreboard card to `Views/League/Index.cshtml` above "Top ranked players", hidden when both are 0. No existing query, number or permission changes.

**Checked here (static, not the real app)**
- By script: all 88 classes of the old overview file and all 19 of the old stats file are still present; only the 11 `lg-board-*` classes are new; braces balance; no `#hex`, `rgba(`, `hv-`, `lg-k`, `uppercase`, `glitch` left in either file. Views and scripts were not edited, so every id, class and `data-*` hook the scripts use is still in the views (the 30 script-built names are unchanged).
- By eye, in headless Chromium (static page with the real CSS and hand-copied markup, sample data, no web fonts so fallback fonts were used): Overview, podium, MVPs, Clash team, stats, leaderboard row, player hero, my-stats tiles, form controls and tables in Steel, Brutal and Forest at 1280px and 360px: no sideways scroll, scoreboard readable, gold only on MVP things. This is NOT a test of the .NET pages (no .NET SDK here).

**Not done / unconfirmed**
- Scoreboard on the page (see above). The page is unchanged: no scoreboard card yet.
- Rank images (`wwwroot/img/ranks`) untouched, so the old Riot art still shows until Phase B.
- The Profile page has no stylesheet of its own: its look comes from `league.css` (C2) and `league-stats.css`. Its dropdowns and sliders were checked in C2 only.
- Forest: Side A green and `--lh-ok` green are close, so a "W" chip and a Side A element can look alike. Not changed.

**You run**
1. Copy the three CSS files; hard-refresh (Ctrl+F5).
2. `/League`, `/League/Leaderboard` (with search, a rank chip, each sort), a player page, `/League/Profile`, and the History page (it also loads these two files) in Steel, Brutal and Forest: podium order (2, 1, 3), no watermark, MVP counts gold, hover and focus ring on rows and cards, 360px width with no sideways scroll, Leaderboard rows readable on a phone.
3. Tab through the Leaderboard filters (the radio chips show a ring), and turn JavaScript off: the filter form still submits.
4. Answer the scoreboard question below.

**Changed / new files:** `wwwroot/css/league-overview.css`, `wwwroot/css/league-stats.css`, `wwwroot/css/league.css` (a comment only), `EXTRACTION-PLAN.md`.

### C3b Scoreboard on the Overview (2026-10-26): WRITTEN, NOT BUILT
Owner said "do whatever you think is better", so the proposal above was applied.
- `Models/LeagueOverviewModels.cs`: `LeagueOverviewServerStats.SideAWins`, `SideBWins`.
- `Services/LeagueOverviewStats.cs`: new pure rule `SideWins(done)` and record `OvSideWins`. Counts finished CUSTOM games with `ModeKey` "normal" or "aram" only (two sides); winner index 0 = Side A, 1 = Side B; no winner counts for neither. Arena (many teams) and Clash (our team vs an opponent) are left out on purpose, because the winner index means something else there.
- `Services/LeagueOverviewService.cs`: two lines in `GetAsync` after the game counts. No query added, no existing number or permission changed.
- `Views/League/Index.cshtml`: "Side balance" card above "Top ranked players", hidden when no game has a winner. Meter width is written inline as `--a` (invariant culture, so a comma locale cannot break it); whole percents add to 100.
- Not run: no .NET SDK here. *You run:* `dotnet build`, open `/League` with at least one finished draft or ARAM game, and check the card in the three looks at 1280px and 360px. Add a unit test for `SideWins` when `LeagueTeamBuilder.Tests` exists.
- Changed: `Models/LeagueOverviewModels.cs`, `Services/LeagueOverviewStats.cs`, `Services/LeagueOverviewService.cs`, `Views/League/Index.cshtml`, `EXTRACTION-PLAN.md`.

### C4 Builder, Clash, History, match popup (2026-10-26): WRITTEN, NOT BUILT
**Done (3 CSS files + a trim of `league.css` section 1b; no view, script, controller, service or model touched)**
- `league-builder.css` (Builder, Clash page slots, the shared `lgb-dlg` dialog), `league-clash.css` (schedule, member response page, Clash page) and `league-history.css` (History list, match popup, Edit result, stats form, champion picker, "Fill from screenshots") rewritten on the `--lh-*` tokens only. Gone: every `--hv-*`, `--lg-k`, `--lg-ok/-warn/-danger` and `--hfe-accent-soft` name, all hex and `rgba()` values, forced uppercase, `backdrop-filter` (the History dialog keeps none on purpose: its champion list is `position: fixed`), the glitch rule, "Anton" comments.
- Decisions applied: team 1 = Side A, team 2 = Side B (was ink) in the Builder panels, map pins and the match popup; tiles, dialogs and rows use `--lh-tb`, controls (chips, radio cards, options, toggles) use `--lh-line`, nested items and dividers use `--lh-rule`; focus rings are 2px `--lh-ink` (selected state keeps the Side A outline); dialogs use the `--lg-float` shadow (so Brutal's gold offset shadow is not used on dialogs); the MVP tag in the match popup is gold (only gold in these files); live / done / win use `--lh-ok`.
- **Warn and danger share Side B.** "Open", "submitted" and "later" badges, warn banners and the stat check use Side B, as do cancelled, loss, busy and errors. Gold is reserved for MVP, and Side A would look like "live" in Forest. They are still told apart by the label text and by shape (cancelled is dashed).
- Small labels lost capitals and wide spacing (12px, `.04em`). Radii: tiles `--lh-r`, small items and pills `--lh-rs`, avatars and toggles `--lg-round`.
- `league.css` section 1b now holds only `--hfe-accent` and `--hfe-font-display`, because `league-export.js` still reads them. Delete it in C5 when that script reads `--lh-*`.
- The `--hv-*` aliases in `leaguehub-theme.css` section 2 are now read by nothing (checked by search); C5 deletes them.

**Checked here (static, not the real app)**
- By script: every `lg*` class of the three old files is still there (62 / 52 / 114), none added, braces balance, no `--hv-`, `--lg-k`, `#hex`, `rgba(`, `uppercase`, `glitch` left. Nothing outside these CSS files reads `--lg-k`, `--lg-ok`, `--lg-warn` or `--lg-danger`.
- By eye, headless Chromium, real CSS with hand-copied markup (slots, chips, radio card, toggle, map pins, banner, pills, dialog, Clash row and choices, History row, match popup with edit / error / warn / delete-armed): Steel, Brutal and Forest at 1100px and 360px, no sideways scroll. Fallback fonts. NOT a test of the .NET pages.

**Not done / still open**
- `league-result-image.css` (99 hex values) and `league-export.js` (fixed map-picture colours, reads the body font): C5.
- Inter / Poppins from Google Fonts on History, MatchDetail and Clash (decision from C1 still open); font files and `html2canvas.min.js` still missing.
- Not seen in a browser: the champion list inside the History dialog, the Fill-from-screenshots step, drag and drop on the Builder, the Discord export dialog with its canvas.

**You run**
1. Copy the 4 CSS files, hard-refresh (Ctrl+F5).
2. `/League/Builder` (pick players, drag a slot, change mode, balance toggle, open the Clash and Finish dialogs, Export), `/LeagueClash`, the Clash schedule and a Clash response page, `/League/History` and a match popup (Edit result, Add stats, champion list, delete two-step) in Steel, Brutal and Forest.
3. Check Tab focus rings, hover on rows and chips, 360px width, and reduced motion (no lift, no pulse).
4. Tell me what looks wrong, by page and look.

**Changed:** `wwwroot/css/league-builder.css`, `league-clash.css`, `league-history.css`, `league.css` (section 1b and its header comment), `EXTRACTION-PLAN.md`.

### C5 Settings pages, result image, alias cleanup (2026-10-26): WRITTEN, NOT BUILT
**Done (CSS, 2 JS colour edits, no view, controller, service or model touched)**
- **Settings pages (13 views)** already use only `league.css` classes (C2). I checked every `lg*` class in them against the stylesheets and scripts: all defined except `lg-hint` (EditGuestNames), which had no rule; added one small rule to `league.css`.
- **Result picture (`league-result-image.css`, `league-result-image.js`).** The picture stays a FIXED palette on purpose (same for everyone, html2canvas cannot read tokens), but it moved from the HFE colours to the Steel look: page `#0e1013`, surfaces `#171a1f` / `#1f2329`, border `#2a2f38`, text `#f2f4f7` / `#8d95a3`, **team 1 = cyan `#19d3ff`, team 2 = orange `#ff7a1a`, gold `#ffd23f` for MVP only** (before, gold was both team 2 and the MVP star). The same colours are in the 4 places in `league-result-image.js` that draw the team swatches, the winner chip and the canvas background. Still no `color-mix()`, `box-shadow` or `backdrop-filter` in the picture, as html2canvas needs.
- **The dialogs around the pictures** (`.lri-dlg`, `.lgx-*`: Export matches on History, the preview dialog) now use `--lh-*` tokens and follow the look. They use no `--lg-*` name, because the preview dialog may be created outside `.lg-page`.
- **Map picture (`league-export.js`).** Reads the look's `--lh-disp` and `--lh-a` instead of the old `--hfe-*` names, and the first two team colours are Side A and Side B (cyan, orange). Its other colours stay fixed.
- **Deleted:** section 1b of `league.css` (the `--hfe-*` bridge) and section 2 of `leaguehub-theme.css` (the 12 `--hv-*` aliases). A search finds no `--hv-*`, `--hfe-*`, `--lg-k`, `--lg-ok`, `--lg-warn` or `--lg-danger` left in `wwwroot` or `Views`.

**Checked here (static)**
- Braces balance in the 3 CSS files, `node --check` passes for the 2 scripts, no `#hex` left in the dialog part of `league-result-image.css`. Not rendered: the picture needs the .NET page, `html2canvas.min.js` (missing) and the champion data.

**Not done / still open**
- `html2canvas.min.js`, the look font files, and the Inter / Poppins decision (the picture uses Inter and Poppins; the History, MatchDetail and Clash views still load them from Google Fonts).
- `wwwroot/img/league-export/bg-*.jpg` backgrounds are not in the repo (Riot-art rule); the picture falls back to the dark colour. Confirm in the click-through that html2canvas does not stop on the missing image.
- Download names and the Discord filename still start with `hfe-` (wording step A9).
- Badges page: B3.

**You run**
1. Copy the changed files, hard-refresh.
2. Open `/LeagueSettings` and each sub-page (Champions, Guests, Tiers, Modes, Auto-send, Clash schedule) in the three looks.
3. Make a result picture and a map picture from a finished match; check the colours (cyan / orange, gold star only on MVP) and that the preview dialog and "Export matches" follow the look.

**Changed:** `wwwroot/css/league.css`, `leaguehub-theme.css`, `league-result-image.css`, `wwwroot/js/league-result-image.js`, `league-export.js`, `EXTRACTION-PLAN.md`.

### Status report from the owner (2026-10-26)
Owner ran everything written up to and including C5 (A3, A4, A5 build, C1 to C5): the project builds and runs and does not crash. Those boxes are ticked on the owner's word. Not reported yet and left unticked: A6 migration check, A7 tests, A8 click-through, A9 wording, and the C1 to C5 "You run" visual checks (looks, 360px, keyboard, result pictures). Open items carried over: font files, `html2canvas.min.js`, the Inter / Poppins decision, `bg-*.jpg` backgrounds.

### A6 Migration check (2026-10-26): DONE
Owner uploaded `20261025233858_InitialCreate.cs`, its Designer and `ApplicationDbContextModelSnapshot.cs`. Checked: 25 tables = 7 Identity (Roles, Users, RoleClaims, UserClaims, UserLogins, UserRoles, UserTokens) + `DataProtectionKeys` + `ActivityLogEntries` + `UserNotifications` + the 15 League tables (AutoSendSettings, Champions, ClashEvents, ClashTeams, ClashResponses, CustomTiers, GuestLinkLogs, MapSpots, Matches, MatchPlayers, MatchTeams, ModeSettings, ProfileChamps, ProfileLanes, Profiles). SQLite annotations, EF 10.0.14, `AspNetUsers` has `IsDeactivated` and `Look`. Nothing extra, nothing missing. Owner opened every page on an empty database without errors. Pages are not yet checked with data: owner will load data on 2026-10-27 (D1 demo seed is the planned way).

### B1 Rank badge data and service (2026-10-27): DONE (owner: migration added, build succeeded)
**Done (no view, script, controller or existing service touched)**
- `Models/LeagueRankBadge.cs`: `Tier` (the `LeagueRank` enum, primary key, never generated) and nullable `ImageUrl`. One row per tier at most; no row or a null `ImageUrl` = the default picture. `ApplicationDbContext`: new `LeagueRankBadges` set, key on `Tier`, `ImageUrl` max 300.
- `Services/RankBadgeRules.cs` (pure, for the B4 tests): the bytes must decode (SkiaSharp), be really PNG, JPEG or WebP (a GIF or an SVG renamed to `.png` is refused), the format must match the file ending, and each side must be 16 to 2048 px.
- `Services/RankBadgeCatalog.cs` (**singleton**, in memory): `Url(rank)` = uploaded picture, else `/img/ranks/default/<tier>.svg`; `UploadedUrl`, `All()`, `ByKey()` (lower-case tier name to address, for the scripts' `cfg.icons` in B2) and `Replace(rows)` (swaps the whole dictionary, so a reader never sees a half-updated list).
- `Services/LeagueRankBadgeService.cs` (scoped): `GetAllAsync`, `SetAsync`, `RemoveAsync`, `ResetAllAsync`, `ReloadCatalogAsync`. Order in `SetAsync`: check type and size (`MediaUploadLimits.RankBadgeExtensions` = png/jpg/jpeg/webp, `RankBadgeMaxBytes` = 1 MB), check the bytes, save the file under `/uploads/rank-badges/<random>`, save the row, **delete the old file only after the row is saved**, refresh the catalog. If the row save fails the new file is deleted and the old badge stays. Remove and reset delete the row, then the file(s).
- `RolePermissions.cs`: `LeagueSettings/Badges`, `UploadBadge`, `RemoveBadge`, `ResetBadges` = Admin. They have no controller action yet (B3); the table entries are harmless until then.
- `Program.cs`: registers the catalog (singleton) and the service (scoped), and fills the catalog after the database starts. If that fails (for example the table does not exist yet) it logs a warning and the site starts anyway with default pictures.

**Not done / not run**
- Nothing compiled (no .NET SDK here). No page uses the catalog yet: the views still show the old `/img/ranks/*.png` URLs until B2, and `wwwroot/img/ranks/default/` does not exist until B2.
- No test project yet (A7); the rules in `RankBadgeRules` are written to be tested in B4.
- Known limit (as planned): the catalog is per process, so behind several instances a change shows after a restart.

**You run**
1. Copy the files (zip), then: `dotnet ef migrations add AddLeagueRankBadges` (the project migrates on start, so `dotnet run` applies it).
2. `dotnet build`, then start the site: it should start as before. In the log there should be NO "Could not load the rank badges" warning. If there is, the migration is missing.
3. Paste any compile errors.
4. Check the new migration creates only the `LeagueRankBadges` table (`Tier` integer key, `ImageUrl` text up to 300, nullable).

**Changed / new files:** new `Models/LeagueRankBadge.cs`, `Services/RankBadgeRules.cs`, `Services/RankBadgeCatalog.cs`, `Services/LeagueRankBadgeService.cs`; changed `Data/ApplicationDbContext.cs`, `Services/MediaUploadLimits.cs`, `Services/RolePermissions.cs`, `Program.cs`, `EXTRACTION-PLAN.md`.

### B2 Swap every hard-coded rank URL (2026-10-27): WRITTEN, NOT BUILT
**Done**
- `Views/_ViewImports.cshtml`: `@inject RankBadgeCatalog Badges`, so every view and partial can call `Badges.Url(rank)`.
- Views now use the catalog: `League/Index` (3 images), `League/Leaderboard` (2), `League/Player`, `League/_MatchDetail` (reads `Badges.ByKey()` once, falls back to the placeholder if a key is unknown), `Shared/_RankSelect` (`data-img`), `LeagueSettings/Guests`.
- `League/Builder` and `LeagueClash/Index`: `iconBase` replaced by `icons = Badges.ByKey()` (lower-case tier name to address).
- `league-builder.js` and `league-clash-board.js`: `rankIcon(key)` reads `cfg.icons[key]`; an unknown key draws an empty 18 px box (`span.lgb-ri` rule added in `league-builder.css`) instead of a broken request.
- `Services/LeagueViewText.cs`: `RankImage` removed (nothing uses it now). Comments that named the old `.png` files updated in `LeagueEnums.cs`, `LeagueBuilderModels.cs`, `LeagueHistoryModels.cs`.
- `wwwroot/img/ranks/default/`: 10 placeholder SVGs (`iron` to `challenger`): shield outline in the tier colour, the tier number (1 to 10) inside. Colours: iron `#8a93a5`, bronze `#c98a5b`, silver `#b4bfd0`, gold `#d9a63c`, platinum `#3fc1b0`, emerald `#34c47a`, diamond `#6aa8ff`, master `#b987ff`, grandmaster `#ff7b7b`, challenger `#4fd8ff`. Measured contrast against Steel / Brutal / Forest tile backgrounds: every colour is 4.8:1 or higher (iron 5.6 / 5.6 / 4.8, lowest bronze 5.1 on Forest).

**Decisions**
- The prototype file (`league-theme-prototype.html`) is not in the zip, so apart from Iron (from the plan) I chose the tier colours myself, keeping the plan's 4.6:1 rule. Change them in the SVG files if you prefer others.
- The Gold tier placeholder is amber (`#d9a63c`), a different shade from the MVP gold (`--lh-gold`), because gold is reserved for MVP in the UI; a custom Gold badge upload replaces it anyway.
- Addresses are written as stored (`/uploads/rank-badges/...` or `/img/ranks/default/...`), the same way avatars are. This assumes the site runs at the domain root.

**Not done / not run**
- Nothing compiled (no .NET SDK here). Checked by script: no `img/ranks/*.png` or `RankImage` left in Views, Services, Models or scripts; both scripts parse; the SVGs are well-formed XML.
- The old `wwwroot/img/ranks/*.png` (Riot art) are not in the zip I received, so there was nothing to delete here. If they exist in your working copy, delete the folder files (not `default/`): `Remove-Item wwwroot/img/ranks/*.png`.
- The placeholders were not looked at in a browser, and the pages were not checked with data.
- Existing match history rows are fine: `RankKey` is only the lower-case tier name, so no data change.

**You run**
1. Copy the files from the zip, delete any old `wwwroot/img/ranks/*.png`, build, start.
2. Open Overview, Leaderboard, a Player page, Builder, Clash, History > match detail, Settings > Guests, and a profile edit page: every rank shows a coloured shield with a number, none shows a broken image.
3. In the browser dev tools Network tab, check there is no 404 for `/img/ranks/`.
4. Check the shields read on all three looks (Steel, Brutal, Forest).

**Changed / new files:** new `wwwroot/img/ranks/default/*.svg` (10); changed `Views/_ViewImports.cshtml`, `Views/League/Index.cshtml`, `Leaderboard.cshtml`, `Player.cshtml`, `_MatchDetail.cshtml`, `Builder.cshtml`, `Views/LeagueClash/Index.cshtml`, `Views/LeagueSettings/Guests.cshtml`, `Views/Shared/_RankSelect.cshtml`, `Services/LeagueViewText.cs`, `Models/LeagueEnums.cs`, `LeagueBuilderModels.cs`, `LeagueHistoryModels.cs`, `wwwroot/js/league-builder.js`, `league-clash-board.js`, `wwwroot/css/league-builder.css`, `EXTRACTION-PLAN.md`.

### B3 Badges page (2026-10-27): WRITTEN, NOT BUILT
**Done**
- `Controllers/LeagueSettingsController.cs`: injects `LeagueRankBadgeService`; new actions `Badges` (GET), `UploadBadge`, `RemoveBadge`, `ResetBadges` (POST, `ValidateAntiForgeryToken`). All four are already Admin-only in `RolePermissions.cs` (B1). After an upload or remove the page returns to that tier's card (`#badge-<tier>`); the result shows in the usual green or red alert (`TempData`).
- `Views/LeagueSettings/Badges.cshtml`: one card per tier (weakest first) with a preview, "Default picture" or "Your picture", an upload form (file type filter in the browser; the server checks again), and "Remove picture" (only when one is uploaded). "Reset all to default" shows only when at least one picture is uploaded. Both removals ask for confirmation. Plain markup on the existing classes (`lg-card`, `lg-grid3`, `lg-mapcard`, `lg-mapform`, `lg-btn`, `lg-btn-danger`, `lg-alert`).
- `Views/LeagueSettings/Index.cshtml`: new "Rank badges" tile, shown only to who may open `Badges` (Admin).
- `wwwroot/css/league.css`: one rule, `.lg-bprev` (preview size, tokens not needed), so a very wide or tall upload stays inside its card.

**Decisions**
- The page copies the Modes map-image cards instead of inventing new markup; C5 already styles them, so it follows the three looks.
- The preview uses its own class, not `.lg-rk`, because `.lg-rk` lives in `league-overview.css`, which this page does not load.

**Not done / not run**
- Nothing compiled (no .NET SDK here). Not opened in a browser. The upload, remove and reset code paths were not run (the rules and service are from B1; their tests are B4).
- No test that a non-Admin gets denied (the table entries are from B1; the A7 tests project will cover it).

**You run**
1. Copy the files, build, start, sign in as Admin, open League settings > Rank badges.
2. All ten tiers show the default shield. Upload a small PNG for one tier: the page returns to that card, shows "Your picture" and a green message. Open the Leaderboard or a Player page at that rank: your picture is there.
3. Try to upload a `.gif`, a text file renamed to `.png`, and a file over 1 MB: each gives a red message and changes nothing.
4. "Remove picture" returns that tier to the default; upload two and use "Reset all to default". Check `wwwroot/uploads/rank-badges/` has no leftover files.
5. Sign in as Officer or Member: no "Rank badges" tile, and `/LeagueSettings/Badges` is denied.
6. Look at the page in the three looks.

**Changed / new files:** new `Views/LeagueSettings/Badges.cshtml`; changed `Controllers/LeagueSettingsController.cs`, `Views/LeagueSettings/Index.cshtml`, `wwwroot/css/league.css`, `EXTRACTION-PLAN.md`.

### B4 Tests for the badge rules (2026-10-27): DONE (owner: 54 of 54 passed)
**Done**
- New test project `LeagueTeamBuilder.Tests` (xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4, net10.0), a **sibling** of the web project (`tests/LeagueTeamBuilder.Tests`, project reference `..\..\src\LeagueTeamBuilder\LeagueTeamBuilder.csproj`). It must not sit inside the web project folder, because the web project compiles every `.cs` file under its own folder.
- `TestImages.cs`: real picture bytes. PNG, JPEG, WebP encoded with SkiaSharp; a real 16 px GIF (68 bytes, made with an image library), a hand-built 16 px BMP, and an SVG text (SkiaSharp cannot encode those).
- `RankBadgeRulesTests.cs` (pure rules): each real format with its matching ending is accepted; ending is case-insensitive; PNG/JPEG/WebP under the wrong ending refused ("doesn't match"); empty file, text renamed to `.png` and an SVG renamed to `.png` refused ("readable image"); GIF (as `.png` and `.gif`) and BMP refused ("must be a PNG"); 16 px and 2048 px pass, 15 px refused as too small, 2049 px as too big; `ExtensionMatches` table; the allowed endings are exactly png/jpg/jpeg/webp (no svg, no gif) and the limit is 1 MB.
- `RankBadgeCatalogTests.cs`: placeholder address, upload wins, `Replace` ignores blank addresses and unknown tiers and swaps the whole list, `ByKey` has the ten lower-case keys, `All` is weakest first, and **every tier has an `.svg` placeholder in `src/LeagueTeamBuilder/wwwroot/img/ranks/default/` with no `<script`** (the test walks up from the test folder to find it).
- `LeagueRankBadgeServiceTests.cs` (real in-memory SQLite database, temporary web folder): upload saves file + row + catalog; **replace deletes the old file only after the new one is saved**; **a forced database failure (a SQLite trigger blocks the update) keeps the old picture, the old file and the catalog, and deletes the new file**; non-picture, wrong ending (gif/svg/bmp/none), over 1 MB, no file, unknown tier, too-small picture: all refused with nothing saved and no file left; remove deletes row + file and only that tier; remove and reset with nothing uploaded say so; reset all removes every row and file; `GetAllAsync` lists ten tiers; a second catalog reads the rows after `ReloadCatalogAsync`.

**Decisions**
- I created the test project now instead of waiting for A7, because B4 cannot exist without one. A7 will add the other tests to the same project.
- The "delete-after-save" tests use a real SQLite database and a trigger, not mocks, so they test the actual order of events.

**Not done / not run**
- Nothing compiled or run (no .NET SDK here), including the tests. I checked braces by script only. The package versions are my best guess without a restore; if `dotnet test` fails to restore, paste the message.
- The expected message texts ("too small", "must be a PNG" and so on) come from reading `RankBadgeRules.cs`; one wrong word would fail a test, which then shows a wording or logic difference worth looking at.
- A test failure may also be a real bug in B1. Paste the failing test names and I will decide which side is wrong.

**You run**
1. Put the folder `LeagueTeamBuilder.Tests` from the zip into `tests/` next to `src/` (create `tests/`). If your layout differs, change the `ProjectReference` path in the `.csproj`. If you use a `.sln`: `dotnet sln add tests/LeagueTeamBuilder.Tests`.
2. `dotnet test tests/LeagueTeamBuilder.Tests` (or Test Explorer in Visual Studio). Expect about 60 test cases; all green.
3. Paste compile errors or failing test names.

**Changed / new files:** new `tests/LeagueTeamBuilder.Tests/LeagueTeamBuilder.Tests.csproj`, `TestImages.cs`, `RankBadgeRulesTests.cs`, `RankBadgeCatalogTests.cs`, `LeagueRankBadgeServiceTests.cs`; changed `EXTRACTION-PLAN.md`.

### Owner report after B4 (2026-10-27)
- Owner put the project in `tests/LeagueTeamBuilder.Tests` and ran `dotnet test tests/LeagueTeamBuilder.Tests` (first attempt failed only because the folder was not there yet). Result: restore fine with the package versions I chose, test project builds, **54 tests, 0 failed**, 5.6 s. So the B1 rules and service behave as written, including delete-after-save and the forced database failure.
- The test run also built the whole web project: **it compiled with 0 errors and 5 warnings**. That includes the Razor views, so B2 (views, `_ViewImports` inject) and B3 (Badges view and controller) compile. B2 and B3 are **still unticked**: nobody has opened the pages yet (the "You run" checks of B2 and B3).
- The 5 warnings are all CS8620 (nullability of `ModeKey` in a tuple: `string` passed where `string?` is expected) at `Services/LeagueBadges.cs(82)` and `Services/LeagueOverviewService.cs` lines 62, 200, 203, 276, into `LeagueOverviewStats.DraftMvpCounts` and `LeagueMvpBuckets.MvpCounts`. Harmless at run time (a `string` is accepted where `string?` is wanted); not touched. Candidate for the A9 pass or a one-line fix.
- The tests found 54 cases, not the "about 60" I estimated; nothing is missing, the theory rows were just fewer than I guessed.

**Changed:** `EXTRACTION-PLAN.md`.

### A7 Tests (2026-10-27): WRITTEN, NOT BUILT, NOT RUN
**Done** (all in `tests/LeagueTeamBuilder.Tests`, next to the B4 files; the B4 files are unchanged)
- `TestSupport.cs`: `SqliteTestDb` (an in-memory SQLite database for one test) and `WebProject.Find` (finds files of the web project from the test's bin folder).
- `RolePermissionsTests.cs`: a table of who may open which action (Member / Officer / Admin); the four badge actions are Admin only; names are case-insensitive; an action not in the table is denied for everybody, Admin included; nobody gets in without sign-in, without a role, or with an unknown role; **every public action of every League controller is in the table** (the same check Program.cs logs at start-up, now a test that fails the build of your tests if someone adds an action and forgets the table); the allowed roles are always "Admin", "Admin + Officer" or all three (no gaps); the checker agrees with the table; `RolePermissionFilter` (allowed = no result, not signed in = challenge, signed in but denied = forbid, script request = 403, unlisted action = forbid, non-League controller = ignored); `UserIdsAllowedTo` on a real database returns only the users whose role may open the action.
- `LeagueLooksTests.cs`: the three looks, `Find` ignores case and spaces and refuses anything unlisted; every look has its fonts stylesheet file, and brutal and forest have a `[data-look]` block in `leaguehub-looks.css`; `LookResolver`: nothing set = steel, configured default (a bad default falls back to steel), cookie beats the saved look, saved look for a signed-in user, an unlisted cookie or saved value counts as missing, visitors never get a saved look, the answer is kept for the request.
- `LeagueGameNameRulesTests.cs`: name from a Riot ID, typed-name checks (empty, line break, no name before `#`, too long), comparison key (case, spaces, zero-width and full-width characters), duplicate finder.
- `LeagueDayRulesTests.cs`: with a fixed UTC+2 zone (so the host's time zone data and summer time cannot change the result): Cairo day, today/yesterday, day range with an exclusive end, local/UTC conversion and input format, the 6-hour "added later" rule, `ParsePlayedOn` (valid, with seconds, blank, nonsense, future, before 2020).
- `TeamBalancerTests.cs`: rank score table, divisions, 60/40 blend, tier value and its fallbacks, lane cost table (5/4/3/2/1 = 0/1/4/6/8), `BestPerm` (all different lanes = cost 0; two for one lane = 8; more than 5 players throws), `Generate` with a seeded `Random`: 10 players make two teams of 5 with everyone placed once (Random, Lanes, Rank), same seed = same teams, 9 players = 5 + 4, Rank balance splits two Challengers, 1 player and 11 players are refused with the right message, ARAM (two teams, the "Lanes don't apply" note), Arena (the asked number of teams).
- `ChampionCatalogServiceTests.cs`: add (trim, blank, 60-character limit, duplicate in any case), bulk add counts, rename rules, disable and enable, delete an unused champion, search.

**Decisions**
- I did not wait for `HFECloud.Tests`; if you still have it, send it and I will compare, but these do not depend on it.
- The team generator is tested by what must always be true (everyone placed once, team sizes, the strongest pair split) and not by exact lineups, so a change to the random shuffling does not break the tests for no reason.

**Not done / not run**
- Nothing compiled or run (no .NET SDK here). Braces and parentheses were checked by script only.
- Not covered: the match, MVP, guest, clash, profile, history, overview, tier, settings and auto-send services, the Discord sender, and the controllers themselves. Most of those need `ILeagueNotifier` and `IActivityLog` fakes and a lot of seed data; that is better done after D1 (the demo seed) gives realistic data. Say if you want a second round of tests before D1.
- One test (Rank balance splits two Challengers) relies on the generator choosing the better of 500 shuffles. With a fixed seed it cannot be random-flaky, but if it fails it is worth a look at the objective in `GenNormal`.

**You run**
1. Copy the new files into `tests/LeagueTeamBuilder.Tests/` (the zip has them in that folder).
2. `dotnet test tests/LeagueTeamBuilder.Tests`. Expect the 54 old tests plus the new ones, all green.
3. Paste compile errors or failing test names. A failing "every action is in the table" test would mean a real missing line in `RolePermissions.cs`, which is worth knowing.

**Changed / new files:** new `tests/LeagueTeamBuilder.Tests/TestSupport.cs`, `RolePermissionsTests.cs`, `LeagueLooksTests.cs`, `LeagueGameNameRulesTests.cs`, `LeagueDayRulesTests.cs`, `TeamBalancerTests.cs`, `ChampionCatalogServiceTests.cs`; changed `EXTRACTION-PLAN.md`.

### Owner report after A7 (2026-10-27)
- Owner ran `dotnet test tests/LeagueTeamBuilder.Tests` (screenshot): restore fine, web project and test project build, **total 168, failed 0, succeeded 168, skipped 0**, 4.1 s. That is the 54 B4 tests plus 114 new ones. So the A7 tests compile and pass, including "every public action of every League controller is in the role table", the four badge actions being Admin-only, the filter cases and the seeded team generator. A7 is ticked.
- Nothing needed fixing. The Rank-balance test (two Challengers split) passed with its fixed seed.
- Still not covered by tests (as said in A7): match, MVP, guest, clash, profile, history, overview, tier, settings and auto-send services, the Discord sender, the controllers. Plan: after D1.

### A8 Click-through (2026-10-27): CHECKLIST WRITTEN, NOT RUN
**What this step is.** Nothing is changed in the code. You open each page once and write down what is wrong. Rule for the notes: one line per problem, `page, role, look, what you did, what you saw`. Behaviour bugs (wrong number, error, button does nothing, access wrong) come first; styling remarks go in a separate list for C6.
**Setup.** `dotnet run`, sign in as Admin (the `Seed:*` user). Make two more accounts through Register, and in `/Admin/Users` set one to Officer and leave one as Member. Use three browser profiles (or one normal and two private windows) so you can stay signed in as each. Pages with no data show empty states only; for real checks you need data (D1 is the planned seed). Until then add by hand: in Settings > Champions the list is seeded; add 10 guests (Settings > Guests) with ranks and lanes, then use them for the builder.

**1. Not signed in (private window)**
- `/` goes to the login page or the League page asks for login. `/League/Builder`, `/LeagueSettings`, `/Admin/Users`, `/Notifications` all lead to login, none shows a page or a stack trace.
- Register works, new account is a Member, login and logout work, a wrong password shows an error. The top bar shows the Look dropdown, and picking a look keeps after reload.

**2. Member**
- Can open: `/League` (Overview), `/League/Leaderboard`, a Player page (click a name), `/League/History`, a match detail, `/League/Profile` (edit own profile: rank, lanes, champions, save), `/LeagueClash`, a clash response page (answer yes / no / later, change it).
- Must be denied (access-denied page, not an error): `/League/Builder`, `/LeagueSettings`, `/LeagueSettings/Badges`, `/LeagueSettings/Guests`, `/Admin/Users`. The League tabs and the Overview must not show buttons the Member cannot use.
- `/Notifications` lists notifications and "Mark all as read" works.

**3. Officer**
- `/League/Builder`: pick 10 players (mix of members and guests), generate in each mode (Normal, ARAM, Arena), switch balance (Lanes, Rank, Tier), drag a player to another slot, regenerate, save as a match. Check team sizes, no player twice, lane clash notices.
- Start a match, finish it with a winner, edit the result, edit the "played on" date (a future date is refused), add stats and an MVP, import stats from the text format, delete a match (two-step confirm).
- History: filters, open a match popup, Result image button (needs `html2canvas`, see "Open items"; if the button does nothing, note the browser console message).
- Clash: schedule a clash (`/LeagueSettings/ClashSchedule`), check members get a notification, answer as a Member, build the clash team, create / rename / delete a team, set a guest busy, next game, end the clash.
- Settings tile page: Guests (add, edit, names, link to an account, delete), player rank, player names, player tier. Must be denied: Champions, Modes, Tiers, Auto-send, Badges, Linked guests, `BackfillMvps`.

**4. Admin**
- Champions (add, bulk add, rename, disable, remove an unused one), Modes (save, upload a map image, remove it, save spots), Tiers (add, rename, move, remove), Linked guests (unlink, undo a link), Auto-send (save; only a valid Discord webhook address is accepted; do not test with a real webhook unless you want a message), **Badges (the B3 checks: upload png, refuse gif / renamed text file / over 1 MB, remove, reset all)**.
- `/Admin/Users`: change a role, deactivate and reactivate an account (a deactivated account cannot sign in; an open session ends within about a minute), the last active Admin cannot be removed or deactivated, you cannot deactivate yourself.
- Overview "Side balance" card appears once a draft or ARAM game has a winner.

**5. Cross-checks**
- Rank pictures (B2): Overview, Leaderboard, Player, Builder, Clash, History popup, Settings > Guests, profile rank dropdown show the coloured shields, no broken image. Network tab: no 404 for `/img/ranks/`.
- Times: a match played at a known time shows the same time in History, the popup and the export (the SQLite UTC fix in A3 is the thing being tested).
- Restart the site (stop and `dotnet run`): the data, the uploaded badge, your look and your login survive.
- Browser console: no red errors on any page (404s for the missing font files and `html2canvas.min.js` are known, see below).

**Open items you will hit (known, not new bugs):** the 9 look font files and `wwwroot/lib/html2canvas/html2canvas.min.js` are not added yet (C1 list), so fonts fall back and the result / map picture may not render; the 4 view tags still point at the CDN for `html2canvas`; History, MatchDetail and Clash load Inter / Poppins from Google Fonts (decision open); the export background images are not in the repo; texts still say HFECloud / HFE (A9).

**Not done / not run:** I cannot run the app here (no .NET SDK, no browser). This is a checklist only; nothing was clicked. **Send back** the list of problems (or "all fine"), with a screenshot for anything odd. Say "next step" for A9 (wording pass) once A8 is ticked, or send the list first so the bugs are fixed before the wording changes.

**Changed / new files:** `EXTRACTION-PLAN.md` only.

### A9 Wording pass (2026-10-27): WRITTEN, NOT BUILT
**Before you read:** you said "next step" without a list from A8, so I took it as "A8 found nothing to fix first". A8 stays unticked until you tell me you ran it. If it did find bugs, send them and they go before anything else.

**Decisions (the proposals from A2, applied; say if you disagree)**
- **One site name.** New config `Site:Name` (default and appsettings value `League Team Builder`). `_Layout` writes it to the page as `window.LTB_SITE.name`; the scripts read it. It replaces `hfecloud.com` in the footer of all three result pictures, `· HFECloud` in the picture date line, in the Builder's map-picture subtitle and in the Clash picture subtitle, and it is the Discord post's bot name (`LeagueDiscordSender`, cut at Discord's 80 characters). No URL setting: a footer with a web address can come later if you want one.
- **Stats text format.** The paste format is now written as `LTB-STATS 1` (the prompt the page gives to the AI, its example, the box placeholder). The reader accepts both `LTB-STATS` and the old `HFE-STATS`, so text people saved earlier still imports. The script global `HfeStatsImport` became `LtbStatsImport` in `league-stats-import.js` and `league-history.js` (it is internal, nothing else reads it).
- **Download names.** `hfe-game-`, `hfe-session-`, `hfe-clash-` and the default `hfe-league.png` are now `ltb-...` (`league-result-image.js`, `league-send.js`, `LeaguePictureRules.DefaultFileName`). No test used the old default name (I searched the tests project).
- **Texts.** "HFECloud account / user / profile" became "account", "user" or "account profile" in `LeagueSettings/Index`, `Guests`, `LinkGuest` (also "whose group has League access" became "active users who may open the League pages", which is what the role table does), `League/Player` (2 places) and `League/Profile` (4 places). Profile now says it copies your account profile's name and picture.
- **Comments fixed because they were wrong now:** `LeagueSettingsController` (it talked about group ticks and "Rescan controllers", which do not exist here) and the `league-result-image.css` header (said HFE colours; the picture uses the Steel palette since C5).
- **Left on purpose (D4 review):** 14 code comments that say where a class came from ("stands where HFECloud's NotificationService stood" and similar, in `ActivityLog`, `FileUploadService`, `DiscordWebhook`, `RolePermissions` (2), `RolePermissionFilter`, `LeagueNotifier`, `MediaUploadLimits` (2), `ApplicationDbContext` (2), `NotificationsController`, `Program.cs`). They are true history, no user sees them. Say if you want them removed before the repo goes public; D4 will ask again.

**Checked (static only):** a search of `src` finds no `HFE` or `HFECloud` in any view, script, style or user-visible string, except the old-format reader (on purpose) and the comments above. `node --check` passes for the 6 changed scripts and `appsettings.json` parses. The Migrations folder was not touched.

**Not done / not run:** nothing compiled or opened in a browser (no .NET SDK). Unconfirmed: that `@Json.Serialize(...)` inside the inline script in `_Layout` renders as plain JSON (it should, and it escapes `<` and `>`), and that `@inject IConfiguration Config` needs no extra `using` (`Microsoft.Extensions.Configuration` is imported by default in web Razor views). The tests project does not reference any of the changed text, so the 168 tests should still pass; run them anyway.

**You run**
1. Copy the files (zip), `dotnet build`, `dotnet test tests/LeagueTeamBuilder.Tests` (expect 168 of 168).
2. Open a page, view source: near the end there is `window.LTB_SITE = { name: "League Team Builder" };`. Change `Site:Name` in `appsettings.json` (for example to `Friday League`), restart, and check it changes.
3. Make a result picture and a map picture: the footer and date line show the site name, no `hfecloud.com`. The downloaded file starts with `ltb-`.
4. History > a match > "Fill from screenshots": the prompt and the example say `LTB-STATS`. Paste an answer whose first line is `HFE-STATS 1` and one with `LTB-STATS 1`: both are read.
5. Profile page (a Member who has no league profile yet) and a Guests > Link page: no "HFECloud" anywhere.
6. Only if Discord is set up: a post shows the site name as the sender.

**Changed / new files:** `Views/Shared/_Layout.cshtml`, `Views/LeagueSettings/Index.cshtml`, `Guests.cshtml`, `LinkGuest.cshtml`, `Views/League/Player.cshtml`, `Profile.cshtml`, `_MatchDetail.cshtml`, `Services/LeagueDiscordSender.cs`, `Services/LeaguePictureRules.cs`, `Controllers/LeagueSettingsController.cs`, `wwwroot/js/league-result-image.js`, `league-send.js`, `league-builder.js`, `league-clash-board.js`, `league-stats-import.js`, `league-history.js`, `wwwroot/css/league-result-image.css`, `appsettings.json`, `EXTRACTION-PLAN.md`.

### D1 Demo seed (2026-10-27): WRITTEN, NOT BUILT
**Before you read:** you said "next step" again without a report on A8, A9, B2, B3 or C6. Those stay unticked (they are checks only you can run). D1 is the next step that is written work, and it also gives A8 the data it needs.

**Done**
- `Data/DemoSeeder.cs` (new). Off by default. `Demo:Seed=true` adds the demo data at start-up when it is not there; `Demo:Reset=true` first removes the demo data (only that) and adds it again. `appsettings.json` has both keys set to `false`. With either on, the log warns that this is fake data.
- **What it adds (all invented):** 16 guest players (Nova, Kestrel, Rook, Mira, Orion, Tavi, Juno, Brisk, Cobalt, Wren, Pixel, Ember, Sable, Quill, Lumen, Dax) from Iron to Master, three mains per lane plus one who plays everything, lane levels, two champions per strong lane (from the champion list), one "does better with" pair; 14 finished custom games over the last three weeks (10 draft, 4 ARAM, evenings UTC 17-21) with full stats for 13 of them and **none for the newest** (to try "Add stats"); 1 waiting (submitted) draft game; a finished **Clash** (3 games: win, win, loss, one team of five, "Opponents" empty, as the Clash board saves it); an upcoming **Clash** in 3 days with 7 answers (3 busy).
- **MVPs** are not invented: the seeder calls the site's own rule (`LeagueMatchService.ApplyMvp`) on each game with stats, so the Overview, Leaderboard and badges show what the rule gives.
- **Numbers are repeatable:** a fixed random seed, so every run (and every machine) produces the same games and stats. Draft games take two players per lane, mains first.
- **How it is found again (reset):** guests whose Riot ID ends in `#DEMO`, matches whose builder log starts with `[Demo]`, clash titles that start with `[Demo]`. Real guests, matches, accounts, the champion list, badges and settings are never touched. The demo players are guests, so no accounts are made; sign in with your own Admin and use Register for Officer / Member test accounts.
- **Wired in:** `Data/DatabaseSetup.cs` calls the seeder right after the identity seed (one line).
- **Tests:** new `tests/LeagueTeamBuilder.Tests/DemoSeederTests.cs`, 12 tests on in-memory SQLite with the real champion list and a fixed clock: 16 guests with lanes and champions, ranks valid (peak not below current, no division from Master), 14 + 1 + 3 games, nobody twice in a game and 5 per team, one player per lane in draft games, MVPs set on the 13 games with stats and none on the newest, dates in the past, both clashes (answers, three games on the finished one), a second run adds nothing, same numbers every time, **reset replaces the demo data and leaves a real guest, a real match and a real clash alone**, nothing happens with an empty champion list.

**Not done / not run**
- Nothing compiled or run (no .NET SDK here); braces checked by script only. Things I could not confirm without a compiler: that `ExecuteDeleteAsync` with `StartsWith("[Demo]")` runs on SQLite (it should; `[` is not special in SQLite's LIKE), that the one test line `Assert.Contains(m.MvpProfileId, winners)` holds for every seeded game (the MVP rule picks from the winning team, so it should), and the `Assert.Equal(new LeagueLane?[] ...)` order check.
- No custom tiers, avatars, notifications, rank badge uploads or result pictures are seeded (tiers and badges are Admin features to try by hand; pictures need `html2canvas`, still missing).
- The seed runs inside start-up, so a very slow disk makes the first start a little longer. It is a few hundred rows.

**You run**
1. Copy the files from the zip (one new file in `src`, one in `tests`), `dotnet build`, `dotnet test tests/LeagueTeamBuilder.Tests` (expect 168 + 12 = 180, all green). Paste any failing test name.
2. Turn it on for your own copy only, without committing it: from the project folder, `dotnet user-secrets set "Demo:Seed" "true"` (or set the environment variable `Demo__Seed=true`), then `dotnet run`. The log shows "Demo:Seed is on" and "Demo data added: 16 players...".
3. Open `/League`: podium, MVP counts, leaderboard, Side balance card (once ARAM or draft games have winners), History with 15 games and a Clash group, the Clash page with the upcoming event, a Player page. Run A8 against this data.
4. Restart with the same setting: the log says the demo data is already there and nothing is added. Set `Demo:Reset` to `true` and restart: the same 16 players and the same games come back (ids are new).
5. To get rid of the demo data: delete the database file (`App_Data/leagueteambuilder.db`, with the `-shm` and `-wal` files) and start again with `Demo:Seed` off. There is no switch that removes only the demo data (see the choice below).

**Choices to confirm**
- Demo players are guests, not accounts. If you want demo accounts (so the Member and Officer pages can be tried without Register), say so; it needs a password for each, which I would put in the config, not in the repo.
- A switch that only removes the demo data (`Demo:Clear`) is not written; item 5 above is the gap. Say if you want it before D3.

**Changed / new files:** new `src/LeagueTeamBuilder/Data/DemoSeeder.cs`, `tests/LeagueTeamBuilder.Tests/DemoSeederTests.cs`; changed `Data/DatabaseSetup.cs`, `appsettings.json`, `EXTRACTION-PLAN.md`.

### D2 README (2026-10-27): WRITTEN, NOT RUN; SCREENSHOTS MISSING
**Before you read:** you said "next step" with no report on D1, A8, A9, B2, B3 or C6. Those stay unticked. D1's 12 tests have not been reported either, so the README does not state a test count.

**Done (one new file: `README.md` at the repo root; it replaces the A1 stub)**
- Sections: what it is, the Riot disclaimer (first thing under the title), status, features, screenshots, run it, demo data, roles, configuration, your data, tests, contributions, license.
- **Every claim was checked against the code in your zip**, not written from memory: the config keys and defaults (`appsettings.json`), the roles table (`RolePermissions.cs`), the three looks (`LeagueLooks.cs`), the settings tiles and nav (what each role sees), the first-Admin rule (`IdentitySeeder.cs`: only when no active Admin, never changes a password), the demo switches (`DemoSeeder` / `DatabaseSetup`), the upload limits for badges, and the database and uploads locations.
- **Left out on purpose because it is not true yet:** Docker and a live demo (D3), the desktop edition (only "planned"), "works offline / no CDN" (the font files and `html2canvas.min.js` are still missing and three views still load Inter and Poppins from Google), and any test count.
- **Contributions line:** "This project does not accept contributions... Pull requests will not be merged... fork it." It promises nothing about issues; say if you want issues closed off or invited.
- The clone URL is `https://github.com/<owner>/league-team-builder.git` (I do not have your exact username; fill it in).

**Not done / not run**
- **No screenshots or GIF.** I cannot run the app here (no .NET SDK, no browser), and I will not draw fake ones. The README links six files that do not exist yet, so the images show as broken until you add them. The repo is private until D4, so nothing public is broken.
- I did not run the README's commands. `dotnet user-secrets` needs the project folder (the README says `cd src/LeagueTeamBuilder` first). The address `dotnet run` prints depends on `launchSettings.json` (https 51054 / http 51055 in Development); the README says "the address printed in the console" instead of a number.
- `global.json` and `LICENSE` were not in the zip you sent; the README names both from the A1 record. Check they exist at the repo root.

**Screenshot shot list (PNG, about 1400 px wide, in `docs/screenshots/`; run with `Demo:Seed=true`, signed in as Admin)**
1. `overview-steel.png`: `/League`, Steel, scrolled to show the podium and the Side balance card.
2. `builder-steel.png`: `/League/Builder` with 10 players picked and teams generated.
3. `history-steel.png`: `/League/History` with one match popup open.
4. `clash-steel.png`: `/LeagueClash` showing the upcoming event.
5. `overview-brutal.png` and 6. `overview-forest.png`: same page as 1 in the other two looks.
Optional GIF (under 5 MB): pick players, Generate, drag one slot, save. Do not show real names or a real Discord webhook. Take them after A8, so a visual bug is not in the picture.

**You run**
1. Copy `README.md` to the repo root (over the old stub), and the empty `docs/screenshots/` folder.
2. On a clean clone follow "Run it" word for word. Anything that fails is a README bug.
3. Take the six screenshots, name them as listed, and check the README renders on GitHub (preview on the private repo).
4. Tell me what to change in the wording.

**Changed / new files:** new `README.md`, `docs/screenshots/.gitkeep`; changed `EXTRACTION-PLAN.md` (D2 line and this record).

### D3 Docker (2026-10-27): WRITTEN, NOT BUILT, NOT RUN
**Correction:** my first D3 included a `render.yaml` and a "live demo on Render" section. You had said you would not host it; I should have taken that out sooner. They are removed. Nothing in the plan, README or files now plans a hosted site. Delete `render.yaml` if you copied it.

**Done (new files at the repo root; no C# change except one comment in `DemoSeeder.cs`)**
- `Dockerfile`: two stages on `mcr.microsoft.com/dotnet/sdk:10.0` and `aspnet:10.0`. Restore is a separate layer. Publishes only the web project (no tests). Runs as the image's non-root user. Listens on 8080.
- **One data folder, `/data`.** Database `/data/leagueteambuilder.db` (`Database__Path`). Uploads: the app writes to `wwwroot/uploads` (hard-coded in 4 services; moving it is the Phase E rule 2 change), so in the image that folder is a link to `/data/uploads`, and the start command makes the folder if a fresh volume hides it. One volume is the whole backup.
- `docker-compose.yml`: one service, port 8080, named volume `ltb-data` on `/data`, settings from `.env` (`SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `SITE_NAME`, `DEFAULT_LOOK`, `DEMO_SEED`, `DEMO_RESET`). No secret in the file. Empty admin settings only log the existing "no active Admin" warning; set them and restart to fix. The reverse-proxy switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is there, commented out.
- `.env.example`, `.dockerignore` (leaves out `bin`, `obj`, `.git`, `tests`, `docs`, any `.db`, `App_Data`, uploads, `.env`).
- `README.md`: new "Run with Docker" section; the data paragraph points to `/data`.
- `Data/DemoSeeder.cs`: one comment changed (it said "used by the live demo"). No code change from the D1 fix you already applied.

**Not done / unconfirmed (no Docker, no .NET SDK here)**
- Nothing built or run. The YAML was parsed only. The first `docker compose up --build` is the test.
- **Link to the uploads folder:** I believe static files are served and written through it, but did not run it. Test: upload a rank badge as Admin, see it on the Leaderboard, restart the container, still there. If the picture 404s, the fallback is two mounts (`/data` and `/app/wwwroot/uploads`).
- **Non-root:** if a bind-mounted folder is not writable by the image's user, the log says "unable to open database file". Fix the folder's owner or delete the `USER` line in the `Dockerfile`.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is the built-in .NET switch; not checked on the .NET 10 image. It matters only behind a proxy that handles https.
- No Docker health check in the image (the runtime image has no curl); `/healthz` exists for anything that wants one.

**You run**
1. Add `.env` to `.gitignore` (and `App_Data/` if not there). Copy the files to the repo root.
2. `cp .env.example .env`, set the admin e-mail and password, `docker compose up --build`. Paste any error.
3. Open `http://localhost:8080`, sign in as Admin; `/healthz` shows `"ok"`.
4. Set `DEMO_SEED=true`, `docker compose up -d`, check the log shows "Demo data added: 16 players".
5. Upload a rank badge, `docker compose restart`: badge, login and data are still there.

**Changed / new files:** new `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`; changed `README.md`, `Data/DemoSeeder.cs` (comment), `EXTRACTION-PLAN.md` (D3 line and this record).

### Owner report and open-items step (2026-10-26): fonts, html2canvas, tests, seeder
**Owner reported**
- `dotnet build`: succeeded with 5 warnings (CS8620, `ModeKey` nullability, in `LeagueOverviewService.cs` and `LeagueBadges.cs`). Not fixed yet.
- `dotnet test tests\LeagueTeamBuilder.Tests`: **180 of 180 passed, 0 failed.** My earlier "expect 181" was wrong: the seeder has 12 tests (168 + 12 = 180). The handoff note that said 13 was a mistake.
- Seeder: "seeder worked" (D1 ticked on the owner's word). No log was sent; the earlier "doesn't work" report is treated as solved.
- The zip files `LeagueTeamBuilder_1.zip` (repo root: README, Dockerfile, docker-compose.yml, `.env.example`, `.dockerignore`, LICENSE, global.json, sln, `.gitignore`) and `LeagueTeamBuilder_Tests.zip` were received and checked: all present.

**Done by the owner (PowerShell, from `src\LeagueTeamBuilder`)**
- Downloaded the 9 font files into `wwwroot/lib/fonts/files/` and `html2canvas.min.js` (198,689 bytes) into `wwwroot/lib/html2canvas/`. All sizes are normal (no error pages).
- Changed the 4 script tags (`League/Builder`, `League/History`, `League/MatchDetail`, `LeagueClash/Index`) to `~/lib/html2canvas/html2canvas.min.js`. Checked with `Select-String`: no jsDelivr link left.

**Not reported / still open**
- Whether the Result image button works in History with the local `html2canvas` (and that it does not stop on the missing `bg-*.jpg`).
- Inter / Poppins from Google Fonts in History, MatchDetail, Clash (owner decision still open).
- `league-export.js` body-font question; the 5 CS8620 warnings; `.gitignore` entries (`App_Data/`, `wwwroot/uploads/`, `.env`); the SIL OFL font licences kept with the font files if published.
- A8, A9, B2, B3, C6, D2 (six screenshots), D3 (Docker checks) unchanged: not reported.

**Changed files:** `EXTRACTION-PLAN.md` only (D1 ticked, this record). The font files and the 4 view edits were done by the owner.

### D5 (a) Tutorial page: design (2026-10-26): DESIGN ONLY, NO CODE
**Owner asked:** one HTML page to upload to Vercel, no database, with a tutorial for Clash and the Team Builder. Hosting is **Vercel (static)**. It does not need a database or a build step: drop `index.html` (and a `screenshots/` folder) in a Vercel project.

**Design (from the handoff, plus the owner's request)**
- One `index.html`: inline CSS and JS, no CDN, no internet fonts (system font stacks), under about 150 KB without images. Works with JavaScript off (`<details>` or CSS-only tabs). Responsive from 320 px. The three looks (Steel default, Brutal, Forest) as a small switcher, token blocks only; values read from `leaguehub-theme.css` / `leaguehub-looks.css` when I write it.
- Four link buttons in one marked block at the top of the file: GitHub, Web files, Desktop files ("Coming soon"), Desktop download ("Coming soon"). `<owner>` placeholders until the repo exists. Every external link `target="_blank" rel="noopener noreferrer"`.
- **New part (owner's request): a "How to use it" tutorial** with two sections, checked against the real views and services before writing: **Team Builder** (pick players, modes Normal / ARAM / Arena, balance by lanes / rank / custom tiers / random, generate, move a slot, save the match, result picture) and **Clash** (schedule, answers, the board, saving the games). Plain, short steps.
- Rest of the outline from the handoff: what you get, screenshots (placeholder block until real ones exist), quick start (Docker / .NET / Desktop disabled), demo data, roles, settings, FAQ, footer with the Riot line.

**Riot artwork rule (flagged to the owner)**
- The owner uploaded three map pictures (Summoner's Rift, Arena, ARAM). They are **Riot artwork**. They are not put in the repo, in the zips, or on the Vercel page. The page uses no Riot pictures; its map/lane drawings, if any, are my own simple shapes.
- The result picture reads `wwwroot/img/league-export/bg-normal.jpg` and `bg-aram.jpg` (`league-result-image.js` line 210). If the owner wants them on his own machine, he may place them there **locally and keep them out of git** (add `wwwroot/img/league-export/` to `.gitignore`). No file is read for Arena. Without the files the picture uses the dark colour (still to confirm in the click-through).

**Open question for the owner:** is the page for people who run the project (install + how to use), or only a how-to-use guide? Default if no answer: both, with the "How to use it" tutorial first.

**Not done:** no page written, nothing run, no Vercel check. Do not say it works on Vercel or in an iframe until the owner has loaded it. Next: D5 (b), the page without the screenshots filled.

**Changed files:** `EXTRACTION-PLAN.md` only.

### Inter / Poppins self-hosted (2026-10-26): WRITTEN, NOT BUILT
**Decision:** the owner said "your choice"; option 1 applied (self-host). Reason: the result picture keeps its designed look and works offline (desktop edition); "no CDN" becomes true.

**Done**
- New `wwwroot/lib/fonts/result-image.css`: `@font-face` for Inter 500, 600, 700, 800 and Poppins 800, 900 (Latin), files in `wwwroot/lib/fonts/files/`.
- `Views/League/History.cshtml`, `Views/League/MatchDetail.cshtml`, `Views/LeagueClash/Index.cshtml`: the Google Fonts `<link>` replaced by `~/lib/fonts/result-image.css`. A search of `Views` and `wwwroot` finds no `googleapis` or `gstatic` left. These 3 files also carry the owner's earlier `html2canvas` tag change (local file), so copying them does not undo it.

**Not done / not run**
- The 6 font files are NOT in the package (no network here). The owner downloads them (command in the reply). Until then the picture uses the fallback font.
- Not built, picture not looked at. The Fontsource file names are written from memory and not checked.
- Keep the SIL OFL licences with the font files when the repo goes public (D4).

**Owner runs**
1. Download the 6 files, check each is more than a few KB.
2. Copy the changed files, `dotnet build`, open History, a match, press Result image: text should be Inter / Poppins, and the browser console (F12, Network) should show no request to `fonts.googleapis.com`.

**Changed / new files:** new `wwwroot/lib/fonts/result-image.css`; changed the 3 views above, `EXTRACTION-PLAN.md`.

### D5 (b) Tutorial page (2026-10-26): WRITTEN, NOT OPENED
**Owner reported:** the 6 Inter / Poppins font files are downloaded (sizes 7.6 to 24 KB, listed in PowerShell). Not reported yet: build, and whether the Result image picture works with the local fonts and `html2canvas`.
**Owner decision ("your choice"):** the page serves both people who run the project and people who want a how-to; the how-to comes first.

**Done**
- `site/index.html`, one file, about 15 KB, inline CSS, no JS, no CDN, no web fonts, no images. Sections: hero with the Riot line and the four buttons, what you get, screenshot placeholders, **How to use the team builder**, **How to use Clash** (members, officers), run it (Docker / .NET / Desktop disabled, in `<details>`), demo data, roles table, settings table, FAQ, footer.
- The four links are in one marked comment block and the four buttons at the top. Two are live (GitHub, Web files, with `<owner>` still to fill in); two are disabled "coming soon" spans (`aria-disabled`). The bottom only has a "Back to the links" button, so there is one place to edit.
- Steps were checked against `league-builder.js`, `league-clash-board.js`, `README.md` and `RolePermissions` wording (button names, balance modes, Clash rules, Send to Discord only when a webhook is set). Commands and tables are copied from the README.
- Steel tokens only (`#0e1013`, `#171a1f`, cyan `#19d3ff`, orange `#ff7a1a`). Focus rings, 320 px layout, tables scroll in their own box.

**Not done / not run**
- No look switcher, no height helper for the iframe, no real screenshots (placeholders only): that is D5 (c).
- Not opened in a browser by me. Not tested on Vercel or in an iframe. HTML tags are balanced (script check only).
- The Clash steps say a Clash has a limit of 4 games; the code comment says the cap is hard on the server, but I did not confirm if it is per team. Owner: check in the click-through.

**Owner runs**
1. Open `site/index.html` in a browser; narrow the window to 320 px; turn JS off; Tab through the links.
2. Replace `<owner>` in the 2 live links when the repo exists.
3. Vercel: make a project from the `site` folder (no build command), open the link. Say what you see.

**Changed / new files:** new `site/index.html`; changed `EXTRACTION-PLAN.md`.

### D5 (c) Look switcher, height helper, screenshots (2026-10-26): WRITTEN, NOT OPENED
**Done (only `site/index.html` changed, now about 18 KB)**
- **Look switcher:** Steel (default), Brutal, Forest, as token blocks only, with the values from `leaguehub-looks.css` (Brutal `#4d8dff` / `#ff4d42` / `#0f0f10`, 0 radius, off-white borders; Forest `#4ade80` / `#ff8a65`, 14 px radius). The buttons are hidden until JavaScript runs, so with JS off the page is Steel and everything works. The choice is kept in `localStorage` (guarded; if the browser blocks it, nothing is kept). Dividers use a separate `--rule` token so Brutal's off-white borders do not become heavy lines. The page uses system fonts in all three looks (the project's own fonts are not loaded on this page).
- **Height helper:** posts `{type: 'ltb-height', height}` to the parent on load, on resize and when the page size changes (`ResizeObserver`). Does nothing when the page is not in a frame. Sent with target origin `*` (the height is not private).
- **Screenshots:** still placeholders. A comment in the file has the exact `<img>` line (lazy, with `width`/`height`, `alt`) to paste for each one once the PNGs exist in `screenshots/` next to the file. I will not invent sizes, files or alt text.

**Parent snippet for the owner's own page (not tested):**
```html
<iframe id="ltb" src="https://YOUR-SITE.vercel.app/" title="League Team Builder guide" loading="lazy"
        style="width:100%;border:0;height:900px"></iframe>
<script>
window.addEventListener('message', function (e) {
  if (e.origin !== 'https://YOUR-SITE.vercel.app') return;   // check the origin
  var d = e.data;
  if (d && d.type === 'ltb-height' && typeof d.height === 'number') {
    document.getElementById('ltb').style.height = Math.ceil(d.height) + 'px';
  }
});
</script>
```

**Not done / not run**
- Not opened in a browser by me; contrast ratios of the three looks were not measured (colours copied from the project's tokens); not tested on Vercel or inside a real iframe. Vercel does not forbid framing by default, but check that your project has no `X-Frame-Options` or `frame-ancestors` header, and that the page that embeds it allows the Vercel origin in `frame-src` if it has a CSP.
- `site/index.html` still says `<owner>` in the two live links.

**Owner runs (checklist, D5 d)**
1. Open `site/index.html`: switch the three looks; reload (the look should stay).
2. Narrow to 320 px: no sideways scroll of the page; tables scroll in their own box.
3. JS off: the page is readable, the look buttons are not shown.
4. Tab through every link and button: a focus ring shows.
5. Vercel: upload the `site` folder, open the link, press all four buttons (the two disabled ones do nothing).
6. Put it in an `<iframe>` with the snippet above: it grows to the page height and no link opens inside the frame.

**Changed files:** `site/index.html`, `EXTRACTION-PLAN.md`.

### D5 redo: interactive demo with a guided tour (2026-10-26): DESIGN ONLY, NO CODE
**What went wrong:** I read "tutorial page" as a text how-to. The owner meant a **working demo**: the real screens, fake data, and a **guided tour** (the screen goes gray, a spotlight points at a button, a box explains it). The text page (`site/index.html` from D5 b and c) is not what was asked for.

**What the owner wants (his words, 2026-10-26)**
- The team builder with a pop-up tour that grays out the screen, points to the buttons and explains them to the user.
- A demo in HTML and CSS with: the **3 themes**, the **team builder**, the **Clash builder**, the **match history**, the **import from screenshots** feature, and the other features.
- Hosted on Vercel, no database.

**Design (defaults; the owner can change any of them)**
- **Static, no database, no server.** Plain files in `site/`: `index.html`, `demo.css`, `demo.js` (small, no framework, no CDN, no internet fonts). The text page becomes `guide.html` (install and roles), linked from the demo. If the owner does not want it, delete it.
- **Same look as the real site:** the real tokens from `leaguehub-theme.css` / `leaguehub-looks.css`, a top bar with the three look buttons (Steel, Brutal, Forest) and three tabs: **Team builder**, **Clash**, **History**. The real class names are not needed; the demo copies the look, not the code.
- **Fake data only:** the 16 invented demo players (names from the demo seed), invented games. No Riot art. Champion names, if shown, are plain text. A visible line: "Demo with invented data. Nothing is saved." and the Riot disclaimer.
- **Guided tour engine** (written once, reused by every tab): the page dims, a lit "hole" shows the target element, a box next to it gives a title, 1 to 3 short sentences, and Back / Next / Skip. Keyboard (Esc closes, arrows move, Tab stays inside the box), focus returns when it closes, `aria-live` text, respects reduced motion, works at 360 px (the box goes below or above the target). Starts from a "Take the tour" button on each tab, and once automatically on the first visit (can be turned off by closing; remembered in `localStorage`, guarded).
- **Team builder tab:** pick 10 players, choose game mode (Normal, ARAM, Arena) and balance mode (4 modes), **Generate teams** (a small balancer written for the demo, not the real C# one), swap by press-and-press or drag, **Submit teams**, Start, Finish with a winner, **Export for Discord** (shows a picture made with canvas in the page, own drawing).
- **Clash tab:** a clash with answers (free / busy), **Set best team**, edit slots, **Save as game N**, Start / Finish, series record, **End clash**.
- **History tab:** list with filters, open a match, **Add stats**, and **Fill from screenshots**.
- **Fill from screenshots, shown as the real feature works** (checked in `league-stats-import.js`): the site never calls an AI. The officer copies a prompt made by the site, pastes it with the screenshots into their own AI chat, pastes the answer back, and the site reads and checks it, then fills the numbers. The demo walks these steps with **screenshots I draw myself** (a simple fake scoreboard, marked "example, not from the game"; no Riot art), a prompt built from the demo players, a ready answer, and the check step (it can show one missing number).
- **The three looks:** the switcher changes tokens only, as in the real site; the choice is kept.

**Steps (one per reply, each with an owner checklist; nothing is ticked until the owner reports)**
1. **D5.1 Shell:** `index.html`, `demo.css`, `demo.js` with the top bar, 3 looks, tabs, fake data, and the **tour engine** with one tour on the Team builder (just the first steps), so the owner sees the style at once.
2. **D5.2 Team builder demo** fully working + its full tour.
3. **D5.3 Clash demo** + tour.
4. **D5.4 History demo + Fill from screenshots** + tour.
5. **D5.5 Polish and check:** 320 px, keyboard, JS off message, contrast, `guide.html` link, Vercel upload, real iframe if wanted.

**Size warning:** the full demo is large (four screens and a tour). It is built in the 5 steps above so each reply stays small enough to finish and to check. Expect about 60 to 90 KB in total, far under any Vercel limit.

**Not done:** no code written for this redo. The text page is unchanged and still unopened. The owner's earlier build / Result image / checklist reports are still pending.

**Changed files:** `EXTRACTION-PLAN.md` only.

### D5.1 Demo shell, looks, tabs, tour engine (2026-10-26): WRITTEN, NOT OPENED
**Done** (new files in `site/`; the old text page is now `guide.html`, with a link to the demo)
- `index.html`, `demo.css`, `demo.js` (about 16 KB in total, no framework, no CDN, no web fonts, no images). Top bar with three tabs (Team builder, Clash, History), the three looks (Steel, Brutal, Forest; same token values as the real site, kept in `localStorage`) and a "Take the tour" button. A banner: "Demo with invented players. Nothing is saved." plus the Riot line and a link to the guide.
- **Team builder (basic):** 16 invented players, pick 10, game mode, 4 balance modes, **Generate teams**, two team cards with a strength number.
- **Tour engine (reused by the next tabs):** the page dims, a lit hole shows the element, a box explains it with Back / Next / Skip. Esc closes, arrow keys move, Tab stays inside the box, focus returns to the button, the page behind is `inert`, the box moves above or below the target, works on a narrow screen, no animation when reduced motion is on. It starts once on the first visit and from "Take the tour".
- **Builder tour, 6 steps:** players, mode, balance, Generate (picks 10 players for the visitor), the result, the looks.
- Clash and History tabs show a short "comes in a later step" text.

**Known limits (on purpose, fixed in D5.2)**
- Balance modes 2 and 4 behave like mode 3 (rank snake order); mode 1 is a shuffle. Normal mode shows each player's main lane only; no lane-aware assignment, no swapping, no Start / Finish, no export picture yet.

**Not done / not run**
- Not opened in a browser by me. The JavaScript passes `node --check` only; I did not run it or test the tour on a phone. Contrast of the looks not measured. Not on Vercel.

**Owner runs**
1. Open `site\index.html`. The tour starts by itself the first time (clear it with `localStorage` or use a private window). Press Next through the 6 steps; press Esc in the middle; press "Take the tour" again.
2. Switch the 3 looks, also while the tour is open to check the box colours (close the tour, switch, open it again).
3. Narrow the window to 360 px and run the tour: the box must stay on the screen and not hide the lit button.
4. Tab through the page, and inside the tour. Tell me anything that looks wrong or unclear.

**Changed / new files:** new `site/index.html`, `site/demo.css`, `site/demo.js`, `site/guide.html`; changed `EXTRACTION-PLAN.md`.

### Owner deleted the guide page (2026-10-26)
The owner deleted `site/guide.html` (the first text page). The links to it are removed from `site/index.html` (banner and the no-JavaScript message). Every mention of `guide.html` in the D5 redo and D5.1 records above is therefore out of date: there is no guide page. Install and roles text stays in `README.md`. The demo's own "Run it yourself" or GitHub link, if wanted, is decided in D5.5 (default: one GitHub button at the top, `<owner>` placeholder).
The owner also asked for a handoff so another AI session can continue: `HANDOFF-PROMPT.md` (rewritten 2026-10-26).
**Changed files:** `site/index.html`, `EXTRACTION-PLAN.md`, `HANDOFF-PROMPT.md`.

### D5.2 Team builder in the demo (2026-10-26): written, not opened in a browser

**Done**
- `site/demo.js`: 16 invented players with current rank, peak rank, lanes and a custom tier. Four balance modes. Lane cost 0, 1, 4, 6 (8 if the lane is not played) and the 60% current + 40% peak blend, as in `TeamBalancer.cs`. Modes 3 and 4 try all 126 splits and the best lane order per team. Mode 2 is a random split with the best lanes. Mode 1 is a plain shuffle. ARAM has no lanes. Arena makes 8 teams of 2.
- Map markers use the X % / Y % values from the owner's position editor screenshots (Normal Draft and ARAM). `mapHtml(kind, teams, only)` can draw one side only; D5.3 Clash will use `only`.
- Swap by pressing two players (on the map or in the lists). Submit teams, Start game, Finish game with a winner (demo only). "Export for Discord" draws a PNG on a canvas with our own code.
- 9-step builder tour. `site/index.html` and `site/demo.css` changed a little.

**Checked:** `node --check` passes. The balance code ran in Node for all modes (right player counts; two-team rank difference 0.0 to 0.4 in the runs). 
**NOT run:** nothing was opened in a browser. Markers, swap, export and the tour are untested. The picture files are not in my zip.
**Not done (left for later):** drag to swap; removing a player with the X button; saving the game into the History tab.
**Owner does:** copy the two map pictures to `site/img/normal-map.jpg` and `site/img/aram-map.jpg`. Open `site/index.html` and run the checklist in the reply.
**Changed files:** `site/index.html`, `site/demo.css`, `site/demo.js`, `src/LeagueTeamBuilder/EXTRACTION-PLAN.md`.

### D5.3 Clash in the demo (2026-10-26): written, not opened in a browser

**Checked in the real code first (`league-clash-board.js`):** the 4-game cap is **per team** (`myGames()` of the active team, "the most a team can have"). A clash has at most 4 teams. The server decides the game number and allows one unfinished game. So the demo text says "at most 4 games for a team". The earlier open question is answered.

**Done**
- Clash tab, one team: 8 invented members, free / busy answers (two start busy), "Set best team" (best 5 free players by the rank blend, best lane order), a drop-down per lane (busy and used players are not listed), a lane-fit note (Main / Second / Off lane), and **one side of the Normal Draft map** (Team 1 positions, markers not clickable).
- Games: Save as game N, Start, Finish with Won or Lost, series record, 4-game hint, End clash with a Yes / Keep going question, then "Start a new demo clash". Answers and slots are locked while a game is saved or in progress.
- 5-step Clash tour. "Take the tour" now starts the tour of the open tab.

**Not done:** multiple teams in one clash; the scheduling part (days and times); the "Pick by" list; the series picture. The real page keeps lineups in the browser; the demo does not.
**Checked:** `node --check` only. Not run in a browser (no browser or jsdom here). **Owner does:** open `site/index.html`, Clash tab, run the checklist in the reply.
**Changed files:** `site/index.html`, `site/demo.css`, `site/demo.js`, `EXTRACTION-PLAN.md`.

### D5.4 History and Fill from screenshots in the demo (2026-10-26): written, not opened in a browser

**Decision:** the demo uses a **verbatim copy** of `wwwroot/js/league-stats-import.js` as `site/league-stats-import.js` (no edits). So the prompt text, the nine required numbers and the checks are the real ones, not retyped. It is our own MIT code and has no Riot art. If the real file changes, copy it again.

**Done**
- History tab: 5 invented finished games (Normal and ARAM), filters by mode and player, open a game (two teams, "No stats yet"), "Add stats" form (champion + 9 numbers per player, "Save stats (demo)", kept only until reload), result lines from `scoreReadiness` / `describeReadiness`.
- Fill from screenshots, same 3 steps as `league-history.js`: (1) game names and optional champions, (2) "Copy prompt" built by `buildPrompt` from the demo players, (3) paste the answer, "Check answer" (`parseAnswer`, problems listed, "Found X of N players, M values"), "Fill the form" fills the stats form, nothing is saved until Save stats.
- A fake scoreboard drawn in HTML/CSS and marked "Example scoreboard drawn by this page. It is not from the game." A button "Use the example answer" inserts a ready answer built from the same numbers; **one number (objective damage of the first player) is empty on purpose**, so the readiness lines show a missing number after Save.
- 9-step History tour.

**Checked in Node:** the prompt builds (3426 characters); the example answer is read for 10 of 10 players, 89 of 90 values, no problems, no missing players.
**NOT run:** nothing was opened in a browser. Copy button (clipboard), forms, filters and the tour are untested.
**Not done:** the "extra numbers" row (the example answer has none), the MVP choice, paging, the Arena games.
**Changed files:** `site/index.html`, `site/demo.css`, `site/demo.js`, new `site/league-stats-import.js`, `EXTRACTION-PLAN.md`.

### D5.5 Polish for the demo (2026-10-26): written, not opened in a browser

**Done**
- **Contrast (computed in Node, WCAG formula):** text pairs in all three looks are 5.3:1 or better (ink on page 16.6 to 17.4, muted text 5.8 to 7.6, link and lane colours 5.3 to 9.8, button text 6.0 to 9.8). Control borders (inputs, selects, text boxes) were only 1.4 (Steel) and 1.6 (Forest) against the page; new token `--ctl` (4.3 and 4.7 in Steel and Forest; Brutal already 17.4) is used for them. Card borders stay soft on purpose.
- **Keyboard:** the tabs had `tabindex=-1` on two of three tabs and no arrow keys, so a keyboard user could not reach Clash or History. Now Left, Right, Home and End move between tabs. Added "Skip to content" link and a visible focus ring check is left to the owner.
- **320 px:** smaller padding and font below 420 px, the grids fit 288 px. Not seen on a real screen.
- **No JavaScript:** a clear message. **Forced colours:** selected items keep an outline.
- **GitHub button** in the top bar and a footer line (MIT, invented players, map credit, Riot line). The link is `https://github.com/OWNER/league-team-builder`: **replace OWNER** before upload (D4).
- **Embed:** `?look=steel|brutal|forest` sets the look. Inside an iframe the tour does not start by itself (the Take the tour button still works). A small icon file is inline.
- `site/vercel.json`: only two plain headers (nosniff, no-referrer). No CSP yet (inline styles and the form handler would need changes).

**Upload to Vercel (not tried by me, owner to check):**
- From GitHub: Add New Project, import the repo, set **Root Directory** to `site`, Framework Preset **Other**, no build command, Deploy.
- Or the CLI in PowerShell: `npm i -g vercel`, then `cd C:\Users\Yousef\source\repos\LeagueTeamBuilder\site`, then `vercel`, then `vercel --prod`.
- **Pictures:** `site/img/normal-map.jpg` and `aram-map.jpg` must be in the upload. If they are in `.gitignore`, the CLI skips them too. Default: keep them committed. A public repo makes them downloadable; this is the owner's decision (2026-10-26).
- **Iframe snippet:** `<iframe src="https://YOUR-DEMO.vercel.app/?look=steel" title="League Team Builder demo" width="100%" height="900" loading="lazy"></iframe>`. Vercel adds no frame block by default; check it when it is live.

**NOT checked:** everything in a browser; screen readers; real 320 px; Vercel. I do not claim it works on Vercel.
**Changed files:** `site/index.html`, `site/demo.css`, `site/demo.js`, new `site/vercel.json`, `EXTRACTION-PLAN.md`.
