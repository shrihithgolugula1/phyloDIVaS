using Amazon.CognitoIdentityProvider;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Mvc;
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

//Load AWS options from appsettings
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

// User-pool authentication uses the Cognito app client and does not require IAM credentials.
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ =>
{
    var region = builder.Configuration["AWS:Region"] ?? "us-east-2";
    return new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(region));
});

//allow frontend origin(react dev server)
builder.Services.AddCors(o => o.AddPolicy("AllowReactApp", p => p
    //change port as needed  
    .WithOrigins("http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReactApp");
app.MapControllers();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

