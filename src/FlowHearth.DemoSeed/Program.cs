using System.Reflection;
using Dapper;
using FlowHearth.Infrastructure.Database.Migrations;
using MySqlConnector;

return await DemoSeed.RunAsync(args);

internal static class DemoSeed
{
    private const string Prefix = "DEMO-";
    private static readonly string[] DemoTeamNames = ["演示项目经理", "演示实施工程师", "演示售后工程师"];
    private static readonly string[] CustomerNames = ["澄星", "远岚", "青禾", "云拓", "启衡", "辰序"];
    private static readonly string[] Industries = ["智能制造", "工业自动化", "新能源装备", "精密加工"];
    private static readonly string[] CustomerLevels = ["A", "B", "B", "C"];
    private static readonly string[] FollowUpMethods = ["Phone", "Visit", "Email", "WeChat"];
    private static readonly string[] FollowUpSummaries = ["需求访谈", "方案沟通", "报价确认", "交付协调"];
    private static readonly string[] ActiveOpportunityStages = ["Lead", "Qualified", "Proposal", "Negotiation"];
    private static readonly string[] MilestoneNames = ["方案确认", "设备集成", "客户验收"];
    private static readonly string[] EquipmentNames = ["视觉检测站", "伺服控制柜", "工业网关", "机器人控制单元"];
    private static readonly string[] EquipmentCategories = ["Camera", "Servo", "Network", "Robot"];
    private static readonly string[] SupplierCategories = ["自动化元件", "控制设备", "工业网络", "机电集成"];
    private static readonly string[] PurchaseItems = ["工业相机套件", "伺服驱动器", "边缘网关", "传感器组件"];
    private static readonly string[] ServiceTitles = ["视觉检测误报排查", "控制柜周期保养", "网络通讯抖动", "传感器标定支持"];
    private static readonly string[] BusinessTables =
    [
        "customers", "contacts", "customer_followups", "opportunities", "projects",
        "project_members", "project_milestones", "equipment", "equipment_components",
        "equipment_parameters", "equipment_versions", "service_tickets", "service_records",
        "suppliers", "purchase_orders", "purchase_order_items", "purchase_receipts",
        "purchase_receipt_items", "receivables", "receipts", "receipt_allocations",
        "payables", "payments", "payment_allocations", "shipments", "shipment_items",
        "attachments",
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != "initialize" || args[2] != "--confirm-isolated-demo")
        {
            Console.Error.WriteLine("Usage: dotnet run --project src/FlowHearth.DemoSeed -- initialize <flowhearth_demo_database> --confirm-isolated-demo");
            return 2;
        }

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (environment is not ("Development" or "Demo"))
        {
            Console.Error.WriteLine("Demo Seed requires DOTNET_ENVIRONMENT=Development or Demo.");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_DEMO_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("FLOWHEARTH_DEMO_MYSQL must contain the isolated database connection string.");
            return 2;
        }

        try
        {
            var options = new MySqlConnectionStringBuilder(connectionString);
            if (options.Server is not ("127.0.0.1" or "localhost" or "::1")
                || !options.Database.StartsWith("flowhearth_demo_", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(options.Database, args[1], StringComparison.OrdinalIgnoreCase)
                || options.Port is 0 or 3306)
            {
                throw new InvalidOperationException("Target must be an explicitly named flowhearth_demo_* database on loopback and a nonstandard local port.");
            }

            await using var connection = new MySqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            var selectedDatabase = await connection.ExecuteScalarAsync<string>("SELECT DATABASE()");
            if (!string.Equals(selectedDatabase, args[1], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Connected database identity did not match the explicit target.");
            }

            var lockName = $"flowhearth_demo_seed_{selectedDatabase}";
            if (await connection.ExecuteScalarAsync<int>("SELECT GET_LOCK(@lockName, 10)", new { lockName }) != 1)
            {
                throw new InvalidOperationException("Could not acquire the Demo Seed database lock.");
            }

            try
            {
                await VerifyMigrationsAsync(connection);
                var existingDemoRows = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM customers WHERE customer_code LIKE 'DEMO-C-%'");
                if (existingDemoRows > 0)
                {
                    await VerifySeedAsync(connection, null);
                    Console.WriteLine("Demo Seed already present and verified; no rows changed.");
                    return 0;
                }

                foreach (var table in BusinessTables)
                {
                    if (await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM `{table}`") != 0)
                    {
                        throw new InvalidOperationException($"Refusing to seed a database containing business data ({table}).");
                    }
                }

                var administratorCount = await connection.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM users u
                    JOIN user_roles ur ON ur.user_id=u.id
                    JOIN roles r ON r.id=ur.role_id
                    WHERE u.is_active=1 AND u.deleted_at_utc IS NULL
                      AND r.code='administrator' AND r.is_active=1
                    """);
                if (administratorCount == 0)
                {
                    throw new InvalidOperationException("Bootstrap a local-only administrator in the isolated database before Demo Seed.");
                }
                var auditCountBefore = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_logs");

                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await PopulateAsync(connection, transaction);
                    await VerifySeedAsync(connection, transaction);
                    var auditCountAfter = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM audit_logs", transaction: transaction);
                    if (auditCountAfter != auditCountBefore)
                    {
                        throw new InvalidOperationException("Demo Seed must not add audit log entries.");
                    }
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

                Console.WriteLine("Isolated Demo Seed initialized and verified. Audit logs were not generated.");
                return 0;
            }
            finally
            {
                await connection.ExecuteAsync("SELECT RELEASE_LOCK(@lockName)", new { lockName });
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Demo Seed refused or failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task VerifyMigrationsAsync(MySqlConnection connection)
    {
        var definitions = new MigrationFileLoader().Load(Path.Combine(AppContext.BaseDirectory, "migrations"));
        var applied = (await connection.QueryAsync<AppliedMigration>(
            "SELECT migration_id AS Id, checksum AS Checksum FROM schema_migrations"))
            .ToDictionary(migration => migration.Id, StringComparer.Ordinal);
        if (definitions.Count != 17 || applied.Count != definitions.Count
            || definitions.Any(definition => !applied.TryGetValue(definition.Id, out var migration)
                || !string.Equals(definition.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("All 17 published migrations and their original checksums must match before Demo Seed.");
        }
    }

    private sealed record AppliedMigration(string Id, string Checksum);

    private static async Task<long> PutAsync(MySqlConnection connection, MySqlTransaction transaction,
        string table, object values)
    {
        var columns = values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name).ToArray();
        var sql = $"INSERT INTO `{table}` ({string.Join(", ", columns.Select(column => $"`{column}`"))}) "
            + $"VALUES ({string.Join(", ", columns.Select(column => $"@{column}"))})";
        await connection.ExecuteAsync(sql, values, transaction);
        return await connection.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()", transaction: transaction);
    }

    private static string Code(string kind, int index) => $"{Prefix}{kind}-{index:000}";
    private static DateTime UtcDaysAgo(int days) => DateTime.UtcNow.Date.AddDays(-days).AddHours(3);

    private static async Task PopulateAsync(MySqlConnection connection, MySqlTransaction transaction)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var now = DateTime.UtcNow;
        var demoReceiverId = await PutAsync(connection, transaction, "users", new
        {
            username = "demo-seed-receiver",
            normalized_username = "DEMO-SEED-RECEIVER",
            display_name = "演示到货记录员（禁用）",
            password_hash = "!DISABLED-DEMO-SEED-ACCOUNT!",
            is_active = 0,
            password_changed_at_utc = now,
            created_at_utc = now,
            updated_at_utc = now,
        });
        var demoTeamIds = new long[3];
        for (var i = 0; i < demoTeamIds.Length; i++)
        {
            demoTeamIds[i] = await PutAsync(connection, transaction, "users", new
            {
                username = $"demo-team-{i + 1:00}",
                normalized_username = $"DEMO-TEAM-{i + 1:00}",
                display_name = DemoTeamNames[i],
                password_hash = "!DISABLED-DEMO-SEED-ACCOUNT!",
                is_active = 0,
                password_changed_at_utc = now,
                created_at_utc = now,
                updated_at_utc = now,
            });
        }
        var customerIds = new long[30];
        var customerCreated = new DateTime[30];
        var contactIds = new long[50];
        var opportunityIds = new long[20];
        var projectIds = new long[12];
        var equipmentIds = new long[20];
        var supplierIds = new long[12];

        for (var i = 0; i < customerIds.Length; i++)
        {
            var created = UtcDaysAgo(i < 25 ? 245 - i * 7 : i == 25 ? 57 : (30 - i) * 5);
            customerCreated[i] = created;
            customerIds[i] = await PutAsync(connection, transaction, "customers", new
            {
                customer_code = Code("C", i + 1),
                name = $"演示·{CustomerNames[i % 6]}智造{i + 1:00}（虚构）",
                short_name = $"演示客户{i + 1:00}",
                industry = Industries[i % 4],
                business_status = i >= 3 && i % 9 == 0 ? "Dormant" : i >= 3 && i % 5 == 0 ? "Prospect" : "Active",
                customer_level = CustomerLevels[i % 4],
                email = $"customer{i + 1:00}@example.test",
                notes = "FlowHearth 视频演示专用虚构客户；所有名称和联系方式均为示例。",
                created_at_utc = created,
                updated_at_utc = created,
            });
        }

        for (var i = 0; i < contactIds.Length; i++)
        {
            var customerIndex = i % customerIds.Length;
            var created = customerCreated[customerIndex].AddDays(1 + i / 30);
            contactIds[i] = await PutAsync(connection, transaction, "contacts", new
            {
                customer_id = customerIds[customerIndex],
                name = $"演示联系人{i + 1:00}",
                title = i % 3 == 0 ? "项目负责人" : "技术联系人",
                department = i % 2 == 0 ? "工程部" : "采购部",
                email = $"contact{i + 1:00}@example.test",
                is_primary = i < 30 ? 1 : 0,
                notes = "虚构联系人",
                created_at_utc = created,
                updated_at_utc = created,
            });
        }

        for (var i = 0; i < 80; i++)
        {
            var customerIndex = i % customerIds.Length;
            var contactIndex = i % contactIds.Length;
            var contactId = contactIndex % 30 == customerIndex ? contactIds[contactIndex] : (long?)null;
            var occurred = customerCreated[customerIndex].AddDays(3 + i / 30);
            await PutAsync(connection, transaction, "customer_followups", new
            {
                customer_id = customerIds[customerIndex],
                contact_id = contactId,
                method = FollowUpMethods[i % 4],
                occurred_at_utc = occurred,
                summary = FollowUpSummaries[i % 4] + $"（演示记录 {i + 1:00}）",
                details = "虚构业务沟通记录，用于展示客户跟进时间线。",
                next_follow_up_at_utc = i % 4 == 0 ? UtcDaysAgo(i % 16 == 0 ? 0 : -3) : (DateTime?)null,
                created_at_utc = occurred,
            });
        }

        var projectNames = new[]
        {
            "产线视觉检测升级", "柔性装配单元改造", "仓储输送控制系统", "能源监测平台集成",
            "机器人码垛工作站", "包装线追溯系统", "数控设备联网", "洁净车间监控改造",
            "自动分拣系统", "测试台控制升级", "设备预测维护试点", "多工位伺服协同",
        };
        for (var i = 0; i < opportunityIds.Length; i++)
        {
            var created = UtcDaysAgo(220 - i * 10);
            var stage = i < 12 ? "Won" : i < 18 ? ActiveOpportunityStages[(i - 12) % 4] : "Lost";
            var amount = 180000m + i * 47000m;
            opportunityIds[i] = await PutAsync(connection, transaction, "opportunities", new
            {
                opportunity_code = Code("O", i + 1),
                customer_id = customerIds[i],
                title = i < 12 ? projectNames[i] : $"演示自动化升级机会{i + 1:00}",
                stage,
                expected_amount = amount,
                probability_percent = stage == "Won" ? 100 : stage == "Lost" ? 0 : 20 + (i % 4) * 20,
                expected_close_date = today.AddDays(i < 12 ? -190 + i * 14 : 10 + i * 5).ToDateTime(TimeOnly.MinValue),
                description = "虚构方案机会，供业务流程演示。",
                lost_reason = stage == "Lost" ? "演示：客户暂缓投资计划" : null,
                created_at_utc = created,
                updated_at_utc = i < 12 ? UtcDaysAgo(190 - i * 14) : created.AddDays(8),
            });
        }

        for (var i = 0; i < projectIds.Length; i++)
        {
            var created = UtcDaysAgo(190 - i * 14);
            var status = i < 3 ? "Completed" : i < 8 ? "Active" : i < 10 ? "OnHold" : "Planning";
            projectIds[i] = await PutAsync(connection, transaction, "projects", new
            {
                project_code = Code("P", i + 1),
                customer_id = customerIds[i],
                source_opportunity_id = opportunityIds[i],
                name = projectNames[i],
                status,
                contract_amount = 180000m + i * 47000m,
                progress_percent = status == "Completed" ? 100m : status == "Active" ? 35m + i * 5m : status == "OnHold" ? 45m : 0m,
                description = "虚构项目，关联演示商机、采购、设备及财务记录。",
                planned_start_date = today.AddDays(i < 10 ? -185 + i * 14 : 10 + i).ToDateTime(TimeOnly.MinValue),
                planned_end_date = today.AddDays(i is 8 or 9 ? -9 : i < 3 ? -20 : i < 5 ? 10 + i * 5 : 20 + i * 8).ToDateTime(TimeOnly.MinValue),
                actual_start_date = i < 10 ? today.AddDays(-180 + i * 14).ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                actual_end_date = i < 3 ? today.AddDays(-24 + i * 4).ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                created_at_utc = created,
                updated_at_utc = i < 3 ? UtcDaysAgo(24 - i * 4) : UtcDaysAgo(1),
            });
            for (var j = 0; j < 2; j++)
            {
                await PutAsync(connection, transaction, "project_members", new
                {
                    project_id = projectIds[i],
                    user_id = demoTeamIds[(i + j) % demoTeamIds.Length],
                    role_name = j == 0 ? "项目经理" : "实施工程师",
                    responsibility = "虚构项目团队角色",
                    created_at_utc = created,
                    updated_at_utc = created,
                });
            }
            for (var j = 0; j < 3; j++)
            {
                await PutAsync(connection, transaction, "project_milestones", new
                {
                    project_id = projectIds[i],
                    name = MilestoneNames[j],
                    due_date = today.AddDays(i < 3 ? -80 + j * 30 : j == 0 ? -170 + i * 14 : j == 1 ? 10 + i * 5 : 20 + i * 8).ToDateTime(TimeOnly.MinValue),
                    completed_at_utc = i < 3 || (j == 0 && i < 8) ? UtcDaysAgo(55 - i * 3 - j * 15) : (DateTime?)null,
                    notes = "演示里程碑",
                    sort_order = j,
                    created_at_utc = created,
                    updated_at_utc = created,
                });
            }
        }

        for (var i = 0; i < equipmentIds.Length; i++)
        {
            var projectIndex = i % projectIds.Length;
            var created = UtcDaysAgo(152 - projectIndex * 12 - i / 12 * 8);
            equipmentIds[i] = await PutAsync(connection, transaction, "equipment", new
            {
                equipment_code = Code("E", i + 1),
                customer_id = customerIds[projectIndex],
                project_id = projectIds[projectIndex],
                name = EquipmentNames[i % 4] + $"（演示 {i + 1:00}）",
                category = EquipmentCategories[i % 4],
                manufacturer = "演示设备制造商",
                model = $"FH-DEMO-{i + 1:000}",
                serial_number = $"SIM-2026-{i + 1:0000}",
                install_location = $"演示产线 {projectIndex + 1:00} 区",
                commissioned_date = i < 10 ? today.AddDays(-35 + i).ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
                notes = "虚构序列号与设备档案",
                created_at_utc = created,
                updated_at_utc = created,
            });
            await PutAsync(connection, transaction, "equipment_components", new
            {
                equipment_id = equipmentIds[i],
                category = "Other",
                name = "演示控制模块",
                manufacturer = "演示元件厂",
                model = $"MOD-D-{i + 1:000}",
                serial_number = $"SIM-MOD-{i + 1:0000}",
                quantity = 1,
                sort_order = 1,
                created_at_utc = created,
                updated_at_utc = created,
            });
            await PutAsync(connection, transaction, "equipment_parameters", new
            {
                equipment_id = equipmentIds[i],
                parameter_group = "运行参数",
                name = "额定功率",
                value = $"{2 + i % 5}",
                unit = "kW",
                sort_order = 1,
                created_at_utc = created,
                updated_at_utc = created,
            });
            await PutAsync(connection, transaction, "equipment_versions", new
            {
                equipment_id = equipmentIds[i],
                version_type = "Firmware",
                version_label = $"v1.{i % 4}.0-demo",
                changelog = "演示固件版本",
                released_date = today.AddDays(-45 + i).ToDateTime(TimeOnly.MinValue),
                created_at_utc = created,
                updated_at_utc = created,
            });
        }

        for (var i = 0; i < supplierIds.Length; i++)
        {
            var created = UtcDaysAgo(200 - i * 12);
            supplierIds[i] = await PutAsync(connection, transaction, "suppliers", new
            {
                supplier_code = Code("S", i + 1),
                name = $"演示·配套供应商{i + 1:00}（虚构）",
                short_name = $"演示供应商{i + 1:00}",
                status = i == 11 ? "Inactive" : "Active",
                category = SupplierCategories[i % 4],
                contact_name = $"演示供应商联系人{i + 1:00}",
                email = $"supplier{i + 1:00}@example.test",
                payment_terms = "验收到货后 30 天",
                credit_days = 30,
                remark = "虚构供应商，不含真实银行账户。",
                created_at_utc = created,
                updated_at_utc = created,
            });
        }

        for (var i = 0; i < 20; i++)
        {
            var projectIndex = i < 12 ? i : 3 + (i - 12) % 9;
            var quantity = 2m + i % 4;
            var unitPrice = 8200m + i * 380m;
            var amount = quantity * unitPrice;
            var orderAge = 170 - projectIndex * 14 - i / 12 * 10;
            var orderDate = today.AddDays(-orderAge).ToDateTime(TimeOnly.MinValue);
            var status = i < 10 ? "Received" : i < 14 ? "PartiallyReceived" : "Ordered";
            var created = UtcDaysAgo(orderAge);
            var orderId = await PutAsync(connection, transaction, "purchase_orders", new
            {
                purchase_order_code = Code("PO", i + 1),
                supplier_id = supplierIds[i % supplierIds.Length],
                project_id = projectIds[projectIndex],
                order_date = orderDate,
                status,
                total_amount = amount,
                contact_name = $"演示供应商联系人{i % supplierIds.Length + 1:00}",
                delivery_address = "演示园区（虚构地址）",
                expected_delivery_date = today.AddDays(-orderAge + 14).ToDateTime(TimeOnly.MinValue),
                remark = "演示采购订单",
                created_at_utc = created,
                updated_at_utc = created,
            });
            var itemId = await PutAsync(connection, transaction, "purchase_order_items", new
            {
                purchase_order_id = orderId,
                item_name = PurchaseItems[i % 4],
                manufacturer = "演示元件厂",
                model = $"D-{i + 1:000}",
                specification = "演示规格",
                quantity,
                unit = "套",
                unit_price = unitPrice,
                amount,
                remark = "虚构采购明细",
                created_at_utc = created,
                updated_at_utc = created,
            });
            if (i < 14)
            {
                var receiptId = await PutAsync(connection, transaction, "purchase_receipts", new
                {
                    purchase_receipt_code = Code("GR", i + 1),
                    purchase_order_id = orderId,
                    received_date = today.AddDays(-orderAge + 8).ToDateTime(TimeOnly.MinValue),
                    received_by_user_id = demoReceiverId,
                    remark = "演示到货",
                    created_at_utc = created.AddDays(8),
                });
                await PutAsync(connection, transaction, "purchase_receipt_items", new
                {
                    purchase_receipt_id = receiptId,
                    purchase_order_item_id = itemId,
                    quantity_received = i < 10 ? quantity : quantity - 1m,
                });
            }

            var payableId = await PutAsync(connection, transaction, "payables", new
            {
                payable_code = Code("AP", i + 1),
                supplier_id = supplierIds[i % supplierIds.Length],
                project_id = projectIds[projectIndex],
                purchase_order_id = orderId,
                title = $"演示采购应付 {i + 1:00}",
                payable_type = "PurchasePayment",
                amount,
                due_date = today.AddDays(i < 12 ? -orderAge + 26 : 12 + i).ToDateTime(TimeOnly.MinValue),
                remark = "与采购金额一致的虚构应付",
                created_at_utc = created.AddDays(1),
                updated_at_utc = created.AddDays(1),
            });
            if (i < 12)
            {
                var paid = i > 0 && i % 3 == 0 ? amount / 2m : amount;
                var paymentId = await PutAsync(connection, transaction, "payments", new
                {
                    payment_code = Code("PM", i + 1),
                    supplier_id = supplierIds[i % supplierIds.Length],
                    payment_date = today.AddDays(-orderAge + Math.Min(20, orderAge - 2)).ToDateTime(TimeOnly.MinValue),
                    amount = paid,
                    payment_method = "BankTransfer",
                    payee_name = $"演示·配套供应商{i % supplierIds.Length + 1:00}（虚构）",
                    bank_reference = $"DEMO-PAY-{i + 1:000}",
                    remark = "虚构付款流水编号",
                    created_at_utc = created.AddDays(Math.Min(20, orderAge - 2)),
                    updated_at_utc = created.AddDays(Math.Min(20, orderAge - 2)),
                });
                await PutAsync(connection, transaction, "payment_allocations", new
                {
                    payment_id = paymentId,
                    payable_id = payableId,
                    allocated_amount = paid,
                    created_at_utc = created.AddDays(Math.Min(20, orderAge - 2)),
                });
            }
        }

        for (var i = 0; i < 12; i++)
        {
            var created = UtcDaysAgo(i < 3 ? 23 - i * 4 : 145 - i * 10);
            var amount = i < 3 ? 180000m + i * 47000m : 40000m + i * 6500m;
            var receivableId = await PutAsync(connection, transaction, "receivables", new
            {
                receivable_code = Code("AR", i + 1),
                customer_id = customerIds[i],
                project_id = projectIds[i],
                title = $"演示项目{(i < 3 ? "验收" : "阶段")}应收 {i + 1:00}",
                receivable_type = i < 3 || i % 2 != 0 ? "AcceptancePayment" : "ProgressPayment",
                amount,
                due_date = today.AddDays(i < 3 ? -17 + i * 4 : i < 8 ? -110 + i * 12 : 15 + i * 2).ToDateTime(TimeOnly.MinValue),
                description = "虚构项目回款计划",
                created_at_utc = created,
                updated_at_utc = created,
            });
            if (i < 9)
            {
                var received = i > 0 && i % 4 == 0 ? amount / 2m : amount;
                var receiptAge = i < 3 ? 21 - i * 4 : i >= 7 ? 3 + (8 - i) * 2 : 125 - i * 12;
                var receiptCreated = UtcDaysAgo(receiptAge);
                var receiptId = await PutAsync(connection, transaction, "receipts", new
                {
                    receipt_code = Code("RC", i + 1),
                    customer_id = customerIds[i],
                    receipt_date = today.AddDays(-receiptAge).ToDateTime(TimeOnly.MinValue),
                    amount = received,
                    payment_method = "BankTransfer",
                    bank_reference = $"DEMO-REC-{i + 1:000}",
                    payer_name = $"演示客户{i + 1:00}",
                    remark = "虚构收款流水编号",
                    created_at_utc = receiptCreated,
                    updated_at_utc = receiptCreated,
                });
                await PutAsync(connection, transaction, "receipt_allocations", new
                {
                    receipt_id = receiptId,
                    receivable_id = receivableId,
                    allocated_amount = received,
                    created_at_utc = receiptCreated,
                });
            }
        }

        for (var i = 0; i < 8; i++)
        {
            var created = UtcDaysAgo(40 - i * 3);
            var received = i < 5;
            var shipmentId = await PutAsync(connection, transaction, "shipments", new
            {
                shipment_code = Code("SH", i + 1),
                customer_id = customerIds[i],
                project_id = projectIds[i],
                shipment_date = today.AddDays(-36 + i * 3).ToDateTime(TimeOnly.MinValue),
                status = received ? "Received" : i == 5 ? "InTransit" : "Preparing",
                receiver_name = $"演示联系人{i + 1:00}",
                logistics_company = "演示物流",
                tracking_number = $"DEMO-TRACK-{i + 1:000}",
                shipping_address = "演示园区，虚构收货地址",
                signed_at_utc = received ? created.AddDays(5) : (DateTime?)null,
                remark = "虚构出货与签收记录",
                created_at_utc = created,
                updated_at_utc = created.AddDays(received ? 5 : 0),
            });
            await PutAsync(connection, transaction, "shipment_items", new
            {
                shipment_id = shipmentId,
                equipment_id = equipmentIds[i],
                item_name = "演示设备交付包",
                manufacturer = "演示设备制造商",
                model = $"FH-DEMO-{i + 1:000}",
                quantity = 1m,
                unit = "套",
                remark = "虚构交付明细",
                created_at_utc = created,
                updated_at_utc = created,
            });
        }

        for (var i = 0; i < 15; i++)
        {
            var equipmentIndex = i % equipmentIds.Length;
            var projectIndex = equipmentIndex % projectIds.Length;
            var reported = UtcDaysAgo(30 - i);
            var status = i < 5 ? "Closed" : i < 9 ? "Resolved" : i < 13 ? "InProgress" : "New";
            var ticketId = await PutAsync(connection, transaction, "service_tickets", new
            {
                service_code = Code("SV", i + 1),
                customer_id = customerIds[projectIndex],
                project_id = projectIds[projectIndex],
                equipment_id = equipmentIds[equipmentIndex],
                title = ServiceTitles[i % 4] + $"（演示 {i + 1:00}）",
                description = "虚构售后请求，用于展示设备到工单的关联。",
                priority = i % 6 == 0 ? "P2" : "P3",
                status,
                assigned_to_user_id = status == "New" ? (long?)null : demoTeamIds[2],
                reported_at_utc = reported,
                responded_at_utc = status != "New" ? reported.AddHours(2) : (DateTime?)null,
                resolved_at_utc = status is "Closed" or "Resolved" ? reported.AddDays(2) : (DateTime?)null,
                closed_at_utc = status == "Closed" ? reported.AddDays(3) : (DateTime?)null,
                root_cause = status is "Closed" or "Resolved" ? "演示：参数漂移" : null,
                solution = status is "Closed" or "Resolved" ? "演示：重新标定并验证" : null,
                downtime_minutes = status == "New" ? 0 : 40,
                created_at_utc = reported,
                updated_at_utc = reported.AddDays(status == "New" ? 0 : status == "Closed" ? 3 : 2),
            });
            if (status != "New")
            {
                for (var recordIndex = 0; recordIndex < 2; recordIndex++)
                {
                    var occurred = reported.AddHours(recordIndex == 0 ? 3 : 5);
                    await PutAsync(connection, transaction, "service_records", new
                    {
                        service_ticket_id = ticketId,
                        record_type = recordIndex == 0 ? "Diagnosis" : "Action",
                        content = recordIndex == 0
                            ? "虚构演示诊断：复现通讯及标定异常，检查参数与连接状态。"
                            : "虚构演示处理：调整参数并完成验证，保留后续观察建议。",
                        duration_minutes = 20,
                        occurred_at_utc = occurred,
                        created_at_utc = occurred,
                        updated_at_utc = occurred,
                    });
                }
            }
        }
    }

    private static async Task VerifySeedAsync(MySqlConnection connection, MySqlTransaction? transaction)
    {
        var expected = new Dictionary<string, (string Column, int Count)>
        {
            ["customers"] = ("customer_code", 30),
            ["contacts"] = ("id", 50),
            ["customer_followups"] = ("id", 80),
            ["opportunities"] = ("opportunity_code", 20),
            ["projects"] = ("project_code", 12),
            ["project_members"] = ("id", 24),
            ["project_milestones"] = ("id", 36),
            ["equipment"] = ("equipment_code", 20),
            ["equipment_components"] = ("id", 20),
            ["equipment_parameters"] = ("id", 20),
            ["equipment_versions"] = ("id", 20),
            ["suppliers"] = ("supplier_code", 12),
            ["purchase_orders"] = ("purchase_order_code", 20),
            ["purchase_order_items"] = ("id", 20),
            ["purchase_receipts"] = ("purchase_receipt_code", 14),
            ["purchase_receipt_items"] = ("id", 14),
            ["payables"] = ("payable_code", 20),
            ["payments"] = ("payment_code", 12),
            ["payment_allocations"] = ("id", 12),
            ["receivables"] = ("receivable_code", 12),
            ["receipts"] = ("receipt_code", 9),
            ["receipt_allocations"] = ("id", 9),
            ["shipments"] = ("shipment_code", 8),
            ["shipment_items"] = ("id", 8),
            ["service_tickets"] = ("service_code", 15),
            ["service_records"] = ("id", 26),
        };
        foreach (var (table, definition) in expected)
        {
            var count = await connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM `{table}`", transaction: transaction);
            if (count != definition.Count)
            {
                throw new InvalidOperationException($"Seed verification failed: {table} has {count} rows, expected {definition.Count}.");
            }
            if (definition.Column != "id" && await connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM `{table}` WHERE `{definition.Column}` NOT LIKE 'DEMO-%'",
                transaction: transaction) != 0)
            {
                throw new InvalidOperationException($"Seed verification found non-demo identifiers in {table}.");
            }
        }

        var broken = await connection.ExecuteScalarAsync<int>(
            """
            SELECT
              (SELECT COUNT(*) FROM projects p JOIN opportunities o ON o.id=p.source_opportunity_id
               WHERE p.customer_id<>o.customer_id OR o.stage<>'Won')
              + (SELECT COUNT(*) FROM equipment e JOIN projects p ON p.id=e.project_id
                 WHERE e.customer_id<>p.customer_id)
              + (SELECT COUNT(*) FROM service_tickets s JOIN equipment e ON e.id=s.equipment_id
                 WHERE s.customer_id<>e.customer_id OR s.project_id<>e.project_id)
              + (SELECT COUNT(*) FROM shipments s JOIN projects p ON p.id=s.project_id
                 WHERE s.customer_id<>p.customer_id)
              + (SELECT COUNT(*) FROM receivables r JOIN projects p ON p.id=r.project_id
                 WHERE r.customer_id<>p.customer_id)
              + (SELECT COUNT(*) FROM payables a JOIN purchase_orders p ON p.id=a.purchase_order_id
                 WHERE a.supplier_id<>p.supplier_id OR a.project_id<>p.project_id OR a.amount<>p.total_amount)
              + (SELECT COUNT(*) FROM purchase_orders p JOIN
                   (SELECT purchase_order_id, SUM(amount) AS item_amount FROM purchase_order_items
                    WHERE deleted_at_utc IS NULL GROUP BY purchase_order_id) i ON i.purchase_order_id=p.id
                 WHERE p.total_amount<>i.item_amount)
              + (SELECT COUNT(*) FROM purchase_order_items i LEFT JOIN
                   (SELECT purchase_order_item_id, SUM(quantity_received) AS received FROM purchase_receipt_items
                    GROUP BY purchase_order_item_id) r ON r.purchase_order_item_id=i.id
                 WHERE COALESCE(r.received,0)>i.quantity)
              + (SELECT COUNT(*) FROM purchase_order_items WHERE amount<>quantity*unit_price)
              + (SELECT COUNT(*) FROM purchase_orders o
                 JOIN (SELECT purchase_order_id,SUM(quantity) quantity FROM purchase_order_items
                       GROUP BY purchase_order_id) i ON i.purchase_order_id=o.id
                 LEFT JOIN (SELECT r.purchase_order_id,SUM(v.quantity_received) quantity
                            FROM purchase_receipts r JOIN purchase_receipt_items v ON v.purchase_receipt_id=r.id
                            GROUP BY r.purchase_order_id) q ON q.purchase_order_id=o.id
                 WHERE o.status<>CASE WHEN COALESCE(q.quantity,0)=0 THEN 'Ordered'
                                     WHEN q.quantity=i.quantity THEN 'Received' ELSE 'PartiallyReceived' END)
              + (SELECT COUNT(*) FROM contacts c JOIN customers p ON p.id=c.customer_id
                 WHERE c.created_at_utc<p.created_at_utc)
              + (SELECT COUNT(*) FROM customer_followups f JOIN customers c ON c.id=f.customer_id
                 LEFT JOIN contacts p ON p.id=f.contact_id
                 WHERE f.occurred_at_utc<c.created_at_utc OR p.customer_id<>f.customer_id
                   OR f.occurred_at_utc<p.created_at_utc)
              + (SELECT COUNT(*) FROM projects p JOIN opportunities o ON o.id=p.source_opportunity_id
                 WHERE p.created_at_utc<o.created_at_utc OR p.actual_end_date<p.actual_start_date)
              + (SELECT COUNT(*) FROM purchase_orders o JOIN suppliers s ON s.id=o.supplier_id
                 JOIN projects p ON p.id=o.project_id
                 WHERE o.created_at_utc<s.created_at_utc OR o.created_at_utc<p.created_at_utc)
              + (SELECT COUNT(*) FROM purchase_receipts r JOIN purchase_orders o ON o.id=r.purchase_order_id
                 WHERE r.received_date<o.order_date)
              + (SELECT COUNT(*) FROM shipment_items i JOIN shipments s ON s.id=i.shipment_id
                 JOIN equipment e ON e.id=i.equipment_id
                 WHERE e.customer_id<>s.customer_id OR e.project_id<>s.project_id)
              + (SELECT COUNT(*) FROM equipment e JOIN projects p ON p.id=e.project_id
                 WHERE e.created_at_utc<p.created_at_utc)
              + (SELECT COUNT(*) FROM service_tickets WHERE reported_at_utc>responded_at_utc
                 OR responded_at_utc>resolved_at_utc OR resolved_at_utc>closed_at_utc)
              + (SELECT COUNT(*) FROM payment_allocations a JOIN payments p ON p.id=a.payment_id
                 JOIN payables b ON b.id=a.payable_id WHERE p.supplier_id<>b.supplier_id)
              + (SELECT COUNT(*) FROM receipt_allocations a JOIN receipts r ON r.id=a.receipt_id
                 JOIN receivables b ON b.id=a.receivable_id WHERE r.customer_id<>b.customer_id)
            """, transaction: transaction);
        if (broken != 0)
        {
            throw new InvalidOperationException($"Seed relationship verification found {broken} inconsistent rows.");
        }

        var completeCases = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM projects p
            JOIN receivables r ON r.project_id=p.id AND r.amount=p.contract_amount
            JOIN receipt_allocations a ON a.receivable_id=r.id AND a.allocated_amount=r.amount
            JOIN receipts c ON c.id=a.receipt_id AND c.amount=r.amount
            WHERE p.project_code IN ('DEMO-P-001','DEMO-P-002','DEMO-P-003')
              AND p.status='Completed' AND DATE(r.created_at_utc)>=p.actual_end_date
              AND a.cancelled_at_utc IS NULL
              AND c.receipt_date>=DATE(r.created_at_utc)
              AND EXISTS (SELECT 1 FROM shipments s WHERE s.project_id=p.id AND s.status='Received')
              AND EXISTS (SELECT 1 FROM service_tickets t WHERE t.project_id=p.id AND t.status='Closed')
              AND EXISTS (SELECT 1 FROM payables b JOIN payment_allocations v ON v.payable_id=b.id
                          JOIN purchase_orders o ON o.id=b.purchase_order_id
                          WHERE b.project_id=p.id AND v.allocated_amount=b.amount
                            AND v.cancelled_at_utc IS NULL AND o.status='Received')
            """, transaction: transaction);
        if (completeCases != 3)
        {
            throw new InvalidOperationException("Seed must contain three fully settled, delivered and serviced completed projects.");
        }

        foreach (var (allocation, transactionTable, transactionId, obligationTable, obligationId) in new[]
        {
            ("payment_allocations", "payments", "payment_id", "payables", "payable_id"),
            ("receipt_allocations", "receipts", "receipt_id", "receivables", "receivable_id"),
        })
        {
            foreach (var (table, key) in new[] { (transactionTable, transactionId), (obligationTable, obligationId) })
            {
                var excess = await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM `{table}` t JOIN (SELECT `{key}`, SUM(allocated_amount) amount " +
                    $"FROM `{allocation}` WHERE cancelled_at_utc IS NULL GROUP BY `{key}`) a ON a.`{key}`=t.id " +
                    "WHERE a.amount>t.amount", transaction: transaction);
                if (excess != 0)
                {
                    throw new InvalidOperationException($"Seed financial verification failed for {table}.");
                }
            }
        }

    }
}
