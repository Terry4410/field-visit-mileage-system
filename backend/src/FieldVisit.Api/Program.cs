using System.Text;
using FieldVisit.Api;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService,CurrentUserService>();
builder.Services.AddScoped<ITokenService,TokenService>();
builder.Services.AddScoped<AuthService>();builder.Services.AddScoped<TripService>();builder.Services.AddScoped<LeaderService>();builder.Services.AddScoped<MasterService>();builder.Services.AddScoped<V160FinalService>();builder.Services.AddScoped<V170PeopleAdminService>();builder.Services.AddScoped<V170LocationService>();builder.Services.AddScoped<V170ProjectLocationAdminService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IBackgroundJobSignal,BackgroundJobSignal>();
builder.Services.AddHostedService<BackgroundJobHostedService>();

var auth=builder.Configuration.GetSection("Auth").Get<AuthOptions>()??new AuthOptions();
var authMode=V170AuthenticationRules.NormalizeMode(auth.Mode);
var signingKey=(auth.JwtKey??"").PadRight(32,'_');

var authentication=builder.Services
    .AddAuthentication(options=>
    {
        options.DefaultAuthenticateScheme=AuthSchemes.AppJwt;
        options.DefaultChallengeScheme=AuthSchemes.AppJwt;
    })
    .AddJwtBearer(
        AuthSchemes.AppJwt,
        o=>
        {
            o.TokenValidationParameters=
                new TokenValidationParameters
                {
                    ValidateIssuer=true,
                    ValidateAudience=true,
                    ValidateLifetime=true,
                    ValidateIssuerSigningKey=true,
                    ValidIssuer=auth.Issuer,
                    ValidAudience=auth.Audience,
                    IssuerSigningKey=
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew=TimeSpan.FromMinutes(2)
                };
        });

if(authMode==AuthenticationModes.Entra)
{
    if(!Guid.TryParse(auth.Entra.TenantId,out var entraTenantId))
        throw new InvalidOperationException(
            "Auth__Entra__TenantId 必須是有效的 Microsoft Entra Tenant GUID。");

    if(string.IsNullOrWhiteSpace(auth.Entra.Audience))
        throw new InvalidOperationException(
            "Auth__Entra__Audience 尚未設定。");

    var instance=
        (auth.Entra.Instance
         ?? "https://login.microsoftonline.com")
        .TrimEnd('/');

    authentication.AddJwtBearer(
        AuthSchemes.Entra,
        o=>
        {
            o.Authority=
                $"{instance}/{entraTenantId:D}/v2.0";

            o.Audience=
                auth.Entra.Audience;

            o.MapInboundClaims=
                false;

            o.TokenValidationParameters=
                new TokenValidationParameters
                {
                    ValidateIssuer=true,
                    ValidateAudience=true,
                    ValidateLifetime=true,
                    ValidateIssuerSigningKey=true,
                    ClockSkew=TimeSpan.FromMinutes(2)
                };
        });
}

builder.Services.AddAuthorization();
var origins=builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()??[];builder.Services.AddCors(o=>o.AddPolicy("Frontend",p=>{var clean=origins.Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();if(clean.Length>0)p.WithOrigins(clean).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition");}));
builder.Services.AddControllers();builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(c=>{c.SwaggerDoc("v1",new OpenApiInfo{Title="Field Visit Mileage API",Version="v1.7.0-uat-candidate"});c.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT",In=ParameterLocation.Header});c.AddSecurityRequirement(new OpenApiSecurityRequirement{[new OpenApiSecurityScheme{Reference=new OpenApiReference{Type=ReferenceType.SecurityScheme,Id="Bearer"}}]=Array.Empty<string>()});});

var app=builder.Build();
app.UseExceptionHandler(handler=>handler.Run(async context=>{var ex=context.Features.Get<IExceptionHandlerFeature>()?.Error;var status=ex switch{UnauthorizedAccessException=>403,KeyNotFoundException=>404,DbUpdateConcurrencyException=>409,InvalidOperationException when ex.Message.Contains("ROWVERSION_CONFLICT",StringComparison.OrdinalIgnoreCase)=>409,InvalidOperationException=>422,_=>500};context.Response.StatusCode=status;context.Response.ContentType="application/problem+json";await context.Response.WriteAsJsonAsync(new{title=status==500?"系統錯誤":"無法完成操作",status,detail=status==500?"系統發生未預期錯誤，請記錄時間後聯絡系統管理者。":ex?.Message,traceId=context.TraceIdentifier});}));
var swaggerEnabled=builder.Configuration.GetValue<bool?>("Swagger:Enabled")??!app.Environment.IsProduction();if(swaggerEnabled){app.UseSwagger();app.UseSwaggerUI();}
app.UseHttpsRedirection();app.UseCors("Frontend");app.UseAuthentication();app.UseAuthorization();app.MapControllers();
var version=builder.Configuration["App:Version"]??"1.7.0-uat-candidate";var schema=builder.Configuration["App:DbSchemaVersion"]??"1.7.0-003";
app.MapGet("/health",()=>Results.Ok(new{status="ok",version,schema,utc=DateTime.UtcNow}));
app.MapGet("/health/live",()=>Results.Ok(new{status="ok",version,utc=DateTime.UtcNow}));
app.MapGet("/health/ready",async(AppDbContext db)=>await db.Database.CanConnectAsync()?Results.Ok(new{status="ready",version,schema,utc=DateTime.UtcNow}):Results.Json(new{status="unavailable",version,utc=DateTime.UtcNow},statusCode:503));
app.Run();
