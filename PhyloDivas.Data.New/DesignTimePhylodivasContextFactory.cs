using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PhyloDivas.Data.New; 
using System.IO;
using PhyloDivas.Data.New.Data;

namespace CapstoneDesign
{
    public class DesignTimePhylodivasContextFactory : IDesignTimeDbContextFactory<PhylodivasContext>
    {
        public PhylodivasContext CreateDbContext(string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
.AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: false) //reads the user secrets that are associated with CapstoneDesign.csproj
                .AddEnvironmentVariables()
                .Build();

            var conn = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("User Secrets missing ConnectionStrings:DefaultConnection");

            var opts = new DbContextOptionsBuilder<PhylodivasContext>()
                .UseNpgsql(conn)
                .Options;
            

            return new PhylodivasContext(opts);
        }
    }
}
