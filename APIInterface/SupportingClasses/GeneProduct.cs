using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GeneProduct
    {
        [JsonProperty("gene_id")]
        public ulong GeneId { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("tax_id")]
        public ulong TaxId { get; set; }

        [JsonProperty("taxname")]
        public string TaxName { get; set; }

        [JsonProperty("common_name")]
        public string CommonName { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("type")]
        public ENUMS.GeneType Type { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("rna_type")]
        public ENUMS.RnaType RnaType { get; set; }

        [JsonProperty("transcripts")]
        public List<Transcript> Transcripts { get; set; }

        [JsonProperty("transcript_count")]
        public int TranscriptCount { get; set; }

        [JsonProperty("protein_count")]
        public int ProteinCount { get; set; }

        [JsonProperty("transcript_type_counts")]
        public List<TranscriptTypeCount> TranscriptTypeCounts { get; set; }
    }
}
