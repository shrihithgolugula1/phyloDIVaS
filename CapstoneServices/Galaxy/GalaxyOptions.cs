namespace CapstoneServices.Galaxy
{
    public sealed class GalaxyOptions
    {
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? DnDsToolId { get; set; }

        // Optional overrides for pipeline steps
        public string? MergeFastaToolId { get; set; }
        public string? TranslateToolId { get; set; }
        public string? MafftToolId { get; set; }
        public string? Pal2NalToolId { get; set; }
        public string? MuscleToolId { get; set; }
        public string? AlignBackTransToolId { get; set; }
        public string? UnzipToolId { get; set; }
    }
}
