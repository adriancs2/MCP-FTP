# mcp-ftp (Auto File Sync With FTP)

A lightweight **.NET Framework 4.8** MCP (Model Context Protocol) server + WinForms credential manager for recursive, zero-credential-exposure FTP/FTPS file synchronization.

## Why `mcp-ftp`?

- **Zero Password Exposure to AI**: FTP credentials are entered by the human through a native WinForms dialog (`ftp_create_profile`) and stored encrypted with **Windows DPAPI** (`ProtectedData`, CurrentUser scope). The AI only ever references a profile name.
- **Smart Auto-Negotiated FTP/FTPS**: Automatically tries **Explicit FTPS** first and falls back to **Plain FTP** (or can be pinned per profile).
- **Instant Local-Snapshot Delta Sync (`sync_snapshot.txt`)**: After an initial `MLSD` sync (or full download), `ftp_sync` detects new/modified local files directly from `profiles/{profile}/sync_snapshot.txt` in milliseconds with **zero remote FTP directory scans** (and 0 FTP connections when nothing changed). Pass `verify_remote: true` anytime to reconcile against the live server.
- **Full Workspace Backup Pull (`ftp_download_all`)**: Moving to another computer? Pull the entire source codebase from FTP in one call while automatically skipping runtime user-uploaded media/databases in `exclude_config.txt` and priming `sync_snapshot.txt`.
- **AI-Friendly Plain-Text Exclude Rules**: Each profile stores its exclusions in `profiles/{profile}/exclude_config.txt` (one path or folder per line).
- **Explicit, Safe Remote Deletion**: Remote files are never deleted automatically by accident. To delete remote files, the AI either writes relative paths to `profiles/{profile}/delete-file.txt` (which is **immediately deleted after being consumed** during `ftp_sync`) or calls `ftp_delete_remote_files`.

---

## Profile Folder Layout

```text
profiles/
  {profile_name}/
    ftp_config.json      # FTP settings + DPAPI-encrypted password (managed via WinForms UI)
    exclude_config.txt   # Plain-text exclude rules (one per line, # for comments)
    sync_snapshot.txt    # Auto-maintained local file size + UTC ticks snapshot for instant sync
    delete-file.txt      # Optional one-shot list of remote files to delete (auto-deleted after sync)
```

Example `exclude_config.txt`:
```text
App_Data/local.db
media/
user_upload/
temp/
*.log
```

---

## MCP Tools

| Tool | Description |
| :--- | :--- |
| `ftp_create_profile` | Creates a new DPAPI-encrypted FTP profile. Supports `show_ui: true` (opens WinForms dialog, optionally pre-filled) and `show_ui: false` (headless setup with optional FTP connection probe). |
| `ftp_edit_profile` | Edits an existing FTP profile via `show_ui: true` (WinForms dialog) or `show_ui: false` (headless partial update preserving the existing DPAPI password if `password` is omitted). |
| `ftp_list_profiles` | Lists configured profile names, local/remote folders, and config file paths (never exposes passwords). |
| `ftp_list_remote_files` | Recursively lists remote files with sizes and UTC timestamps (`MLSD`), skipping excluded folders unless `include_excluded` is `true`. |
| `ftp_sync` | Syncs new/modified local files to FTP using local `sync_snapshot.txt` (or `MLSD` on first run / `verify_remote: true`), skipping excluded paths and consuming `delete-file.txt`. |
| `ftp_read_remote_file` | Reads a UTF-8 text file directly from the remote FTP server into memory (supports `tail_lines` and `max_bytes` - ideal for PHP `error_log`). |
| `ftp_download` | Downloads one or more specific remote files to `local_folder` (or a custom `local_dir`), preserving relative subfolder structure. |
| `ftp_download_all` | Recursively downloads the full source workspace from FTP while skipping `exclude_config.txt` (runtime user uploads/DBs) and updating `sync_snapshot.txt`. |
| `ftp_get_excludes` | Reads `exclude_config.txt` for a profile. |
| `ftp_set_excludes` | Updates `exclude_config.txt` for a profile. |
| `ftp_delete_remote_files` | Deletes specific remote files by relative path. |

---

## Connecting it to your AI client

`mcp-ftp` is a standard **stdio** MCP server: the client starts
`mcp-ftp.exe` and talks to it over standard input and output. Point your client
at the full path of the executable.

**Claude Desktop** (including its Code tab) - add this to `claude_desktop_config.json`, then restart
Claude Desktop. The easiest way to open the file is Settings -> Developer -> Edit Config. With the
current Windows installer it is at
`%USERPROFILE%\AppData\Local\Claude-Data\claude_pzs8sxrjxfjjc\claude_desktop_config.json`
(older installs: `%APPDATA%\Claude\claude_desktop_config.json`).

```json
{
  "mcpServers": {
    "mcp-ftp": {
      "command": "C:\\tools\\mcp-ftp\\mcp-ftp.exe"
    }
  }
}
```

**Claude Code**

```bash
claude mcp add mcp-ftp -- "C:\tools\mcp-ftp\mcp-ftp.exe"
```

**Google Gemini Antigravity** - add this to `%USERPROFILE%\.gemini\config\mcp_config.json`
(create the file if it does not exist), then restart Antigravity:

```json
{
  "mcpServers": {
    "mcp-ftp": {
      "command": "C:\\tools\\mcp-ftp\\mcp-ftp.exe",
      "args": []
    }
  }
}
```

If the file already lists other servers, add the `"mcp-ftp"` entry inside the
existing `"mcpServers"` object rather than adding a second one.

**Any other MCP client** - register a stdio server whose command is the path to
`mcp-ftp.exe`. It takes no arguments and needs no environment variables.

---

## Standalone CLI / UI
```powershell
# Open the WinForms Profile Creator (blank or pre-filled)
mcp-ftp.exe --ui
mcp-ftp.exe --create-profile --show-ui true --profile myshop --host ftp.myshop.com --user admin --local "C:\www\myshop"

# Create or edit a profile headlessly via CLI (DPAPI-encrypted, probes FTP connection automatically)
mcp-ftp.exe --create-profile --show-ui false --profile myshop --host ftp.myshop.com --port 21 --security ExplicitFTPS --user admin --pass "secret" --local "C:\www\myshop" --url "https://myshop.com"
mcp-ftp.exe --edit-profile vector --show-ui false --url "https://vector.apps.kiankok.edu.my"

# Edit an existing profile in WinForms UI
mcp-ftp.exe --ui vector

# Trigger a sync directly from CLI (add --verify to scan remote MLSD, or --force to upload all)
mcp-ftp.exe --sync vector

# List remote files (add --all to include excluded directories)
mcp-ftp.exe --list-remote vector

# Read remote text file (optional tail line count)
mcp-ftp.exe --read-remote vector error_log 100

# Download specific remote files to local folder
mcp-ftp.exe --download vector index.php config/app.php

# Pull entire workspace from FTP (excluding runtime user uploads in exclude_config.txt)
mcp-ftp.exe --download-all vector
```

---

## Building from Source

Requires **.NET Framework 4.8** and MSBuild (Visual Studio 2019/2022):
```powershell
msbuild src\MCP-FTP\MCP-FTP.csproj /p:Configuration=Release
```
