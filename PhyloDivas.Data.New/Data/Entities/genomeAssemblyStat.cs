using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

public partial class genomeAssemblyStat
{
    [Key]
    public string assembly_id { get; set; }

    public int? total_chromosomes { get; set; }

    public long? total_sequence_length { get; set; }

    public long? total_ungapped_length { get; set; }

    public int? num_contigs { get; set; }

    public long? contig_n50 { get; set; }

    public int? contig_l50 { get; set; }

    public int? num_scaffolds { get; set; }

    public long? scaffold_n50 { get; set; }

    public int? scaffold_l50 { get; set; }

    public int? gaps_between_scaffolds { get; set; }

    [Precision(5, 2)]
    public decimal? gc_percent { get; set; }

    public int? num_organelles { get; set; }

    [ForeignKey("assembly_id")]
    [InverseProperty("genome_assembly_stat")]
    public virtual genomeAssembly assembly { get; set; } = null!;
}
