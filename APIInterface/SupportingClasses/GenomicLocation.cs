using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GenomicLocation
    {
        [JsonProperty("genomic_accession_version")]
        public string GenomicAccessionVersion { get; set; }

        [JsonProperty("sequence_name")]
        public string SequenceName { get; set; }

        [JsonProperty("genomic_range")]
        public Range GenomicRange { get; set; }

        [JsonProperty("exons")]
        public List<Range> Exons { get; set; }
    }
}
