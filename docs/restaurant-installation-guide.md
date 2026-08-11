# Restaurant POS — local installation and handover guide

This POS is designed for one restaurant computer and works without an internet connection. The application, its SQLite database, and its backups remain on that computer.

## Before installation

- Use a reliable Windows 10 or Windows 11 computer with a current Windows account dedicated to the restaurant. Do not use a personal staff account.
- Connect and test the thermal printer in Windows before installing the POS.
- Keep a USB drive or another secure local storage device available for periodic off-machine backups.
- Ensure the computer has a password and is protected by a UPS if power cuts are common.

## Install the application

For the current development build, install the .NET 8 Desktop Runtime, then copy the published POS folder to a stable location such as `C:\RestaurantPOS`.

To publish the release from the development machine:

```powershell
dotnet publish src/RestaurantPos.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\RestaurantPOS
```

Copy the resulting `publish\RestaurantPOS` folder to the restaurant computer, then open `RestaurantPos.Desktop.exe`. A self-contained publish includes the required .NET runtime, so the target computer does not need a separate .NET installation.

Create a desktop shortcut to that executable. Configure the Windows thermal printer as the default printer if it will be the usual receipt printer.

## First launch

1. Open the application. It creates the local database automatically.
2. Sign in as **Administrator** using the temporary PIN **1234**.
3. The temporary PIN must be replaced before staff use. The current development build does not yet include the staff-management screen, so the installer/developer must complete that step before production handover.
4. Create one sample order, complete it, and confirm that the invoice appears in the Windows print dialog.
5. Use **Back up now** and confirm that a database copy was created.

## Where data is stored

For the current build, data is kept under the Windows account that runs the app:

```text
%LocalAppData%\RestaurantPos\restaurant.db
%LocalAppData%\RestaurantPos\Backups\
```

Use the same dedicated Windows account every day. If the app is run from a different Windows account, that account gets a different local data folder.

The SQLite database contains structured business records only: menu data, staff, orders, payments, tables, reports, and the audit trail. It does not store receipt PDFs, images, or other large binary files.

## Backup routine

The POS creates a backup when it starts and every 12 hours while it remains open. It keeps the newest 14 local copies.

At least once a week:

1. Click **Back up now**.
2. Copy the newest `restaurant-*.db` file from the Backups folder to the USB drive.
3. Label the USB backup with the restaurant name and date.
4. Store the drive away from the main computer.

Do not copy `restaurant.db` while the POS is running. Use the generated backup file instead.

## Restoring after a computer failure

1. Install the same POS release on the replacement computer.
2. Open it once, then close it. This creates its data folder.
3. Copy the chosen backup file into `%LocalAppData%\RestaurantPos\` and rename it to `restaurant.db`, replacing the empty database.
4. Start the POS and sign in. Confirm the latest paid bills are present before processing new orders.

Keep the failed database and its backup copies unchanged until restoration is confirmed.

## Operational rules

- Do not delete `restaurant.db` to free space. Sales history should remain available for reporting and tax needs.
- Do not store receipt images or PDFs inside the database.
- Do not move the installed program folder or database while the POS is running.
- Use Windows updates only outside service hours, and take a backup beforehand.
- Test printer paper, printer connection, and a sample print before each service day.

## Future optional off-site backup

Azure Blob Storage can later receive encrypted copies of the generated backup files. It should remain optional and must never be required for normal billing or checkout; the local SQLite database stays the source of truth.
