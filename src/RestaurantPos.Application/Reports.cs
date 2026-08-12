using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public sealed record PaymentBreakdown(PaymentMethod Method, decimal Amount);
public enum SalesReportPeriod { Daily, Monthly, Yearly }
public sealed record ReportActivity(DateTime OccurredLocal, AuditAction Action, string EntityId, string Detail);
public sealed record SalesReport(SalesReportPeriod Period, DateTime PeriodStart, DateTime PeriodEnd, int PaidOrderCount, decimal SalesTotal, decimal TaxTotal, IReadOnlyList<PaymentBreakdown> Payments, IReadOnlyList<ReportActivity> Activity);

public interface IReportingService
{
    Task<SalesReport> GetSalesAsync(DateTime businessDate, SalesReportPeriod period, CancellationToken cancellationToken = default);
}
