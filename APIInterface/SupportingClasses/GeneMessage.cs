using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace APIInterface.SupportingClasses
{
    public class GeneMessage
    {
        [JsonProperty("error")]
        public GeneError Error { get; set; }

        [JsonProperty("warning")]
        public SupportingClasses.GeneWarning Warning { get; set; }
    }
}
