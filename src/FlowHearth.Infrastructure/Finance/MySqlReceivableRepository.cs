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

public sealed class MySqlReceivableRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IReceivableRepository
{
    public async Task<PagedResult<ReceivableSummary>> ListReceivablesAsync(
        ReceivableListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var orderBy = ReceivableOrderBy(criteria.SortBy, criteria.SortDescending);
        var sql =
            $$"""
            WITH allocation_totals AS
            (
                SELECT receivable_id, SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations
                WHERE cancelled_at_utc IS NULL
                GROUP BY receivable_id
            ),
            receivable_rows AS
            (
                SELECT r.id AS Id,r.receivable_code AS Code,r.customer_id AS CustomerId,
                       c.name AS CustomerName,r.project_id AS ProjectId,
                       p.project_code AS ProjectCode,p.name AS ProjectName,
                       r.title AS Title,r.receivable_type AS ReceivableType,
                       r.amount AS Amount,COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                       r.amount-COALESCE(a.AllocatedAmount,0) AS OutstandingAmount,
                       r.due_date AS DueDate,
                       CASE
                           WHEN COALESCE(a.AllocatedAmount,0)=r.amount THEN 'Paid'
                           WHEN r.due_date < @BusinessDate AND COALESCE(a.AllocatedAmount,0)>0
                               THEN 'PartiallyOverdue'
                           WHEN r.due_date < @BusinessDate THEN 'Overdue'
                           WHEN COALESCE(a.AllocatedAmount,0)>0 THEN 'PartiallyPaid'
                           ELSE 'NotDue'
                       END AS Status,
                       CASE WHEN r.due_date < @BusinessDate
                                  AND COALESCE(a.AllocatedAmount,0) < r.amount
                            THEN DATEDIFF(@BusinessDate,r.due_date) ELSE 0 END
                           AS OverdueDays,
                       (r.archived_at_utc IS NOT NULL) AS IsArchived,
                       r.version AS Version,r.updated_at_utc AS UpdatedAtUtc
                FROM receivables AS r
                INNER JOIN customers AS c ON c.id=r.customer_id
                INNER JOIN projects AS p ON p.id=r.project_id
                LEFT JOIN allocation_totals AS a ON a.receivable_id=r.id
                WHERE (@ArchiveMode=2
                       OR (@ArchiveMode=0 AND r.archived_at_utc IS NULL)
                       OR (@ArchiveMode=1 AND r.archived_at_utc IS NOT NULL))
                  AND (@CustomerId IS NULL OR r.customer_id=@CustomerId)
                  AND (@ProjectId IS NULL OR r.project_id=@ProjectId)
                  AND (@ReceivableType IS NULL OR r.receivable_type=@ReceivableType)
                  AND (@DueFrom IS NULL OR r.due_date>=@DueFrom)
                  AND (@DueTo IS NULL OR r.due_date<=@DueTo)
                  AND (@OverdueOnly IS NULL OR @OverdueOnly=0
                       OR (r.due_date<@BusinessDate
                           AND COALESCE(a.AllocatedAmount,0)<r.amount))
                  AND (@SearchPattern IS NULL
                       OR CONVERT(r.receivable_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR r.title LIKE @SearchPattern ESCAPE '='
                       OR c.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=')
            )
            SELECT * FROM receivable_rows
            WHERE @Status IS NULL OR Status=@Status
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            WITH allocation_totals AS
            (
                SELECT receivable_id, SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations
                WHERE cancelled_at_utc IS NULL
                GROUP BY receivable_id
            ),
            receivable_rows AS
            (
                SELECT r.receivable_code AS Code,
                       CASE
                           WHEN COALESCE(a.AllocatedAmount,0)=r.amount THEN 'Paid'
                           WHEN r.due_date < @BusinessDate AND COALESCE(a.AllocatedAmount,0)>0
                               THEN 'PartiallyOverdue'
                           WHEN r.due_date < @BusinessDate THEN 'Overdue'
                           WHEN COALESCE(a.AllocatedAmount,0)>0 THEN 'PartiallyPaid'
                           ELSE 'NotDue'
                       END AS Status
                FROM receivables AS r
                INNER JOIN customers AS c ON c.id=r.customer_id
                INNER JOIN projects AS p ON p.id=r.project_id
                LEFT JOIN allocation_totals AS a ON a.receivable_id=r.id
                WHERE (@ArchiveMode=2
                       OR (@ArchiveMode=0 AND r.archived_at_utc IS NULL)
                       OR (@ArchiveMode=1 AND r.archived_at_utc IS NOT NULL))
                  AND (@CustomerId IS NULL OR r.customer_id=@CustomerId)
                  AND (@ProjectId IS NULL OR r.project_id=@ProjectId)
                  AND (@ReceivableType IS NULL OR r.receivable_type=@ReceivableType)
                  AND (@DueFrom IS NULL OR r.due_date>=@DueFrom)
                  AND (@DueTo IS NULL OR r.due_date<=@DueTo)
                  AND (@OverdueOnly IS NULL OR @OverdueOnly=0
                       OR (r.due_date<@BusinessDate
                           AND COALESCE(a.AllocatedAmount,0)<r.amount))
                  AND (@SearchPattern IS NULL
                       OR CONVERT(r.receivable_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR r.title LIKE @SearchPattern ESCAPE '='
                       OR c.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=')
            )
            SELECT COUNT(*) FROM receivable_rows
            WHERE @Status IS NULL OR Status=@Status;
            """;
        var parameters = new
        {
            BusinessDate = criteria.BusinessDate,
            ArchiveMode = (int)criteria.ArchiveMode,
            criteria.CustomerId,
            criteria.ProjectId,
            ReceivableType = criteria.ReceivableType?.ToString(),
            criteria.DueFrom,
            criteria.DueTo,
            OverdueOnly = criteria.OverdueOnly.HasValue
                ? criteria.OverdueOnly.Value ? 1 : 0
                : (int?)null,
            Status = criteria.Status?.ToString(),
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<ReceivableRow>())
            .Select(row => row.ToSummary())
            .ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new PagedResult<ReceivableSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<ReceivableDetails?> GetReceivableAsync(
        ulong receivableId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT r.id AS Id,r.receivable_code AS Code,r.customer_id AS CustomerId,
                   c.customer_code AS CustomerCode,c.name AS CustomerName,
                   r.project_id AS ProjectId,p.project_code AS ProjectCode,
                   p.name AS ProjectName,p.contract_amount AS ProjectContractAmount,
                   r.title AS Title,r.receivable_type AS ReceivableType,r.amount AS Amount,
                   COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   r.amount-COALESCE(a.AllocatedAmount,0) AS OutstandingAmount,
                   r.due_date AS DueDate,
                   CASE
                       WHEN COALESCE(a.AllocatedAmount,0)=r.amount THEN 'Paid'
                       WHEN r.due_date < @BusinessDate AND COALESCE(a.AllocatedAmount,0)>0
                           THEN 'PartiallyOverdue'
                       WHEN r.due_date < @BusinessDate THEN 'Overdue'
                       WHEN COALESCE(a.AllocatedAmount,0)>0 THEN 'PartiallyPaid'
                       ELSE 'NotDue'
                   END AS Status,
                   CASE WHEN r.due_date < @BusinessDate
                              AND COALESCE(a.AllocatedAmount,0) < r.amount
                        THEN DATEDIFF(@BusinessDate,r.due_date) ELSE 0 END
                       AS OverdueDays,
                   r.description AS Description,r.remark AS Remark,
                   (r.archived_at_utc IS NOT NULL) AS IsArchived,
                   r.archived_at_utc AS ArchivedAtUtc,r.version AS Version,
                   r.created_at_utc AS CreatedAtUtc,r.updated_at_utc AS UpdatedAtUtc
            FROM receivables AS r
            INNER JOIN customers AS c ON c.id=r.customer_id
            INNER JOIN projects AS p ON p.id=r.project_id
            LEFT JOIN
            (
                SELECT receivable_id,SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations
                WHERE cancelled_at_utc IS NULL
                GROUP BY receivable_id
            ) AS a ON a.receivable_id=r.id
            WHERE r.id=@ReceivableId;

            SELECT a.id AS Id,a.receipt_id AS ReceiptId,a.receivable_id AS ReceivableId,
                   r.receivable_code AS ReceivableCode,r.title AS ReceivableTitle,
                   r.project_id AS ProjectId,p.project_code AS ProjectCode,
                   p.name AS ProjectName,r.receivable_type AS ReceivableType,
                   r.amount AS ReceivableAmount,rc.receipt_code AS ReceiptCode,
                   rc.receipt_date AS ReceiptDate,rc.amount AS ReceiptAmount,
                   a.allocated_amount AS AllocatedAmount,u.display_name AS CreatedByDisplayName,
                   (a.cancelled_at_utc IS NOT NULL) AS IsCancelled,
                   a.version AS Version,a.created_at_utc AS CreatedAtUtc,
                   a.cancelled_at_utc AS CancelledAtUtc
            FROM receipt_allocations a
            INNER JOIN receivables r ON r.id=a.receivable_id
            INNER JOIN receipts rc ON rc.id=a.receipt_id
            INNER JOIN projects p ON p.id=r.project_id
            LEFT JOIN users u ON u.id=a.created_by_user_id
            WHERE a.receivable_id=@ReceivableId
            ORDER BY a.created_at_utc DESC,a.id DESC;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(
                sql,
                new { ReceivableId = receivableId, BusinessDate = businessDate },
                null,
                cancellationToken));
        var row = await result.ReadSingleOrDefaultAsync<ReceivableRow>();
        if (row is null)
        {
            return null;
        }

        var allocations = (await result.ReadAsync<ReceiptAllocationRow>())
            .Select(item => item.ToDetails())
            .ToArray();
        return row.ToDetails(allocations);
    }

    public async Task<ReceivableDetails> CreateReceivableAsync(
        ReceivableWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var project = await connection.QuerySingleOrDefaultAsync<ProjectPartyRow>(
            Command(
                "SELECT id AS Id,customer_id AS CustomerId FROM projects WHERE id=@ProjectId AND archived_at_utc IS NULL FOR UPDATE;",
                new { data.ProjectId },
                transaction,
                cancellationToken));
        if (project is null || project.CustomerId != data.CustomerId)
        {
            throw FlowHearthValidationException.For("projectId", "项目不存在、已归档或不属于当前客户。");
        }

        var sequence = await NextSequenceAsync(
            connection,
            transaction,
            "receivable",
            data.NowUtc,
            cancellationToken);
        var settings = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var code = FinanceCodes.Format(
            settings.NumberPrefixes.Receivable,
            data.NowUtc.Year,
            sequence);
        const string sql =
            """
            INSERT INTO receivables
                (receivable_code,customer_id,project_id,title,receivable_type,
                 amount,due_date,description,remark,version,created_at_utc,
                 created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@CustomerId,@ProjectId,@Title,@ReceivableType,
                 @Amount,@DueDate,@Description,@Remark,1,@NowUtc,
                 @ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        var id = await connection.QuerySingleAsync<ulong>(
            Command(
                sql,
                new
                {
                    Code = code,
                    data.CustomerId,
                    data.ProjectId,
                    data.Title,
                    ReceivableType = data.ReceivableType.ToString(),
                    data.Amount,
                    data.DueDate,
                    data.Description,
                    data.Remark,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await AuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "receivable.created",
            "receivable",
            id,
            code,
            $"创建应收 {data.Title}，金额 {data.Amount:F2}",
            null,
            ReceivableSnapshot(data, code),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceivableAsync(id, data.BusinessDate, cancellationToken)
            ?? throw new InvalidOperationException("Created receivable could not be loaded.");
    }

    public async Task<ReceivableDetails?> UpdateReceivableAsync(
        ulong receivableId,
        ReceivableWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadReceivableLockAsync(connection, transaction, receivableId, cancellationToken);
        if (before is null || before.Version != data.Version || before.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (data.Amount < before.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "应收金额不能小于已核销金额。");
        }

        const string sql =
            """
            UPDATE receivables
            SET title=@Title,receivable_type=@ReceivableType,amount=@Amount,
                due_date=@DueDate,description=@Description,remark=@Remark,
                version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ReceivableId AND version=@Version AND archived_at_utc IS NULL;
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    ReceivableId = receivableId,
                    data.Title,
                    ReceivableType = data.ReceivableType.ToString(),
                    data.Amount,
                    data.DueDate,
                    data.Description,
                    data.Remark,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await AuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "receivable.updated",
            "receivable",
            receivableId,
            before.Code,
            $"修改应收 {data.Title}，金额 {before.Amount:F2} → {data.Amount:F2}",
            before,
            ReceivableSnapshot(data, before.Code),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceivableAsync(receivableId, data.BusinessDate, cancellationToken);
    }

    public async Task<ReceivableDetails?> SetReceivableArchivedAsync(
        ulong receivableId,
        SetFinanceArchiveData data,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE receivables
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ReceivableId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL))
              AND (@Archived=0 OR NOT EXISTS
                  (SELECT 1 FROM receipt_allocations AS a
                   WHERE a.receivable_id=receivables.id AND a.cancelled_at_utc IS NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadReceivableLockAsync(connection, transaction, receivableId, cancellationToken);
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
                    ReceivableId = receivableId,
                    Archived = data.Archived ? 1 : 0,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await AuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            data.Archived ? "receivable.archived" : "receivable.restored",
            "receivable",
            receivableId,
            before.Code,
            $"{(data.Archived ? "归档" : "恢复")}应收 {before.Title}",
            new { archived = !data.Archived, before.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceivableAsync(receivableId, businessDate, cancellationToken);
    }

    public async Task<PagedResult<ReceiptSummary>> ListReceiptsAsync(
        int page,
        int pageSize,
        string? search,
        FinanceArchiveMode archiveMode,
        ulong? customerId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? hasUnallocated,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var orderBy = ReceiptOrderBy(sortBy, sortDescending);
        var sql =
            $$"""
            SELECT r.id AS Id,r.receipt_code AS Code,r.customer_id AS CustomerId,
                   c.name AS CustomerName,r.receipt_date AS ReceiptDate,r.amount AS Amount,
                   COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   r.amount-COALESCE(a.AllocatedAmount,0) AS UnallocatedAmount,
                   r.payment_method AS PaymentMethod,r.bank_reference AS BankReference,
                   r.payer_name AS PayerName,(r.archived_at_utc IS NOT NULL) AS IsArchived,
                   r.version AS Version,r.updated_at_utc AS UpdatedAtUtc
            FROM receipts AS r
            INNER JOIN customers AS c ON c.id=r.customer_id
            LEFT JOIN
            (
                SELECT receipt_id,SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations WHERE cancelled_at_utc IS NULL GROUP BY receipt_id
            ) AS a ON a.receipt_id=r.id
            WHERE (@ArchiveMode=2
                   OR (@ArchiveMode=0 AND r.archived_at_utc IS NULL)
                   OR (@ArchiveMode=1 AND r.archived_at_utc IS NOT NULL))
              AND (@CustomerId IS NULL OR r.customer_id=@CustomerId)
              AND (@DateFrom IS NULL OR r.receipt_date>=@DateFrom)
              AND (@DateTo IS NULL OR r.receipt_date<=@DateTo)
              AND (@HasUnallocated IS NULL
                   OR (@HasUnallocated=1 AND r.amount>COALESCE(a.AllocatedAmount,0))
                   OR (@HasUnallocated=0 AND r.amount=COALESCE(a.AllocatedAmount,0)))
              AND (@SearchPattern IS NULL
                   OR CONVERT(r.receipt_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR r.bank_reference LIKE @SearchPattern ESCAPE '='
                   OR r.payer_name LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '=')
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM receipts AS r
            INNER JOIN customers AS c ON c.id=r.customer_id
            LEFT JOIN
            (
                SELECT receipt_id,SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations WHERE cancelled_at_utc IS NULL GROUP BY receipt_id
            ) AS a ON a.receipt_id=r.id
            WHERE (@ArchiveMode=2
                   OR (@ArchiveMode=0 AND r.archived_at_utc IS NULL)
                   OR (@ArchiveMode=1 AND r.archived_at_utc IS NOT NULL))
              AND (@CustomerId IS NULL OR r.customer_id=@CustomerId)
              AND (@DateFrom IS NULL OR r.receipt_date>=@DateFrom)
              AND (@DateTo IS NULL OR r.receipt_date<=@DateTo)
              AND (@HasUnallocated IS NULL
                   OR (@HasUnallocated=1 AND r.amount>COALESCE(a.AllocatedAmount,0))
                   OR (@HasUnallocated=0 AND r.amount=COALESCE(a.AllocatedAmount,0)))
              AND (@SearchPattern IS NULL
                   OR CONVERT(r.receipt_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR r.bank_reference LIKE @SearchPattern ESCAPE '='
                   OR r.payer_name LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '=');
            """;
        var parameters = new
        {
            ArchiveMode = (int)archiveMode,
            CustomerId = customerId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            HasUnallocated = hasUnallocated.HasValue
                ? hasUnallocated.Value ? 1 : 0
                : (int?)null,
            SearchPattern = MySqlLikePattern.Contains(search),
            PageSize = pageSize,
            Offset = ((long)page - 1) * pageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<ReceiptSummaryRow>())
            .Select(row => row.ToSummary())
            .ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new PagedResult<ReceiptSummary>(items, page, pageSize, total);
    }

    public async Task<ReceiptDetails?> GetReceiptAsync(
        ulong receiptId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT r.id AS Id,r.receipt_code AS Code,r.customer_id AS CustomerId,
                   c.customer_code AS CustomerCode,c.name AS CustomerName,
                   r.receipt_date AS ReceiptDate,r.amount AS Amount,
                   COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   r.amount-COALESCE(a.AllocatedAmount,0) AS UnallocatedAmount,
                   r.payment_method AS PaymentMethod,r.bank_reference AS BankReference,
                   r.payer_name AS PayerName,r.remark AS Remark,
                   (r.archived_at_utc IS NOT NULL) AS IsArchived,
                   r.archived_at_utc AS ArchivedAtUtc,r.version AS Version,
                   r.created_at_utc AS CreatedAtUtc,r.updated_at_utc AS UpdatedAtUtc
            FROM receipts AS r
            INNER JOIN customers AS c ON c.id=r.customer_id
            LEFT JOIN
            (
                SELECT receipt_id,SUM(allocated_amount) AS AllocatedAmount
                FROM receipt_allocations WHERE cancelled_at_utc IS NULL GROUP BY receipt_id
            ) AS a ON a.receipt_id=r.id
            WHERE r.id=@ReceiptId;

            SELECT a.id AS Id,a.receipt_id AS ReceiptId,a.receivable_id AS ReceivableId,
                   rv.receivable_code AS ReceivableCode,rv.title AS ReceivableTitle,
                   rv.project_id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
                   rv.receivable_type AS ReceivableType,rv.amount AS ReceivableAmount,
                   r.receipt_code AS ReceiptCode,r.receipt_date AS ReceiptDate,
                   r.amount AS ReceiptAmount,
                   a.allocated_amount AS AllocatedAmount,
                   u.display_name AS CreatedByDisplayName,
                   (a.cancelled_at_utc IS NOT NULL) AS IsCancelled,
                   a.version AS Version,a.created_at_utc AS CreatedAtUtc,
                   a.cancelled_at_utc AS CancelledAtUtc
            FROM receipt_allocations AS a
            INNER JOIN receivables AS rv ON rv.id=a.receivable_id
            INNER JOIN projects AS p ON p.id=rv.project_id
            INNER JOIN receipts AS r ON r.id=a.receipt_id
            LEFT JOIN users AS u ON u.id=a.created_by_user_id
            WHERE a.receipt_id=@ReceiptId
            ORDER BY a.created_at_utc,a.id;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new { ReceiptId = receiptId }, null, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<ReceiptHeader>();
        if (header is null)
        {
            return null;
        }

        var allocations = (await result.ReadAsync<ReceiptAllocationRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        return header.ToDetails(allocations);
    }

    public async Task<ReceiptDetails> CreateReceiptAsync(
        ReceiptWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customerExists = await connection.ExecuteScalarAsync<int>(
            Command(
                "SELECT COUNT(*) FROM customers WHERE id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;",
                new { data.CustomerId },
                transaction,
                cancellationToken));
        if (customerExists != 1)
        {
            throw FlowHearthValidationException.For("customerId", "客户不存在或已归档。");
        }

        var sequence = await NextSequenceAsync(connection, transaction, "receipt", data.NowUtc, cancellationToken);
        var settings = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var code = FinanceCodes.Format(settings.NumberPrefixes.Receipt, data.NowUtc.Year, sequence);
        const string sql =
            """
            INSERT INTO receipts
                (receipt_code,customer_id,receipt_date,amount,payment_method,
                 bank_reference,payer_name,remark,version,created_at_utc,
                 created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@CustomerId,@ReceiptDate,@Amount,@PaymentMethod,
                 @BankReference,@PayerName,@Remark,1,@NowUtc,
                 @ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        var id = await connection.QuerySingleAsync<ulong>(
            Command(sql, ReceiptParameters(data, code), transaction, cancellationToken));
        await AuditAsync(
            connection, transaction, data.ActorUserId, "receipt.created", "receipt", id, code,
            $"创建收款 {code}，金额 {data.Amount:F2}", null, ReceiptSnapshot(data, code),
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Created receipt could not be loaded.");
    }

    public async Task<ReceiptDetails?> UpdateReceiptAsync(
        ulong receiptId,
        ReceiptWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadReceiptLockAsync(connection, transaction, receiptId, cancellationToken);
        if (before is null || before.Version != data.Version || before.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (data.Amount < before.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "收款金额不能小于已核销金额。");
        }

        const string sql =
            """
            UPDATE receipts
            SET receipt_date=@ReceiptDate,amount=@Amount,payment_method=@PaymentMethod,
                bank_reference=@BankReference,payer_name=@PayerName,remark=@Remark,
                version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ReceiptId AND version=@Version AND archived_at_utc IS NULL;
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    ReceiptId = receiptId,
                    data.ReceiptDate,
                    data.Amount,
                    PaymentMethod = data.PaymentMethod.ToString(),
                    data.BankReference,
                    data.PayerName,
                    data.Remark,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await AuditAsync(
            connection, transaction, data.ActorUserId, "receipt.updated", "receipt", receiptId,
            before.Code, $"修改收款 {before.Code}，金额 {before.Amount:F2} → {data.Amount:F2}",
            before, ReceiptSnapshot(data, before.Code), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(receiptId, cancellationToken);
    }

    public Task<ReceiptDetails?> SetReceiptArchivedAsync(
        ulong receiptId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken) =>
        SetReceiptArchivedCoreAsync(receiptId, data, cancellationToken);

    public async Task<ReceiptDetails?> AllocateReceiptAsync(
        ulong receiptId,
        ReceiptAllocationWriteData data,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AllocateReceiptCoreAsync(
                receiptId, data, cancellationToken);
        }
        catch (MySqlConnector.MySqlException exception)
            when (exception.Number is 1205 or 1213)
        {
            return null;
        }
    }

    private async Task<ReceiptDetails?> AllocateReceiptCoreAsync(
        ulong receiptId,
        ReceiptAllocationWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var receipt = await LoadReceiptLockAsync(connection, transaction, receiptId, cancellationToken);
        if (receipt is null || receipt.Version != data.ReceiptVersion || receipt.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var runningReceiptAllocated = receipt.AllocatedAmount;
        foreach (var item in data.Allocations.OrderBy(item => item.ReceivableId))
        {
            var receivable = await LoadReceivableLockAsync(
                connection,
                transaction,
                item.ReceivableId,
                cancellationToken);
            if (receivable is null || receivable.IsArchived)
            {
                throw FlowHearthValidationException.For("receivableId", "应收账款不存在或已归档。");
            }

            if (receivable.CustomerId != receipt.CustomerId)
            {
                throw FlowHearthValidationException.For("receivableId", "收款和应收必须属于同一客户。");
            }

            if (item.Amount > receipt.Amount - runningReceiptAllocated)
            {
                throw FlowHearthValidationException.For("amount", "核销总额超过收款未核销余额。");
            }

            if (item.Amount > receivable.Amount - receivable.AllocatedAmount)
            {
                throw FlowHearthValidationException.For("amount", $"核销金额超过应收 {receivable.Code} 的未收余额。");
            }

            const string insertSql =
                """
                INSERT INTO receipt_allocations
                    (receipt_id,receivable_id,allocated_amount,version,
                     created_at_utc,created_by_user_id)
                VALUES
                    (@ReceiptId,@ReceivableId,@Amount,1,@NowUtc,@ActorUserId);
                SELECT LAST_INSERT_ID();
                """;
            var allocationId = await connection.QuerySingleAsync<ulong>(
                Command(
                    insertSql,
                    new
                    {
                        ReceiptId = receiptId,
                        item.ReceivableId,
                        item.Amount,
                        data.NowUtc,
                        data.ActorUserId,
                    },
                    transaction,
                    cancellationToken));
            await AuditAsync(
                connection, transaction, data.ActorUserId, "receipt_allocation.created",
                "receipt_allocation", allocationId, receipt.Code,
                $"收款 {receipt.Code} 核销应收 {receivable.Code}，金额 {item.Amount:F2}",
                null, new
                {
                    receiptId,
                    item.ReceivableId,
                    amount = item.Amount,
                    receiptAllocatedBefore = runningReceiptAllocated,
                    receiptAllocatedAfter = runningReceiptAllocated + item.Amount,
                    receivableAllocatedBefore = receivable.AllocatedAmount,
                    receivableAllocatedAfter = receivable.AllocatedAmount + item.Amount,
                }, data.NowUtc, cancellationToken);
            runningReceiptAllocated += item.Amount;
        }

        var bumped = await connection.ExecuteAsync(
            Command(
                "UPDATE receipts SET version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@ReceiptId AND version=@Version;",
                new
                {
                    ReceiptId = receiptId,
                    Version = data.ReceiptVersion,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (bumped != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(receiptId, cancellationToken);
    }

    public async Task<ReceiptDetails?> CancelReceiptAllocationAsync(
        ulong receiptId,
        ulong allocationId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var receipt = await LoadReceiptLockAsync(connection, transaction, receiptId, cancellationToken);
        var allocation = await connection.QuerySingleOrDefaultAsync<AllocationLockRow>(
            Command(
                """
                SELECT a.id AS Id,a.receipt_id AS ReceiptId,a.receivable_id AS ReceivableId,
                       a.allocated_amount AS Amount,a.version AS Version,
                       rv.receivable_code AS ReceivableCode
                FROM receipt_allocations AS a
                INNER JOIN receivables AS rv ON rv.id=a.receivable_id
                WHERE a.id=@AllocationId AND a.receipt_id=@ReceiptId
                  AND a.cancelled_at_utc IS NULL
                FOR UPDATE;
                """,
                new { AllocationId = allocationId, ReceiptId = receiptId },
                transaction,
                cancellationToken));
        if (receipt is null || allocation is null || allocation.Version != version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var affected = await connection.ExecuteAsync(
            Command(
                "UPDATE receipt_allocations SET cancelled_at_utc=@NowUtc,cancelled_by_user_id=@ActorUserId,version=version + 1 WHERE id=@AllocationId AND version=@Version AND cancelled_at_utc IS NULL;",
                new
                {
                    AllocationId = allocationId,
                    Version = version,
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(
            Command(
                "UPDATE receipts SET version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@ReceiptId;",
                new { ReceiptId = receiptId, NowUtc = nowUtc, ActorUserId = actorUserId },
                transaction,
                cancellationToken));
        await AuditAsync(
            connection, transaction, actorUserId, "receipt_allocation.cancelled",
            "receipt_allocation", allocationId, receipt.Code,
            $"取消收款 {receipt.Code} 对应收 {allocation.ReceivableCode} 的核销 {allocation.Amount:F2}",
            new
            {
                allocation.ReceiptId,
                allocation.ReceivableId,
                amount = allocation.Amount,
                cancelled = false,
            },
            new
            {
                allocation.ReceiptId,
                allocation.ReceivableId,
                amount = allocation.Amount,
                cancelled = true,
            }, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(receiptId, cancellationToken);
    }

    private async Task<ReceiptDetails?> SetReceiptArchivedCoreAsync(
        ulong receiptId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadReceiptLockAsync(connection, transaction, receiptId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        const string sql =
            """
            UPDATE receipts
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version + 1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ReceiptId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL))
              AND (@Archived=0 OR NOT EXISTS
                  (SELECT 1 FROM receipt_allocations AS a
                   WHERE a.receipt_id=receipts.id AND a.cancelled_at_utc IS NULL));
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    ReceiptId = receiptId,
                    Archived = data.Archived ? 1 : 0,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await AuditAsync(
            connection, transaction, data.ActorUserId,
            data.Archived ? "receipt.archived" : "receipt.restored", "receipt", receiptId,
            before.Code, $"{(data.Archived ? "归档" : "恢复")}收款 {before.Code}",
            new { archived = !data.Archived, before.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetReceiptAsync(receiptId, cancellationToken);
    }

    private static async Task<ReceivableLockRow?> LoadReceivableLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong receivableId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<ReceivableLockRow>(
            Command(
                """
                SELECT r.receivable_code AS Code,r.customer_id AS CustomerId,
                       r.project_id AS ProjectId,r.title AS Title,r.amount AS Amount,
                       COALESCE(SUM(CASE WHEN a.cancelled_at_utc IS NULL
                                         THEN a.allocated_amount ELSE 0 END),0) AS AllocatedAmount,
                       (r.archived_at_utc IS NOT NULL) AS IsArchived,r.version AS Version
                FROM receivables AS r
                LEFT JOIN receipt_allocations AS a ON a.receivable_id=r.id
                WHERE r.id=@ReceivableId
                GROUP BY r.id
                FOR UPDATE;
                """,
                new { ReceivableId = receivableId },
                transaction,
                cancellationToken));

    private static async Task<ReceiptLockRow?> LoadReceiptLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong receiptId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<ReceiptLockRow>(
            Command(
                """
                SELECT r.receipt_code AS Code,r.customer_id AS CustomerId,r.amount AS Amount,
                       COALESCE(SUM(CASE WHEN a.cancelled_at_utc IS NULL
                                         THEN a.allocated_amount ELSE 0 END),0) AS AllocatedAmount,
                       (r.archived_at_utc IS NOT NULL) AS IsArchived,r.version AS Version
                FROM receipts AS r
                LEFT JOIN receipt_allocations AS a ON a.receipt_id=r.id
                WHERE r.id=@ReceiptId
                GROUP BY r.id
                FOR UPDATE;
                """,
                new { ReceiptId = receiptId },
                transaction,
                cancellationToken));

    private static async Task<ulong> NextSequenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        string entity,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<ulong>(
            Command(
                """
                INSERT INTO number_sequences
                    (sequence_name,current_value,version,updated_at_utc)
                VALUES
                    (@SequenceName,LAST_INSERT_ID(1),1,@NowUtc)
                ON DUPLICATE KEY UPDATE
                    current_value=LAST_INSERT_ID(current_value + 1),
                    version=version + 1,updated_at_utc=VALUES(updated_at_utc);
                SELECT LAST_INSERT_ID();
                """,
                new { SequenceName = $"{entity}:{nowUtc.Year:D4}", NowUtc = nowUtc },
                transaction,
                cancellationToken));

    private static object ReceiptParameters(ReceiptWriteData data, string code) =>
        new
        {
            Code = code,
            data.CustomerId,
            data.ReceiptDate,
            data.Amount,
            PaymentMethod = data.PaymentMethod.ToString(),
            data.BankReference,
            data.PayerName,
            data.Remark,
            data.NowUtc,
            data.ActorUserId,
        };

    private static object ReceiptSnapshot(ReceiptWriteData data, string code) =>
        new
        {
            code,
            data.CustomerId,
            data.ReceiptDate,
            data.Amount,
            paymentMethod = data.PaymentMethod.ToString(),
            data.BankReference,
            data.PayerName,
            data.Remark,
        };

    private static object ReceivableSnapshot(ReceivableWriteData data, string code) =>
        new
        {
            code,
            data.CustomerId,
            data.ProjectId,
            data.Title,
            receivableType = data.ReceivableType.ToString(),
            data.Amount,
            data.DueDate,
            data.Description,
            data.Remark,
        };

    private static Task<int> AuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        string entityType,
        ulong entityId,
        string entityCode,
        string summary,
        object? before,
        object? after,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            Command(
                """
                INSERT INTO audit_logs
                    (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                     entity_code,summary,before_json,after_json)
                VALUES
                    (@OccurredAtUtc,@ActorUserId,@Action,@EntityType,@EntityId,
                     @EntityCode,@Summary,@BeforeJson,@AfterJson);
                """,
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

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private static string ReceivableOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"Code {direction},Id {direction}",
            "amount" => $"Amount {direction},Id {direction}",
            "duedate" => $"DueDate {direction},Id {direction}",
            "outstandingamount" => $"OutstandingAmount {direction},Id {direction}",
            _ => $"UpdatedAtUtc {direction},Id {direction}",
        };
    }

    private static string ReceiptOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"r.receipt_code {direction},r.id {direction}",
            "amount" => $"r.amount {direction},r.id {direction}",
            "receiptdate" => $"r.receipt_date {direction},r.id {direction}",
            "unallocatedamount" => $"UnallocatedAmount {direction},r.id {direction}",
            _ => $"r.updated_at_utc {direction},r.id {direction}",
        };
    }

    private sealed class ProjectPartyRow
    {
        public ulong Id { get; init; }
        public ulong CustomerId { get; init; }
    }

    private sealed class ReceivableLockRow
    {
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public ulong ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class ReceiptLockRow
    {
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class AllocationLockRow
    {
        public ulong Id { get; init; }
        public ulong ReceiptId { get; init; }
        public ulong ReceivableId { get; init; }
        public decimal Amount { get; init; }
        public ulong Version { get; init; }
        public string ReceivableCode { get; init; } = string.Empty;
    }

    private sealed class ReceiptHeader
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public DateTime ReceiptDate { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public decimal UnallocatedAmount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string? BankReference { get; init; }
        public string? PayerName { get; init; }
        public string? Remark { get; init; }
        public long IsArchived { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ReceiptDetails ToDetails(IReadOnlyList<ReceiptAllocationDetails> allocations) =>
            new(
                Id, Code, CustomerId, CustomerCode, CustomerName,
                DateOnly.FromDateTime(ReceiptDate), Amount,
                AllocatedAmount, UnallocatedAmount,
                Enum.Parse<PaymentMethod>(PaymentMethod), BankReference, PayerName,
                Remark, IsArchived != 0, ArchivedAtUtc, Version, CreatedAtUtc, UpdatedAtUtc,
                allocations);
    }

    private sealed class ReceivableRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public decimal ProjectContractAmount { get; init; }
        public string Title { get; init; } = string.Empty;
        public string ReceivableType { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public decimal OutstandingAmount { get; init; }
        public DateTime DueDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public int OverdueDays { get; init; }
        public string? Description { get; init; }
        public string? Remark { get; init; }
        public long IsArchived { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ReceivableSummary ToSummary() =>
            new(
                Id, Code, CustomerId, CustomerName, ProjectId, ProjectCode, ProjectName,
                Title, Enum.Parse<ReceivableType>(ReceivableType), Amount,
                AllocatedAmount, OutstandingAmount, DateOnly.FromDateTime(DueDate),
                Enum.Parse<FinanceBalanceStatus>(Status), OverdueDays,
                IsArchived != 0, Version,
                UpdatedAtUtc);

        public ReceivableDetails ToDetails(
            IReadOnlyList<ReceiptAllocationDetails> allocations) =>
            new(
                Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode,
                ProjectName, ProjectContractAmount, Title,
                Enum.Parse<ReceivableType>(ReceivableType), Amount, AllocatedAmount,
                OutstandingAmount, DateOnly.FromDateTime(DueDate),
                Enum.Parse<FinanceBalanceStatus>(Status), OverdueDays, Description, Remark,
                IsArchived != 0, ArchivedAtUtc, Version, CreatedAtUtc, UpdatedAtUtc,
                allocations);
    }

    private sealed class ReceiptSummaryRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public DateTime ReceiptDate { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public decimal UnallocatedAmount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string? BankReference { get; init; }
        public string? PayerName { get; init; }
        public long IsArchived { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ReceiptSummary ToSummary() =>
            new(
                Id, Code, CustomerId, CustomerName, DateOnly.FromDateTime(ReceiptDate),
                Amount, AllocatedAmount, UnallocatedAmount,
                Enum.Parse<PaymentMethod>(PaymentMethod), BankReference, PayerName,
                IsArchived != 0, Version, UpdatedAtUtc);
    }

    private sealed class ReceiptAllocationRow
    {
        public ulong Id { get; init; }
        public ulong ReceiptId { get; init; }
        public ulong ReceivableId { get; init; }
        public string ReceivableCode { get; init; } = string.Empty;
        public string ReceivableTitle { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public string ReceivableType { get; init; } = string.Empty;
        public decimal ReceivableAmount { get; init; }
        public string ReceiptCode { get; init; } = string.Empty;
        public DateTime ReceiptDate { get; init; }
        public decimal ReceiptAmount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public string? CreatedByDisplayName { get; init; }
        public long IsCancelled { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? CancelledAtUtc { get; init; }

        public ReceiptAllocationDetails ToDetails() =>
            new(
                Id, ReceiptId, ReceivableId, ReceivableCode, ReceivableTitle,
                ProjectId, ProjectCode, ProjectName,
                Enum.Parse<ReceivableType>(ReceivableType), ReceivableAmount,
                ReceiptCode, DateOnly.FromDateTime(ReceiptDate), ReceiptAmount,
                AllocatedAmount, CreatedByDisplayName, IsCancelled != 0,
                Version, CreatedAtUtc, CancelledAtUtc);
    }
}
