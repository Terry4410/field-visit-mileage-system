using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

internal static class V180LocationDuplicateGovernance
{
    internal sealed record Candidate(Location Row,IReadOnlyList<string> Reasons);
    private sealed record DistinctEvidence(
        int CandidateLocationId,
        string SourceSignature,
        string CandidateSignature,
        string Reason);

    internal static V170LocationDuplicateComparable Comparable(Location row) =>
        new(row.LocationId,row.LocationName,row.Address,row.PlusCode,row.TaxId);

    internal static async Task<IReadOnlyList<Candidate>> FindCandidatesAsync(
        AppDbContext db,Location source,CancellationToken ct,bool suppressReviewed=true)
    {
        var rows=await db.Locations.AsNoTracking()
            .Where(x=>x.OrganizationId==source.OrganizationId
                &&x.LocationId!=source.LocationId
                &&x.DuplicateOfLocationId==null)
            .OrderByDescending(x=>x.IsActive)
            .ThenBy(x=>x.LocationName)
            .ToListAsync(ct);

        var comparable=Comparable(source);
        var candidates=rows
            .Select(x=>new Candidate(x,V170LocationDuplicateRules.MatchReasons(comparable,Comparable(x))))
            .Where(x=>x.Reasons.Count>0)
            .Take(50)
            .ToList();

        if(!suppressReviewed||candidates.Count==0)return candidates;

        var reviews=await db.LocationApprovalHistories.AsNoTracking()
            .Where(x=>x.LocationId==source.LocationId&&x.Action=="DuplicateDistinctConfirmed")
            .OrderByDescending(x=>x.ActionAt)
            .Select(x=>x.Comments)
            .ToListAsync(ct);
        if(reviews.Count==0)return candidates;

        var sourceSignature=V170LocationDuplicateRules.Signature(comparable);
        return candidates.Where(candidate=>
        {
            var candidateSignature=V170LocationDuplicateRules.Signature(Comparable(candidate.Row));
            foreach(var comments in reviews)
            {
                if(string.IsNullOrWhiteSpace(comments))continue;
                try
                {
                    var evidence=JsonSerializer.Deserialize<DistinctEvidence>(comments);
                    if(evidence is not null
                        &&evidence.CandidateLocationId==candidate.Row.LocationId
                        &&evidence.SourceSignature==sourceSignature
                        &&evidence.CandidateSignature==candidateSignature)
                        return false;
                }
                catch(JsonException){}
            }
            return true;
        }).ToList();
    }

    internal static async Task<bool> RefreshSuspectFlagAsync(
        AppDbContext db,Location source,int actorUserId,CancellationToken ct)
    {
        if(source.DuplicateOfLocationId.HasValue)return false;
        var candidates=await FindCandidatesAsync(db,source,ct);
        var next=candidates.Count>0
            ?V170LocationDuplicateRules.SuspectedReason
            :source.DuplicateReason==V170LocationDuplicateRules.SuspectedReason?null:source.DuplicateReason;
        if(string.Equals(source.DuplicateReason,next,StringComparison.Ordinal))return false;

        var old=source.DuplicateReason;
        source.DuplicateReason=next;
        source.UpdatedAt=DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            UserId=actorUserId,
            EntityType="Location",
            EntityId=source.LocationId.ToString(),
            Action=next is null?"LocationDuplicateSuspectCleared":"LocationDuplicateSuspected",
            OldValues=JsonSerializer.Serialize(new{DuplicateReason=old}),
            NewValues=JsonSerializer.Serialize(new
            {
                DuplicateReason=next,
                Candidates=candidates.Select(x=>new{x.Row.LocationId,x.Row.LocationCode,x.Row.LocationName,x.Reasons})
            }),
            CreatedAt=DateTime.UtcNow
        });
        return true;
    }

    internal static void AddDistinctEvidence(
        AppDbContext db,Location source,Location candidate,int actorUserId,string reason)
    {
        var now=DateTime.UtcNow;
        var sourceEvidence=new DistinctEvidence(
            candidate.LocationId,
            V170LocationDuplicateRules.Signature(Comparable(source)),
            V170LocationDuplicateRules.Signature(Comparable(candidate)),
            reason);
        var candidateEvidence=new DistinctEvidence(
            source.LocationId,
            V170LocationDuplicateRules.Signature(Comparable(candidate)),
            V170LocationDuplicateRules.Signature(Comparable(source)),
            reason);

        db.LocationApprovalHistories.AddRange(
            new LocationApprovalHistory
            {
                LocationId=source.LocationId,Action="DuplicateDistinctConfirmed",
                ReviewedByUserId=actorUserId,Comments=JsonSerializer.Serialize(sourceEvidence),ActionAt=now
            },
            new LocationApprovalHistory
            {
                LocationId=candidate.LocationId,Action="DuplicateDistinctConfirmed",
                ReviewedByUserId=actorUserId,Comments=JsonSerializer.Serialize(candidateEvidence),ActionAt=now
            });
        db.AuditLogs.Add(new AuditLog
        {
            UserId=actorUserId,EntityType="Location",EntityId=source.LocationId.ToString(),
            Action="LocationDuplicateDistinctConfirmed",
            NewValues=JsonSerializer.Serialize(new
            {
                CandidateLocationId=candidate.LocationId,
                candidate.LocationCode,
                candidate.LocationName,
                Reason=reason
            }),
            CreatedAt=now
        });
    }
}
