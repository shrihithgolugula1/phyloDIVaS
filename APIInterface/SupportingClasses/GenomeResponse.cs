using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace APIInterface
{
    public class GenomeResponse
    {
        [JsonProperty("reports")]
        public List<GenomeReport>? Reports { get; set; }

        [JsonProperty("content_type")]
        public string? ContentType { get; set; }

        [JsonProperty("total_count")]
        public int? TotalCount { get; set; }

        [JsonProperty("next_page_token")]
        public string? NextPageToken { get; set; }

        [JsonProperty("messages")]
        public List<SupportingClasses.Message>? Messages { get; set; }
    }
}
