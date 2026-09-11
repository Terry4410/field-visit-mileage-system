using System.Data;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class VisitTypeMembershipCoordinator(AppDbContext db) : IVisitTypeMembershipCoordinator
{
    private const string Resource = "FieldVisit.VisitTypeOrder";

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var resourceParameter = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Resource };
            var resultParameter = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
            await db.Database.ExecuteSqlRawAsync(
                "EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;",
                new object[] { resultParameter, resourceParameter },
                ct);
            var lockResult = Convert.ToInt32(resultParameter.Value);
            if (lockResult < 0)
                throw new InvalidOperationException("VISITTYPE_ORDER_LOCK_FAILED：無法取得拜訪形式排序鎖。");

            // Current locking read is intentional under both RCSI and explicit SNAPSHOT.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT VisitTypeId FROM dbo.VisitTypes WITH (UPDLOCK,HOLDLOCK) WHERE IsActive=1 ORDER BY VisitTypeId;",
                ct);

            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
