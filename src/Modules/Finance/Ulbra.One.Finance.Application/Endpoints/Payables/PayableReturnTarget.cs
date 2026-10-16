namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>Role a requisition-sourced payable is returned to when Finance rejects/returns it (AEL-935).</summary>
public enum PayableReturnTarget { Requester, Controllership }
