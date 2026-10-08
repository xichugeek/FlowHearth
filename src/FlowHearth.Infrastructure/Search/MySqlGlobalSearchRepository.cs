using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Search;

namespace FlowHearth.Infrastructure.Search;

public sealed class MySqlGlobalSearchRepository(IDbConnectionFactory connectionFactory)
    : IGlobalSearchRepository
{
    public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
        GlobalSearchQuery query,
        CancellationToken cancellationToken)
    {
        // Business codes are stored as ASCII. Convert the column, never the
        // user's Unicode query, for both matching and relevance comparisons.
        const string sql =
            """
            SELECT Kind,TargetType,TargetId,Code,Title,Subtitle,IsArchived
            FROM
            (
                SELECT 'Customer' AS Kind,'Customer' AS TargetType,c.id AS TargetId,
                       c.customer_code AS Code,c.name AS Title,
                       COALESCE(c.short_name,c.industry) AS Subtitle,
                       (c.archived_at_utc IS NOT NULL) AS IsArchived,
                       CASE
                           WHEN CONVERT(c.customer_code USING utf8mb4)=@ExactQuery OR c.name=@ExactQuery THEN 0
                           WHEN CONVERT(c.customer_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '='
                             OR c.name LIKE @PrefixPattern ESCAPE '=' THEN 1
                           ELSE 2
                       END AS Relevance,
                       c.updated_at_utc AS UpdatedAtUtc
                FROM customers AS c
                WHERE @IncludeCustomers=1
                  AND (CONVERT(c.customer_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR c.name LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Contact','Customer',c.id,c.customer_code,ct.name,
                       CONCAT(c.name,
                              CASE WHEN ct.mobile IS NULL THEN ''
                                   ELSE CONCAT(' · ',ct.mobile) END),
                       (c.archived_at_utc IS NOT NULL),
                       CASE
                           WHEN ct.name=@ExactQuery OR ct.mobile=@ExactQuery THEN 0
                           WHEN ct.name LIKE @PrefixPattern ESCAPE '='
                             OR ct.mobile LIKE @PrefixPattern ESCAPE '=' THEN 1
                           ELSE 2
                       END,
                       ct.updated_at_utc
                FROM contacts AS ct
                INNER JOIN customers AS c ON c.id=ct.customer_id
                WHERE @IncludeCustomers=1 AND ct.deleted_at_utc IS NULL
                  AND (ct.name LIKE @ContainsPattern ESCAPE '='
                       OR ct.mobile LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Project','Project',p.id,p.project_code,p.name,c.name,
                       (p.archived_at_utc IS NOT NULL),
                       CASE
                           WHEN CONVERT(p.project_code USING utf8mb4)=@ExactQuery OR p.name=@ExactQuery THEN 0
                           WHEN CONVERT(p.project_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '='
                             OR p.name LIKE @PrefixPattern ESCAPE '=' THEN 1
                           ELSE 2
                       END,
                       p.updated_at_utc
                FROM projects AS p
                INNER JOIN customers AS c ON c.id=p.customer_id
                WHERE @IncludeProjects=1
                  AND (CONVERT(p.project_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR p.name LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Equipment','Equipment',e.id,e.equipment_code,e.name,
                       CONCAT(c.name,
                              CASE WHEN e.serial_number IS NULL THEN ''
                                   ELSE CONCAT(' · ',e.serial_number) END),
                       (e.archived_at_utc IS NOT NULL),
                       CASE
                           WHEN CONVERT(e.equipment_code USING utf8mb4)=@ExactQuery OR e.name=@ExactQuery
                             OR e.serial_number=@ExactQuery THEN 0
                           WHEN CONVERT(e.equipment_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '='
                             OR e.name LIKE @PrefixPattern ESCAPE '='
                             OR e.serial_number LIKE @PrefixPattern ESCAPE '=' THEN 1
                           ELSE 2
                       END,
                       e.updated_at_utc
                FROM equipment AS e
                INNER JOIN customers AS c ON c.id=e.customer_id
                WHERE @IncludeEquipment=1
                  AND (CONVERT(e.equipment_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR e.name LIKE @ContainsPattern ESCAPE '='
                       OR e.serial_number LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'ServiceTicket','ServiceTicket',s.id,s.service_code,s.title,
                       c.name,(s.archived_at_utc IS NOT NULL),
                       CASE
                           WHEN CONVERT(s.service_code USING utf8mb4)=@ExactQuery OR s.title=@ExactQuery THEN 0
                           WHEN CONVERT(s.service_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '='
                             OR s.title LIKE @PrefixPattern ESCAPE '=' THEN 1
                           ELSE 2
                       END,
                       s.updated_at_utc
                FROM service_tickets AS s
                INNER JOIN customers AS c ON c.id=s.customer_id
                WHERE @IncludeService=1
                  AND (CONVERT(s.service_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR s.title LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'PurchaseOrder','PurchaseOrder',po.id,po.purchase_order_code,
                       s.name,CONCAT(p.project_code,' · ',p.name),
                       (po.archived_at_utc IS NOT NULL),
                       CASE WHEN CONVERT(po.purchase_order_code USING utf8mb4)=@ExactQuery THEN 0
                            WHEN CONVERT(po.purchase_order_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '=' THEN 1
                            ELSE 2 END,
                       po.updated_at_utc
                FROM purchase_orders po
                INNER JOIN suppliers s ON s.id=po.supplier_id
                INNER JOIN projects p ON p.id=po.project_id
                WHERE @IncludePurchases=1
                  AND (CONVERT(po.purchase_order_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR s.name LIKE @ContainsPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR p.name LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Payable','Payable',ap.id,ap.payable_code,ap.title,
                       CONCAT(s.name,' · ',p.project_code),
                       (ap.archived_at_utc IS NOT NULL),
                       CASE WHEN CONVERT(ap.payable_code USING utf8mb4)=@ExactQuery THEN 0
                            WHEN CONVERT(ap.payable_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '=' THEN 1
                            ELSE 2 END,
                       ap.updated_at_utc
                FROM payables ap
                INNER JOIN suppliers s ON s.id=ap.supplier_id
                INNER JOIN projects p ON p.id=ap.project_id
                LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
                WHERE @IncludePayables=1
                  AND (CONVERT(ap.payable_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR ap.title LIKE @ContainsPattern ESCAPE '='
                       OR s.name LIKE @ContainsPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR p.name LIKE @ContainsPattern ESCAPE '='
                       OR CONVERT(po.purchase_order_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Payment','Payment',pm.id,pm.payment_code,s.name,
                       pm.bank_reference,(pm.archived_at_utc IS NOT NULL),
                       CASE WHEN CONVERT(pm.payment_code USING utf8mb4)=@ExactQuery THEN 0
                            WHEN CONVERT(pm.payment_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '=' THEN 1
                            ELSE 2 END,
                       pm.updated_at_utc
                FROM payments pm
                INNER JOIN suppliers s ON s.id=pm.supplier_id
                WHERE @IncludePayments=1
                  AND (CONVERT(pm.payment_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR s.name LIKE @ContainsPattern ESCAPE '='
                       OR pm.bank_reference LIKE @ContainsPattern ESCAPE '=')

                UNION ALL

                SELECT 'Shipment','Shipment',sh.id,sh.shipment_code,p.name,
                       CONCAT(c.name,
                              CASE WHEN sh.tracking_number IS NULL THEN ''
                                   ELSE CONCAT(' · ',sh.tracking_number) END),
                       (sh.archived_at_utc IS NOT NULL),
                       CASE WHEN CONVERT(sh.shipment_code USING utf8mb4)=@ExactQuery
                                  OR sh.tracking_number=@ExactQuery THEN 0
                            WHEN CONVERT(sh.shipment_code USING utf8mb4) LIKE @PrefixPattern ESCAPE '='
                              OR sh.tracking_number LIKE @PrefixPattern ESCAPE '=' THEN 1
                            ELSE 2 END,
                       sh.updated_at_utc
                FROM shipments sh
                INNER JOIN projects p ON p.id=sh.project_id
                INNER JOIN customers c ON c.id=sh.customer_id
                WHERE @IncludeShipments=1
                  AND (CONVERT(sh.shipment_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR sh.tracking_number LIKE @ContainsPattern ESCAPE '='
                       OR c.name LIKE @ContainsPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @ContainsPattern ESCAPE '='
                       OR p.name LIKE @ContainsPattern ESCAPE '=')
            ) AS search_results
            ORDER BY Relevance,IsArchived,UpdatedAtUtc DESC,Kind,TargetId
            LIMIT @Limit;
            """;

        var parameters = new
        {
            ExactQuery = query.Query,
            query.ContainsPattern,
            query.PrefixPattern,
            query.Limit,
            IncludeCustomers = query.Scope.Customers ? 1 : 0,
            IncludeProjects = query.Scope.Projects ? 1 : 0,
            IncludeEquipment = query.Scope.Equipment ? 1 : 0,
            IncludeService = query.Scope.Service ? 1 : 0,
            IncludePurchases = query.Scope.Purchases ? 1 : 0,
            IncludePayables = query.Scope.Payables ? 1 : 0,
            IncludePayments = query.Scope.Payments ? 1 : 0,
            IncludeShipments = query.Scope.Shipments ? 1 : 0,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var rows = await connection.QueryAsync<SearchResultRow>(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));
        return rows.Select(row => row.ToResult()).ToArray();
    }

    private sealed class SearchResultRow
    {
        public string Kind { get; init; } = string.Empty;
        public string TargetType { get; init; } = string.Empty;
        public ulong TargetId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Subtitle { get; init; }
        public long IsArchived { get; init; }

        public GlobalSearchResult ToResult() =>
            new(
                Kind,
                TargetType,
                TargetId,
                Code,
                Title,
                Subtitle,
                IsArchived != 0);
    }
}
