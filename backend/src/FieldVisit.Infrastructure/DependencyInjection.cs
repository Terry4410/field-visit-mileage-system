using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FieldVisit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration)
    {
        var cs=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection 尚未設定。");
        services.AddDbContext<AppDbContext>(o=>
        {
            o.UseSqlServer(cs,sql=>sql.EnableRetryOnFailure(5,TimeSpan.FromSeconds(10),null));
            o.ReplaceService<IModelCustomizer,NotificationModelCustomizer>();
        });
        services.AddDbContext<V180TeamLocationNoteDbContext>(o=>o.UseSqlServer(cs,sql=>sql.EnableRetryOnFailure(5,TimeSpan.FromSeconds(10),null)));
        services.AddScoped<IUserRepository,UserRepository>();services.AddScoped<ITripRepository,TripRepository>();services.AddScoped<IMasterRepository,MasterRepository>();services.AddScoped<MileageRepository>();services.AddScoped<IMileageRepository,DbWork2MileageRepositoryDecorator>();services.AddScoped<IFinalizedMileageRateImpactReader,FinalizedMileageRateImpactReader>();services.AddScoped<ITransactionBoundary,EfTransactionBoundary>();services.AddScoped<IWorkflowRepository,WorkflowRepository>();services.AddScoped<ITripSnapshotRepository,TripSnapshotRepository>();services.AddScoped<IUnitOfWork>(sp=>sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IVisitTypeMembershipCoordinator,VisitTypeMembershipCoordinator>();
        services.AddScoped<IV160FinalRepository,V160FinalRepository>();services.AddScoped<IV180ManagedLocationGovernanceRepository,V180ManagedLocationGovernanceRepository>();services.AddScoped<IV170AccessControl,V170AccessControl>();services.AddScoped<IV170LocationRepository,V170LocationRepository>();services.AddScoped<IV170ProjectLocationAdminRepository,V170ProjectLocationAdminRepository>();services.AddScoped<IV170PeopleAdminRepository,V170PeopleAdminRepository>();services.AddScoped<IV170PeopleAdminWriter,V170PeopleAdminWriter>();services.AddScoped<IV170PeopleBulkWorkbookService,V170PeopleBulkWorkbookService>();services.AddScoped<IV180OrganizationPeopleReader,V180OrganizationPeopleReader>();services.AddScoped<IV180OrganizationPeopleWriter,V180OrganizationPeopleWriter>();services.AddScoped<IV180TeamCenterLifecycleWriter,V180TeamCenterLifecycleWriter>();services.AddScoped<IV180TeamCenterAdminReader,V180TeamCenterAdminReader>();services.AddScoped<IV180DeploymentSiteReader,V180DeploymentSiteReader>();services.AddScoped<IV180DeploymentSiteWriter,V180DeploymentSiteWriter>();services.AddScoped<IV180TripContextReader,V180TripContextReader>();services.AddScoped<IV180TeamLocationNoteService,V180TeamLocationNoteService>();services.AddScoped<IReportDocumentService,ReportDocumentService>();services.AddScoped<IWorkbookImportService,WorkbookImportService>();services.AddScoped<IBackgroundJobService,BackgroundJobService>();
        services.AddSingleton(new NotificationRuntimeEnvironment((configuration["Notifications:EnvironmentCode"]??"").Trim()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<INotificationRecipientResolver,EfNotificationRecipientResolver>();
        services.AddScoped<INotificationOutboxWriter,EfNotificationOutboxWriter>();
        services.AddScoped<INotificationCollisionTranslator,EfNotificationCollisionTranslator>();
        services.AddScoped<ILocationNotificationEvents,EfLocationNotificationEvents>();
        services.AddScoped<ILocationMutationBoundary,EfLocationMutationBoundary>();
        var route=(configuration["Providers:Route"]??"Mock").Trim();var geo=(configuration["Providers:Geocoding"]??"Mock").Trim();
        if(!route.Equals("Mock",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("v1.6.0 僅允許 Providers:Route=Mock；Google Routes 請於 v1.7.0 啟用。");
        if(!geo.Equals("Mock",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("v1.6.0 僅允許 Providers:Geocoding=Mock。");
        services.AddScoped<IRouteCalculationService,MockRouteCalculationService>();services.AddScoped<IGeocodingService,MockGeocodingService>();
        return services;
    }
}
