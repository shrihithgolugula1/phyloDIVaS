using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

public partial class genomeSpecies
{
    [Key]
    public int tax_id { get; set; }

    [StringLength(255)]
    public string organism_name { get; set; } = null!;

    [StringLength(255)]
    public string? common_name { get; set; }

    [InverseProperty("tax")]
    public virtual ICollection<gene> genes { get; set; } = new List<gene>();

    [InverseProperty("tax")]
    public virtual ICollection<genomeAssembly> genome_assemblies { get; set; } = new List<genomeAssembly>();

    [InverseProperty("tax")]
    public virtual ICollection<genomeSpeciesSynonym> genome_species_synonyms { get; set; } = new List<genomeSpeciesSynonym>();

    [InverseProperty("resolved_tax")]
    public virtual ICollection<searchHistory> search_histories { get; set; } = new List<searchHistory>();
}
