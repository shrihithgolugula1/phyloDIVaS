using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PhyloDivas.Data.New.Entities;

[Table("api_error_log")]
[Index("date_time", Name = "idx_api_error_when", AllDescending = true)]
public partial class apiErrorLog
{
    [Key]
    public long id { get; set; }

    [StringLength(200)]
    public string api_call { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string? request_obj { get; set; }

    [Column(TypeName = "jsonb")]
    public string? response_obj { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime date_time { get; set; }
}
