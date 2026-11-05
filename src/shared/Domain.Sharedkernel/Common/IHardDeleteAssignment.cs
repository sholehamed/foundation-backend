namespace Domain.SharedKernel.Common;

/// <summary>
/// Marks a permission/role assignment link whose revocation must physically
/// remove the association so it stops granting access immediately. The
/// transaction-bound authorization audit ledger retains the historical event.
/// Normal business entities continue using ordinary soft deletion.
/// </summary>
public interface IHardDeleteAssignment { }
