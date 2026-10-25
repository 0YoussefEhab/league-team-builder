using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeagueTeamBuilder.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ActorLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeactivated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Look = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueAutoSendSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GameResult = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClashProgress = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueAutoSendSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueChampions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueChampions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueClashEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueClashEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueCustomTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueCustomTiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueGuestLinkLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LinkedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkedByLabel = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    UserLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    GuestName = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    UndoneUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UndoneByLabel = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueGuestLinkLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeagueModeSettings",
                columns: table => new
                {
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MapImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    MapAspect = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueModeSettings", x => x.Mode);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Link = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueClashTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueClashTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueClashTeams_LeagueClashEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "LeagueClashEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    IsGuest = table.Column<bool>(type: "INTEGER", nullable: false),
                    InGameName = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarSource = table.Column<int>(type: "INTEGER", nullable: false),
                    CurTier = table.Column<int>(type: "INTEGER", nullable: false),
                    CurDiv = table.Column<int>(type: "INTEGER", nullable: true),
                    PeakTier = table.Column<int>(type: "INTEGER", nullable: false),
                    PeakDiv = table.Column<int>(type: "INTEGER", nullable: true),
                    MainLane = table.Column<int>(type: "INTEGER", nullable: false),
                    DoBetterWithId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomTierId = table.Column<int>(type: "INTEGER", nullable: true),
                    RiotId = table.Column<string>(type: "TEXT", nullable: true),
                    RiotPuuid = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueProfiles_LeagueCustomTiers_CustomTierId",
                        column: x => x.CustomTierId,
                        principalTable: "LeagueCustomTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueProfiles_LeagueProfiles_DoBetterWithId",
                        column: x => x.DoBetterWithId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMapSpots",
                columns: table => new
                {
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Team = table.Column<int>(type: "INTEGER", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMapSpots", x => new { x.Mode, x.Team, x.Slot });
                    table.ForeignKey(
                        name: "FK_LeagueMapSpots_LeagueModeSettings_Mode",
                        column: x => x.Mode,
                        principalTable: "LeagueModeSettings",
                        principalColumn: "Mode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueClashResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RespondedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueClashResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueClashResponses_LeagueClashEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "LeagueClashEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueClashResponses_LeagueProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ModeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ModeName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WinnerTeamIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    ClashEventId = table.Column<int>(type: "INTEGER", nullable: true),
                    GameNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ClashTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    BuilderLog = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    TeamKills0 = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamKills1 = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RiotMatchId = table.Column<string>(type: "TEXT", nullable: true),
                    MvpProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamMvpProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    StatsMvpProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    StatsTeamMvpProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    MvpIsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    StatsEditedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    StatsEditedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueClashEvents_ClashEventId",
                        column: x => x.ClashEventId,
                        principalTable: "LeagueClashEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueClashTeams_ClashTeamId",
                        column: x => x.ClashTeamId,
                        principalTable: "LeagueClashTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueProfiles_MvpProfileId",
                        column: x => x.MvpProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueProfiles_StatsMvpProfileId",
                        column: x => x.StatsMvpProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueProfiles_StatsTeamMvpProfileId",
                        column: x => x.StatsTeamMvpProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LeagueMatches_LeagueProfiles_TeamMvpProfileId",
                        column: x => x.TeamMvpProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LeagueProfileChamps",
                columns: table => new
                {
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Lane = table.Column<int>(type: "INTEGER", nullable: false),
                    ChampionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueProfileChamps", x => new { x.ProfileId, x.Lane, x.ChampionId });
                    table.ForeignKey(
                        name: "FK_LeagueProfileChamps_LeagueChampions_ChampionId",
                        column: x => x.ChampionId,
                        principalTable: "LeagueChampions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueProfileChamps_LeagueProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueProfileLanes",
                columns: table => new
                {
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Lane = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueProfileLanes", x => new { x.ProfileId, x.Lane });
                    table.ForeignKey(
                        name: "FK_LeagueProfileLanes_LeagueProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMatchTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMatchTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueMatchTeams_LeagueMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "LeagueMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMatchPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Lane = table.Column<int>(type: "INTEGER", nullable: true),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    NameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    WasGuest = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurRankSnapshot = table.Column<int>(type: "INTEGER", nullable: true),
                    CurDivSnapshot = table.Column<int>(type: "INTEGER", nullable: true),
                    PeakRankSnapshot = table.Column<int>(type: "INTEGER", nullable: true),
                    PeakDivSnapshot = table.Column<int>(type: "INTEGER", nullable: true),
                    Kills = table.Column<int>(type: "INTEGER", nullable: true),
                    Deaths = table.Column<int>(type: "INTEGER", nullable: true),
                    Assists = table.Column<int>(type: "INTEGER", nullable: true),
                    ChampionId = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayedLane = table.Column<int>(type: "INTEGER", nullable: true),
                    Cs = table.Column<int>(type: "INTEGER", nullable: true),
                    DamageDealt = table.Column<int>(type: "INTEGER", nullable: true),
                    DamageTaken = table.Column<int>(type: "INTEGER", nullable: true),
                    Gold = table.Column<int>(type: "INTEGER", nullable: true),
                    VisionScore = table.Column<int>(type: "INTEGER", nullable: true),
                    ObjectiveDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMatchPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueMatchPlayers_LeagueChampions_ChampionId",
                        column: x => x.ChampionId,
                        principalTable: "LeagueChampions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeagueMatchPlayers_LeagueMatchTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "LeagueMatchTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueMatchPlayers_LeagueProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "LeagueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_CreatedUtc",
                table: "ActivityLogEntries",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClashEvents_StartsAtUtc",
                table: "LeagueClashEvents",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClashResponses_EventId_ProfileId",
                table: "LeagueClashResponses",
                columns: new[] { "EventId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClashResponses_ProfileId",
                table: "LeagueClashResponses",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClashTeams_EventId_Name",
                table: "LeagueClashTeams",
                columns: new[] { "EventId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_ClashEventId_GameNumber",
                table: "LeagueMatches",
                columns: new[] { "ClashEventId", "GameNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_ClashTeamId",
                table: "LeagueMatches",
                column: "ClashTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_MvpProfileId",
                table: "LeagueMatches",
                column: "MvpProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_PlayedUtc",
                table: "LeagueMatches",
                column: "PlayedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_StatsMvpProfileId",
                table: "LeagueMatches",
                column: "StatsMvpProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_StatsTeamMvpProfileId",
                table: "LeagueMatches",
                column: "StatsTeamMvpProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatches_TeamMvpProfileId",
                table: "LeagueMatches",
                column: "TeamMvpProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatchPlayers_ChampionId",
                table: "LeagueMatchPlayers",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatchPlayers_ProfileId",
                table: "LeagueMatchPlayers",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatchPlayers_TeamId",
                table: "LeagueMatchPlayers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMatchTeams_MatchId_Index",
                table: "LeagueMatchTeams",
                columns: new[] { "MatchId", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueProfileChamps_ChampionId",
                table: "LeagueProfileChamps",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueProfiles_CustomTierId",
                table: "LeagueProfiles",
                column: "CustomTierId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueProfiles_DoBetterWithId",
                table: "LeagueProfiles",
                column: "DoBetterWithId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueProfiles_UserId",
                table: "LeagueProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_ReadUtc",
                table: "UserNotifications",
                columns: new[] { "UserId", "ReadUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogEntries");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "LeagueAutoSendSettings");

            migrationBuilder.DropTable(
                name: "LeagueClashResponses");

            migrationBuilder.DropTable(
                name: "LeagueGuestLinkLogs");

            migrationBuilder.DropTable(
                name: "LeagueMapSpots");

            migrationBuilder.DropTable(
                name: "LeagueMatchPlayers");

            migrationBuilder.DropTable(
                name: "LeagueProfileChamps");

            migrationBuilder.DropTable(
                name: "LeagueProfileLanes");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "LeagueModeSettings");

            migrationBuilder.DropTable(
                name: "LeagueMatchTeams");

            migrationBuilder.DropTable(
                name: "LeagueChampions");

            migrationBuilder.DropTable(
                name: "LeagueMatches");

            migrationBuilder.DropTable(
                name: "LeagueClashTeams");

            migrationBuilder.DropTable(
                name: "LeagueProfiles");

            migrationBuilder.DropTable(
                name: "LeagueClashEvents");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "LeagueCustomTiers");
        }
    }
}
