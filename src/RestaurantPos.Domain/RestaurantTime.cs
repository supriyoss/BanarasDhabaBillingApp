namespace RestaurantPos.Domain;

public static class RestaurantTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();
    public static DateTime ToLocal(DateTime value)
    {
        var utc = value.Kind switch { DateTimeKind.Utc => value, DateTimeKind.Local => value.ToUniversalTime(), _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }
    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" }) try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Kolkata", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
    }
}
