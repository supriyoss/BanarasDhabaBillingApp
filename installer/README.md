# Restaurant POS installer

The release process publishes a self-contained 64-bit Windows executable and wraps it in a standard `.msi` installer.

The installer deliberately contains only the application. The database, logs, and backups remain on the restaurant machine under `%LocalAppData%\RestaurantPos`, so application upgrades do not overwrite sales data.
