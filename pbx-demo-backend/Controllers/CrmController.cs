using System.Text.Json;
using CallControl.Api.Domain;
using CallControl.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallControl.Api.Controllers;

[ApiController]
[Authorize(Policy = "SupervisorOnly")]
[Route("api/crm")]
public sealed class CrmController : ControllerBase
{
    private readonly CrmManagementService _crmManagementService;
    private readonly CallCdrService _callCdrService;

    public CrmController(CrmManagementService crmManagementService, CallCdrService callCdrService)
    {
        _crmManagementService = crmManagementService;
        _callCdrService = callCdrService;
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<CrmUserResponse>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _crmManagementService.GetUsersAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult<CrmUserResponse>> CreateUser(
        [FromBody] CrmCreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _crmManagementService.CreateUserAsync(request, cancellationToken);
        return Ok(created);
    }

    [HttpPut("users/{id:int}")]
    public async Task<ActionResult<CrmUserResponse>> UpdateUser(
        int id,
        [FromBody] CrmUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _crmManagementService.UpdateUserAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken cancellationToken)
    {
        await _crmManagementService.DeleteUserAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("users/validate-friendly-name")]
    public async Task<IActionResult> ValidateFriendlyName(
        [FromBody] CrmValidateFriendlyNameRequest request,
        CancellationToken cancellationToken)
    {
        await _crmManagementService.ValidateFriendlyUrlAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("users/{id:int}/friendly-name")]
    public async Task<IActionResult> UpdateFriendlyName(
        int id,
        [FromBody] CrmUpdateFriendlyNameRequest request,
        CancellationToken cancellationToken)
    {
        await _crmManagementService.UpdateFriendlyNameAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("departments")]
    public async Task<ActionResult<IReadOnlyList<CrmDepartmentResponse>>> GetDepartments(CancellationToken cancellationToken)
    {
        var departments = await _crmManagementService.GetDepartmentsAsync(cancellationToken);
        return Ok(departments);
    }

    [HttpPost("departments")]
    public async Task<ActionResult<CrmDepartmentResponse>> CreateDepartment(
        [FromBody] CrmCreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _crmManagementService.CreateDepartmentAsync(request, cancellationToken);
        return Ok(created);
    }

    [HttpPut("departments/{id:int}")]
    public async Task<ActionResult<CrmDepartmentResponse>> UpdateDepartment(
        int id,
        [FromBody] CrmUpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _crmManagementService.UpdateDepartmentAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("departments/{id:int}")]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken cancellationToken)
    {
        await _crmManagementService.DeleteDepartmentAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("system/version")]
    public async Task<ActionResult<CrmVersionResponse>> GetThreeCxVersion(CancellationToken cancellationToken)
    {
        var version = await _crmManagementService.GetThreeCxVersionAsync(cancellationToken);
        return Ok(version);
    }

    [HttpGet("system/groups/default")]
    public async Task<ActionResult<JsonElement>> GetDefaultGroup(CancellationToken cancellationToken)
    {
        var group = await _crmManagementService.GetDefaultGroupPropertiesAsync(cancellationToken);
        return Ok(group);
    }

    [HttpGet("system/groups/{groupId:int}/members")]
    public async Task<ActionResult<JsonElement>> GetGroupMembers(int groupId, CancellationToken cancellationToken)
    {
        var members = await _crmManagementService.GetGroupMembersAsync(groupId, cancellationToken);
        return Ok(members);
    }

    [HttpGet("system/3cx-users")]
    public async Task<ActionResult<JsonElement>> GetThreeCxUsers(CancellationToken cancellationToken)
    {
        var users = await _crmManagementService.GetThreeCxUsersAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost("system/parking")]
    public async Task<ActionResult<CrmSharedParkingResponse>> CreateSharedParking(
        [FromBody] CrmCreateSharedParkingRequest request,
        CancellationToken cancellationToken)
    {
        var parking = await _crmManagementService.CreateSharedParkingAsync(request, cancellationToken);
        return Ok(parking);
    }

    [HttpGet("system/parking/{number}")]
    public async Task<ActionResult<CrmSharedParkingResponse>> GetParkingByNumber(
        string number,
        CancellationToken cancellationToken)
    {
        var parking = await _crmManagementService.GetParkingByNumberAsync(number, cancellationToken);
        return Ok(parking);
    }

    [HttpDelete("system/parking/{parkingId:int}")]
    public async Task<IActionResult> DeleteSharedParking(int parkingId, CancellationToken cancellationToken)
    {
        await _crmManagementService.DeleteSharedParkingAsync(parkingId, cancellationToken);
        return NoContent();
    }

    [HttpGet("calls/history")]
    public async Task<ActionResult<CrmCallHistoryResponse>> GetCallHistory(
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        [FromQuery] int? operatorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _callCdrService.GetCallHistoryAsync(operatorUserId, take, skip, cancellationToken);
        return Ok(response);
    }

    [HttpGet("calls/analytics")]
    public async Task<ActionResult<CrmCallAnalyticsResponse>> GetCallAnalytics(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var normalizedDays = Math.Clamp(days, 1, 90);
        var periodEndUtc = DateTimeOffset.UtcNow;
        var periodStartUtc = periodEndUtc.AddDays(-normalizedDays);
        var response = await _callCdrService.GetCallAnalyticsAsync(periodStartUtc, periodEndUtc, cancellationToken);
        return Ok(response);
    }
}
