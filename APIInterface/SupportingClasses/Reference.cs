using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Reference
    {
        [JsonProperty("pmids")]
        public List<ulong> Pmids { get; set; }
    }
}
