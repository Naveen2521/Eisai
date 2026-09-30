using Eisai.Api.Common;
using Eisai.Api.Middleware;
using Eisai.Application.Common;
using Eisai.Application.DependencyInjection;
using Eisai.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Eisai.Api")
        .WriteTo.Console();
});

builder.Services.AddControllersWithViews().AddJsonOptions(options =>

{

    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;

    options.JsonSerializerOptions.PropertyNamingPolicy = null;
});

// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => ToCamelCase(entry.Key),
                    entry => entry.Value!.Errors.Select(error => error.ErrorMessage).Distinct().ToArray());

            return new ObjectResult(ApiResponse<object>.From(
                ServiceResult.Fail(AppStatus.BadRequest, AppStatus.GetDefaultMessage(AppStatus.BadRequest), errors)))
            {
                StatusCode = AppStatus.BadRequest
            };
        };
    });

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("EisaiWeb", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Eisai API");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors("EisaiWeb");

app.UseAuthorization();

app.MapControllers();

app.Run();

static string ToCamelCase(string name)
{
    if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
    {
        return name;
    }

    return char.ToLowerInvariant(name[0]) + name[1..];
}