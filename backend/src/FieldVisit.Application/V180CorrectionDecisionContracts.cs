namespace FieldVisit.Application;

public interface IV180CorrectionClosureService
{
    Task<CorrectionRequestDto> CloseAsync(
        long correctionRequestId,
        CloseCorrectionRequest request,
        CancellationToken ct);
}
