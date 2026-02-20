using CallControl.Api.Domain;
using CallControl.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CallControl.Api.Services;

public sealed class UserDirectoryService
{
    private readonly IDbContextFactory<SoftphoneDbContext> _dbContextFactory;

    public UserDirectoryService(IDbContextFactory<SoftphoneDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<AppUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return null;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.Username == normalizedUsername,
                cancellationToken);

        return entity?.ToRecord();
    }

    public async Task<AppUserRecord?> FindByIdAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return entity?.ToRecord();
    }
}
