using FlowHearth.Application.Common;

namespace FlowHearth.Application.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    public static void Validate(string password, string field = "password")
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw FlowHearthValidationException.For(field, "密码不能为空。");
        }

        if (password.Length is < MinimumLength or > MaximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"密码长度必须为 {MinimumLength} 至 {MaximumLength} 个字符。");
        }

        if (!password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw FlowHearthValidationException.For(
                field,
                "密码必须同时包含大写字母、小写字母、数字和特殊字符。");
        }
    }
}
