using CapstoneServices.Galaxy;
using CapstoneServices.NCBI;
using AnalysisAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Force Kestrel to listen on a stable port for the frontend
builder.WebHost.UseUrls("http://127.0.0.1:7040");

// Add services to the container
builder.Services.AddControllers();

// Configure CORS to allow requests from the React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => {policy
            .WithOrigins("https://phylodivas.org",
                "https://phylodivas.com",
                "http://localhost:3000")// For local development)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();}); // Enable credentials for SSE
});

// Bind Galaxy and NCBI options from configuration and register typed HttpClients
builder.Services.Configure<GalaxyOptions>(builder.Configuration.GetSection("Galaxy"));
builder.Services.Configure<NcbiOptions>(builder.Configuration.GetSection("Ncbi"));
builder.Services.AddHttpClient<GalaxyClient>(client =>
    client.Timeout = TimeSpan.FromMinutes(15));
builder.Services.AddHttpClient<NcbiClient>();
builder.Services.AddSingleton<GalaxyBustedPipeline>();
builder.Services.AddSingleton<GalaxyOrthoFinderPipeline>();
builder.Services.AddSingleton<GalaxyOptions>();


// Add progress session manager for SSE
builder.Services.AddSingleton<ProgressSessionManager>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors("AllowReactApp");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
