using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FieldVisit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration)
    {
        var cs=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection 尚未設定。");
        services.AddDbContext<AppDbContext>(o=>o.UseSqlServer(cs,sql=>sql.EnableRetryOnFailure(5,TimeSpan.FromSeconds(10),null)));
        services.AddScoped<IUserRepository,UserRepository>();services.AddScoped<ITripRepository,TripRepository>();services.AddScoped<IMasterRepository,MasterRepository>();services.AddScoped<IMileageRepository,MileageRepository>();services.AddScoped<IWorkflowRepository,WorkflowRepository>();services.AddScoped<ITripSnapshotRepository,TripSnapshotRepository>();services.AddScoped<IUnitOfWork>(sp=>sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IV160FinalRepository,V160FinalRepository>();services.AddScoped<IV170AccessControl,V170AccessControl>();services.AddScoped<IV170PeopleAdminRepository,V170PeopleAdminRepository>();services.AddScoped<IReportDocumentService,ReportDocumentService>();services.AddScoped<IWorkbookImportService,WorkbookImportService>();services.AddScoped<IBackgroundJobService,BackgroundJobService>();
        var route=(configuration["Providers:Route"]??"Mock").Trim();var geo=(configuration["Providers:Geocoding"]??"Mock").Trim();
        if(!route.Equals("Mock",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("v1.6.0 僅允許 Providers:Route=Mock；Google Routes 請於 v1.7.0 啟用。");
        if(!geo.Equals("Mock",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("v1.6.0 僅允許 Providers:Geocoding=Mock。");
        services.AddScoped<IRouteCalculationService,MockRouteCalculationService>();services.AddScoped<IGeocodingService,MockGeocodingService>();
        return services;
    }
}
