var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient("CapstoneDesignAPI", c =>
{
    // 👇 Change this port to match the one your CapstoneDesign API runs on
    c.BaseAddress = new Uri("http://localhost:5149/");
});

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

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
