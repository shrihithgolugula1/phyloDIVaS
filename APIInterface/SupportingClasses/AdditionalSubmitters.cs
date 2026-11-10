using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class AdditionalSubmitters
    {

        [JsonProperty("genbank_accession")]
        public string? genbank_accession { get; set; }

        //refseq_accession: string
        [JsonProperty("refseq_accession")]
        public string? refseq_accession { get; set; }

        //chr_name: string
        [JsonProperty("chr_name")]
        public string? chr_name { get; set; }

        //molecule_type: string
        [JsonProperty("molecule_type")]
        public string? molecule_type { get; set; }

        //submitter: string
        [JsonProperty("submitter")]
        public string? submitter { get; set; }

        //bioproject_accession: string
        [JsonProperty("bioproject_accession")]
        public string? bioproject_accession { get; set; }
    }
}
