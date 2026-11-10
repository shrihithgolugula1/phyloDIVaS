using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class Protein
    {
        [JsonProperty("accession_version")]
        public string AccessionVersion { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("length")]
        public int Length { get; set; }

        [JsonProperty("isoform_name")]
        public string IsoformName { get; set; }

        [JsonProperty("ensembl_protein")]
        public string EnsemblProtein { get; set; }

        [JsonProperty("mature_peptides")]
        public List<MaturePeptide> MaturePeptides { get; set; }
    }
}
