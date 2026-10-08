using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using FlowHearth.Infrastructure.Database;
using MySqlConnector;

namespace FlowHearth.Infrastructure.Security;

public sealed class MySqlSecurityRepository(IDbConnectionFactory connectionFactory) :
    IAuthenticationRepository,
    IAdministratorBootstrapRepository,
    ISecurityAdministrationRepository
{
    public async Task<UserAuthenticationRecord?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await QueryAuthenticationRecordAsync(
            connection,
            "normalized_username = @NormalizedUsername",
            new { NormalizedUsername = normalizedUsername },
            cancellationToken);
    }

    public async Task<UserAuthenticationRecord?> FindByIdAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await QueryAuthenticationRecordAsync(
            connection,
            "id = @UserId",
            new { UserId = userId },
            cancellationToken);
    }

    public async Task<DateTime?> RecordFailedLoginAsync(
        ulong userId,
        DateTime nowUtc,
        int maximumAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken)
    {
        const string selectSql =
            """
            SELECT failed_login_count AS FailedLoginCount,
                   lockout_end_utc AS LockoutEndUtc
            FROM users
            WHERE id = @UserId
              AND is_active = 1
              AND deleted_at_utc IS NULL
            FOR UPDATE;
            """;
        const string updateSql =
            """
            UPDATE users
            SET failed_login_count = @FailedLoginCount,
                lockout_end_utc = @LockoutEndUtc,
                updated_at_utc = @NowUtc,
                version = version + 1
            WHERE id = @UserId
              AND is_active = 1
              AND deleted_at_utc IS NULL;
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<FailedLoginState>(
            new CommandDefinition(
                selectSql,
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));
        if (state is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (state.LockoutEndUtc is { } currentLockoutEndUtc
            && currentLockoutEndUtc > nowUtc)
        {
            await transaction.CommitAsync(cancellationToken);
            return currentLockoutEndUtc;
        }

        var previousFailedCount = state.LockoutEndUtc.HasValue
            ? 0u
            : state.FailedLoginCount;
        var failedLoginCount = checked(previousFailedCount + 1);
        DateTime? lockoutEndUtc = failedLoginCount >= maximumAttempts
            ? nowUtc.Add(lockoutDuration)
            : null;
        await connection.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    UserId = userId,
                    FailedLoginCount = failedLoginCount,
                    LockoutEndUtc = lockoutEndUtc,
                    NowUtc = nowUtc,
                },
                transaction,
                cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return lockoutEndUtc;
    }

    public async Task<AuthenticatedUser?> RecordSuccessfulLoginAsync(
        ulong userId,
        string? replacementPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE users
            SET failed_login_count = 0,
                lockout_end_utc = NULL,
                last_login_at_utc = @NowUtc,
                password_hash = COALESCE(@ReplacementPasswordHash, password_hash),
                updated_at_utc = @NowUtc,
                version = version + 1
            WHERE id = @UserId
              AND is_active = 1
              AND deleted_at_utc IS NULL;
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    NowUtc = nowUtc,
                    ReplacementPasswordHash = replacementPasswordHash,
                },
                cancellationToken: cancellationToken));
        return affected == 0
            ? null
            : await GetAuthenticatedUserAsync(connection, userId, cancellationToken);
    }

    public async Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await GetAuthenticatedUserAsync(connection, userId, cancellationToken);
    }

    public async Task<SecuritySession?> GetSecuritySessionAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS UserId,
                   is_active AS IsActive,
                   security_version AS SecurityVersion
            FROM users
            WHERE id = @UserId
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SecuritySession>(
            new CommandDefinition(
                sql,
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    public async Task UpdatePasswordAsync(
        ulong userId,
        string passwordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE users
            SET password_hash = @PasswordHash,
                password_changed_at_utc = @NowUtc,
                failed_login_count = 0,
                lockout_end_utc = NULL,
                security_version = security_version + 1,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @UserId
            WHERE id = @UserId
              AND is_active = 1
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { UserId = userId, PasswordHash = passwordHash, NowUtc = nowUtc },
                transaction,
                cancellationToken: cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new NotFoundException("当前用户不存在或已停用。");
        }

        var identity = await GetAuditUserIdentityAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        await WriteSecurityAuditAsync(
            connection,
            transaction,
            userId,
            "security.user.password_changed",
            "User",
            userId,
            identity?.Username,
            $"用户 {identity?.DisplayName ?? "当前用户"} 修改密码",
            null,
            new { PasswordChanged = true },
            nowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CreateFirstAdministratorAsync(
        BootstrapAdministratorData administrator,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var existingUserId = await connection.QueryFirstOrDefaultAsync<ulong?>(
                new CommandDefinition(
                    "SELECT id FROM users ORDER BY id LIMIT 1 FOR UPDATE;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            if (existingUserId.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var administratorRoleId = await connection.QuerySingleOrDefaultAsync<ulong>(
                new CommandDefinition(
                    "SELECT id FROM roles WHERE code = 'administrator' AND is_active = 1 AND deleted_at_utc IS NULL;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            if (administratorRoleId == 0)
            {
                throw new InvalidOperationException(
                    "The administrator role has not been seeded. Run migrations first.");
            }

            var userId = await InsertUserAsync(
                connection,
                transaction,
                administrator.Username,
                administrator.NormalizedUsername,
                administrator.DisplayName,
                administrator.Email,
                administrator.NormalizedEmail,
                administrator.PasswordHash,
                actorUserId: null,
                administrator.CreatedAtUtc,
                cancellationToken);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO user_roles
                        (user_id, role_id, assigned_at_utc, assigned_by_user_id)
                    VALUES
                        (@UserId, @RoleId, @AssignedAtUtc, NULL);
                    """,
                    new
                    {
                        UserId = userId,
                        RoleId = administratorRoleId,
                        AssignedAtUtc = administrator.CreatedAtUtc,
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            await WriteSecurityAuditAsync(
                connection,
                transaction,
                null,
                "security.user.bootstrap_created",
                "User",
                userId,
                administrator.Username,
                $"创建首位管理员 {administrator.DisplayName}",
                null,
                new
                {
                    administrator.Username,
                    administrator.DisplayName,
                    administrator.Email,
                    Role = "administrator",
                },
                administrator.CreatedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<PagedResult<UserSummary>> ListUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        const string whereClause =
            """
            deleted_at_utc IS NULL
            AND (@SearchPattern IS NULL
                 OR username LIKE @SearchPattern ESCAPE '='
                 OR display_name LIKE @SearchPattern ESCAPE '='
                 OR email LIKE @SearchPattern ESCAPE '=')
            """;
        var parameters = new
        {
            SearchPattern = MySqlLikePattern.Contains(search),
            Offset = ((long)page - 1) * pageSize,
            PageSize = pageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var total = await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                $"SELECT COUNT(*) FROM users WHERE {whereClause};",
                parameters,
                cancellationToken: cancellationToken));
        var rows = (await connection.QueryAsync<UserRow>(
            new CommandDefinition(
                $"""
                SELECT id AS Id,
                       username AS Username,
                       display_name AS DisplayName,
                       email AS Email,
                       is_active AS IsActive,
                       last_login_at_utc AS LastLoginAtUtc,
                       lockout_end_utc AS LockoutEndUtc,
                       version AS Version
                FROM users
                WHERE {whereClause}
                ORDER BY display_name, id
                LIMIT @PageSize OFFSET @Offset;
                """,
                parameters,
                cancellationToken: cancellationToken))).AsList();
        var roles = await GetRolesByUserIdsAsync(
            connection,
            rows.Select(row => row.Id).ToArray(),
            cancellationToken);
        var items = rows.Select(row => ToUserSummary(row, roles)).ToArray();
        return new PagedResult<UserSummary>(items, page, pageSize, total);
    }

    public async Task<UserDetails?> GetUserAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await GetUserAsync(connection, userId, cancellationToken);
    }

    public async Task<UserDetails> CreateUserAsync(
        CreateUserData user,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureIdsExistAsync(
                connection,
                transaction,
                "roles",
                user.RoleIds,
                "roleIds",
                cancellationToken);
            var userId = await InsertUserAsync(
                connection,
                transaction,
                user.Username,
                user.NormalizedUsername,
                user.DisplayName,
                user.Email,
                user.NormalizedEmail,
                user.PasswordHash,
                user.ActorUserId,
                user.CreatedAtUtc,
                cancellationToken);
            await ReplaceUserRolesAsync(
                connection,
                transaction,
                userId,
                user.RoleIds,
                user.ActorUserId,
                user.CreatedAtUtc,
                cancellationToken);
            await WriteSecurityAuditAsync(
                connection,
                transaction,
                user.ActorUserId,
                "security.user.created",
                "User",
                userId,
                user.Username,
                $"创建用户 {user.DisplayName}",
                null,
                new
                {
                    user.Username,
                    user.DisplayName,
                    user.Email,
                    user.RoleIds,
                    IsActive = true,
                },
                user.CreatedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetUserAsync(connection, userId, cancellationToken)
                ?? throw new InvalidOperationException("The created user could not be loaded.");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw new ConflictException("用户名或邮箱已存在。");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<UserDetails?> UpdateUserAsync(
        ulong userId,
        UpdateUserData user,
        CancellationToken cancellationToken)
    {
        const string updateSql =
            """
            UPDATE users
            SET display_name = @DisplayName,
                email = @Email,
                normalized_email = @NormalizedEmail,
                is_active = @IsActive,
                failed_login_count = CASE WHEN @IsActive = 1 THEN failed_login_count ELSE 0 END,
                lockout_end_utc = CASE WHEN @IsActive = 1 THEN lockout_end_utc ELSE NULL END,
                security_version = security_version + 1,
                version = version + 1,
                updated_at_utc = @UpdatedAtUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @UserId
              AND version = @Version
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = await GetAuditUserSnapshotAsync(
                connection,
                transaction,
                userId,
                cancellationToken);
            if (before is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await EnsureIdsExistAsync(
                connection,
                transaction,
                "roles",
                user.RoleIds,
                "roleIds",
                cancellationToken);
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new
                    {
                        UserId = userId,
                        user.DisplayName,
                        user.Email,
                        user.NormalizedEmail,
                        user.IsActive,
                        user.Version,
                        user.UpdatedAtUtc,
                        user.ActorUserId,
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await ReplaceUserRolesAsync(
                connection,
                transaction,
                userId,
                user.RoleIds,
                user.ActorUserId,
                user.UpdatedAtUtc,
                cancellationToken);
            await WriteSecurityAuditAsync(
                connection,
                transaction,
                user.ActorUserId,
                "security.user.updated",
                "User",
                userId,
                before.Username,
                $"更新用户 {user.DisplayName}",
                before,
                new
                {
                    user.DisplayName,
                    user.Email,
                    user.IsActive,
                    user.RoleIds,
                },
                user.UpdatedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetUserAsync(connection, userId, cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw new ConflictException("邮箱已被其他用户使用。");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task ResetPasswordAsync(
        ulong userId,
        string passwordHash,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE users
            SET password_hash = @PasswordHash,
                password_changed_at_utc = @NowUtc,
                failed_login_count = 0,
                lockout_end_utc = NULL,
                security_version = security_version + 1,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @UserId
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var identity = await GetAuditUserIdentityAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (identity is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new NotFoundException("用户不存在。");
        }

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    PasswordHash = passwordHash,
                    ActorUserId = actorUserId,
                    NowUtc = nowUtc,
                },
                transaction,
                cancellationToken: cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new NotFoundException("用户不存在。");
        }

        await WriteSecurityAuditAsync(
            connection,
            transaction,
            actorUserId,
            "security.user.password_reset",
            "User",
            userId,
            identity.Username,
            $"重置用户 {identity.DisplayName} 的密码",
            null,
            new { PasswordReset = true },
            nowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var rows = (await connection.QueryAsync<RoleRow>(
            new CommandDefinition(
                RoleSelectSql + " ORDER BY r.is_system DESC, r.name, r.id;",
                cancellationToken: cancellationToken))).AsList();
        var permissions = await GetPermissionsByRoleIdsAsync(
            connection,
            rows.Select(row => row.Id).ToArray(),
            cancellationToken);
        return rows.Select(row => ToRoleDetails(row, permissions)).ToArray();
    }

    public async Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id,
                   code AS Code,
                   name AS Name,
                   module AS Module,
                   description AS Description
            FROM permissions
            WHERE is_active = 1
            ORDER BY module, code;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return (await connection.QueryAsync<PermissionDetails>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<RoleDetails> CreateRoleAsync(
        CreateRoleData role,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO roles
                (code, name, description, is_system, is_active, version,
                 created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @Name, @Description, 0, 1, 1,
                 @CreatedAtUtc, @ActorUserId,
                 @CreatedAtUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureIdsExistAsync(
                connection,
                transaction,
                "permissions",
                role.PermissionIds,
                "permissionIds",
                cancellationToken);
            var roleId = await connection.QuerySingleAsync<ulong>(
                new CommandDefinition(sql, role, transaction, cancellationToken: cancellationToken));
            await ReplaceRolePermissionsAsync(
                connection,
                transaction,
                roleId,
                role.PermissionIds,
                role.ActorUserId,
                role.CreatedAtUtc,
                cancellationToken);
            await WriteSecurityAuditAsync(
                connection,
                transaction,
                role.ActorUserId,
                "security.role.created",
                "Role",
                roleId,
                role.Code,
                $"创建角色 {role.Name}",
                null,
                new
                {
                    role.Code,
                    role.Name,
                    role.Description,
                    role.PermissionIds,
                    IsActive = true,
                },
                role.CreatedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetRoleAsync(connection, roleId, cancellationToken)
                ?? throw new InvalidOperationException("The created role could not be loaded.");
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw new ConflictException("角色代码已存在。");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<RoleDetails?> UpdateRoleAsync(
        ulong roleId,
        UpdateRoleData role,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE roles
            SET name = @Name,
                description = @Description,
                is_active = @IsActive,
                version = version + 1,
                updated_at_utc = @UpdatedAtUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @RoleId
              AND version = @Version
              AND code <> 'administrator'
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = await GetAuditRoleSnapshotAsync(
                connection,
                transaction,
                roleId,
                cancellationToken);
            if (before is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await EnsureIdsExistAsync(
                connection,
                transaction,
                "permissions",
                role.PermissionIds,
                "permissionIds",
                cancellationToken);
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        RoleId = roleId,
                        role.Name,
                        role.Description,
                        role.IsActive,
                        role.Version,
                        role.UpdatedAtUtc,
                        role.ActorUserId,
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await ReplaceRolePermissionsAsync(
                connection,
                transaction,
                roleId,
                role.PermissionIds,
                role.ActorUserId,
                role.UpdatedAtUtc,
                cancellationToken);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE users AS u
                    INNER JOIN user_roles AS ur ON ur.user_id = u.id
                    SET u.security_version = u.security_version + 1
                    WHERE ur.role_id = @RoleId;
                    """,
                    new { RoleId = roleId },
                    transaction,
                    cancellationToken: cancellationToken));
            await WriteSecurityAuditAsync(
                connection,
                transaction,
                role.ActorUserId,
                "security.role.updated",
                "Role",
                roleId,
                before.Code,
                $"更新角色 {role.Name}",
                before,
                new
                {
                    role.Name,
                    role.Description,
                    role.IsActive,
                    role.PermissionIds,
                },
                role.UpdatedAtUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetRoleAsync(connection, roleId, cancellationToken);
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    private static async Task<UserAuthenticationRecord?> QueryAuthenticationRecordAsync(
        DbConnection connection,
        string predicate,
        object parameters,
        CancellationToken cancellationToken)
    {
        var sql =
            $"""
            SELECT id AS Id,
                   username AS Username,
                   display_name AS DisplayName,
                   email AS Email,
                   password_hash AS PasswordHash,
                   is_active AS IsActive,
                   failed_login_count AS FailedLoginCount,
                   lockout_end_utc AS LockoutEndUtc,
                   security_version AS SecurityVersion
            FROM users
            WHERE {predicate}
              AND deleted_at_utc IS NULL;
            """;
        return await connection.QuerySingleOrDefaultAsync<UserAuthenticationRecord>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    private static async Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
        DbConnection connection,
        ulong userId,
        CancellationToken cancellationToken)
    {
        const string userSql =
            """
            SELECT id AS Id,
                   username AS Username,
                   display_name AS DisplayName,
                   email AS Email,
                   security_version AS SecurityVersion
            FROM users
            WHERE id = @UserId
              AND is_active = 1
              AND deleted_at_utc IS NULL;
            """;
        var user = await connection.QuerySingleOrDefaultAsync<AuthenticatedUserRow>(
            new CommandDefinition(
                userSql,
                new { UserId = userId },
                cancellationToken: cancellationToken));
        if (user is null)
        {
            return null;
        }

        var roles = (await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT r.code
                FROM roles AS r
                INNER JOIN user_roles AS ur ON ur.role_id = r.id
                WHERE ur.user_id = @UserId
                  AND r.is_active = 1
                  AND r.deleted_at_utc IS NULL
                ORDER BY r.code;
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken))).AsList();
        var permissions = (await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT DISTINCT p.code
                FROM permissions AS p
                INNER JOIN role_permissions AS rp ON rp.permission_id = p.id
                INNER JOIN roles AS r ON r.id = rp.role_id
                INNER JOIN user_roles AS ur ON ur.role_id = r.id
                WHERE ur.user_id = @UserId
                  AND p.is_active = 1
                  AND r.is_active = 1
                  AND r.deleted_at_utc IS NULL
                ORDER BY p.code;
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken))).AsList();
        return new AuthenticatedUser(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Email,
            user.SecurityVersion,
            roles,
            permissions);
    }

    private static async Task<ulong> InsertUserAsync(
        DbConnection connection,
        DbTransaction transaction,
        string username,
        string normalizedUsername,
        string displayName,
        string? email,
        string? normalizedEmail,
        string passwordHash,
        ulong? actorUserId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO users
                (username, normalized_username, display_name,
                 email, normalized_email, password_hash,
                 is_active, failed_login_count,
                 password_changed_at_utc, security_version, version,
                 created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@Username, @NormalizedUsername, @DisplayName,
                 @Email, @NormalizedEmail, @PasswordHash,
                 1, 0,
                 @CreatedAtUtc, 1, 1,
                 @CreatedAtUtc, @ActorUserId,
                 @CreatedAtUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        return await connection.QuerySingleAsync<ulong>(
            new CommandDefinition(
                sql,
                new
                {
                    Username = username,
                    NormalizedUsername = normalizedUsername,
                    DisplayName = displayName,
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    PasswordHash = passwordHash,
                    ActorUserId = actorUserId,
                    CreatedAtUtc = createdAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<UserDetails?> GetUserAsync(
        DbConnection connection,
        ulong userId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id,
                   username AS Username,
                   display_name AS DisplayName,
                   email AS Email,
                   is_active AS IsActive,
                   last_login_at_utc AS LastLoginAtUtc,
                   lockout_end_utc AS LockoutEndUtc,
                   version AS Version
            FROM users
            WHERE id = @UserId
              AND deleted_at_utc IS NULL;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            new CommandDefinition(
                sql,
                new { UserId = userId },
                cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var roles = await GetRolesByUserIdsAsync(
            connection,
            [userId],
            cancellationToken);
        return ToUserDetails(row, roles);
    }

    private static async Task<RoleDetails?> GetRoleAsync(
        DbConnection connection,
        ulong roleId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<RoleRow>(
            new CommandDefinition(
                RoleSelectSql + " AND r.id = @RoleId;",
                new { RoleId = roleId },
                cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var permissions = await GetPermissionsByRoleIdsAsync(
            connection,
            [roleId],
            cancellationToken);
        return ToRoleDetails(row, permissions);
    }

    private static async Task<IReadOnlyDictionary<ulong, IReadOnlyList<RoleReference>>>
        GetRolesByUserIdsAsync(
            DbConnection connection,
            ulong[] userIds,
            CancellationToken cancellationToken)
    {
        if (userIds.Length == 0)
        {
            return new Dictionary<ulong, IReadOnlyList<RoleReference>>();
        }

        var rows = await connection.QueryAsync<UserRoleRow>(
            new CommandDefinition(
                """
                SELECT ur.user_id AS UserId,
                       r.id AS Id,
                       r.code AS Code,
                       r.name AS Name
                FROM user_roles AS ur
                INNER JOIN roles AS r ON r.id = ur.role_id
                WHERE ur.user_id IN @UserIds
                ORDER BY r.name, r.id;
                """,
                new { UserIds = userIds },
                cancellationToken: cancellationToken));
        return rows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RoleReference>)group
                    .Select(row => new RoleReference(row.Id, row.Code, row.Name))
                    .ToArray());
    }

    private static async Task<
        IReadOnlyDictionary<ulong, IReadOnlyList<PermissionReference>>>
        GetPermissionsByRoleIdsAsync(
            DbConnection connection,
            ulong[] roleIds,
            CancellationToken cancellationToken)
    {
        if (roleIds.Length == 0)
        {
            return new Dictionary<ulong, IReadOnlyList<PermissionReference>>();
        }

        var rows = await connection.QueryAsync<RolePermissionRow>(
            new CommandDefinition(
                """
                SELECT rp.role_id AS RoleId,
                       p.id AS Id,
                       p.code AS Code,
                       p.name AS Name
                FROM role_permissions AS rp
                INNER JOIN permissions AS p ON p.id = rp.permission_id
                WHERE rp.role_id IN @RoleIds
                ORDER BY p.module, p.code;
                """,
                new { RoleIds = roleIds },
                cancellationToken: cancellationToken));
        return rows
            .GroupBy(row => row.RoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PermissionReference>)group
                    .Select(row => new PermissionReference(row.Id, row.Code, row.Name))
                    .ToArray());
    }

    private static async Task ReplaceUserRolesAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong userId,
        IReadOnlyCollection<ulong> roleIds,
        ulong actorUserId,
        DateTime assignedAtUtc,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM user_roles WHERE user_id = @UserId;",
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO user_roles
                    (user_id, role_id, assigned_at_utc, assigned_by_user_id)
                SELECT @UserId, id, @AssignedAtUtc, @ActorUserId
                FROM roles
                WHERE id IN @RoleIds;
                """,
                new
                {
                    UserId = userId,
                    RoleIds = roleIds,
                    AssignedAtUtc = assignedAtUtc,
                    ActorUserId = actorUserId,
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task ReplaceRolePermissionsAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong roleId,
        IReadOnlyCollection<ulong> permissionIds,
        ulong actorUserId,
        DateTime assignedAtUtc,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM role_permissions WHERE role_id = @RoleId;",
                new { RoleId = roleId },
                transaction,
                cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO role_permissions
                    (role_id, permission_id, assigned_at_utc, assigned_by_user_id)
                SELECT @RoleId, id, @AssignedAtUtc, @ActorUserId
                FROM permissions
                WHERE id IN @PermissionIds;
                """,
                new
                {
                    RoleId = roleId,
                    PermissionIds = permissionIds,
                    AssignedAtUtc = assignedAtUtc,
                    ActorUserId = actorUserId,
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task EnsureIdsExistAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyCollection<ulong> ids,
        string field,
        CancellationToken cancellationToken)
    {
        var supportedTable = table switch
        {
            "roles" => "roles",
            "permissions" => "permissions",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        var count = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                $"SELECT COUNT(*) FROM {supportedTable} WHERE id IN @Ids AND is_active = 1;",
                new { Ids = ids },
                transaction,
                cancellationToken: cancellationToken));
        if (count != ids.Count)
        {
            throw FlowHearthValidationException.For(field, "包含不存在或已停用的项目。");
        }
    }

    private static async Task<UserAuditIdentity?> GetAuditUserIdentityAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong userId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<UserAuditIdentity>(
            new CommandDefinition(
                """
                SELECT username AS Username,display_name AS DisplayName
                FROM users
                WHERE id=@UserId AND deleted_at_utc IS NULL
                FOR UPDATE;
                """,
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<UserAuditSnapshot?> GetAuditUserSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong userId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<UserAuditRow>(
            new CommandDefinition(
                """
                SELECT username AS Username,display_name AS DisplayName,
                       email AS Email,is_active AS IsActive
                FROM users
                WHERE id=@UserId AND deleted_at_utc IS NULL
                FOR UPDATE;
                """,
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var roleIds = (await connection.QueryAsync<ulong>(
            new CommandDefinition(
                "SELECT role_id FROM user_roles WHERE user_id=@UserId ORDER BY role_id;",
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();
        return new UserAuditSnapshot(
            row.Username,
            row.DisplayName,
            row.Email,
            row.IsActive,
            roleIds);
    }

    private static async Task<RoleAuditSnapshot?> GetAuditRoleSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong roleId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<RoleAuditRow>(
            new CommandDefinition(
                """
                SELECT code AS Code,name AS Name,description AS Description,
                       is_active AS IsActive
                FROM roles
                WHERE id=@RoleId AND deleted_at_utc IS NULL
                FOR UPDATE;
                """,
                new { RoleId = roleId },
                transaction,
                cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var permissionIds = (await connection.QueryAsync<ulong>(
            new CommandDefinition(
                "SELECT permission_id FROM role_permissions WHERE role_id=@RoleId ORDER BY permission_id;",
                new { RoleId = roleId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();
        return new RoleAuditSnapshot(
            row.Code,
            row.Name,
            row.Description,
            row.IsActive,
            permissionIds);
    }

    private static Task<int> WriteSecurityAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong? actorUserId,
        string action,
        string entityType,
        ulong entityId,
        string? entityCode,
        string summary,
        object? before,
        object? after,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO audit_logs
                    (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                     entity_code,summary,before_json,after_json)
                VALUES
                    (@NowUtc,@ActorUserId,@Action,@EntityType,@EntityId,
                     @EntityCode,@Summary,@BeforeJson,@AfterJson);
                """,
                new
                {
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    EntityCode = entityCode,
                    Summary = summary,
                    BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
                    AfterJson = after is null ? null : JsonSerializer.Serialize(after),
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static UserSummary ToUserSummary(
        UserRow row,
        IReadOnlyDictionary<ulong, IReadOnlyList<RoleReference>> roles)
    {
        return new UserSummary(
            row.Id,
            row.Username,
            row.DisplayName,
            row.Email,
            row.IsActive,
            row.LastLoginAtUtc,
            row.LockoutEndUtc,
            row.Version,
            roles.GetValueOrDefault(row.Id) ?? []);
    }

    private static UserDetails ToUserDetails(
        UserRow row,
        IReadOnlyDictionary<ulong, IReadOnlyList<RoleReference>> roles)
    {
        return new UserDetails(
            row.Id,
            row.Username,
            row.DisplayName,
            row.Email,
            row.IsActive,
            row.LastLoginAtUtc,
            row.LockoutEndUtc,
            row.Version,
            roles.GetValueOrDefault(row.Id) ?? []);
    }

    private static RoleDetails ToRoleDetails(
        RoleRow row,
        IReadOnlyDictionary<ulong, IReadOnlyList<PermissionReference>> permissions)
    {
        return new RoleDetails(
            row.Id,
            row.Code,
            row.Name,
            row.Description,
            row.IsSystem,
            row.IsActive,
            row.Version,
            permissions.GetValueOrDefault(row.Id) ?? []);
    }

    private static async Task RollbackIfNeededAsync(
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.Connection is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private const string RoleSelectSql =
        """
        SELECT r.id AS Id,
               r.code AS Code,
               r.name AS Name,
               r.description AS Description,
               r.is_system AS IsSystem,
               r.is_active AS IsActive,
               r.version AS Version
        FROM roles AS r
        WHERE r.deleted_at_utc IS NULL
        """;

    private sealed class AuthenticatedUserRow
    {
        public ulong Id { get; init; }

        public required string Username { get; init; }

        public required string DisplayName { get; init; }

        public string? Email { get; init; }

        public ulong SecurityVersion { get; init; }
    }

    private sealed class UserAuditIdentity
    {
        public required string Username { get; init; }

        public required string DisplayName { get; init; }
    }

    private sealed class UserAuditRow
    {
        public required string Username { get; init; }

        public required string DisplayName { get; init; }

        public string? Email { get; init; }

        public bool IsActive { get; init; }
    }

    private sealed record UserAuditSnapshot(
        string Username,
        string DisplayName,
        string? Email,
        bool IsActive,
        IReadOnlyList<ulong> RoleIds);

    private sealed class RoleAuditRow
    {
        public required string Code { get; init; }

        public required string Name { get; init; }

        public string? Description { get; init; }

        public bool IsActive { get; init; }
    }

    private sealed record RoleAuditSnapshot(
        string Code,
        string Name,
        string? Description,
        bool IsActive,
        IReadOnlyList<ulong> PermissionIds);

    private sealed class UserRow
    {
        public ulong Id { get; init; }

        public required string Username { get; init; }

        public required string DisplayName { get; init; }

        public string? Email { get; init; }

        public bool IsActive { get; init; }

        public DateTime? LastLoginAtUtc { get; init; }

        public DateTime? LockoutEndUtc { get; init; }

        public ulong Version { get; init; }
    }

    private sealed class RoleRow
    {
        public ulong Id { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }

        public string? Description { get; init; }

        public bool IsSystem { get; init; }

        public bool IsActive { get; init; }

        public ulong Version { get; init; }
    }

    private sealed class UserRoleRow
    {
        public ulong UserId { get; init; }

        public ulong Id { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }
    }

    private sealed class FailedLoginState
    {
        public uint FailedLoginCount { get; init; }

        public DateTime? LockoutEndUtc { get; init; }
    }

    private sealed class RolePermissionRow
    {
        public ulong RoleId { get; init; }

        public ulong Id { get; init; }

        public required string Code { get; init; }

        public required string Name { get; init; }
    }
}
