using System.Text;
using System.Text.RegularExpressions;
using EduCenterOS.BuildingBlocks.Results;

namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal static partial class RegistrationInputs
{
    internal static Result<string> Phone(string? input)
    {
        if (input is null || input.Length > 32) return Result<string>.Failure(RegistrationErrors.Field("phoneNumber"));
        var phone = input.Trim();
        if (phone.StartsWith("0020", StringComparison.Ordinal)) phone = "+20" + phone[4..];
        if (phone.StartsWith("01", StringComparison.Ordinal)) phone = "+20" + phone[1..];
        return PhonePattern().IsMatch(phone) ? Result<string>.Success(phone) : Result<string>.Failure(RegistrationErrors.Field("phoneNumber"));
    }
    internal static Result<string> Name(string? input)
    {
        if (input is null || input.Length > 200 || input.Any(char.IsControl)) return Result<string>.Failure(RegistrationErrors.Field("fullName"));
        try
        {
            var name = input.Trim().Normalize(NormalizationForm.FormC);
            return name.Length is >= 2 and <= 200 ? Result<string>.Success(name) : Result<string>.Failure(RegistrationErrors.Field("fullName"));
        }
        catch (ArgumentException) { return Result<string>.Failure(RegistrationErrors.Field("fullName")); }
    }
    internal static Result<string> Email(string input)
    {
        if (input.Length > 254 || input.Any(char.IsControl)) return Result<string>.Failure(RegistrationErrors.Field("emailAddress"));
        var email = input.Trim().ToLowerInvariant();
        var at = email.IndexOf('@');
        return email.Length is >= 3 and <= 254 && at is >= 1 and <= 64 && EmailPattern().IsMatch(email)
            ? Result<string>.Success(email) : Result<string>.Failure(RegistrationErrors.Field("emailAddress"));
    }
    internal static bool Password(string? input, int minimum, int maximum) => input is not null
        && input.Length >= minimum && input.Length <= maximum && !input.Any(char.IsControl);
    [GeneratedRegex(@"^\+201[0125][0-9]{8}$", RegexOptions.CultureInvariant)] private static partial Regex PhonePattern();
    [GeneratedRegex(@"^[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
