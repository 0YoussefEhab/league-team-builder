namespace LeagueTeamBuilder.Models
{
    // Step 12h2: the League Discord auto-send switches. ONE row (Id = 1), created the first time it is saved; no row = both off.
    // GameResult: send the Game result picture when a custom game is finished. ClashProgress: send the Clash progress picture
    // when a Clash game is finished. The picture is drawn and sent by the browser of the person who finishes the game.
    public class LeagueAutoSendSettings
    {
        public const int RowId = 1;

        public int Id { get; set; } = RowId;
        public bool GameResult { get; set; }
        public bool ClashProgress { get; set; }
        public DateTime UpdatedUtc { get; set; }
        // Plain text id of the user who last saved it (no foreign key: a deleted user must never block this row).
        public string? UpdatedByUserId { get; set; }
    }
}
