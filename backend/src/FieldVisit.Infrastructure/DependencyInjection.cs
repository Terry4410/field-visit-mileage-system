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
        services.AddScoped<IUserRepository,UserRepository>();services.AddScoped<ITripRepository,TripRepository>();services.AddScoped<IMasterRepository,MasterRepository>();services.AddScoped<IMileageRepository,MileageRepository>();services.AddScoped<IWorkflowRepository,WorkflowRepository>();services.AddScoped<ITripSnapshotRepository,TripSnapshotRepository>();services.AddScoped<IUnitOfWork>(sp=>sp.GetRequiredService<AppDbContext>());services.AddScoped<IV180TripContextReader,V180TripContextReader>();services.AddScoped<IV180GoogleMileageGovernanceRepository,V180GoogleMileageGovernanceRepository>();
        services.AddScoped<IV160FinalRepository,V160FinalRepository>();services.AddScoped<IV170AccessControl,V170AccessControl>();services.AddScoped<IV170LocationRepository,V170LocationRepository>();services.AddScoped<IV170ProjectLocationAdminRepository,V170ProjectLocationAdminRepository>();services.AddScoped<IV170PeopleAdminRepository,V170PeopleAdminRepository>();services.AddScoped<IV170PeopleAdminWriter,V170PeopleAdminWriter>();services.AddScoped<IV170PeopleBulkWorkbookService,V170PeopleBulkWorkbookService>();services.AddScoped<IReportDocumentService,ReportDocumentService>();services.AddScoped<IWorkbookImportService,WorkbookImportService>();services.AddScoped<IBackgroundJobService,BackgroundJobService>();
        services.AddScoped<IV180MasterDataAdminRepository,V180MasterDataAdminRepository>();
        services.AddScoped<IV180MasterDataBulkWorkbookService,V180MasterDataBulkWorkbookService>();
        services.AddScoped<V180GoogleMileageOrchestrationService>();
        services.AddHttpClient<GoogleMapsRouteProvider>();
        services.AddHttpClient<GoogleMapsGeocodingProvider>();

        var route=(configuration["Providers:Route"]??"Mock").Trim();
        var geo=(configuration["Providers:Geocoding"]??"Mock").Trim();
        var googleKey=configuration["GoogleMaps:ApiKey"];

        services.AddScoped<IRouteCalculationService,MockRouteCalculationService>();
        services.AddScoped<IGeocodingService,MockGeocodingService>();

        if(route.Equals("Google",StringComparison.OrdinalIgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(googleKey))throw new InvalidOperationException("GoogleMaps__ApiKey 尚未設定。");
            services.AddScoped<IV180RouteProvider>(sp=>sp.GetRequiredService<GoogleMapsRouteProvider>());
        }
        else if(route.Equals("Mock",StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IV180RouteProvider,V180DisabledRouteProvider>();
        else
            throw new InvalidOperationException("Providers__Route 只允許 Mock 或 Google。");

        if(geo.Equals("Google",StringComparison.OrdinalIgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(googleKey))throw new InvalidOperationException("GoogleMaps__ApiKey 尚未設定。");
            services.AddScoped<IV180GeocodingProvider>(sp=>sp.GetRequiredService<GoogleMapsGeocodingProvider>());
        }
        else if(geo.Equals("Mock",StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IV180GeocodingProvider,V180DisabledGeocodingProvider>();
        else
            throw new InvalidOperationException("Providers__Geocoding 只允許 Mock 或 Google。");
        return services;
    }
}
