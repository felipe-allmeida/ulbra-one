using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Validation;

internal static class BankInfoMapping
{
    public static BankInfo ToBankInfo(this BankInfoRequest? req) => req is null
        ? BankInfo.Empty
        : new BankInfo(req.BankCode ?? "", req.Branch ?? "", req.Account ?? "",
            req.AccountDigit ?? "", req.PixKey ?? "");
}
