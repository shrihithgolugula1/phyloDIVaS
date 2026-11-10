using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("gene_reference_standard")]
public partial class geneReferenceStandard
{
    [Key]
    public long gene_reference_standard_id { get; set; }

    public int gene_id { get; set; }

    [StringLength(32)]
    public string accession_version { get; set; } = null!;

    [StringLength(32)]
    public string? type { get; set; }

    public long? range_start { get; set; }

    public long? range_end { get; set; }

    [StringLength(8)]
    public string? orientation { get; set; }

    [ForeignKey("gene_id")]
    [InverseProperty("gene_reference_standards")]
    public virtual gene gene { get; set; } = null!;
}
