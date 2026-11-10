using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.Extensions.CognitoAuthentication;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Data;
using PhyloDivas.Data.New.Repositories;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

//read connection string (users-secret/env/appsettings)
var conn = builder.Configuration.GetConnectionString("PhylodivasDb");

// registers EF Core DbContext for runtime
builder.Services.AddDbContext<PhylodivasContext>(options =>
    options.UseNpgsql(conn));

// Add services to the container.
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(sp =>
{
    var config = builder.Configuration.GetSection("AWS");
    var region = RegionEndpoint.GetBySystemName(config["Region"]);
    return new AmazonCognitoIdentityProviderClient(region);
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("AllowReactApp");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
