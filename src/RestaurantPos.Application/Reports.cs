using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public sealed record PaymentBreakdown(PaymentMethod Method, decimal Amount);
public sealed record DailySalesReport(DateTime BusinessDate, int PaidOrderCount, decimal SalesTotal, decimal TaxTotal, IReadOnlyList<PaymentBreakdown> Payments, IReadOnlyList<AuditEntry> AuditEntries);

public interface IReportingService
{
    Task<DailySalesReport> GetDailySalesAsync(DateTime businessDate, CancellationToken cancellationToken = default);
}
