using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Finance;

public sealed class MySqlSupplierRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    TimeProvider? timeProvider = null) : ISupplierRepository
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<PagedResult<SupplierSummary>> ListAsync(
        SupplierListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var orderBy = GetOrderBy(criteria.SortBy, criteria.SortDescending);
        var sql =
            $$"""
            WITH purchase_totals AS
            (
                SELECT supplier_id, COALESCE(SUM(total_amount), 0) AS TotalPurchased
                FROM purchase_orders
                WHERE archived_at_utc IS NULL AND status NOT IN ('Draft','Cancelled')
                GROUP BY supplier_id
            ),
            payment_totals AS
            (
                SELECT supplier_id, COALESCE(SUM(amount), 0) AS TotalPaid
                FROM payments
                WHERE archived_at_utc IS NULL
                GROUP BY supplier_id
            ),
            payable_totals AS
            (
                SELECT p.supplier_id,
                       COALESCE(SUM(p.amount - COALESCE(a.AllocatedAmount, 0)), 0)
                           AS OutstandingPayable
                FROM payables AS p
                LEFT JOIN
                (
                    SELECT payable_id, SUM(allocated_amount) AS AllocatedAmount
                    FROM payment_allocations
                    WHERE cancelled_at_utc IS NULL
                    GROUP BY payable_id
                ) AS a ON a.payable_id = p.id
                WHERE p.archived_at_utc IS NULL
                GROUP BY p.supplier_id
            )
            SELECT s.id AS Id,
                   s.supplier_code AS Code,
                   s.name AS Name,
                   s.short_name AS ShortName,
                   s.status AS Status,
                   s.category AS Category,
                   s.contact_name AS ContactName,
                   COALESCE(NULLIF(s.mobile, ''), NULLIF(s.phone, ''), NULLIF(s.email, ''))
                       AS ContactMethod,
                   COALESCE(pt.TotalPurchased, 0) AS TotalPurchased,
                   COALESCE(mt.TotalPaid, 0) AS TotalPaid,
                   COALESCE(apt.OutstandingPayable, 0) AS OutstandingPayable,
                   (s.archived_at_utc IS NOT NULL) AS IsArchived,
                   s.version AS Version,
                   s.updated_at_utc AS UpdatedAtUtc
            FROM suppliers AS s
            LEFT JOIN purchase_totals AS pt ON pt.supplier_id = s.id
            LEFT JOIN payment_totals AS mt ON mt.supplier_id = s.id
            LEFT JOIN payable_totals AS apt ON apt.supplier_id = s.id
            WHERE (@ArchiveMode = 2
                   OR (@ArchiveMode = 0 AND s.archived_at_utc IS NULL)
                   OR (@ArchiveMode = 1 AND s.archived_at_utc IS NOT NULL))
              AND (@Status IS NULL OR s.status = @Status)
              AND (@Category IS NULL OR s.category = @Category)
              AND (@SearchPattern IS NULL
                   OR CONVERT(s.supplier_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR s.name LIKE @SearchPattern ESCAPE '='
                   OR s.short_name LIKE @SearchPattern ESCAPE '='
                   OR s.contact_name LIKE @SearchPattern ESCAPE '=')
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM suppliers AS s
            WHERE (@ArchiveMode = 2
                   OR (@ArchiveMode = 0 AND s.archived_at_utc IS NULL)
                   OR (@ArchiveMode = 1 AND s.archived_at_utc IS NOT NULL))
              AND (@Status IS NULL OR s.status = @Status)
              AND (@Category IS NULL OR s.category = @Category)
              AND (@SearchPattern IS NULL
                   OR CONVERT(s.supplier_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR s.name LIKE @SearchPattern ESCAPE '='
                   OR s.short_name LIKE @SearchPattern ESCAPE '='
                   OR s.contact_name LIKE @SearchPattern ESCAPE '=');
            """;
        var parameters = new
        {
            ArchiveMode = (int)criteria.ArchiveMode,
            Status = criteria.Status?.ToString(),
            criteria.Category,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<SupplierSummaryRow>())
            .Select(MapSummary)
            .ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new PagedResult<SupplierSummary>(
            items,
            criteria.Page,
            criteria.PageSize,
            total);
    }

    public async Task<SupplierDetails?> GetAsync(
        ulong supplierId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT s.id AS Id,
                   s.supplier_code AS Code,
                   s.name AS Name,
                   s.short_name AS ShortName,
                   s.status AS Status,
                   s.category AS Category,
                   s.contact_name AS ContactName,
                   s.mobile AS Mobile,
                   s.phone AS Phone,
                   s.email AS Email,
                   s.wechat AS WeChat,
                   s.province AS Province,
                   s.city AS City,
                   s.address AS Address,
                   s.payment_terms AS PaymentTerms,
                   s.credit_days AS CreditDays,
                   s.bank_name AS BankName,
                   s.bank_account_name AS BankAccountName,
                   s.remark AS Remark,
                   COALESCE((SELECT SUM(po.total_amount) FROM purchase_orders AS po
                             WHERE po.supplier_id=s.id AND po.archived_at_utc IS NULL
                               AND po.status NOT IN ('Draft','Cancelled')), 0) AS TotalPurchased,
                   COALESCE((SELECT SUM(pm.amount) FROM payments AS pm
                             WHERE pm.supplier_id=s.id AND pm.archived_at_utc IS NULL), 0)
                       AS TotalPaid,
                   COALESCE((SELECT SUM(pa.allocated_amount)
                             FROM payment_allocations pa
                             INNER JOIN payments pm ON pm.id=pa.payment_id
                                 AND pm.archived_at_utc IS NULL
                             INNER JOIN payables ap ON ap.id=pa.payable_id
                                 AND ap.archived_at_utc IS NULL
                             WHERE ap.supplier_id=s.id
                               AND pa.cancelled_at_utc IS NULL),0) AS AllocatedPaid,
                   COALESCE((SELECT SUM(pm.amount-COALESCE(a.AllocatedAmount,0))
                             FROM payments pm
                             LEFT JOIN
                             (
                                 SELECT payment_id,SUM(allocated_amount) AllocatedAmount
                                 FROM payment_allocations
                                 WHERE cancelled_at_utc IS NULL GROUP BY payment_id
                             ) a ON a.payment_id=pm.id
                             WHERE pm.supplier_id=s.id
                               AND pm.archived_at_utc IS NULL),0) AS UnallocatedPaid,
                   COALESCE((SELECT SUM(p.amount) FROM payables AS p
                             WHERE p.supplier_id=s.id AND p.archived_at_utc IS NULL), 0)
                       AS TotalPayable,
                   COALESCE((SELECT SUM(p.amount - COALESCE(a.AllocatedAmount, 0))
                             FROM payables AS p
                             LEFT JOIN
                             (
                                 SELECT payable_id, SUM(allocated_amount) AS AllocatedAmount
                                 FROM payment_allocations
                                 WHERE cancelled_at_utc IS NULL
                                 GROUP BY payable_id
                             ) AS a ON a.payable_id=p.id
                             WHERE p.supplier_id=s.id AND p.archived_at_utc IS NULL), 0)
                       AS OutstandingPayable,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(a.AllocatedAmount,0))
                             FROM payables ap
                             LEFT JOIN
                             (
                                 SELECT payable_id,SUM(allocated_amount) AllocatedAmount
                                 FROM payment_allocations
                                 WHERE cancelled_at_utc IS NULL GROUP BY payable_id
                             ) a ON a.payable_id=ap.id
                             WHERE ap.supplier_id=s.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date<@BusinessDate
                               AND ap.amount>COALESCE(a.AllocatedAmount,0)),0)
                       AS OverduePayable,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(a.AllocatedAmount,0))
                             FROM payables ap
                             LEFT JOIN
                             (
                                 SELECT payable_id,SUM(allocated_amount) AllocatedAmount
                                 FROM payment_allocations
                                 WHERE cancelled_at_utc IS NULL GROUP BY payable_id
                             ) a ON a.payable_id=ap.id
                             WHERE ap.supplier_id=s.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date>=@BusinessDate
                               AND ap.amount>COALESCE(a.AllocatedAmount,0)),0)
                       AS NotDuePayable,
                   (s.archived_at_utc IS NOT NULL) AS IsArchived,
                   s.archived_at_utc AS ArchivedAtUtc,
                   s.version AS Version,
                   s.created_at_utc AS CreatedAtUtc,
                   s.updated_at_utc AS UpdatedAtUtc
            FROM suppliers AS s
            WHERE s.id=@SupplierId;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SupplierDetailsRow>(
            Command(
                sql,
                new
                {
                    SupplierId = supplierId,
                    BusinessDate = await BusinessDateAsync(cancellationToken),
                },
                null,
                cancellationToken));
        return row is null ? null : MapDetails(row);
    }

    public async Task<SupplierDetails> CreateAsync(
        SupplierWriteData data,
        CancellationToken cancellationToken)
    {
        const string sequenceSql =
            """
            INSERT INTO number_sequences
                (sequence_name, current_value, version, updated_at_utc)
            VALUES
                (@SequenceName, LAST_INSERT_ID(1), 1, @NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value=LAST_INSERT_ID(current_value + 1),
                version=version + 1,
                updated_at_utc=VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO suppliers
                (supplier_code,name,short_name,status,category,contact_name,
                 mobile,phone,email,wechat,province,city,address,payment_terms,
                 credit_days,bank_name,bank_account_name,remark,version,
                 created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@Name,@ShortName,@Status,@Category,@ContactName,
                 @Mobile,@Phone,@Email,@WeChat,@Province,@City,@Address,@PaymentTerms,
                 @CreditDays,@BankName,@BankAccountName,@Remark,1,
                 @NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var sequence = await connection.QuerySingleAsync<ulong>(
            Command(
                sequenceSql,
                new
                {
                    SequenceName = $"supplier:{data.NowUtc.Year:D4}",
                    data.NowUtc,
                },
                transaction,
                cancellationToken));
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var code = FinanceCodes.Format(
            runtime.NumberPrefixes.Supplier,
            data.NowUtc.Year,
            sequence);
        var supplierId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                ToParameters(data, code),
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "supplier.created",
            supplierId,
            code,
            $"创建供应商 {data.Name}",
            null,
            Snapshot(data, code),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(supplierId, cancellationToken)
            ?? throw new InvalidOperationException("Created supplier could not be loaded.");
    }

    public async Task<SupplierDetails?> UpdateAsync(
        ulong supplierId,
        SupplierWriteData data,
        CancellationToken cancellationToken)
    {
        const string selectSql =
            """
            SELECT supplier_code AS Code,name AS Name,short_name AS ShortName,
                   status AS Status,category AS Category,contact_name AS ContactName,
                   mobile AS Mobile,phone AS Phone,email AS Email,wechat AS WeChat,
                   province AS Province,city AS City,address AS Address,
                   payment_terms AS PaymentTerms,credit_days AS CreditDays,
                   bank_name AS BankName,bank_account_name AS BankAccountName,
                   remark AS Remark,version AS Version
            FROM suppliers
            WHERE id=@SupplierId AND archived_at_utc IS NULL
            FOR UPDATE;
            """;
        const string updateSql =
            """
            UPDATE suppliers
            SET name=@Name,short_name=@ShortName,status=@Status,category=@Category,
                contact_name=@ContactName,mobile=@Mobile,phone=@Phone,email=@Email,
                wechat=@WeChat,province=@Province,city=@City,address=@Address,
                payment_terms=@PaymentTerms,credit_days=@CreditDays,
                bank_name=@BankName,bank_account_name=@BankAccountName,remark=@Remark,
                version=version + 1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@SupplierId AND version=@Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var before = await connection.QuerySingleOrDefaultAsync<SupplierSnapshot>(
            Command(selectSql, new { SupplierId = supplierId }, transaction, cancellationToken));
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var affected = await connection.ExecuteAsync(
            Command(
                updateSql,
                ToParameters(data, before.Code, supplierId),
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "supplier.updated",
            supplierId,
            before.Code,
            $"修改供应商 {data.Name}",
            before,
            Snapshot(data, before.Code),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(supplierId, cancellationToken);
    }

    public async Task<SupplierDetails?> SetArchivedAsync(
        ulong supplierId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE suppliers
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version + 1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@SupplierId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var before = await connection.QuerySingleOrDefaultAsync<SupplierSnapshot>(
            Command(
                "SELECT supplier_code AS Code,name AS Name,version AS Version FROM suppliers WHERE id=@SupplierId FOR UPDATE;",
                new { SupplierId = supplierId },
                transaction,
                cancellationToken));
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    SupplierId = supplierId,
                    Archived = data.Archived ? 1 : 0,
                    data.Version,
                    data.ActorUserId,
                    data.NowUtc,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            data.Archived ? "supplier.archived" : "supplier.restored",
            supplierId,
            before.Code,
            $"{(data.Archived ? "归档" : "恢复")}供应商 {before.Name}",
            new { archived = !data.Archived, before.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(supplierId, cancellationToken);
    }

    private static object ToParameters(
        SupplierWriteData data,
        string code,
        ulong? supplierId = null) =>
        new
        {
            SupplierId = supplierId,
            Code = code,
            data.Name,
            data.ShortName,
            Status = data.Status.ToString(),
            data.Category,
            data.ContactName,
            data.Mobile,
            data.Phone,
            data.Email,
            data.WeChat,
            data.Province,
            data.City,
            data.Address,
            data.PaymentTerms,
            data.CreditDays,
            data.BankName,
            data.BankAccountName,
            data.Remark,
            data.Version,
            data.ActorUserId,
            data.NowUtc,
        };

    private static object Snapshot(SupplierWriteData data, string code) =>
        new
        {
            code,
            data.Name,
            data.ShortName,
            status = data.Status.ToString(),
            data.Category,
            data.ContactName,
            data.Mobile,
            data.Phone,
            data.Email,
            data.WeChat,
            data.Province,
            data.City,
            data.Address,
            data.PaymentTerms,
            data.CreditDays,
            data.BankName,
            data.BankAccountName,
            data.Remark,
        };

    private static Task<int> WriteAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        ulong entityId,
        string entityCode,
        string summary,
        object? before,
        object? after,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO audit_logs
                (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                 entity_code,summary,before_json,after_json)
            VALUES
                (@OccurredAtUtc,@ActorUserId,@Action,'supplier',@EntityId,
                 @EntityCode,@Summary,@BeforeJson,@AfterJson);
            """;
        return connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    OccurredAtUtc = occurredAtUtc,
                    ActorUserId = actorUserId,
                    Action = action,
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
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private async Task<DateOnly> BusinessDateAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(runtime.BusinessTimeZone);
        }
        catch (TimeZoneNotFoundException) when (
            string.Equals(runtime.BusinessTimeZone, "Asia/Shanghai", StringComparison.Ordinal))
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }

        return FinanceBusinessDatePolicy.GetBusinessDate(clock.GetUtcNow(), zone);
    }

    private static string GetOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"s.supplier_code {direction},s.id {direction}",
            "name" => $"s.name {direction},s.id {direction}",
            "totalpurchased" => $"TotalPurchased {direction},s.id {direction}",
            "outstandingpayable" => $"OutstandingPayable {direction},s.id {direction}",
            _ => $"s.updated_at_utc {direction},s.id {direction}",
        };
    }

    private static SupplierSummary MapSummary(SupplierSummaryRow row) =>
        new(
            row.Id,
            row.Code,
            row.Name,
            row.ShortName,
            Enum.Parse<SupplierStatus>(row.Status),
            row.Category,
            row.ContactName,
            row.ContactMethod,
            row.TotalPurchased,
            row.TotalPaid,
            row.OutstandingPayable,
            row.IsArchived,
            row.Version,
            row.UpdatedAtUtc);

    private static SupplierDetails MapDetails(SupplierDetailsRow row) =>
        new(
            row.Id,
            row.Code,
            row.Name,
            row.ShortName,
            Enum.Parse<SupplierStatus>(row.Status),
            row.Category,
            row.ContactName,
            row.Mobile,
            row.Phone,
            row.Email,
            row.WeChat,
            row.Province,
            row.City,
            row.Address,
            row.PaymentTerms,
            row.CreditDays,
            row.BankName,
            row.BankAccountName,
            row.Remark,
            row.TotalPurchased,
            row.TotalPayable,
            row.TotalPaid,
            row.AllocatedPaid,
            row.UnallocatedPaid,
            row.OutstandingPayable,
            row.OverduePayable,
            row.NotDuePayable,
            row.IsArchived,
            row.ArchivedAtUtc,
            row.Version,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private sealed class SupplierSummaryRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? ShortName { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? Category { get; init; }
        public string? ContactName { get; init; }
        public string? ContactMethod { get; init; }
        public decimal TotalPurchased { get; init; }
        public decimal TotalPaid { get; init; }
        public decimal OutstandingPayable { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class SupplierDetailsRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? ShortName { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? Category { get; init; }
        public string? ContactName { get; init; }
        public string? Mobile { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? WeChat { get; init; }
        public string? Province { get; init; }
        public string? City { get; init; }
        public string? Address { get; init; }
        public string? PaymentTerms { get; init; }
        public int CreditDays { get; init; }
        public string? BankName { get; init; }
        public string? BankAccountName { get; init; }
        public string? Remark { get; init; }
        public decimal TotalPurchased { get; init; }
        public decimal TotalPayable { get; init; }
        public decimal TotalPaid { get; init; }
        public decimal AllocatedPaid { get; init; }
        public decimal UnallocatedPaid { get; init; }
        public decimal OutstandingPayable { get; init; }
        public decimal OverduePayable { get; init; }
        public decimal NotDuePayable { get; init; }
        public bool IsArchived { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class SupplierSnapshot
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? ShortName { get; init; }
        public string? Status { get; init; }
        public string? Category { get; init; }
        public string? ContactName { get; init; }
        public string? Mobile { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? WeChat { get; init; }
        public string? Province { get; init; }
        public string? City { get; init; }
        public string? Address { get; init; }
        public string? PaymentTerms { get; init; }
        public int CreditDays { get; init; }
        public string? BankName { get; init; }
        public string? BankAccountName { get; init; }
        public string? Remark { get; init; }
        public ulong Version { get; init; }
    }
}
