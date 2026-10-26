namespace LeagueTeamBuilder.Services
{
    // Trimmed copy of HFECloud's limits: only what the League code uses (league pictures and map images).
    // The sizes are our choice (the HFECloud values were not in hand); change them here.
    public static class MediaUploadLimits
    {
        // League profile picture. The profile form's file input accepts the same four types.
        public static readonly string[] AvatarExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        public const long AvatarMaxBytes = 2 * 1024 * 1024;

        // Rank badges (Phase B). No SVG on purpose: an uploaded SVG can carry script. The picture must also decode (RankBadgeRules).
        public static readonly string[] RankBadgeExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        public const long RankBadgeMaxBytes = 1 * 1024 * 1024;

        // Public images (the map image of a mode). The settings service removes .gif and .jfif from this list itself.
        public static readonly string[] PublicImageExtensions = { ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".gif" };
        public const long PublicImageMaxBytes = 5 * 1024 * 1024;
    }
}
