# mcp-ftp (Auto File Sync With FTP)

A lightweight **.NET Framework 4.8** MCP (Model Context Protocol) server + WinForms credential manager for recursive, zero-credential-exposure FTP/FTPS file synchronization.

## Why `mcp-ftp`?

- **Zero Password Exposure to AI**: FTP credentials are entered by the human through a native WinForms dialog (`ftp_create_profile`) and stored encrypted with **Windows DPAPI** (`ProtectedData`, CurrentUser scope). The AI only ever references a profile name.
- **Smart Auto-Negotiated FTP/FTPS**: Automatically tries **Explicit FTPS** first and falls back to **Plain FTP** (or can be pinned per profile).
- **Recursive DateTime + Size Sync (`MLSD`)**: Scans remote files via `MLSD` and only uploads new or modified files, automatically creating remote subdirectories as needed.
- **AI-Friendly Plain-Text Exclude Rules**: Each profile stores its exclusions in `profiles/{profile}/exclude_config.txt` (one path or folder per line).
- **Explicit, Safe Remote Deletion**: Remote files are never deleted automatically by accident. To delete remote files, the AI either writes relative paths to `profiles/{profile}/delete-file.txt` (which is **immediately deleted after being consumed** during `ftp_sync`) or calls `ftp_delete_remote_files`.

---

## Profile Folder Layout

```text
profiles/
  {profile_name}/
    ftp_config.json      # FTP settings + DPAPI-encrypted password (managed via WinForms UI)
    exclude_config.txt   # Plain-text exclude rules (one per line, # for comments)
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
| `ftp_create_profile` | Opens an empty WinForms dialog for the user to enter FTP credentials and save a new profile securely. |
| `ftp_edit_profile` | Opens the WinForms dialog to edit an existing profile. |
| `ftp_list_profiles` | Lists configured profile names, local/remote folders, and config file paths (never exposes passwords). |
| `ftp_list_remote_files` | Recursively lists remote files with sizes and UTC timestamps (`MLSD`). |
| `ftp_sync` | Recursively syncs local files to FTP (datetime + size matching), respecting `exclude_config.txt`, and consumes `delete-file.txt` if present. |
| `ftp_get_excludes` | Reads `exclude_config.txt` for a profile. |
| `ftp_set_excludes` | Updates `exclude_config.txt` for a profile. |
| `ftp_delete_remote_files` | Deletes specific remote files by relative path. |

---

## CLI & MCP Usage

### Run as MCP Server (stdio)
```json
{
  "mcpServers": {
    "mcp-ftp": {
      "command": "D:\\aifiles\\Auto-File-Sync-With-FTP\\github\\src\\MCP-FTP\\bin\\Release\\mcp-ftp.exe"
    }
  }
}
```

### Run Standalone CLI / UI
```powershell
# Open the WinForms Profile Creator
mcp-ftp.exe --ui

# Edit an existing profile
mcp-ftp.exe --ui vector

# Trigger a sync directly from CLI
mcp-ftp.exe --sync vector

# List remote files
mcp-ftp.exe --list-remote vector
```

---

## Building from Source

Requires **.NET Framework 4.8** and MSBuild (Visual Studio 2019/2022):
```powershell
msbuild src\MCP-FTP\MCP-FTP.csproj /p:Configuration=Release
```
