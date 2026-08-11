# Restaurant POS

Windows-first, offline-first restaurant billing application. The desktop app is a WPF client backed by a local SQLite database. It deliberately has no cloud dependency in V1.

## Solution layout

- `src/RestaurantPos.Domain` — business entities and shared enums.
- `src/RestaurantPos.Application` — use-case contracts and POS calculations.
- `src/RestaurantPos.Infrastructure` — EF Core SQLite persistence, printing and local backup services.
- `src/RestaurantPos.Desktop` — WPF staff application and composition root.

## Run locally

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on the restaurant workstation or development machine.
2. From this folder, run `dotnet restore` and then `dotnet run --project src/RestaurantPos.Desktop`.

On first launch, the app creates its data folder under `%LocalAppData%\\RestaurantPos`, applies the initial SQLite schema, and adds a small demo menu. Production setup should replace the demo users and menu with a setup workflow before staff use.

## Data policy

The SQLite database stores structured sales history only. Invoices are rendered and printed on demand; PDFs, receipt images, and other binary blobs are not stored in the database. Backups are local rotating copies, and logs have an independent short retention period.
