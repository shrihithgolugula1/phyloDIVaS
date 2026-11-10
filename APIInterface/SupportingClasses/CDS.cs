using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class CDS
    {
        [JsonProperty("accession_version")]
        public string AccessionVersion { get; set; }

        [JsonProperty("range")]
        public List<Range> Range { get; set; }
    }
}
