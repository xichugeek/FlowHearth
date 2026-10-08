using System.Data;
using Dapper;

namespace FlowHearth.Infrastructure.Database;

internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) =>
        value switch
        {
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            string text => DateOnly.Parse(
                text,
                System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException(
                $"Cannot convert {value.GetType().Name} to DateOnly."),
        };
}
