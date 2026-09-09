using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/v180")]
public sealed class V180DeploymentSiteAdminController(
    IV180DeploymentSiteReader reader,
    IV180DeploymentSiteWriter writer,
    ICurrentUserService current) : ControllerBase
{
    [HttpGet("deployment-sites")]
    public async Task<ActionResult<PagedResult<V180DeploymentSiteAdminDto>>> Search(
        [FromQuery] V180DeploymentSiteQuery query, CancellationToken ct) =>
        Ok(await reader.SearchAsync(current.GetRequired(), query, ct));

    [HttpGet("deployment-sites/{deploymentSiteId:int}")]
    public async Task<ActionResult<V180DeploymentSiteAdminDto>> Get(
        int deploymentSiteId, [FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        var row = await reader.GetAsync(current.GetRequired(), deploymentSiteId, asOf, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("deployment-sites/{deploymentSiteId:int}/location-assignments")]
    public async Task<ActionResult<IReadOnlyList<V180DeploymentSiteLocationAssignmentDto>>> LocationAssignments(
        int deploymentSiteId, [FromQuery] bool includeHistory = true,
        [FromQuery] DateOnly? asOf = null, CancellationToken ct = default) =>
        Ok(await reader.LocationAssignmentsAsync(current.GetRequired(), deploymentSiteId, includeHistory, asOf, ct));

    [HttpGet("deployment-sites/{deploymentSiteId:int}/team-assignments")]
    public async Task<ActionResult<IReadOnlyList<V180TeamDeploymentSiteAssignmentDto>>> TeamAssignments(
        int deploymentSiteId, [FromQuery] bool includeHistory = true,
        [FromQuery] DateOnly? asOf = null, CancellationToken ct = default) =>
        Ok(await reader.TeamAssignmentsAsync(current.GetRequired(), deploymentSiteId, includeHistory, asOf, ct));

    [HttpGet("deployment-sites/{deploymentSiteId:int}/employment-assignments")]
    public async Task<ActionResult<IReadOnlyList<V180EmploymentDeploymentSiteAssignmentDto>>> EmploymentAssignments(
        int deploymentSiteId, [FromQuery] bool includeHistory = true,
        [FromQuery] DateOnly? asOf = null, CancellationToken ct = default) =>
        Ok(await reader.EmploymentAssignmentsAsync(current.GetRequired(), deploymentSiteId, includeHistory, asOf, ct));

    [HttpPost("deployment-sites")]
    public async Task<ActionResult<V180DeploymentSiteWriteResult>> CreateSite(
        [FromBody] V180CreateDeploymentSiteRequest request, CancellationToken ct) =>
        Ok(await writer.CreateSiteAsync(current.GetRequired(), request, ct));

    [HttpPut("deployment-sites/{deploymentSiteId:int}")]
    public async Task<ActionResult<V180DeploymentSiteWriteResult>> UpdateSite(
        int deploymentSiteId, [FromBody] V180UpdateDeploymentSiteRequest request, CancellationToken ct) =>
        Ok(await writer.UpdateSiteAsync(current.GetRequired(), deploymentSiteId, request, ct));

    [HttpPost("deployment-sites/{deploymentSiteId:int}/deactivate")]
    public async Task<ActionResult<V180DeploymentSiteWriteResult>> DeactivateSite(
        int deploymentSiteId, [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await writer.DeactivateSiteAsync(current.GetRequired(), deploymentSiteId, request, ct));

    [HttpPost("deployment-site-location-assignments")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> CreateLocationAssignment(
        [FromBody] V180CreateDeploymentSiteLocationAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.CreateLocationAssignmentAsync(current.GetRequired(), request, ct));

    [HttpPut("deployment-site-location-assignments/{assignmentId:long}")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> UpdateLocationAssignment(
        long assignmentId, [FromBody] V180UpdateDeploymentSiteLocationAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.UpdateLocationAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("deployment-site-location-assignments/{assignmentId:long}/end")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> EndLocationAssignment(
        long assignmentId, [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await writer.EndLocationAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("team-deployment-site-assignments")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> CreateTeamAssignment(
        [FromBody] V180CreateTeamDeploymentSiteAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.CreateTeamAssignmentAsync(current.GetRequired(), request, ct));

    [HttpPut("team-deployment-site-assignments/{assignmentId:long}")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> UpdateTeamAssignment(
        long assignmentId, [FromBody] V180UpdateTeamDeploymentSiteAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.UpdateTeamAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("team-deployment-site-assignments/{assignmentId:long}/end")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> EndTeamAssignment(
        long assignmentId, [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await writer.EndTeamAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("employment-deployment-site-assignments")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> CreateEmploymentAssignment(
        [FromBody] V180CreateEmploymentDeploymentSiteAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.CreateEmploymentAssignmentAsync(current.GetRequired(), request, ct));

    [HttpPut("employment-deployment-site-assignments/{assignmentId:long}")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> UpdateEmploymentAssignment(
        long assignmentId, [FromBody] V180UpdateEmploymentDeploymentSiteAssignmentRequest request, CancellationToken ct) =>
        Ok(await writer.UpdateEmploymentAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("employment-deployment-site-assignments/{assignmentId:long}/end")]
    public async Task<ActionResult<V180DeploymentAssignmentWriteResult>> EndEmploymentAssignment(
        long assignmentId, [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await writer.EndEmploymentAssignmentAsync(current.GetRequired(), assignmentId, request, ct));
}
