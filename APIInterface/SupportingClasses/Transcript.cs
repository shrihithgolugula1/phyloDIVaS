using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Transcript
    {
        [JsonProperty("accession_version")]
        public string AccessionVersion { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("length")]
        public int Length { get; set; }

        [JsonProperty("cds")]
        public CDS Cds { get; set; }

        [JsonProperty("genomic_locations")]
        public List<GenomicLocation> GenomicLocations { get; set; }

        [JsonProperty("ensembl_transcript")]
        public string EnsemblTranscript { get; set; }

        [JsonProperty("protein")]
        public Protein Protein { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("type")]
        public ENUMS.TranscriptType Type { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty("select_category")]
        public ENUMS.SelectCategory SelectCategory { get; set; }
    }
}
