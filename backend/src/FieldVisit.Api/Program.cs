using System.Text;
using FieldVisit.Api;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<LeaderService>();
builder.Services.AddScoped<MasterService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddInfrastructure(builder.Configuration);

var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var signingKey = (auth.JwtKey ?? "").PadRight(32, '_');
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("Frontend", p =>
{
    if (origins.Length > 0) p.WithOrigins(origins.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Field Visit Mileage API - Existing Schema 1.5.0", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = ex switch
    {
        UnauthorizedAccessException => 403,
        KeyNotFoundException => 404,
        DbUpdateConcurrencyException => 409,
        InvalidOperationException when ex.Message.Contains("ROWVERSION_CONFLICT", StringComparison.OrdinalIgnoreCase) => 409,
        InvalidOperationException => 422,
        _ => 500
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new { title = status == 500 ? "系統錯誤" : "無法完成操作", status, detail = status == 500 ? "系統發生未預期錯誤。" : ex?.Message });
}));

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", schema = "1.5.0", utc = DateTime.UtcNow }));
app.MapGet("/health/db", async (AppDbContext db) => Results.Ok(new { status = await db.Database.CanConnectAsync() ? "ok" : "unavailable", database = db.Database.GetDbConnection().Database, utc = DateTime.UtcNow }));
app.Run();
