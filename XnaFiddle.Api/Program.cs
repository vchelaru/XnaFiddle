using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using XnaFiddle.Api.Data;
using XnaFiddle.Api.Slugs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<FiddleDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("FiddleDb"))
    .UseExceptionProcessor());

builder.Services.AddSingleton<ISlugGenerator, SlugGenerator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
