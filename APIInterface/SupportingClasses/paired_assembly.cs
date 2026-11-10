using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace APIInterface.SupportingClasses
{
    public class paired_assembly
    {
        [JsonProperty("accession")]
        public string accession { get; set; }

        [JsonProperty("status")]
        public string status { get; set; }

        [JsonProperty("only_genbank")]
        public string only_genbank { get; set; }

        [JsonProperty("refseq_genbank_are_different")]
        public bool refseq_genbank_are_different { get; set; }

        [JsonProperty("differences")]
        public string differences {  get; set; }
          
    }
}
