using Ulbra.One.Registry.Domain.ValueObjects;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Registry.Domain.Entities;

/// <summary>Vendor — individual or legal entity. Document normalized (CPF digits-only, CNPJ alphanumeric).</summary>
public class VendorEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public PersonType PersonType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TradeName { get; private set; }
    public string Document { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public string? PaymentTerms { get; private set; }
    public BankInfo BankInfo { get; private set; } = BankInfo.Empty;
    public PaymentSchedule PaymentSchedule { get; private set; } = PaymentSchedule.Default;
    // Payment methods the vendor accepts. Empty = no restriction.
    public IReadOnlyList<PaymentMethod> AcceptedPaymentMethods { get; private set; } = [];
    public TaxRegime TaxRegime { get; private set; } = TaxRegime.Unknown;
    // Indicativos para a retenção de INSS — sugestão do cadastro, confirmada contra a NF no lançamento.
    // CprbForeseen: CPRB (folha desonerada) previsto? Sim(true)/Não(false)/Desconhecido(null) → sugere 3,5% vs 11%.
    public bool? CprbForeseen { get; private set; }
    // Anexo do Simples (só relevante quando TaxRegime=SimplesNacional): Anexo IV retém 11%, demais isento.
    public SimplesAnexo? SimplesAnexo { get; private set; }
    // Primary CNAE (main economic activity) from the CNPJ lookup — code + description, stored loosely
    // (not validated against an official table here). Reference for classification / AEL-634.
    public string? PrimaryCnaeCode { get; private set; }
    public string? PrimaryCnaeDescription { get; private set; }
    // Vendor-specific withholding rates (per tax). RATE override only: the system rules decide whether/which tax
    // is withheld; when there is an entry here for a withheld tax, this rate takes precedence.
    public IReadOnlyList<VendorWithholdingRate> WithholdingRates { get; private set; } = [];
    // Default expense ledger accounts for this vendor — loose Guid references (no cross-module FK) to the
    // Accounting LedgerAccount. Surfaced as suggestions when classifying a Payable. Empty = none.
    public IReadOnlyList<Guid> AssociatedLedgerAccountIds { get; private set; } = [];
    public bool Active { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    private VendorEntity() { }

    public VendorEntity(PersonType personType, string name, string? tradeName,
        string document, string? email, string? phone, Address address, string? paymentTerms,
        BankInfo bankInfo, TaxRegime taxRegime, PaymentSchedule paymentSchedule,
        bool? cprbForeseen = null, SimplesAnexo? simplesAnexo = null,
        IReadOnlyList<PaymentMethod>? acceptedPaymentMethods = null,
        IReadOnlyList<VendorWithholdingRate>? withholdingRates = null,
        IReadOnlyList<Guid>? associatedLedgerAccountIds = null,
        string? primaryCnaeCode = null, string? primaryCnaeDescription = null)
    {
        Apply(personType, name, tradeName, document, email, phone, address, paymentTerms, bankInfo, taxRegime,
            paymentSchedule, cprbForeseen, simplesAnexo, acceptedPaymentMethods, withholdingRates,
            associatedLedgerAccountIds, primaryCnaeCode, primaryCnaeDescription);
    }

    public void Update(PersonType personType, string name, string? tradeName, string document,
        string? email, string? phone, Address address, string? paymentTerms,
        BankInfo bankInfo, TaxRegime taxRegime, PaymentSchedule paymentSchedule,
        bool? cprbForeseen = null, SimplesAnexo? simplesAnexo = null,
        IReadOnlyList<PaymentMethod>? acceptedPaymentMethods = null,
        IReadOnlyList<VendorWithholdingRate>? withholdingRates = null,
        IReadOnlyList<Guid>? associatedLedgerAccountIds = null,
        string? primaryCnaeCode = null, string? primaryCnaeDescription = null) =>
        Apply(personType, name, tradeName, document, email, phone, address, paymentTerms, bankInfo, taxRegime,
            paymentSchedule, cprbForeseen, simplesAnexo, acceptedPaymentMethods, withholdingRates,
            associatedLedgerAccountIds, primaryCnaeCode, primaryCnaeDescription);

    private void Apply(PersonType personType, string name, string? tradeName, string document,
        string? email, string? phone, Address address, string? paymentTerms,
        BankInfo bankInfo, TaxRegime taxRegime, PaymentSchedule paymentSchedule,
        bool? cprbForeseen, SimplesAnexo? simplesAnexo,
        IReadOnlyList<PaymentMethod>? acceptedPaymentMethods,
        IReadOnlyList<VendorWithholdingRate>? withholdingRates,
        IReadOnlyList<Guid>? associatedLedgerAccountIds,
        string? primaryCnaeCode, string? primaryCnaeDescription)
    {
        PersonType = personType;
        Name = name;
        TradeName = tradeName;
        Document = personType == PersonType.Company
            ? DocumentValidator.NormalizeCnpj(document)
            : DocumentValidator.Normalize(document);
        Email = email;
        Phone = phone;
        Address = address;
        PaymentTerms = paymentTerms;
        BankInfo = bankInfo with { PixKey = PixKeyNormalizer.Normalize(bankInfo.PixKey) };
        PaymentSchedule = paymentSchedule;
        TaxRegime = taxRegime;
        CprbForeseen = cprbForeseen;
        SimplesAnexo = simplesAnexo;
        AcceptedPaymentMethods = acceptedPaymentMethods ?? [];
        WithholdingRates = withholdingRates ?? [];
        AssociatedLedgerAccountIds = associatedLedgerAccountIds ?? [];
        PrimaryCnaeCode = primaryCnaeCode;
        PrimaryCnaeDescription = primaryCnaeDescription;
    }

    public void Deactivate() => Active = false;
    public void Activate() => Active = true;

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
