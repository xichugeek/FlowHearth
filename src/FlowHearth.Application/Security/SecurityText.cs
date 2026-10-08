using System.Net.Mail;
using System.Text.RegularExpressions;
using FlowHearth.Application.Common;

namespace FlowHearth.Application.Security;

internal static partial class SecurityText
{
    public static string NormalizeUsername(string username)
    {
        var trimmed = RequireText(username, "username", "用户名", 64);
        if (!UsernamePattern().IsMatch(trimmed))
        {
            throw FlowHearthValidationException.For(
                "username",
                "用户名须为 3 至 64 位字母、数字、点、下划线或连字符。");
        }

        return trimmed.ToUpperInvariant();
    }

    public static string NormalizeRoleCode(string code)
    {
        var trimmed = RequireText(code, "code", "角色代码", 64).ToLowerInvariant();
        if (!RoleCodePattern().IsMatch(trimmed))
        {
            throw FlowHearthValidationException.For(
                "code",
                "角色代码须为 2 至 64 位小写字母、数字或连字符。");
        }

        return trimmed;
    }

    public static string RequireText(
        string value,
        string field,
        string label,
        int maximumLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw FlowHearthValidationException.For(field, $"{label}不能为空。");
        }

        if (trimmed.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
        }

        return trimmed;
    }

    public static (string? Email, string? NormalizedEmail) NormalizeEmail(
        string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, null);
        }

        if (trimmed.Length > 254
            || !MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowHearthValidationException.For("email", "邮箱地址格式无效。");
        }

        return (trimmed, trimmed.ToUpperInvariant());
    }

    public static string? OptionalText(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
        }

        return trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleCodePattern();
}
