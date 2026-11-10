using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;


namespace PhyloDivas.Data.New.Entities
{
	[Table("feedback_form")]
	public partial class feedbackForm
	{
		[Key]
		public long feedback_id { get; set; }

		[StringLength(255)]
		public string? contact_email { get; set; }

		[Column(TypeName = "timestamp without time zone")]
		public DateTime date_submitted { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

		[StringLength(50)]
		public string? usage_frequency { get; set; }

		[Range(1, 5)]
		public int? overall_experience_rating { get; set; }

		public bool? found_what_you_were_looking_for { get; set; }

		public bool? navigation_ease { get; set; }

		[Range(1, 5)]
		public int? performance_satisfaction { get; set; }

		[Range(1, 5)]
		public int? recommend_likelihood { get; set; }

		public string? liked_most { get; set; }

		public string? improvements_suggestions { get; set; }

		public string? issues_bugs { get; set; }

		public string? other_comments { get; set; }
	}
}