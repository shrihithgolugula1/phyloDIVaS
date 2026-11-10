using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[PrimaryKey("gene_id", "source", "identifier")]
[Table("gene_identifier_xref")]
public partial class geneIdentifierXref
{
    [Key]
    public int gene_id { get; set; }

    [Key]
    [StringLength(32)]
    public string source { get; set; } = null!;

    [Key]
    [StringLength(64)]
    public string identifier { get; set; } = null!;

    [ForeignKey("gene_id")]
    [InverseProperty("gene_identifier_xrefs")]
    public virtual gene gene { get; set; } = null!;
}
