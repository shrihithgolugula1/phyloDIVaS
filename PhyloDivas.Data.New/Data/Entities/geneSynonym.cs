using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[PrimaryKey("gene_id", "synonym")]
[Table("gene_synonym")]
public partial class geneSynonym
{
    [Key]
    public int gene_id { get; set; }

    [Key]
    [StringLength(255)]
    public string synonym { get; set; } = null!;

    [ForeignKey("gene_id")]
    [InverseProperty("gene_synonyms")]
    public virtual gene gene { get; set; } = null!;
}
