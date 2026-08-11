# V1 architecture decisions

The application is a single Windows desktop process, split into small layers for testability rather than deployment. It uses EF Core with one SQLite database on the restaurant's main machine. There are no network calls in the V1 critical path.

`Order` preserves snapshots of item name, price, and GST rate in each `OrderLine`, so later menu changes cannot alter a historical bill. Totals are persisted for reporting and can be recalculated using the application calculator during checkout. Every meaningful action will add an `AuditEntry`; the schema is ready before the workflows are added.

`IReceiptPrinter` separates checkout from hardware-specific ESC/POS or Windows-print-queue work. `IBackupService` uses SQLite's online backup API and keeps 14 rotating local backup copies. Application logs should be written separately under `%LocalAppData%\\RestaurantPos\\Logs` with a 30-day retention job; they are deliberately not retained in SQLite.

Production hardening before use: replace the seeded placeholder administrator PIN with a salted password hash, add a staff setup screen, configure restaurant GST/invoice settings, and choose/install the thermal-printer adapter.
