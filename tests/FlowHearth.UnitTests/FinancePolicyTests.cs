using FlowHearth.Domain.Finance;

namespace FlowHearth.UnitTests;

public sealed class FinancePolicyTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    [Theory]
    [InlineData(0, 2026, 9, 2, FinanceBalanceStatus.NotDue)]
    [InlineData(40, 2026, 9, 2, FinanceBalanceStatus.PartiallyPaid)]
    [InlineData(100, 2026, 8, 1, FinanceBalanceStatus.Paid)]
    [InlineData(0, 2026, 8, 31, FinanceBalanceStatus.Overdue)]
    [InlineData(40, 2026, 8, 31, FinanceBalanceStatus.PartiallyOverdue)]
    public void BalanceStatusUsesAmountAllocationAndDueDate(
        decimal allocated,
        int year,
        int month,
        int day,
        FinanceBalanceStatus expected)
    {
        var actual = FinanceBalancePolicy.Calculate(
            100m,
            allocated,
            new DateOnly(year, month, day),
            Today);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AllocationAcceptsPartialAndMultiObligationAmounts()
    {
        FinanceAllocationPolicy.Validate(100_000m, 60_000m, 80_000m, 0m, 40_000m, true);
    }

    [Theory]
    [InlineData(100, 90, 100, 0, 11)]
    [InlineData(100, 0, 100, 90, 11)]
    public void AllocationRejectsOverAllocation(
        decimal transactionAmount,
        decimal transactionAllocated,
        decimal obligationAmount,
        decimal obligationAllocated,
        decimal requested)
    {
        Assert.Throws<InvalidOperationException>(() =>
            FinanceAllocationPolicy.Validate(
                transactionAmount,
                transactionAllocated,
                obligationAmount,
                obligationAllocated,
                requested,
                true));
    }

    [Fact]
    public void AllocationRejectsDifferentParties()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FinanceAllocationPolicy.Validate(100m, 0m, 100m, 0m, 10m, false));
    }

    [Fact]
    public void ShanghaiBusinessDateDoesNotExpireDueDateAfterUtcDayChanges()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var instant = new DateTimeOffset(2026, 8, 31, 16, 30, 0, TimeSpan.Zero);

        var businessDate = FinanceBusinessDatePolicy.GetBusinessDate(instant, zone);
        var status = FinanceBalancePolicy.Calculate(
            100m, 0m, new DateOnly(2026, 9, 1), businessDate);

        Assert.Equal(new DateOnly(2026, 9, 1), businessDate);
        Assert.Equal(FinanceBalanceStatus.NotDue, status);
    }

    [Theory]
    [InlineData(0, PaymentStatus.Unpaid)]
    [InlineData(40, PaymentStatus.PartiallyPaid)]
    [InlineData(100, PaymentStatus.Paid)]
    public void PaymentStatusComesOnlyFromAllocatedBalance(
        decimal allocated,
        PaymentStatus expected)
    {
        Assert.Equal(expected, PaymentBalancePolicy.Calculate(100m, allocated));
    }

    [Fact]
    public void PaidPayableIsNeverOverdue()
    {
        Assert.False(PaymentBalancePolicy.IsOverdue(
            100m, 100m, Today.AddDays(-30), Today));
        Assert.True(PaymentBalancePolicy.IsOverdue(
            100m, 99m, Today.AddDays(-1), Today));
        Assert.False(PaymentBalancePolicy.IsOverdue(
            100m, 0m, Today, Today));
    }

    [Fact]
    public void PaymentAndPayableBalancesUseTheSameAllocatedFact()
    {
        Assert.Equal(60m, PaymentBalancePolicy.Remaining(100m, 40m));
        Assert.Equal(0m, PaymentBalancePolicy.Remaining(100m, 100m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PaymentBalancePolicy.Remaining(100m, 100.01m));
    }

    [Theory]
    [InlineData(100, 60, 80, 30, 40, 40, 50)]
    [InlineData(100, 120, 40, 30, -20, -20, 10)]
    public void OperatingSummarySeparatesGrossProfitFromCashFlow(
        decimal contractAmount,
        decimal purchaseAmount,
        decimal receivedAllocated,
        decimal paidAllocated,
        decimal expectedGrossProfit,
        decimal expectedGrossMargin,
        decimal expectedCashNetInflow)
    {
        var result = OperatingFinancePolicy.Calculate(
            contractAmount, purchaseAmount, receivedAllocated, paidAllocated);

        Assert.Equal(expectedGrossProfit, result.GrossProfit);
        Assert.Equal(expectedGrossMargin, result.GrossMargin);
        Assert.Equal(expectedCashNetInflow, result.CashNetInflow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OperatingSummaryDoesNotInventMarginWithoutPositiveContract(
        decimal contractAmount)
    {
        var result = OperatingFinancePolicy.Calculate(contractAmount, 20m, 0m, 0m);

        Assert.Null(result.GrossMargin);
    }

    [Theory]
    [InlineData(0, PayableAgingBucket.NotDue)]
    [InlineData(1, PayableAgingBucket.Days1To30)]
    [InlineData(30, PayableAgingBucket.Days1To30)]
    [InlineData(31, PayableAgingBucket.Days31To60)]
    [InlineData(60, PayableAgingBucket.Days31To60)]
    [InlineData(61, PayableAgingBucket.Days61To90)]
    [InlineData(90, PayableAgingBucket.Days61To90)]
    [InlineData(91, PayableAgingBucket.Over90Days)]
    public void PayableAgingUsesBusinessDateAndRemainingBalance(
        int overdueDays,
        PayableAgingBucket expected)
    {
        Assert.Equal(expected, PaymentBalancePolicy.GetAgingBucket(
            100m, 40m, Today.AddDays(-overdueDays), Today));
        Assert.Null(PaymentBalancePolicy.GetAgingBucket(
            100m, 100m, Today.AddDays(-overdueDays), Today));
    }

    [Fact]
    public void PercentagePlanAssignsRoundingRemainderToLastItem()
    {
        var amounts = ReceivablePlanPolicy.CalculatePercentageAmounts(
            100m, [33.33m, 33.33m, 33.34m]);

        Assert.Equal([33.33m, 33.33m, 33.34m], amounts);
        Assert.Equal(100m, amounts.Sum());
    }

    [Theory]
    [InlineData(2.5, 19.995, 49.99)]
    [InlineData(3, 100, 300)]
    public void PurchaseItemAmountIsServerCalculated(
        decimal quantity,
        decimal unitPrice,
        decimal expected)
    {
        Assert.Equal(expected, PurchaseOrderPolicy.CalculateItemAmount(quantity, unitPrice));
    }

    [Theory]
    [InlineData(0, PurchaseOrderStatus.Ordered)]
    [InlineData(2, PurchaseOrderStatus.PartiallyReceived)]
    [InlineData(5, PurchaseOrderStatus.Received)]
    public void PurchaseStatusTracksCumulativeReceipt(
        decimal received,
        PurchaseOrderStatus expected)
    {
        Assert.Equal(expected, PurchaseOrderPolicy.StatusFromReceiptTotals(5m, received));
    }

    [Fact]
    public void PurchaseReceiptRejectsExcessQuantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PurchaseOrderPolicy.StatusFromReceiptTotals(5m, 5.0001m));
    }

    [Theory]
    [InlineData(ShipmentStatus.Preparing, ShipmentStatus.Shipped, true)]
    [InlineData(ShipmentStatus.Shipped, ShipmentStatus.InTransit, true)]
    [InlineData(ShipmentStatus.Shipped, ShipmentStatus.Received, true)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Received, true)]
    [InlineData(ShipmentStatus.Preparing, ShipmentStatus.Cancelled, true)]
    [InlineData(ShipmentStatus.Received, ShipmentStatus.Shipped, false)]
    [InlineData(ShipmentStatus.Received, ShipmentStatus.Preparing, false)]
    [InlineData(ShipmentStatus.Received, ShipmentStatus.Cancelled, false)]
    [InlineData(ShipmentStatus.Shipped, ShipmentStatus.Cancelled, false)]
    public void ShipmentTransitionsPreserveTerminalHistory(
        ShipmentStatus current,
        ShipmentStatus target,
        bool expected)
    {
        Assert.Equal(expected, ShipmentStatusPolicy.CanTransition(current, target));
    }
}
