using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FieldVisit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection 尚未設定。");
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IMasterRepository, MasterRepository>();
        services.AddScoped<IMileageRepository, MileageRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IRouteCalculationService, MockRouteCalculationService>();
        services.AddScoped<IGeocodingService, MockGeocodingService>();
        return services;
    }
}
