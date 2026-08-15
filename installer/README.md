# Restaurant POS installer

The release process publishes a self-contained 64-bit Windows executable and wraps it in a standard `.msi` installer.

The installer deliberately contains only the application. The setup wizard lets the user choose the application installation directory.

The database, logs, and backups remain under `%LocalAppData%\RestaurantPos`, so application upgrades do not overwrite sales data. During an interactive uninstall, the user is asked whether to keep or permanently remove that local data. Unattended and upgrade uninstalls preserve it by default.
