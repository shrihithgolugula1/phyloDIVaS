using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("genome_chromosome")]
[Index("assembly_id", "chromosome_name", Name = "chromosome_assembly_id_chromosome_name_key", IsUnique = true)]
public partial class genomeChromosome
{
    [Key]
    public long chromosome_id { get; set; }

    public string assembly_id { get; set; }

    [StringLength(16)]
    public string chromosome_name { get; set; } = null!;

    [StringLength(32)]
    public string? refseq_accession { get; set; }

    [StringLength(32)]
    public string? genbank_accession { get; set; }

    public long? size_bp { get; set; }

    [Precision(5, 2)]
    public decimal? gc_percent { get; set; }

    public int? unlocalized_count { get; set; }

    [ForeignKey("assembly_id")]
    [InverseProperty("genome_chromosomes")]
    public virtual genomeAssembly assembly { get; set; } = null!;
}
