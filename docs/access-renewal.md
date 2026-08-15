# Monthly access renewal

Banaras Dhaba POS starts with 30 days of access on the Windows account used for the restaurant. The login window shows the installation ID, expiry date, and remaining days. Expired access blocks operational sign-in but does not delete or modify the restaurant database, backups, or reports.

## Issue a renewal code

The private key is intentionally excluded from Git and from every MSI. Back up this file securely and never send it to the client:

```text
.license-keys\BanarasDhabaPOS-LicensePrivateKey.pem
```

Ask the client for the installation ID displayed on the login screen. From the repository root, generate a signed code:

```powershell
.\tools\New-RenewalCode.ps1 -InstallationId "CLIENT-INSTALLATION-ID" -Days 30
```

Send only the generated `BD1...` renewal code to the client. The client pastes it into the renewal field and selects **Apply renewal code**. Codes are signed for one installation and cannot be reused on a different installation.

Use a different `-Days` value when a longer paid period is required. A code must extend the current access-until date; applying an older or shorter code will be rejected.

## Operational notes

- The encrypted access state is stored at `%LocalAppData%\RestaurantPos\access.license`, backed up under the current user's `Software\BanarasDhabaPOS` registry key, and protected for that Windows user.
- Keep using the same dedicated Windows account for the POS.
- Changing the computer clock backwards locks access until the clock is corrected and a valid access period remains.
- Reinstalling the application preserves the access history. Selecting **Remove data** during uninstall removes the database and file copy, but the registry backup prevents a fresh trial from being created by reinstalling.
- This is offline license enforcement. It is intended to prevent casual unauthorized continuation, not to replace a server-backed subscription platform.
