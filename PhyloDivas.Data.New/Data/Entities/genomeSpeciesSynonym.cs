using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[PrimaryKey("tax_id", "synonym")]
[Table("genome_species_synonym")]
public partial class genomeSpeciesSynonym
{
    [Key]
    public int tax_id { get; set; }

    [Key]
    [StringLength(255)]
    public string synonym { get; set; } = null!;

    [ForeignKey("tax_id")]
    [InverseProperty("genome_species_synonyms")]
    public virtual genomeSpecies tax { get; set; } = null!;
}
