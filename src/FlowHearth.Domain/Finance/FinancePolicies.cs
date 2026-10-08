namespace FlowHearth.Domain.Finance;

public static class FinanceBusinessDatePolicy
{
    public static DateOnly GetBusinessDate(
        DateTimeOffset instant,
        TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
}

public static class FinanceBalancePolicy
{
    public static FinanceBalanceStatus Calculate(
        decimal amount,
        decimal allocatedAmount,
        DateOnly dueDate,
        DateOnly businessDate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (allocatedAmount < 0 || allocatedAmount > amount)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedAmount));
        }

        if (allocatedAmount == amount)
        {
            return FinanceBalanceStatus.Paid;
        }

        if (dueDate < businessDate)
        {
            return allocatedAmount > 0
                ? FinanceBalanceStatus.PartiallyOverdue
                : FinanceBalanceStatus.Overdue;
        }

        return allocatedAmount > 0
            ? FinanceBalanceStatus.PartiallyPaid
            : FinanceBalanceStatus.NotDue;
    }
}

public static class FinanceAllocationPolicy
{
    public static void Validate(
        decimal transactionAmount,
        decimal transactionAllocatedAmount,
        decimal obligationAmount,
        decimal obligationAllocatedAmount,
        decimal requestedAmount,
        bool sameParty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedAmount);

        if (!sameParty)
        {
            throw new InvalidOperationException("Allocation parties must match.");
        }

        if (transactionAmount <= 0
            || transactionAllocatedAmount < 0
            || transactionAllocatedAmount > transactionAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionAllocatedAmount));
        }

        if (obligationAmount <= 0
            || obligationAllocatedAmount < 0
            || obligationAllocatedAmount > obligationAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(obligationAllocatedAmount));
        }

        if (requestedAmount > transactionAmount - transactionAllocatedAmount)
        {
            throw new InvalidOperationException("Allocation exceeds the transaction balance.");
        }

        if (requestedAmount > obligationAmount - obligationAllocatedAmount)
        {
            throw new InvalidOperationException("Allocation exceeds the obligation balance.");
        }
    }
}

public static class PaymentBalancePolicy
{
    public static decimal Remaining(decimal amount, decimal allocatedAmount)
    {
        Calculate(amount, allocatedAmount);
        return amount - allocatedAmount;
    }

    public static PaymentStatus Calculate(decimal amount, decimal allocatedAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (allocatedAmount < 0 || allocatedAmount > amount)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedAmount));
        }

        if (allocatedAmount == 0)
        {
            return PaymentStatus.Unpaid;
        }

        return allocatedAmount == amount
            ? PaymentStatus.Paid
            : PaymentStatus.PartiallyPaid;
    }

    public static bool IsOverdue(
        decimal amount,
        decimal allocatedAmount,
        DateOnly dueDate,
        DateOnly businessDate) =>
        Calculate(amount, allocatedAmount) != PaymentStatus.Paid
        && dueDate < businessDate;

    public static PayableAgingBucket? GetAgingBucket(
        decimal amount,
        decimal allocatedAmount,
        DateOnly dueDate,
        DateOnly businessDate)
    {
        if (Calculate(amount, allocatedAmount) == PaymentStatus.Paid)
        {
            return null;
        }

        var overdueDays = businessDate.DayNumber - dueDate.DayNumber;
        return overdueDays switch
        {
            <= 0 => PayableAgingBucket.NotDue,
            <= 30 => PayableAgingBucket.Days1To30,
            <= 60 => PayableAgingBucket.Days31To60,
            <= 90 => PayableAgingBucket.Days61To90,
            _ => PayableAgingBucket.Over90Days,
        };
    }
}

public sealed record OperatingFinanceResult(
    decimal GrossProfit,
    decimal? GrossMargin,
    decimal CashNetInflow);

public static class OperatingFinancePolicy
{
    public static OperatingFinanceResult Calculate(
        decimal contractAmount,
        decimal purchaseAmount,
        decimal receivedAllocatedAmount,
        decimal paidAllocatedAmount)
    {
        var grossProfit = contractAmount - purchaseAmount;
        decimal? grossMargin = contractAmount <= 0
            ? null
            : decimal.Round(grossProfit / contractAmount * 100m, 2);
        return new OperatingFinanceResult(
            grossProfit, grossMargin, receivedAllocatedAmount - paidAllocatedAmount);
    }
}

public static class ReceivablePlanPolicy
{
    public static IReadOnlyList<decimal> CalculatePercentageAmounts(
        decimal contractAmount,
        IReadOnlyList<decimal> percentages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contractAmount);
        ArgumentNullException.ThrowIfNull(percentages);
        if (percentages.Count == 0 || percentages.Any(value => value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(percentages));
        }

        if (percentages.Sum() != 100m)
        {
            throw new ArgumentException("Percentages must total 100.", nameof(percentages));
        }

        var result = new decimal[percentages.Count];
        var allocated = 0m;
        for (var index = 0; index < percentages.Count - 1; index++)
        {
            result[index] = decimal.Round(
                contractAmount * percentages[index] / 100m,
                2,
                MidpointRounding.AwayFromZero);
            allocated += result[index];
        }

        result[^1] = contractAmount - allocated;
        if (result[^1] <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentages));
        }

        return result;
    }
}

public static class PurchaseOrderPolicy
{
    public static decimal CalculateItemAmount(decimal quantity, decimal unitPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);

        return decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
    }

    public static PurchaseOrderStatus StatusFromReceiptTotals(
        decimal orderedQuantity,
        decimal receivedQuantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderedQuantity);

        if (receivedQuantity < 0 || receivedQuantity > orderedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity));
        }

        if (receivedQuantity == 0)
        {
            return PurchaseOrderStatus.Ordered;
        }

        return receivedQuantity == orderedQuantity
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }
}

public static class ShipmentStatusPolicy
{
    public static bool IsTerminal(ShipmentStatus status) =>
        status is ShipmentStatus.Received or ShipmentStatus.Cancelled;

    public static bool CanTransition(ShipmentStatus current, ShipmentStatus target)
    {
        if (current == target || IsTerminal(current))
        {
            return false;
        }

        return current switch
        {
            ShipmentStatus.Preparing => target is ShipmentStatus.Shipped
                or ShipmentStatus.Cancelled,
            ShipmentStatus.Shipped => target is ShipmentStatus.InTransit
                or ShipmentStatus.Received,
            ShipmentStatus.InTransit => target is ShipmentStatus.Received,
            _ => false,
        };
    }
}
