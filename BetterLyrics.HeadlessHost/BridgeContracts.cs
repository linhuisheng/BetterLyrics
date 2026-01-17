namespace BetterLyrics.HeadlessHost
{
    public sealed class PlaybackSnapshot
    {
        public string? PlayerId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public bool IsPlaying { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }

        public string? LyricsText { get; set; }
        public string? LyricsRaw { get; set; }
        public string? Translation { get; set; }
        public string? Transliteration { get; set; }

        public int? CurrentLineIndex { get; set; }
        public string? CurrentLineText { get; set; }
        public double? CurrentLineStartMs { get; set; }
        public double? CurrentLineEndMs { get; set; }
    }

    public sealed class LyricsSearchSnapshot
    {
        public bool Found { get; set; }
        public string? Provider { get; set; }
        public int MatchPercentage { get; set; }

        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public double? DurationSeconds { get; set; }

        public string? Raw { get; set; }
        public string? Translation { get; set; }
        public string? Transliteration { get; set; }
        public string? Error { get; set; }
    }
}
