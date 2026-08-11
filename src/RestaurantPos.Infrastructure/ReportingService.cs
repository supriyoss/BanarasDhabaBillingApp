using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class ReportingService(RestaurantDbContext db) : IReportingService
{
    public async Task<DailySalesReport> GetDailySalesAsync(DateTime businessDate, CancellationToken cancellationToken = default)
    {
        var localStart = DateTime.SpecifyKind(businessDate.Date, DateTimeKind.Local);
        var start = localStart.ToUniversalTime();
        var end = localStart.AddDays(1).ToUniversalTime();
        var orders = await db.Orders.Include(x => x.Payments).Where(x => x.Status == OrderStatus.Paid && x.ClosedUtc >= start && x.ClosedUtc < end).ToListAsync(cancellationToken);
        var payments = orders.SelectMany(x => x.Payments).GroupBy(x => x.Method).OrderBy(x => x.Key).Select(x => new PaymentBreakdown(x.Key, x.Sum(p => p.Amount))).ToList();
        var audit = await db.AuditEntries.Where(x => x.OccurredUtc >= start && x.OccurredUtc < end).OrderByDescending(x => x.OccurredUtc).Take(100).ToListAsync(cancellationToken);
        return new DailySalesReport(businessDate.Date, orders.Count, orders.Sum(x => x.GrandTotal), orders.Sum(x => x.TaxAmount), payments, audit);
    }
}
