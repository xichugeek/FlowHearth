using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Dashboard;

namespace FlowHearth.Infrastructure.Dashboard;

public sealed class MySqlDashboardRepository(IDbConnectionFactory connectionFactory)
    : IDashboardRepository
{
    public async Task<DashboardSnapshot> GetAsync(
        DashboardQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CustomerTrendBuckets.Count != 6)
        {
            throw new ArgumentException(
                "The dashboard query must contain exactly six customer trend buckets.",
                nameof(query));
        }

        const string sql =
            """
            SELECT
                CASE WHEN @IncludeCustomers=1 THEN
                    (SELECT COUNT(*) FROM customers WHERE archived_at_utc IS NULL)
                END AS TotalCustomers,
                CASE WHEN @IncludeCustomers=1 THEN
                    (SELECT COUNT(*) FROM customers
                     WHERE archived_at_utc IS NULL
                       AND created_at_utc>=@CurrentMonthStartUtc
                       AND created_at_utc<@NextMonthStartUtc)
                END AS NewCustomersThisMonth,
                CASE WHEN @IncludeOpportunities=1 THEN
                    (SELECT COUNT(*) FROM opportunities
                     WHERE archived_at_utc IS NULL
                       AND stage IN ('Lead','Qualified','Proposal','Negotiation'))
                END AS ActiveOpportunities,
                CASE WHEN @IncludeOpportunities=1 THEN
                    (SELECT COALESCE(SUM(expected_amount),0) FROM opportunities
                     WHERE archived_at_utc IS NULL
                       AND stage IN ('Lead','Qualified','Proposal','Negotiation'))
                END AS ExpectedOpportunityAmount,
                CASE WHEN @IncludeProjects=1 THEN
                    (SELECT COUNT(*) FROM projects
                     WHERE archived_at_utc IS NULL
                       AND status IN ('Planning','Active','OnHold'))
                END AS ActiveProjects,
                CASE WHEN @IncludeProjects=1 THEN
                    (SELECT COUNT(*) FROM projects
                     WHERE archived_at_utc IS NULL
                       AND status IN ('Planning','Active','OnHold')
                       AND planned_end_date>=@DeliveryStartDate
                       AND planned_end_date<=@DeliveryEndDate)
                END AS ProjectsNearingDelivery,
                CASE WHEN @IncludeService=1 THEN
                    (SELECT COUNT(*) FROM service_tickets
                     WHERE archived_at_utc IS NULL
                       AND status NOT IN ('Closed','Cancelled'))
                END AS OpenTickets,
                CASE WHEN @IncludeService=1 THEN
                    (SELECT COUNT(*) FROM service_tickets
                     WHERE archived_at_utc IS NULL
                       AND status NOT IN ('Closed','Cancelled')
                       AND priority IN ('P1','P2'))
                END AS OpenPriorityTickets,
                CASE WHEN @IncludeCustomers=1 THEN
                    (SELECT COUNT(*)
                     FROM customer_followups AS f
                     INNER JOIN customers AS c ON c.id=f.customer_id
                     WHERE f.deleted_at_utc IS NULL
                       AND c.archived_at_utc IS NULL
                       AND f.next_follow_up_at_utc>=@TodayStartUtc
                       AND f.next_follow_up_at_utc<@TomorrowStartUtc)
                END AS DueFollowUpsToday,
                CASE WHEN @IncludeCustomers=1 THEN
                    (SELECT COUNT(*)
                     FROM customer_followups AS f
                     INNER JOIN customers AS c ON c.id=f.customer_id
                     WHERE f.deleted_at_utc IS NULL
                       AND c.archived_at_utc IS NULL
                       AND f.next_follow_up_at_utc>=@TomorrowStartUtc
                       AND f.next_follow_up_at_utc<@NextSevenDaysEndUtc)
                END AS NextSevenDaysFollowUps;

            SELECT stage AS `Key`,COUNT(*) AS `Count`
            FROM opportunities
            WHERE @IncludeOpportunities=1 AND archived_at_utc IS NULL
            GROUP BY stage
            ORDER BY FIELD(stage,'Lead','Qualified','Proposal','Negotiation','Won','Lost');

            SELECT status AS `Key`,COUNT(*) AS `Count`
            FROM projects
            WHERE @IncludeProjects=1 AND archived_at_utc IS NULL
            GROUP BY status
            ORDER BY FIELD(status,'Planning','Active','OnHold','Completed','Cancelled');

            SELECT @Trend0Month AS Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend0StartUtc AND created_at_utc<@Trend0EndUtc)
                   ELSE 0 END AS `Count`
            UNION ALL
            SELECT @Trend1Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend1StartUtc AND created_at_utc<@Trend1EndUtc)
                   ELSE 0 END
            UNION ALL
            SELECT @Trend2Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend2StartUtc AND created_at_utc<@Trend2EndUtc)
                   ELSE 0 END
            UNION ALL
            SELECT @Trend3Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend3StartUtc AND created_at_utc<@Trend3EndUtc)
                   ELSE 0 END
            UNION ALL
            SELECT @Trend4Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend4StartUtc AND created_at_utc<@Trend4EndUtc)
                   ELSE 0 END
            UNION ALL
            SELECT @Trend5Month,
                   CASE WHEN @IncludeCustomers=1 THEN
                       (SELECT COUNT(*) FROM customers
                        WHERE created_at_utc>=@Trend5StartUtc AND created_at_utc<@Trend5EndUtc)
                   ELSE 0 END;
            """;

        var parameters = CreateParameters(query);
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var metrics = await result.ReadSingleAsync<DashboardMetrics>();
        var opportunityStages = (await result.ReadAsync<DashboardDistributionItem>()).AsList();
        var projectStatuses = (await result.ReadAsync<DashboardDistributionItem>()).AsList();
        var trend = (await result.ReadAsync<DashboardTrendPoint>()).AsList();

        return new DashboardSnapshot(
            metrics,
            opportunityStages,
            projectStatuses,
            query.Scope.Customers ? trend : []);
    }

    private static DynamicParameters CreateParameters(DashboardQuery query)
    {
        var parameters = new DynamicParameters(new
        {
            IncludeCustomers = query.Scope.Customers ? 1 : 0,
            IncludeOpportunities = query.Scope.Opportunities ? 1 : 0,
            IncludeProjects = query.Scope.Projects ? 1 : 0,
            IncludeService = query.Scope.Service ? 1 : 0,
            query.CurrentMonthStartUtc,
            query.NextMonthStartUtc,
            query.TodayStartUtc,
            query.TomorrowStartUtc,
            query.NextSevenDaysEndUtc,
            query.DeliveryStartDate,
            query.DeliveryEndDate,
        });

        for (var index = 0; index < query.CustomerTrendBuckets.Count; index++)
        {
            var bucket = query.CustomerTrendBuckets[index];
            parameters.Add($"Trend{index}Month", bucket.Month);
            parameters.Add($"Trend{index}StartUtc", bucket.StartUtc);
            parameters.Add($"Trend{index}EndUtc", bucket.EndUtc);
        }

        return parameters;
    }
}
