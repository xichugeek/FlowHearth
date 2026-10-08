# Local development scripts

These Windows scripts manage the user-local MySQL development instance used on
the primary development machine. They never connect to production.

- `Initialize-LocalMySql.ps1` is a one-time bootstrap for a fresh, already
  initialized MySQL 8.4 ZIP installation. It refuses to run when credentials
  already exist or port 3306 is in use. It creates separate runtime and
  migrator accounts, stores their connection strings in .NET user secrets and
  removes its one-time SQL file.
- `Start-LocalMySql.ps1` starts the configured server hidden and verifies that
  it owns the loopback listener.
- `Stop-LocalMySql.ps1` performs an authenticated, clean shutdown.
- `my.ini.example` documents the loopback-only local configuration. Copy it to
  a location outside the repository and replace only the path placeholders.

The one-time initializer must never be pointed at an existing or production
database server.
