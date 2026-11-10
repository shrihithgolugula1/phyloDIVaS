using APIInterface.SupportingClasses;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface
{
    public class GenomeReport
    {
        [JsonProperty("accession")]
        public string Accession { get; set; }

        [JsonProperty("current_accession")]
        public string? CurrentAccession { get; set; }

        [JsonProperty("paired_accession")]
        public string? PairedAccession { get; set; }

        [JsonProperty("organism")]
        public Organism? Organism { get; set; }

        [JsonProperty("assembly_info")]
        public AssemblyInfo? AssemblyInfo { get; set; }

        [JsonProperty("assembly_stats")]
        public AssemblyStats? AssemblyStats { get; set; }

        [JsonProperty("additional_submitters")]
        public AdditionalSubmitters? AdditionalSubmitters { get; set; }

        [JsonProperty("organelle_info")]
        public List<OrganelleInfo>? OrganelleInfo { get; set; }

        [JsonProperty("annotation_info")]
        public AnnotationInfo? AnnotationInfo { get; set; }
    }
}
