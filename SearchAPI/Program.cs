using CapstoneServices;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;
using Microsoft.EntityFrameworkCore;
using PhyloDivas.Data.New.Data;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost3000",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddScoped<DataCheckService>();
var mvcBuilder = builder.Services.AddControllers();
builder.Services.AddScoped<IGenericRepository<gene>, GenericRepository<gene>>();
builder.Services.AddScoped<IGenericRepository<gene>, GenericRepository<gene>>();
builder.Services.AddScoped<IGenericRepository<genomeSpecies>, GenericRepository<genomeSpecies>>();
builder.Services.AddScoped<IGenericRepository<genomeAssembly>, GenericRepository<genomeAssembly>>();
builder.Services.AddScoped<IGenericRepository<genomeAssemblyStat>, GenericRepository<genomeAssemblyStat>>();
builder.Services.AddScoped<IGenericRepository<genomeChromosome>, GenericRepository<genomeChromosome>>();
builder.Services.AddScoped<IGenericRepository<taxon>, GenericRepository<taxon>>();


builder.Services.AddDbContext<PhylodivasContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});


var app = builder.Build();



app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("AllowLocalhost3000");
app.UseAuthorization();

app.MapControllers();

app.Run();


