using FieldVisit.Infrastructure;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Candidate 011 EF metadata only; uses a dummy SQL Server provider
/// without opening a connection or executing any SQL/DDL/migration.
/// </summary>
public sealed class V180B3EfSchemaContractTests
{
    private static AppDbContext ModelOnly() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;TrustServerCertificate=True;")
            .Options);

    [Theory]
    [InlineData("EntityKind",40)]
    [InlineData("EntityId",80)]
    [InlineData("OperationCode",80)]
    [InlineData("RiskCode",20)]
    [InlineData("Status",30)]
    [InlineData("ReviewReason",1000)]
    [InlineData("ExpectedEntityRowVersion",8)]
    public void Request_column_lengths_match_011_design(string column,int maxLength)
    {
        using var db=ModelOnly();
        var entity=db.Model.FindEntityType(typeof(V180B3ChangeRequest))!;
        Assert.Equal("ChangeRequests",entity.GetTableName());
        Assert.Equal(maxLength,entity.FindProperty(column)!.GetMaxLength());
    }

    [Fact]
    public void B3_rowversion_json_and_UTC_precision_are_modelled()
    {
        using var db=ModelOnly();
        var request=db.Model.FindEntityType(typeof(V180B3ChangeRequest))!;
        var token=request.FindProperty("RowVersion")!;
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate,token.ValueGenerated);
        Assert.Equal("rowversion",token.GetColumnType());
        Assert.Equal("nvarchar(max)",request.FindProperty("BeforeJson")!.GetColumnType());
        Assert.Equal("nvarchar(max)",request.FindProperty("ProposedJson")!.GetColumnType());
        Assert.False(request.FindProperty("ProposedJson")!.IsNullable);
        Assert.Equal(3,request.FindProperty("SubmittedAt")!.GetPrecision());
        Assert.Equal(3,request.FindProperty("ReviewedAt")!.GetPrecision());
        Assert.Equal(3,request.FindProperty("AppliedAt")!.GetPrecision());

        var events=db.Model.FindEntityType(typeof(V180B3ChangeEvent))!;
        Assert.Equal("ChangeRequestEvents",events.GetTableName());
        Assert.Equal(40,events.FindProperty("EventType")!.GetMaxLength());
        Assert.Equal(3,events.FindProperty("OccurredAt")!.GetPrecision());
        Assert.Equal("nvarchar(max)",events.FindProperty("DetailsJson")!.GetColumnType());
    }

    [Fact]
    public void Unique_pending_and_decision_key_filters_are_explicit()
    {
        using var db=ModelOnly();
        var request=db.Model.FindEntityType(typeof(V180B3ChangeRequest))!;
        var indexes=request.GetIndexes().ToArray();
        Assert.Contains(indexes,i=>i.IsUnique
            &&i.GetDatabaseName()==V180B3SqlSafetyRules.RequestPublicIdIndex
            &&i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"RequestPublicId"}));
        Assert.Contains(indexes,i=>i.IsUnique
            &&i.GetDatabaseName()==V180B3SqlSafetyRules.PendingRequestIndex
            &&i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"OrganizationId","EntityKind","EntityId"})
            &&i.GetFilter()=="[Status] = 'Pending'");
        var events=db.Model.FindEntityType(typeof(V180B3ChangeEvent))!;
        Assert.Contains(events.GetIndexes(),i=>i.IsUnique
            &&i.GetDatabaseName()==V180B3SqlSafetyRules.DecisionKeyIndex
            &&i.GetFilter()=="[DecisionKey] IS NOT NULL");
    }

    [Fact]
    public void All_B3_foreign_keys_are_no_action_and_historical_rows_cannot_cascade()
    {
        using var db=ModelOnly();
        static bool Matches(IEntityType type,string property,Type principal) =>
            type.GetForeignKeys().Any(fk=>
                fk.Properties.Select(p=>p.Name).SequenceEqual(new[]{property})
                &&fk.PrincipalEntityType.ClrType==principal
                &&fk.DeleteBehavior==DeleteBehavior.NoAction);
        var request=db.Model.FindEntityType(typeof(V180B3ChangeRequest))!;
        Assert.True(Matches(request,"OrganizationId",typeof(Organization)));
        Assert.True(Matches(request,"TeamId",typeof(Team)));
        Assert.True(Matches(request,"RequestedByUserId",typeof(User)));
        Assert.True(Matches(request,"ReviewedByUserId",typeof(User)));
        var events=db.Model.FindEntityType(typeof(V180B3ChangeEvent))!;
        Assert.True(Matches(events,"ChangeRequestId",typeof(V180B3ChangeRequest)));
        Assert.True(Matches(events,"ActorUserId",typeof(User)));
        Assert.All(request.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.NoAction,fk.DeleteBehavior));
        Assert.All(events.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.NoAction,fk.DeleteBehavior));
    }
}
