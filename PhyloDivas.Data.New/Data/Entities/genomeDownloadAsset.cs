using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("genome_download_asset")]
[Index("assembly_id", Name = "idx_download_asset_assembly")]
public partial class genomeDownloadAsset
{
    [Key]
    public long download_id { get; set; }

    public string assembly_id { get; set; }

    [StringLength(32)]
    public string file_kind { get; set; } = null!;

    [StringLength(255)]
    public string api_endpoint { get; set; } = null!;

    [StringLength(32)]
    public string accession_for_call { get; set; } = null!;

    [ForeignKey("assembly_id")]
    [InverseProperty("genome_download_assets")]
    public virtual genomeAssembly assembly { get; set; } = null!;
}

