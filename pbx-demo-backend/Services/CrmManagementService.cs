using System.Text.Json;
using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CallControl.Api.Services;

public sealed class CrmManagementService
{
    private readonly IDbContextFactory<SoftphoneDbContext> _dbContextFactory;
    private readonly ThreeCxConfigurationClient _threeCxConfigurationClient;
    private readonly PasswordHasher _passwordHasher;
    private readonly ILogger<CrmManagementService> _logger;

    public CrmManagementService(
        IDbContextFactory<SoftphoneDbContext> dbContextFactory,
        ThreeCxConfigurationClient threeCxConfigurationClient,
        PasswordHasher passwordHasher,
        ILogger<CrmManagementService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _threeCxConfigurationClient = threeCxConfigurationClient;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CrmUserResponse>> GetUsersAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var users = await dbContext.Users
            .AsNoTracking()
            .Include(v => v.DepartmentMemberships)
            .ThenInclude(v => v.AppDepartment)
            .OrderBy(v => v.Username)
            .ToListAsync(cancellationToken);

        return users.Select(ToUserResponse).ToList();
    }

    public async Task<IReadOnlyList<CrmDepartmentResponse>> GetDepartmentsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var departments = await dbContext.Departments
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .ToListAsync(cancellationToken);

        return departments.Select(ToDepartmentResponse).ToList();
    }

    public Task<CrmUserResponse> CreateUserAsync(CrmCreateUserRequest request, CancellationToken cancellationToken)
        => CreateUserInternalAsync(request, cancellationToken);

    public Task<CrmUserResponse> UpdateUserAsync(int appUserId, CrmUpdateUserRequest request, CancellationToken cancellationToken)
        => UpdateUserInternalAsync(appUserId, request, cancellationToken);

    public Task DeleteUserAsync(int appUserId, CancellationToken cancellationToken)
        => DeleteUserInternalAsync(appUserId, cancellationToken);

    public Task<CrmDepartmentResponse> CreateDepartmentAsync(CrmCreateDepartmentRequest request, CancellationToken cancellationToken)
        => CreateDepartmentInternalAsync(request, cancellationToken);

    public Task<CrmDepartmentResponse> UpdateDepartmentAsync(int appDepartmentId, CrmUpdateDepartmentRequest request, CancellationToken cancellationToken)
        => UpdateDepartmentInternalAsync(appDepartmentId, request, cancellationToken);

    public Task DeleteDepartmentAsync(int appDepartmentId, CancellationToken cancellationToken)
        => DeleteDepartmentInternalAsync(appDepartmentId, cancellationToken);

    public Task ValidateFriendlyUrlAsync(CrmValidateFriendlyNameRequest request, CancellationToken cancellationToken)
        => ValidateFriendlyUrlInternalAsync(request, cancellationToken);

    public Task UpdateFriendlyNameAsync(int appUserId, CrmUpdateFriendlyNameRequest request, CancellationToken cancellationToken)
        => UpdateFriendlyNameInternalAsync(appUserId, request, cancellationToken);

    public Task<CrmSharedParkingResponse> CreateSharedParkingAsync(CrmCreateSharedParkingRequest request, CancellationToken cancellationToken)
        => CreateSharedParkingInternalAsync(request, cancellationToken);

    public Task<CrmSharedParkingResponse> GetParkingByNumberAsync(string number, CancellationToken cancellationToken)
        => GetParkingByNumberInternalAsync(number, cancellationToken);

    public Task DeleteSharedParkingAsync(int parkingId, CancellationToken cancellationToken)
        => DeleteSharedParkingInternalAsync(parkingId, cancellationToken);

    public Task<JsonElement> GetGroupMembersAsync(int threeCxGroupId, CancellationToken cancellationToken)
        => GetGroupMembersInternalAsync(threeCxGroupId, cancellationToken);

    public Task<JsonElement> GetDefaultGroupPropertiesAsync(CancellationToken cancellationToken)
        => GetDefaultGroupPropertiesInternalAsync(cancellationToken);

    public Task<JsonElement> GetThreeCxUsersAsync(CancellationToken cancellationToken)
        => GetThreeCxUsersInternalAsync(cancellationToken);

    public Task<CrmVersionResponse> GetThreeCxVersionAsync(CancellationToken cancellationToken)
        => GetThreeCxVersionInternalAsync(cancellationToken);

    private async Task<CrmUserResponse> CreateUserInternalAsync(CrmCreateUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new BadRequestException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BadRequestException("Password is required.");
        }

        if (request.Role == AppUserRole.User && string.IsNullOrWhiteSpace(request.OwnedExtension))
        {
            throw new BadRequestException("OwnedExtension is required for user accounts.");
        }

        CrmServiceSupport.ValidateDepartmentRoles(request.DepartmentRoles);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedUsername = request.Username.Trim();
        var normalizedEmail = request.EmailAddress.Trim();
        var normalizedExtension = request.OwnedExtension.Trim();

        if (await dbContext.Users.AnyAsync(v => v.Username == normalizedUsername, cancellationToken))
        {
            throw new BadRequestException($"Username '{normalizedUsername}' already exists.");
        }

        if (await dbContext.Users.AnyAsync(v => v.EmailAddress.ToLower() == normalizedEmail.ToLower(), cancellationToken))
        {
            throw new BadRequestException($"Email '{normalizedEmail}' already exists.");
        }

        var departments = await ResolveDepartmentsAsync(dbContext, request.DepartmentRoles, cancellationToken);
        if(!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var existingThreeCxUser = await FindThreeCxUserByEmailAsync(normalizedEmail, cancellationToken);
            if (existingThreeCxUser.HasValue)
            {
                throw new BadRequestException($"A 3CX user with email '{normalizedEmail}' already exists.");
            }
        }

        var userEntity = new AppUserEntity
        {
            Username = normalizedUsername,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmailAddress = normalizedEmail,
            OwnedExtension = normalizedExtension,
            ControlDn = CrmServiceSupport.NormalizeOptional(request.ControlDn),
            Role = request.Role,
            Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN"),
            PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet),
            VmEmailOptions = CrmServiceSupport.NormalizeOrDefault(request.VmEmailOptions, "Notification"),
            SendEmailMissedCalls = request.SendEmailMissedCalls,
            Require2Fa = request.Require2Fa,
            CallUsEnableChat = request.CallUsEnableChat,
            ClickToCallId = CrmServiceSupport.NormalizeOptional(request.ClickToCallId),
            WebMeetingFriendlyName = CrmServiceSupport.NormalizeOptional(request.WebMeetingFriendlyName),
            SipUsername = CrmServiceSupport.NormalizeOptional(request.SipUsername),
            SipAuthId = CrmServiceSupport.NormalizeOptional(request.SipAuthId),
            SipPassword = CrmServiceSupport.NormalizeOptional(request.SipPassword),
            SipDisplayName = CrmServiceSupport.NormalizeOptional(request.SipDisplayName),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        int? createdThreeCxUserId = null;
        try
        {
            var createPayload = new
            {
                AccessPassword = CrmServiceSupport.NormalizeOrDefault(
                    request.ThreeCxAccessPassword,
                    CrmServiceSupport.GenerateRandomPassword()),
                EmailAddress = userEntity.EmailAddress,
                FirstName = userEntity.FirstName,
                Id = 0,
                Language = userEntity.Language,
                LastName = userEntity.LastName,
                Number = userEntity.OwnedExtension,
                PromptSet = userEntity.PromptSet ?? string.Empty,
                SendEmailMissedCalls = userEntity.SendEmailMissedCalls,
                VMEmailOptions = userEntity.VmEmailOptions,
                Require2FA = userEntity.Require2Fa
            };

            var created = await _threeCxConfigurationClient.PostJsonAsync(
                "/xapi/v1/Users",
                createPayload,
                cancellationToken);
            createdThreeCxUserId = CrmServiceSupport.GetInt32(created, "Id")
                ?? throw new InternalServerErrorException("3CX did not return created user id.");
            userEntity.ThreeCxUserId = createdThreeCxUserId;

            if (request.DepartmentRoles.Count > 0)
            {
                await AssignRolesInThreeCxAsync(
                    createdThreeCxUserId.Value,
                    departments,
                    request.DepartmentRoles,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(userEntity.ClickToCallId) && !string.IsNullOrWhiteSpace(userEntity.OwnedExtension))
            {
                await ValidateFriendlyUrlInternalAsync(
                    new CrmValidateFriendlyNameRequest
                    {
                        FriendlyName = userEntity.ClickToCallId,
                        Pair = userEntity.OwnedExtension
                    },
                    cancellationToken);

                await UpdateFriendlyNameInThreeCxAsync(
                    createdThreeCxUserId.Value,
                    userEntity.CallUsEnableChat,
                    userEntity.ClickToCallId,
                    userEntity.WebMeetingFriendlyName ?? userEntity.ClickToCallId,
                    cancellationToken);
            }

            dbContext.Users.Add(userEntity);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var role in request.DepartmentRoles)
            {
                var department = departments[role.AppDepartmentId];
                dbContext.DepartmentMemberships.Add(new AppDepartmentMembershipEntity
                {
                    AppUserId = userEntity.Id,
                    AppDepartmentId = department.Id,
                    ThreeCxRoleName = CrmServiceSupport.NormalizeRoleName(role.RoleName),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Entry(userEntity)
                .Collection(v => v.DepartmentMemberships)
                .Query()
                .Include(v => v.AppDepartment)
                .LoadAsync(cancellationToken);

            return ToUserResponse(userEntity);
        }
        catch
        {
            if (createdThreeCxUserId.HasValue)
            {
                await TryDeleteThreeCxUsersAsync([createdThreeCxUserId.Value], cancellationToken);
            }

            throw;
        }
    }

    private async Task<CrmUserResponse> UpdateUserInternalAsync(
        int appUserId,
        CrmUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Role == AppUserRole.User && string.IsNullOrWhiteSpace(request.OwnedExtension))
        {
            throw new BadRequestException("OwnedExtension is required for user accounts.");
        }

        CrmServiceSupport.ValidateDepartmentRoles(request.DepartmentRoles);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userEntity = await dbContext.Users
            .Include(v => v.DepartmentMemberships)
            .ThenInclude(v => v.AppDepartment)
            .FirstOrDefaultAsync(v => v.Id == appUserId, cancellationToken)
            ?? throw new NotFoundException($"User id '{appUserId}' was not found.");

        var normalizedEmail = request.EmailAddress.Trim();
        if (normalizedEmail != null && await dbContext.Users.AnyAsync(
                v => v.Id != appUserId && v.EmailAddress.ToLower() == normalizedEmail.ToLower(),
                cancellationToken))
        {
            throw new BadRequestException($"Email '{normalizedEmail}' already exists.");
        }

        var departments = await ResolveDepartmentsAsync(dbContext, request.DepartmentRoles, cancellationToken);
        if (userEntity.ThreeCxUserId.HasValue)
        {
            var updatePayload = new
            {
                Id = userEntity.ThreeCxUserId.Value,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                EmailAddress = normalizedEmail,
                Number = request.OwnedExtension.Trim(),
                Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN"),
                PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet) ?? string.Empty,
                SendEmailMissedCalls = request.SendEmailMissedCalls,
                VMEmailOptions = CrmServiceSupport.NormalizeOrDefault(request.VmEmailOptions, "Notification"),
                Require2FA = request.Require2Fa
            };

            await _threeCxConfigurationClient.SendPatchNoContentAsync(
                $"/xapi/v1/Users({userEntity.ThreeCxUserId.Value})",
                updatePayload,
                cancellationToken);

            if (request.DepartmentRoles.Count > 0)
            {
                await AssignRolesInThreeCxAsync(
                    userEntity.ThreeCxUserId.Value,
                    departments,
                    request.DepartmentRoles,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(request.ClickToCallId))
            {
                await ValidateFriendlyUrlInternalAsync(
                    new CrmValidateFriendlyNameRequest
                    {
                        FriendlyName = request.ClickToCallId,
                        Pair = request.OwnedExtension
                    },
                    cancellationToken);

                await UpdateFriendlyNameInThreeCxAsync(
                    userEntity.ThreeCxUserId.Value,
                    request.CallUsEnableChat,
                    request.ClickToCallId,
                    CrmServiceSupport.NormalizeOrDefault(request.WebMeetingFriendlyName, request.ClickToCallId),
                    cancellationToken);
            }
        }

        userEntity.FirstName = request.FirstName.Trim();
        userEntity.LastName = request.LastName.Trim();
        userEntity.EmailAddress = normalizedEmail;
        userEntity.OwnedExtension = request.OwnedExtension.Trim();
        userEntity.ControlDn = CrmServiceSupport.NormalizeOptional(request.ControlDn);
        userEntity.Role = request.Role;
        userEntity.Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN");
        userEntity.PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet);
        userEntity.VmEmailOptions = CrmServiceSupport.NormalizeOrDefault(request.VmEmailOptions, "Notification");
        userEntity.SendEmailMissedCalls = request.SendEmailMissedCalls;
        userEntity.Require2Fa = request.Require2Fa;
        userEntity.CallUsEnableChat = request.CallUsEnableChat;
        userEntity.ClickToCallId = CrmServiceSupport.NormalizeOptional(request.ClickToCallId);
        userEntity.WebMeetingFriendlyName = CrmServiceSupport.NormalizeOptional(request.WebMeetingFriendlyName);
        userEntity.SipUsername = CrmServiceSupport.NormalizeOptional(request.SipUsername);
        userEntity.SipAuthId = CrmServiceSupport.NormalizeOptional(request.SipAuthId);
        userEntity.SipPassword = CrmServiceSupport.NormalizeOptional(request.SipPassword);
        userEntity.SipDisplayName = CrmServiceSupport.NormalizeOptional(request.SipDisplayName);
        userEntity.IsActive = request.IsActive;
        userEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            userEntity.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        }

        dbContext.DepartmentMemberships.RemoveRange(userEntity.DepartmentMemberships);
        userEntity.DepartmentMemberships.Clear();

        foreach (var role in request.DepartmentRoles)
        {
            var department = departments[role.AppDepartmentId];
            userEntity.DepartmentMemberships.Add(new AppDepartmentMembershipEntity
            {
                AppUserId = userEntity.Id,
                AppDepartmentId = department.Id,
                ThreeCxRoleName = CrmServiceSupport.NormalizeRoleName(role.RoleName),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(userEntity)
            .Collection(v => v.DepartmentMemberships)
            .Query()
            .Include(v => v.AppDepartment)
            .LoadAsync(cancellationToken);

        return ToUserResponse(userEntity);
    }

    private async Task DeleteUserInternalAsync(int appUserId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userEntity = await dbContext.Users
            .Include(v => v.DepartmentMemberships)
            .FirstOrDefaultAsync(v => v.Id == appUserId, cancellationToken)
            ?? throw new NotFoundException($"User id '{appUserId}' was not found.");

        if (userEntity.Role == AppUserRole.Supervisor)
        {
            var remainingSupervisors = await dbContext.Users.CountAsync(
                v => v.Id != appUserId && v.Role == AppUserRole.Supervisor && v.IsActive,
                cancellationToken);
            if (remainingSupervisors == 0)
            {
                throw new BadRequestException("At least one active supervisor account must remain.");
            }
        }

        if (userEntity.ThreeCxUserId.HasValue)
        {
            await TryDeleteThreeCxUsersAsync([userEntity.ThreeCxUserId.Value], cancellationToken);
        }

        dbContext.Users.Remove(userEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CrmDepartmentResponse> CreateDepartmentInternalAsync(
        CrmCreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BadRequestException("Department name is required.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedName = request.Name.Trim();

        if (await dbContext.Departments.AnyAsync(v => v.Name == normalizedName, cancellationToken))
        {
            throw new BadRequestException($"Department '{normalizedName}' already exists.");
        }

        if (await CheckDepartmentExistsInThreeCxAsync(normalizedName, cancellationToken))
        {
            throw new BadRequestException($"Department '{normalizedName}' already exists in 3CX.");
        }

        int? createdGroupId = null;
        string? createdGroupNumber = null;
        int? createdWebsiteLinkId = null;

        try
        {
            var createPayload = new
            {
                AllowCallService = request.AllowCallService,
                Id = 0,
                Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN"),
                Name = normalizedName,
                PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet) ?? string.Empty,
                Props = request.Props,
                TimeZoneId = CrmServiceSupport.NormalizeOrDefault(request.TimeZoneId, "51"),
                DisableCustomPrompt = request.DisableCustomPrompt
            };

            var created = await _threeCxConfigurationClient.PostJsonAsync("/xapi/v1/Groups", createPayload, cancellationToken);
            createdGroupId = CrmServiceSupport.GetInt32(created, "Id")
                ?? throw new InternalServerErrorException("3CX did not return created department id.");
            createdGroupNumber = CrmServiceSupport.GetString(created, "Number");

            if (request.Routing is not null)
            {
                await ConfigureDepartmentRoutingAsync(createdGroupId.Value, request.Routing, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(request.LiveChatLink))
            {
                var liveChatLink = request.LiveChatLink.Trim();
                var existingLinkId = await FindLiveChatLinkIdAsync(liveChatLink, cancellationToken);
                if (existingLinkId.HasValue)
                {
                    throw new BadRequestException($"Live chat link '{liveChatLink}' is already in use.");
                }

                createdWebsiteLinkId = await CreateLiveChatLinkAsync(
                    createdGroupId.Value,
                    normalizedName,
                    createdGroupNumber ?? string.Empty,
                    liveChatLink,
                    request.LiveChatWebsite,
                    cancellationToken);
            }

            var departmentEntity = new AppDepartmentEntity
            {
                Name = normalizedName,
                ThreeCxGroupId = createdGroupId.Value,
                ThreeCxGroupNumber = createdGroupNumber,
                Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN"),
                TimeZoneId = CrmServiceSupport.NormalizeOrDefault(request.TimeZoneId, "51"),
                PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet),
                DisableCustomPrompt = request.DisableCustomPrompt,
                PropsJson = CrmServiceSupport.SerializeAsJson(request.Props),
                RoutingJson = request.Routing is null ? null : CrmServiceSupport.SerializeAsJson(request.Routing),
                LiveChatLink = CrmServiceSupport.NormalizeOptional(request.LiveChatLink),
                LiveChatWebsite = CrmServiceSupport.NormalizeOptional(request.LiveChatWebsite),
                ThreeCxWebsiteLinkId = createdWebsiteLinkId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.Departments.Add(departmentEntity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToDepartmentResponse(departmentEntity);
        }
        catch
        {
            if (createdGroupId.HasValue)
            {
                await TryDeleteThreeCxDepartmentAsync(createdGroupId.Value, cancellationToken);
            }

            throw;
        }
    }

    private async Task<CrmDepartmentResponse> UpdateDepartmentInternalAsync(
        int appDepartmentId,
        CrmUpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BadRequestException("Department name is required.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var departmentEntity = await dbContext.Departments.FirstOrDefaultAsync(v => v.Id == appDepartmentId, cancellationToken)
            ?? throw new NotFoundException($"Department id '{appDepartmentId}' was not found.");

        var normalizedName = request.Name.Trim();
        if (await dbContext.Departments.AnyAsync(v => v.Id != appDepartmentId && v.Name == normalizedName, cancellationToken))
        {
            throw new BadRequestException($"Department '{normalizedName}' already exists.");
        }

        var updatePayload = new
        {
            Id = departmentEntity.ThreeCxGroupId,
            Name = normalizedName,
            Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN"),
            TimeZoneId = CrmServiceSupport.NormalizeOrDefault(request.TimeZoneId, "51"),
            PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet) ?? string.Empty,
            DisableCustomPrompt = request.DisableCustomPrompt,
            AllowCallService = request.AllowCallService,
            Props = request.Props
        };

        await _threeCxConfigurationClient.SendPatchNoContentAsync(
            $"/xapi/v1/Groups({departmentEntity.ThreeCxGroupId})",
            updatePayload,
            cancellationToken);

        if (request.Routing is not null)
        {
            await ConfigureDepartmentRoutingAsync(departmentEntity.ThreeCxGroupId, request.Routing, cancellationToken);
        }

        var normalizedLiveChatLink = CrmServiceSupport.NormalizeOptional(request.LiveChatLink);
        if (!string.IsNullOrWhiteSpace(normalizedLiveChatLink)
            && !string.Equals(departmentEntity.LiveChatLink, normalizedLiveChatLink, StringComparison.Ordinal))
        {
            var existingLinkId = await FindLiveChatLinkIdAsync(normalizedLiveChatLink, cancellationToken);
            if (existingLinkId.HasValue)
            {
                throw new BadRequestException($"Live chat link '{normalizedLiveChatLink}' is already in use.");
            }

            departmentEntity.ThreeCxWebsiteLinkId = await CreateLiveChatLinkAsync(
                departmentEntity.ThreeCxGroupId,
                normalizedName,
                departmentEntity.ThreeCxGroupNumber ?? string.Empty,
                normalizedLiveChatLink,
                request.LiveChatWebsite,
                cancellationToken);
        }

        departmentEntity.Name = normalizedName;
        departmentEntity.Language = CrmServiceSupport.NormalizeOrDefault(request.Language, "EN");
        departmentEntity.TimeZoneId = CrmServiceSupport.NormalizeOrDefault(request.TimeZoneId, "51");
        departmentEntity.PromptSet = CrmServiceSupport.NormalizeOptional(request.PromptSet);
        departmentEntity.DisableCustomPrompt = request.DisableCustomPrompt;
        departmentEntity.PropsJson = CrmServiceSupport.SerializeAsJson(request.Props);
        departmentEntity.RoutingJson = request.Routing is null ? null : CrmServiceSupport.SerializeAsJson(request.Routing);
        departmentEntity.LiveChatLink = normalizedLiveChatLink;
        departmentEntity.LiveChatWebsite = CrmServiceSupport.NormalizeOptional(request.LiveChatWebsite);
        departmentEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDepartmentResponse(departmentEntity);
    }

    private async Task DeleteDepartmentInternalAsync(int appDepartmentId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var departmentEntity = await dbContext.Departments.FirstOrDefaultAsync(v => v.Id == appDepartmentId, cancellationToken)
            ?? throw new NotFoundException($"Department id '{appDepartmentId}' was not found.");

        await _threeCxConfigurationClient.SendPostNoContentAsync(
            "/xapi/v1/Groups/Pbx.DeleteCompanyById",
            new { id = departmentEntity.ThreeCxGroupId },
            cancellationToken);

        dbContext.Departments.Remove(departmentEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateFriendlyUrlInternalAsync(
        CrmValidateFriendlyNameRequest request,
        CancellationToken cancellationToken)
    {
        var friendlyName = request.FriendlyName?.Trim() ?? string.Empty;
        var pair = request.Pair?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(friendlyName) || string.IsNullOrWhiteSpace(pair))
        {
            throw new BadRequestException("FriendlyName and Pair are required.");
        }

        await _threeCxConfigurationClient.SendPostNoContentAsync(
            "/xapi/v1/WebsiteLinks/Pbx.ValidateLink",
            new
            {
                model = new
                {
                    FriendlyName = friendlyName,
                    Pair = pair
                }
            },
            cancellationToken);
    }

    private async Task UpdateFriendlyNameInternalAsync(
        int appUserId,
        CrmUpdateFriendlyNameRequest request,
        CancellationToken cancellationToken)
    {
        var clickToCallId = request.ClickToCallId?.Trim() ?? string.Empty;
        var webMeetingFriendlyName = request.WebMeetingFriendlyName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clickToCallId) || string.IsNullOrWhiteSpace(webMeetingFriendlyName))
        {
            throw new BadRequestException("ClickToCallId and WebMeetingFriendlyName are required.");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userEntity = await dbContext.Users.FirstOrDefaultAsync(v => v.Id == appUserId, cancellationToken)
            ?? throw new NotFoundException($"User id '{appUserId}' was not found.");

        if (!userEntity.ThreeCxUserId.HasValue)
        {
            throw new BadRequestException("User is not linked with a 3CX user id.");
        }

        await UpdateFriendlyNameInThreeCxAsync(
            userEntity.ThreeCxUserId.Value,
            request.CallUsEnableChat,
            clickToCallId,
            webMeetingFriendlyName,
            cancellationToken);

        userEntity.CallUsEnableChat = request.CallUsEnableChat;
        userEntity.ClickToCallId = clickToCallId;
        userEntity.WebMeetingFriendlyName = webMeetingFriendlyName;
        userEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CrmSharedParkingResponse> CreateSharedParkingInternalAsync(
        CrmCreateSharedParkingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.GroupIds is null || request.GroupIds.Count == 0)
        {
            throw new BadRequestException("At least one GroupId is required.");
        }

        var payload = new
        {
            Groups = request.GroupIds.Distinct().Select(v => new { GroupId = v }).ToList(),
            Id = 0
        };

        var created = await _threeCxConfigurationClient.PostJsonAsync("/xapi/v1/Parkings", payload, cancellationToken);
        return new CrmSharedParkingResponse
        {
            Id = CrmServiceSupport.GetInt32(created, "Id") ?? 0,
            Number = CrmServiceSupport.GetString(created, "Number") ?? string.Empty
        };
    }

    private async Task<CrmSharedParkingResponse> GetParkingByNumberInternalAsync(
        string number,
        CancellationToken cancellationToken)
    {
        var normalizedNumber = number?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedNumber))
        {
            throw new BadRequestException("Parking number is required.");
        }

        var escaped = normalizedNumber.Replace("'", "''");
        var details = await _threeCxConfigurationClient.GetJsonAsync(
            $"/xapi/v1/Parkings/Pbx.GetByNumber(number='{Uri.EscapeDataString(escaped)}')",
            cancellationToken);

        return new CrmSharedParkingResponse
        {
            Id = CrmServiceSupport.GetInt32(details, "Id") ?? 0,
            Number = CrmServiceSupport.GetString(details, "Number") ?? normalizedNumber
        };
    }

    private Task DeleteSharedParkingInternalAsync(int parkingId, CancellationToken cancellationToken)
    {
        if (parkingId <= 0)
        {
            throw new BadRequestException("Parking id must be greater than zero.");
        }

        return _threeCxConfigurationClient.SendDeleteNoContentAsync(
            $"/xapi/v1/Parkings({parkingId})",
            cancellationToken);
    }

    private Task<JsonElement> GetGroupMembersInternalAsync(int threeCxGroupId, CancellationToken cancellationToken)
    {
        if (threeCxGroupId <= 0)
        {
            throw new BadRequestException("Group id must be greater than zero.");
        }

        return _threeCxConfigurationClient.GetJsonAsync(
            $"/xapi/v1/Groups({threeCxGroupId})?$expand=Members",
            cancellationToken);
    }

    private Task<JsonElement> GetDefaultGroupPropertiesInternalAsync(CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString("Name eq 'DEFAULT'");
        return _threeCxConfigurationClient.GetJsonAsync(
            $"/xapi/v1/Groups?$filter={filter}",
            cancellationToken);
    }

    private Task<JsonElement> GetThreeCxUsersInternalAsync(CancellationToken cancellationToken)
    {
        var query = "$top=100&$skip=0&$orderby=Number&$select=Id,FirstName,LastName,Number,EmailAddress&$expand=Groups($expand=Rights)";
        return _threeCxConfigurationClient.GetJsonAsync($"/xapi/v1/Users?{query}", cancellationToken);
    }

    private async Task<CrmVersionResponse> GetThreeCxVersionInternalAsync(CancellationToken cancellationToken)
    {
        var result = await _threeCxConfigurationClient.GetVersionProbeAsync(
            "/xapi/v1/Defs?$select=Id",
            cancellationToken);

        return new CrmVersionResponse
        {
            Version = result.Version
        };
    }

    private static async Task<Dictionary<int, AppDepartmentEntity>> ResolveDepartmentsAsync(
        SoftphoneDbContext dbContext,
        IReadOnlyList<CrmUserDepartmentRoleRequest> requestedRoles,
        CancellationToken cancellationToken)
    {
        if (requestedRoles.Count == 0)
        {
            return new Dictionary<int, AppDepartmentEntity>();
        }

        var requestedDepartmentIds = requestedRoles.Select(v => v.AppDepartmentId).Distinct().ToList();
        var departments = await dbContext.Departments
            .Where(v => requestedDepartmentIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var missingDepartmentId = requestedDepartmentIds.FirstOrDefault(id => !departments.ContainsKey(id));
        if (missingDepartmentId > 0)
        {
            throw new NotFoundException($"Department id '{missingDepartmentId}' was not found.");
        }

        return departments;
    }

    private async Task<int?> FindThreeCxUserByEmailAsync(string emailAddress, CancellationToken cancellationToken)
    {
        var escapedEmail = emailAddress.ToLowerInvariant().Replace("'", "''");
        var filter = Uri.EscapeDataString($"tolower(EmailAddress) eq '{escapedEmail}'");
        var query = $"$top=1&$filter={filter}&$orderby=Number&$select=Id,FirstName,LastName,Number,EmailAddress&$expand=Groups($expand=Rights)";
        var response = await _threeCxConfigurationClient.GetJsonAsync($"/xapi/v1/Users?{query}", cancellationToken);

        var first = CrmServiceSupport.GetValueArray(response).FirstOrDefault();
        return first.ValueKind == JsonValueKind.Undefined ? null : CrmServiceSupport.GetInt32(first, "Id");
    }

    private async Task AssignRolesInThreeCxAsync(
        int threeCxUserId,
        IReadOnlyDictionary<int, AppDepartmentEntity> departments,
        IReadOnlyList<CrmUserDepartmentRoleRequest> requestedRoles,
        CancellationToken cancellationToken)
    {
        var groups = requestedRoles
            .Select(role =>
            {
                var department = departments[role.AppDepartmentId];
                return new
                {
                    GroupId = department.ThreeCxGroupId,
                    Rights = new
                    {
                        RoleName = CrmServiceSupport.NormalizeRoleName(role.RoleName)
                    }
                };
            })
            .ToList();

        var payload = new
        {
            Groups = groups,
            Id = threeCxUserId
        };

        await _threeCxConfigurationClient.SendPatchNoContentAsync(
            $"/xapi/v1/Users({threeCxUserId})",
            payload,
            cancellationToken);
    }

    private Task UpdateFriendlyNameInThreeCxAsync(
        int threeCxUserId,
        bool callUsEnableChat,
        string clickToCallId,
        string webMeetingFriendlyName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            CallUsEnableChat = callUsEnableChat,
            ClickToCallId = clickToCallId,
            Id = threeCxUserId,
            WebMeetingFriendlyName = webMeetingFriendlyName
        };

        return _threeCxConfigurationClient.SendPatchNoContentAsync(
            $"/xapi/v1/Users({threeCxUserId})",
            payload,
            cancellationToken);
    }

    private async Task TryDeleteThreeCxUsersAsync(
        IReadOnlyList<int> threeCxUserIds,
        CancellationToken cancellationToken)
    {
        if (threeCxUserIds.Count == 0)
        {
            return;
        }

        try
        {
            await _threeCxConfigurationClient.PostJsonAsync(
                "/xapi/v1/Users/Pbx.BatchDelete",
                new { Ids = threeCxUserIds },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete 3CX users during rollback: {@Ids}", threeCxUserIds);
        }
    }

    private async Task<bool> CheckDepartmentExistsInThreeCxAsync(string departmentName, CancellationToken cancellationToken)
    {
        var escapedName = departmentName.Replace("'", "''");
        var filter = Uri.EscapeDataString($"Name eq '{escapedName}'");
        var response = await _threeCxConfigurationClient.GetJsonAsync(
            $"/xapi/v1/Groups?$filter={filter}",
            cancellationToken);
        return CrmServiceSupport.GetValueArray(response).Any();
    }

    private Task ConfigureDepartmentRoutingAsync(
        int threeCxGroupId,
        CrmDepartmentRoutingDto routing,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Id = threeCxGroupId,
            BreakRoute = routing.BreakRoute,
            OfficeRoute = routing.OfficeRoute,
            OutOfOfficeRoute = routing.OutOfOfficeRoute,
            HolidaysRoute = routing.HolidaysRoute
        };

        return _threeCxConfigurationClient.SendPatchNoContentAsync(
            $"/xapi/v1/Groups({threeCxGroupId})",
            payload,
            cancellationToken);
    }

    private async Task<int?> FindLiveChatLinkIdAsync(string link, CancellationToken cancellationToken)
    {
        var escapedLink = link.Replace("'", "''");
        var filter = Uri.EscapeDataString($"Link eq '{escapedLink}'");
        var response = await _threeCxConfigurationClient.GetJsonAsync(
            $"/xapi/v1/WebsiteLinks?$filter={filter}",
            cancellationToken);

        var first = CrmServiceSupport.GetValueArray(response).FirstOrDefault();
        return first.ValueKind == JsonValueKind.Undefined ? null : CrmServiceSupport.GetInt32(first, "Id");
    }

    private async Task<int> CreateLiveChatLinkAsync(
        int groupId,
        string groupName,
        string groupNumber,
        string link,
        string? website,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Advanced = new
            {
                CallTitle = string.Empty,
                CommunicationOptions = "PhoneAndChat",
                EnableDirectCall = true,
                IgnoreQueueOwnership = false
            },
            CallsEnabled = true,
            ChatEnabled = true,
            DefaultRecord = true,
            DN = new
            {
                Id = groupId,
                Name = groupName,
                Number = groupNumber,
                Type = "Group"
            },
            General = new
            {
                AllowSoundNotifications = true,
                Authentication = "None",
                DisableOfflineMessages = false,
                Greeting = "DesktopAndMobile"
            },
            Group = groupNumber,
            Link = link,
            Name = string.Empty,
            Styling = new
            {
                Animation = "NoAnimation",
                Minimized = true
            },
            Translations = new
            {
                GreetingMessage = string.Empty,
                StartChatButtonText = string.Empty,
                UnavailableMessage = string.Empty
            },
            Website = string.IsNullOrWhiteSpace(website)
                ? new[] { "https://localhost" }
                : new[] { website.Trim() }
        };

        var created = await _threeCxConfigurationClient.PostJsonAsync(
            "/xapi/v1/WebsiteLinks",
            payload,
            cancellationToken);

        return CrmServiceSupport.GetInt32(created, "Id")
            ?? throw new InternalServerErrorException("3CX did not return the created live chat id.");
    }

    private async Task TryDeleteThreeCxDepartmentAsync(int threeCxGroupId, CancellationToken cancellationToken)
    {
        try
        {
            await _threeCxConfigurationClient.SendPostNoContentAsync(
                "/xapi/v1/Groups/Pbx.DeleteCompanyById",
                new { id = threeCxGroupId },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete 3CX department during rollback: {GroupId}", threeCxGroupId);
        }
    }

    private static CrmUserResponse ToUserResponse(AppUserEntity user)
    {
        return new CrmUserResponse
        {
            Id = user.Id,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmailAddress = user.EmailAddress,
            OwnedExtension = user.OwnedExtension,
            ControlDn = user.ControlDn,
            Role = user.Role,
            Language = user.Language,
            PromptSet = user.PromptSet,
            VmEmailOptions = user.VmEmailOptions,
            SendEmailMissedCalls = user.SendEmailMissedCalls,
            Require2Fa = user.Require2Fa,
            CallUsEnableChat = user.CallUsEnableChat,
            ClickToCallId = user.ClickToCallId,
            WebMeetingFriendlyName = user.WebMeetingFriendlyName,
            ThreeCxUserId = user.ThreeCxUserId,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            DepartmentRoles = user.DepartmentMemberships
                .OrderBy(v => v.AppDepartment.Name)
                .Select(v => new CrmDepartmentRoleResponse
                {
                    AppDepartmentId = v.AppDepartmentId,
                    ThreeCxGroupId = v.AppDepartment.ThreeCxGroupId,
                    DepartmentName = v.AppDepartment.Name,
                    RoleName = v.ThreeCxRoleName
                })
                .ToList()
        };
    }

    private static CrmDepartmentResponse ToDepartmentResponse(AppDepartmentEntity department)
    {
        return new CrmDepartmentResponse
        {
            Id = department.Id,
            Name = department.Name,
            ThreeCxGroupId = department.ThreeCxGroupId,
            ThreeCxGroupNumber = department.ThreeCxGroupNumber,
            Language = department.Language,
            TimeZoneId = department.TimeZoneId,
            PromptSet = department.PromptSet,
            DisableCustomPrompt = department.DisableCustomPrompt,
            Props = CrmServiceSupport.DeserializeOrDefault(department.PropsJson, new CrmDepartmentPropsDto()),
            Routing = CrmServiceSupport.DeserializeOrDefault<CrmDepartmentRoutingDto?>(department.RoutingJson, null),
            LiveChatLink = department.LiveChatLink,
            LiveChatWebsite = department.LiveChatWebsite,
            ThreeCxWebsiteLinkId = department.ThreeCxWebsiteLinkId,
            CreatedAtUtc = department.CreatedAtUtc,
            UpdatedAtUtc = department.UpdatedAtUtc
        };
    }
}
