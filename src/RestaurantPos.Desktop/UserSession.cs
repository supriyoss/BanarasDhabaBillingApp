using RestaurantPos.Domain;

namespace RestaurantPos.Desktop;

public sealed class UserSession
{
    public AppUser? CurrentUser { get; private set; }
    public void SignIn(AppUser user) => CurrentUser = user;
    public void SignOut() => CurrentUser = null;
}
