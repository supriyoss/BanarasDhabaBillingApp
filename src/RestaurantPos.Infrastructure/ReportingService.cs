using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class ReportingService(RestaurantDbContext db) : IReportingService
{
    public async Task<SalesReport> GetSalesAsync(DateTime businessDate, SalesReportPeriod period, CancellationToken cancellationToken = default)
    {
        var periodStart = period switch
        {
            SalesReportPeriod.Monthly => new DateTime(businessDate.Year, businessDate.Month, 1),
            SalesReportPeriod.Yearly => new DateTime(businessDate.Year, 1, 1),
            _ => businessDate.Date
        };
        var periodEnd = period switch
        {
            SalesReportPeriod.Monthly => periodStart.AddMonths(1),
            SalesReportPeriod.Yearly => periodStart.AddYears(1),
            _ => periodStart.AddDays(1)
        };
        var start = DateTime.SpecifyKind(periodStart, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(periodEnd, DateTimeKind.Local).ToUniversalTime();
        var orders = await db.Orders.Include(x => x.Payments).Where(x => x.Status == OrderStatus.Paid && x.ClosedUtc >= start && x.ClosedUtc < end).ToListAsync(cancellationToken);
        var payments = orders.SelectMany(x => x.Payments).GroupBy(x => x.Method).OrderBy(x => x.Key).Select(x => new PaymentBreakdown(x.Key, x.Sum(p => p.Amount))).ToList();
        var audit = await db.AuditEntries.Where(x => x.OccurredUtc >= start && x.OccurredUtc < end).OrderByDescending(x => x.OccurredUtc).Take(100).ToListAsync(cancellationToken);
        var activity = audit.Select(x => new ReportActivity(x.OccurredUtc.ToLocalTime(), x.Action, x.EntityId, x.Detail)).ToList();
        return new SalesReport(period, periodStart, periodEnd, orders.Count, orders.Sum(x => x.GrandTotal), orders.Sum(x => x.TaxAmount), payments, activity);
    }
}
