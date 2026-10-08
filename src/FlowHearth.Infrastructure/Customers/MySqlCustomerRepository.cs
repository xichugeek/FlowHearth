using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Customers;

namespace FlowHearth.Infrastructure.Customers;

public sealed class MySqlCustomerRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null)
    : ICustomerRepository
{
    public async Task<PagedResult<CustomerSummary>> ListAsync(
        CustomerListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var orderBy = GetOrderBy(criteria.SortBy, criteria.SortDescending);
        var sql =
            $$"""
            WITH contact_counts AS
            (
                SELECT customer_id, COUNT(*) AS ContactCount
                FROM contacts
                WHERE deleted_at_utc IS NULL
                GROUP BY customer_id
            ),
            next_followups AS
            (
                SELECT customer_id, MIN(next_follow_up_at_utc) AS NextFollowUpAtUtc
                FROM customer_followups
                WHERE deleted_at_utc IS NULL
                  AND next_follow_up_at_utc IS NOT NULL
                GROUP BY customer_id
            )
            SELECT c.id AS Id,
                   c.customer_code AS Code,
                   c.name AS Name,
                   c.short_name AS ShortName,
                   c.industry AS Industry,
                   c.business_status AS Status,
                   c.customer_level AS Level,
                   c.phone AS Phone,
                   c.email AS Email,
                   (c.archived_at_utc IS NOT NULL) AS IsArchived,
                   nf.NextFollowUpAtUtc AS NextFollowUpAtUtc,
                   COALESCE(cc.ContactCount, 0) AS ContactCount,
                   c.version AS Version,
                   c.updated_at_utc AS UpdatedAtUtc,
                   pc.name AS PrimaryContactName,
                   COALESCE(NULLIF(pc.mobile, ''), NULLIF(pc.phone, ''), NULLIF(pc.email, '')) AS PrimaryContactMethod
            FROM customers AS c
            LEFT JOIN contact_counts AS cc ON cc.customer_id = c.id
            LEFT JOIN next_followups AS nf ON nf.customer_id = c.id
            LEFT JOIN contacts AS pc
              ON pc.id =
                 (
                     SELECT contact.id
                     FROM contacts AS contact
                     WHERE contact.customer_id = c.id
                       AND contact.deleted_at_utc IS NULL
                     ORDER BY contact.is_primary DESC, contact.id
                     LIMIT 1
                 )
            WHERE (@ArchiveMode = 2
                   OR (@ArchiveMode = 0 AND c.archived_at_utc IS NULL)
                   OR (@ArchiveMode = 1 AND c.archived_at_utc IS NOT NULL))
              AND (@SearchPattern IS NULL
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR c.short_name LIKE @SearchPattern ESCAPE '='
                   OR c.phone LIKE @SearchPattern ESCAPE '=')
              AND (@NextFollowUpBeforeUtc IS NULL
                   OR nf.NextFollowUpAtUtc <= @NextFollowUpBeforeUtc)
              AND (@Status IS NULL OR c.business_status = @Status)
              AND (@Level IS NULL OR c.customer_level = @Level)
              AND (@ProvinceCode IS NULL OR c.province_code = @ProvinceCode)
              AND (@CityCode IS NULL OR c.city_code = @CityCode)
              AND (@DistrictCode IS NULL OR c.district_code = @DistrictCode)
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM customers AS c
            WHERE (@ArchiveMode = 2
                   OR (@ArchiveMode = 0 AND c.archived_at_utc IS NULL)
                   OR (@ArchiveMode = 1 AND c.archived_at_utc IS NOT NULL))
              AND (@SearchPattern IS NULL
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR c.short_name LIKE @SearchPattern ESCAPE '='
                   OR c.phone LIKE @SearchPattern ESCAPE '=')
              AND (@NextFollowUpBeforeUtc IS NULL
                   OR EXISTS
                   (
                       SELECT 1
                       FROM customer_followups AS f
                       WHERE f.customer_id = c.id
                         AND f.deleted_at_utc IS NULL
                         AND f.next_follow_up_at_utc IS NOT NULL
                       GROUP BY f.customer_id
                       HAVING MIN(f.next_follow_up_at_utc) <= @NextFollowUpBeforeUtc
                   ))
              AND (@Status IS NULL OR c.business_status = @Status)
              AND (@Level IS NULL OR c.customer_level = @Level)
              AND (@ProvinceCode IS NULL OR c.province_code = @ProvinceCode)
              AND (@CityCode IS NULL OR c.city_code = @CityCode)
              AND (@DistrictCode IS NULL OR c.district_code = @DistrictCode);
            """;
        var parameters = new
        {
            ArchiveMode = (int)criteria.ArchiveMode,
            SearchPattern = CreateSearchPattern(criteria.Search),
            criteria.NextFollowUpBeforeUtc,
            Status = criteria.Status?.ToString(),
            Level = criteria.Level?.ToString(),
            criteria.ProvinceCode,
            criteria.CityCode,
            criteria.DistrictCode,
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };

        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));
        var items = (await result.ReadAsync<CustomerSummaryRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new PagedResult<CustomerSummary>(
            items,
            criteria.Page,
            criteria.PageSize,
            total);
    }

    public async Task<CustomerDetails?> GetAsync(
        ulong customerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await GetAsync(connection, null, customerId, cancellationToken);
    }

    public async Task<CustomerDetails> CreateAsync(
        CreateCustomerData data,
        CancellationToken cancellationToken)
    {
        const string nextSequenceSql =
            """
            INSERT INTO number_sequences
                (sequence_name, current_value, version, updated_at_utc)
            VALUES
                (@SequenceName, LAST_INSERT_ID(1), 1, @NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value = LAST_INSERT_ID(current_value + 1),
                version = version + 1,
                updated_at_utc = VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO customers
                (customer_code, name, short_name, industry, business_status,
                 customer_level, phone, email,
                 website, province_code, city_code, district_code, address, notes, version,
                 created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @Name, @ShortName, @Industry, @Status,
                 @Level, @Phone, @Email,
                 @Website, @ProvinceCode, @CityCode, @DistrictCode, @Address, @Notes, 1,
                 @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var sequenceName = $"customer:{data.NowUtc.Year:D4}";
        var nextValue = await connection.QuerySingleAsync<ulong>(
            Command(
                nextSequenceSql,
                new { SequenceName = sequenceName, data.NowUtc },
                transaction,
                cancellationToken));
        var prefix = runtimeSettingsProvider is null
            ? CustomerCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Customer;
        var code = CustomerCode.Format(prefix, data.NowUtc.Year, nextValue);
        var customerId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    Code = code,
                    data.Name,
                    data.ShortName,
                    data.Industry,
                    Status = data.Status.ToString(),
                    Level = data.Level.ToString(),
                    data.Phone,
                    data.Email,
                    data.Website,
                    data.ProvinceCode,
                    data.CityCode,
                    data.DistrictCode,
                    data.Address,
                    data.Notes,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "customer.created",
            "customer",
            customerId,
            code,
            $"创建客户 {data.Name}",
            null,
            new { code, data.Name, status = data.Status.ToString(), level = data.Level.ToString() },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Created customer could not be loaded.");
    }

    public async Task<CustomerDetails?> UpdateAsync(
        ulong customerId,
        UpdateCustomerData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE customers
            SET name = @Name,
                short_name = @ShortName,
                industry = @Industry,
                business_status = @Status,
                customer_level = @Level,
                phone = @Phone,
                email = @Email,
                website = @Website,
                province_code = @ProvinceCode,
                city_code = @CityCode,
                district_code = @DistrictCode,
                address = @Address,
                notes = @Notes,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @CustomerId
              AND version = @Version
              AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, customerId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    CustomerId = customerId,
                    data.Name,
                    data.ShortName,
                    data.Industry,
                    Status = data.Status.ToString(),
                    Level = data.Level.ToString(),
                    data.Phone,
                    data.Email,
                    data.Website,
                    data.ProvinceCode,
                    data.CityCode,
                    data.DistrictCode,
                    data.Address,
                    data.Notes,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "customer.updated",
            "customer",
            customerId,
            before?.Code,
            $"更新客户 {data.Name}",
            before,
            new { data.Name, data.ShortName, data.Industry, status = data.Status.ToString(), level = data.Level.ToString(), data.Phone, data.Email, data.Website, data.ProvinceCode, data.CityCode, data.DistrictCode, data.Address, data.Notes },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(customerId, cancellationToken);
    }

    public async Task<CustomerDetails?> SetArchivedAsync(
        ulong customerId,
        SetCustomerArchiveData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE customers
            SET archived_at_utc = @ArchivedAtUtc,
                archived_by_user_id = @ArchivedByUserId,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @CustomerId
              AND version = @Version
              AND ((@Archived = 1 AND archived_at_utc IS NULL)
                   OR (@Archived = 0 AND archived_at_utc IS NOT NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, customerId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    CustomerId = customerId,
                    data.Archived,
                    ArchivedAtUtc = data.Archived ? data.NowUtc : (DateTime?)null,
                    ArchivedByUserId = data.Archived ? data.ActorUserId : (ulong?)null,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var action = data.Archived ? "customer.archived" : "customer.restored";
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            action,
            "customer",
            customerId,
            before?.Code,
            data.Archived ? $"归档客户 {before?.Name}" : $"恢复客户 {before?.Name}",
            new { archived = !data.Archived },
            new { archived = data.Archived },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(customerId, cancellationToken);
    }

    public async Task<ContactDetails> CreateContactAsync(
        ulong customerId,
        CreateContactData data,
        CancellationToken cancellationToken)
    {
        const string insertSql =
            """
            INSERT INTO contacts
                (customer_id, name, title, department, mobile, phone, email,
                 wechat, is_primary, notes, version,
                 created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@CustomerId, @Name, @Title, @Department, @Mobile, @Phone, @Email,
                 @WeChat, @IsPrimary, @Notes, 1,
                 @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await GetMutableCustomerAsync(connection, transaction, customerId, cancellationToken);
        if (data.IsPrimary)
        {
            await ClearPrimaryContactsAsync(connection, transaction, customerId, null, data.ActorUserId, data.NowUtc, cancellationToken);
        }

        var contactId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    CustomerId = customerId,
                    data.Name,
                    data.Title,
                    data.Department,
                    data.Mobile,
                    data.Phone,
                    data.Email,
                    data.WeChat,
                    data.IsPrimary,
                    data.Notes,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "contact.created",
            "contact",
            contactId,
            customer.Code,
            $"为客户 {customer.Name} 添加联系人 {data.Name}",
            null,
            new { customerId, data.Name, data.IsPrimary },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetContactAsync(connection, null, customerId, contactId, cancellationToken)
            ?? throw new InvalidOperationException("Created contact could not be loaded.");
    }

    public async Task<ContactDetails?> UpdateContactAsync(
        ulong customerId,
        ulong contactId,
        UpdateContactData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE contacts
            SET name = @Name,
                title = @Title,
                department = @Department,
                mobile = @Mobile,
                phone = @Phone,
                email = @Email,
                wechat = @WeChat,
                is_primary = @IsPrimary,
                notes = @Notes,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @ContactId
              AND customer_id = @CustomerId
              AND version = @Version
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await GetMutableCustomerAsync(connection, transaction, customerId, cancellationToken);
        var before = await GetContactAsync(connection, transaction, customerId, contactId, cancellationToken);
        if (data.IsPrimary)
        {
            await ClearPrimaryContactsAsync(connection, transaction, customerId, contactId, data.ActorUserId, data.NowUtc, cancellationToken);
        }

        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    CustomerId = customerId,
                    ContactId = contactId,
                    data.Name,
                    data.Title,
                    data.Department,
                    data.Mobile,
                    data.Phone,
                    data.Email,
                    data.WeChat,
                    data.IsPrimary,
                    data.Notes,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "contact.updated",
            "contact",
            contactId,
            customer.Code,
            $"更新客户 {customer.Name} 的联系人 {data.Name}",
            before,
            new { customerId, data.Name, data.IsPrimary },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetContactAsync(connection, null, customerId, contactId, cancellationToken);
    }

    public async Task<bool> DeleteContactAsync(
        ulong customerId,
        ulong contactId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE contacts
            SET deleted_at_utc = @NowUtc,
                deleted_by_user_id = @ActorUserId,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId,
                version = version + 1,
                is_primary = 0
            WHERE id = @ContactId
              AND customer_id = @CustomerId
              AND version = @Version
              AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await GetMutableCustomerAsync(connection, transaction, customerId, cancellationToken);
        var before = await GetContactAsync(connection, transaction, customerId, contactId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, new { CustomerId = customerId, ContactId = contactId, Version = version, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            "contact.deleted",
            "contact",
            contactId,
            customer.Code,
            $"删除客户 {customer.Name} 的联系人 {before?.Name}",
            before,
            null,
            nowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<CustomerFollowUpDetails> CreateFollowUpAsync(
        ulong customerId,
        CreateCustomerFollowUpData data,
        CancellationToken cancellationToken)
    {
        const string insertSql =
            """
            INSERT INTO customer_followups
                (customer_id, contact_id, method, occurred_at_utc, summary,
                 details, next_follow_up_at_utc, created_at_utc, created_by_user_id)
            VALUES
                (@CustomerId, @ContactId, @Method, @OccurredAtUtc, @Summary,
                 @Details, @NextFollowUpAtUtc, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await GetMutableCustomerAsync(connection, transaction, customerId, cancellationToken);
        if (data.ContactId.HasValue)
        {
            var contactExists = await connection.ExecuteScalarAsync<bool>(
                Command(
                    "SELECT EXISTS(SELECT 1 FROM contacts WHERE id = @ContactId AND customer_id = @CustomerId AND deleted_at_utc IS NULL);",
                    new { data.ContactId, CustomerId = customerId },
                    transaction,
                    cancellationToken));
            if (!contactExists)
            {
                throw new ConflictException("联系人状态已改变，请刷新后重试。");
            }
        }

        var followUpId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    CustomerId = customerId,
                    data.ContactId,
                    Method = data.Method.ToString(),
                    data.OccurredAtUtc,
                    data.Summary,
                    data.Details,
                    data.NextFollowUpAtUtc,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "customer_followup.created",
            "customer_followup",
            followUpId,
            customer.Code,
            $"记录客户 {customer.Name} 跟进：{data.Summary}",
            null,
            new { customerId, data.ContactId, method = data.Method.ToString(), data.OccurredAtUtc, data.Summary, data.NextFollowUpAtUtc },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetFollowUpAsync(connection, null, customerId, followUpId, cancellationToken)
            ?? throw new InvalidOperationException("Created follow-up could not be loaded.");
    }

    private static async Task<CustomerDetails?> GetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong customerId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id,
                   customer_code AS Code,
                   name AS Name,
                   short_name AS ShortName,
                   industry AS Industry,
                   business_status AS Status,
                   customer_level AS Level,
                   phone AS Phone,
                   email AS Email,
                   website AS Website,
                   province_code AS ProvinceCode,
                   city_code AS CityCode,
                   district_code AS DistrictCode,
                   address AS Address,
                   notes AS Notes,
                   (archived_at_utc IS NOT NULL) AS IsArchived,
                   archived_at_utc AS ArchivedAtUtc,
                   version AS Version,
                   created_at_utc AS CreatedAtUtc,
                   updated_at_utc AS UpdatedAtUtc
            FROM customers
            WHERE id = @CustomerId;

            SELECT id AS Id,
                   customer_id AS CustomerId,
                   name AS Name,
                   title AS Title,
                   department AS Department,
                   mobile AS Mobile,
                   phone AS Phone,
                   email AS Email,
                   wechat AS WeChat,
                   is_primary AS IsPrimary,
                   notes AS Notes,
                   version AS Version,
                   created_at_utc AS CreatedAtUtc,
                   updated_at_utc AS UpdatedAtUtc
            FROM contacts
            WHERE customer_id = @CustomerId
              AND deleted_at_utc IS NULL
            ORDER BY is_primary DESC, name, id;

            SELECT f.id AS Id,
                   f.customer_id AS CustomerId,
                   f.contact_id AS ContactId,
                   c.name AS ContactName,
                   f.method AS Method,
                   f.occurred_at_utc AS OccurredAtUtc,
                   f.summary AS Summary,
                   f.details AS Details,
                   f.next_follow_up_at_utc AS NextFollowUpAtUtc,
                   u.display_name AS CreatedByDisplayName,
                   f.created_at_utc AS CreatedAtUtc
            FROM customer_followups AS f
            LEFT JOIN contacts AS c ON c.id = f.contact_id
            LEFT JOIN users AS u ON u.id = f.created_by_user_id
            WHERE f.customer_id = @CustomerId
              AND f.deleted_at_utc IS NULL
            ORDER BY f.occurred_at_utc DESC, f.id DESC;
            """;
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new { CustomerId = customerId }, transaction, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<CustomerHeader>();
        if (header is null)
        {
            return null;
        }

        var contacts = (await result.ReadAsync<ContactRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        var followUps = (await result.ReadAsync<FollowUpRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        return new CustomerDetails(
            header.Id,
            header.Code,
            header.Name,
            header.ShortName,
            header.Industry,
            header.Phone,
            header.Email,
            header.Website,
            header.Address,
            header.Notes,
            header.IsArchived,
            header.ArchivedAtUtc,
            header.Version,
            header.CreatedAtUtc,
            header.UpdatedAtUtc,
            contacts,
            followUps,
            Enum.Parse<CustomerStatus>(header.Status, true),
            Enum.Parse<CustomerLevel>(header.Level, true),
            header.ProvinceCode,
            header.CityCode,
            header.DistrictCode);
    }

    private static async Task<ContactDetails?> GetContactAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong customerId,
        ulong contactId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id, customer_id AS CustomerId, name AS Name,
                   title AS Title, department AS Department, mobile AS Mobile,
                   phone AS Phone, email AS Email, wechat AS WeChat,
                   is_primary AS IsPrimary, notes AS Notes, version AS Version,
                   created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
            FROM contacts
            WHERE id = @ContactId
              AND customer_id = @CustomerId
              AND deleted_at_utc IS NULL;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<ContactRow>(
            Command(sql, new { CustomerId = customerId, ContactId = contactId }, transaction, cancellationToken));
        return row?.ToDetails();
    }

    private static async Task<CustomerFollowUpDetails?> GetFollowUpAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong customerId,
        ulong followUpId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT f.id AS Id, f.customer_id AS CustomerId,
                   f.contact_id AS ContactId, c.name AS ContactName,
                   f.method AS Method, f.occurred_at_utc AS OccurredAtUtc,
                   f.summary AS Summary, f.details AS Details,
                   f.next_follow_up_at_utc AS NextFollowUpAtUtc,
                   u.display_name AS CreatedByDisplayName,
                   f.created_at_utc AS CreatedAtUtc
            FROM customer_followups AS f
            LEFT JOIN contacts AS c ON c.id = f.contact_id
            LEFT JOIN users AS u ON u.id = f.created_by_user_id
            WHERE f.id = @FollowUpId AND f.customer_id = @CustomerId
              AND f.deleted_at_utc IS NULL;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<FollowUpRow>(
            Command(sql, new { CustomerId = customerId, FollowUpId = followUpId }, transaction, cancellationToken));
        return row?.ToDetails();
    }

    private static async Task<CustomerMutationSnapshot> GetMutableCustomerAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        CancellationToken cancellationToken)
    {
        var customer = await GetSnapshotAsync(connection, transaction, customerId, cancellationToken);
        if (customer is null || customer.IsArchived)
        {
            throw new ConflictException("客户状态已改变，请刷新后重试。");
        }

        return customer;
    }

    private static Task<CustomerMutationSnapshot?> GetSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT customer_code AS Code, name AS Name,
                   short_name AS ShortName, industry AS Industry,
                   business_status AS Status, customer_level AS Level,
                   phone AS Phone, email AS Email, website AS Website,
                   province_code AS ProvinceCode, city_code AS CityCode,
                   district_code AS DistrictCode,
                   address AS Address, notes AS Notes,
                   (archived_at_utc IS NOT NULL) AS IsArchived,
                   version AS Version
            FROM customers
            WHERE id = @CustomerId
            FOR UPDATE;
            """;
        return connection.QuerySingleOrDefaultAsync<CustomerMutationSnapshot>(
            Command(sql, new { CustomerId = customerId }, transaction, cancellationToken));
    }

    private static Task<int> ClearPrimaryContactsAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        ulong? exceptContactId,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE contacts
            SET is_primary = 0,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE customer_id = @CustomerId
              AND deleted_at_utc IS NULL
              AND is_primary = 1
              AND (@ExceptContactId IS NULL OR id <> @ExceptContactId);
            """;
        return connection.ExecuteAsync(
            Command(sql, new { CustomerId = customerId, ExceptContactId = exceptContactId, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
    }

    private static Task<int> WriteAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        string entityType,
        ulong entityId,
        string? entityCode,
        string summary,
        object? before,
        object? after,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO audit_logs
                (occurred_at_utc, actor_user_id, action, entity_type,
                 entity_id, entity_code, summary, before_json, after_json)
            VALUES
                (@OccurredAtUtc, @ActorUserId, @Action, @EntityType,
                 @EntityId, @EntityCode, @Summary, @BeforeJson, @AfterJson);
            """;
        return connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    OccurredAtUtc = occurredAtUtc,
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
                cancellationToken));
    }

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            sql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);
    }

    private static string? CreateSearchPattern(string? search)
    {
        return search is null
            ? null
            : $"%{search.Replace("=", "==", StringComparison.Ordinal).Replace("%", "=%", StringComparison.Ordinal).Replace("_", "=_", StringComparison.Ordinal)}%";
    }

    private static string GetOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"c.customer_code {direction}, c.id {direction}",
            "name" => $"c.name {direction}, c.id {direction}",
            "nextfollowupat" => $"nf.NextFollowUpAtUtc IS NULL, nf.NextFollowUpAtUtc {direction}, c.id {direction}",
            _ => $"c.updated_at_utc {direction}, c.id {direction}",
        };
    }

    private sealed class CustomerSummaryRow
    {
        public ulong Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? ShortName { get; init; }

        public string? Industry { get; init; }

        public string Status { get; init; } = string.Empty;

        public string Level { get; init; } = string.Empty;

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public bool IsArchived { get; init; }

        public DateTime? NextFollowUpAtUtc { get; init; }

        public int ContactCount { get; init; }

        public ulong Version { get; init; }

        public DateTime UpdatedAtUtc { get; init; }

        public string? PrimaryContactName { get; init; }

        public string? PrimaryContactMethod { get; init; }

        public CustomerSummary ToDetails() =>
            new(
                Id,
                Code,
                Name,
                ShortName,
                Industry,
                Phone,
                Email,
                IsArchived,
                NextFollowUpAtUtc,
                ContactCount,
                Version,
                UpdatedAtUtc,
                PrimaryContactName,
                PrimaryContactMethod,
                Enum.Parse<CustomerStatus>(Status, true),
                Enum.Parse<CustomerLevel>(Level, true));
    }

    private sealed class CustomerHeader
    {
        public ulong Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? ShortName { get; init; }

        public string? Industry { get; init; }

        public string Status { get; init; } = string.Empty;

        public string Level { get; init; } = string.Empty;

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public string? Website { get; init; }

        public string? ProvinceCode { get; init; }

        public string? CityCode { get; init; }

        public string? DistrictCode { get; init; }

        public string? Address { get; init; }

        public string? Notes { get; init; }

        public bool IsArchived { get; init; }

        public DateTime? ArchivedAtUtc { get; init; }

        public ulong Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class ContactRow
    {
        public ulong Id { get; init; }

        public ulong CustomerId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Department { get; init; }

        public string? Mobile { get; init; }

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public string? WeChat { get; init; }

        public bool IsPrimary { get; init; }

        public string? Notes { get; init; }

        public ulong Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime UpdatedAtUtc { get; init; }

        public ContactDetails ToDetails() =>
            new(Id, CustomerId, Name, Title, Department, Mobile, Phone, Email, WeChat, IsPrimary, Notes, Version, CreatedAtUtc, UpdatedAtUtc);
    }

    private sealed class FollowUpRow
    {
        public ulong Id { get; init; }

        public ulong CustomerId { get; init; }

        public ulong? ContactId { get; init; }

        public string? ContactName { get; init; }

        public string Method { get; init; } = string.Empty;

        public DateTime OccurredAtUtc { get; init; }

        public string Summary { get; init; } = string.Empty;

        public string? Details { get; init; }

        public DateTime? NextFollowUpAtUtc { get; init; }

        public string? CreatedByDisplayName { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public CustomerFollowUpDetails ToDetails() =>
            new(
                Id,
                CustomerId,
                ContactId,
                ContactName,
                Enum.Parse<CustomerFollowUpMethod>(Method, true),
                OccurredAtUtc,
                Summary,
                Details,
                NextFollowUpAtUtc,
                CreatedByDisplayName,
                CreatedAtUtc);
    }

    private sealed class CustomerMutationSnapshot
    {
        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? ShortName { get; init; }

        public string? Industry { get; init; }

        public string Status { get; init; } = string.Empty;

        public string Level { get; init; } = string.Empty;

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public string? Website { get; init; }

        public string? ProvinceCode { get; init; }

        public string? CityCode { get; init; }

        public string? DistrictCode { get; init; }

        public string? Address { get; init; }

        public string? Notes { get; init; }

        public bool IsArchived { get; init; }

        public ulong Version { get; init; }
    }
}
