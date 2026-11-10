using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APIInterface.SupportingClasses
{
    public class GeneOntology
    {
        [JsonProperty("assigned_by")]
        public string AssignedBy { get; set; }

        [JsonProperty("molecular_functions")]
        public List<OntologyTerm> MolecularFunctions { get; set; }

        [JsonProperty("biological_processes")]
        public List<OntologyTerm> BiologicalProcesses { get; set; }

        [JsonProperty("cellular_components")]
        public List<OntologyTerm> CellularComponents { get; set; }
    }
}
