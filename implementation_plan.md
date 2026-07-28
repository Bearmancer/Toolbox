# Infrastructure Overhaul - Implementation Plan

## Overview

Restore and verify infrastructure state based on hand-off document. Create Servy skill documentation, fix GDrive sync bootstrap script, set up Google Drive folder with symlinks, install Google-Drive-Sync Servy service, and verify all system configurations.

## Current State Summary

| Component | Status | Evidence |
|-----------|--------|----------|
| JetBrains 2025.3.3 dead entries | ✓ REMOVED | `Test-Path` returns False |
| Task Scheduler: Toolbox-Daily-Sync (09:00, S4U) | ✓ READY | `Get-ScheduledTask` |
| Task Scheduler: AutoUpdateAll (05:00, S4U) | ✓ READY | `Get-ScheduledTask` |
| Legacy tasks purged | ✓ DONE | Not found |
| Rclone v1.74.4 | ✓ INSTALLED | WinGet |
| Rclone `personal:` remote (5 TiB total, 933 GiB used) | ✓ CONFIGURED | `rclone about personal:` |
| OpenCode permissions | ✓ CONFIGURED | `opencode.json` verified |
| Servy: OpenCodeWeb | ✓ RUNNING | Admin `servy-cli export` |
| Servy: Toolbox-Daily-Sync | ✓ REMOVED | Not found in Servy |
| Servy: AutoUpdateAll | ✓ REMOVED | Not found in Servy |
| Servy: Google-Drive-Sync | ✓ INSTALLED | `sc.exe qc` confirmed, Auto start |
| Bootstrap script | ✓ FIXED | Dynamic paths, --resync only, no comments |
| Servy skill | ✓ CREATED | Researched from 5+ sources |
| Build | ✓ 0 errors, 0 warnings | Terminal verified |
| Google Drive folder | ✓ CREATED | `C:\Users\Lance\Google Drive\Music\` with junctions |
| foobar2000 shell | ✓ WORKS | User confirmed |

## Google Drive Sync State

### Remote Structure (personal:)
```
personal:/
├── Android/
├── Computer/
├── Documents/
├── Education/
├── Essays/
├── Family/
├── Games/
├── Medical/
├── Music/
│   ├── Classical/    ← 3,467 files, 87.5 GiB
│   ├── Elvis/
│   ├── Elvis Presley - 1972 - As Recorded at Madison Square Garden (2023 CD)/
│   └── Songs/        ← 14,899 files, 219.5 GiB
├── Personal/
├── Pictures/
├── Sir Fapsalot/
├── Spoken Word/
│   ├── Interviews/
│   ├── Miscellaneous/
│   ├── Rehearsals/
│   └── Talks/
└── Spreadsheets/
```

### Local Structure (G:\ drive)
```
G:\
├── Classical/    ← 3,973 files, 100.3 GB
└── Songs/        ← 30,133 files, 411.66 GB
```

### Symlink Structure (~/Google Drive)
```
C:\Users\Lance\Google Drive\
└── Music\
    ├── Classical → G:\Classical (junction)
    └── Songs → G:\Songs (junction)
```

### Sync Delta
| Directory | Remote | Local | Delta |
|-----------|--------|-------|-------|
| Classical | 3,467 files, 87.5 GiB | 3,973 files, 100.3 GB | ~506 files, ~12.8 GB to upload |
| Songs | 14,899 files, 219.5 GiB | 30,133 files, 411.66 GB | ~15,234 files, ~192 GB to upload |

### Sync Status
- **Google-Drive-Sync service**: Installed (Auto start), currently STOPPED
- **rclone bisync --resync**: Running for Classical (timed out at 30s, but sync is actively happening)
- **Next**: Run full --resync for Songs after Classical completes

---

## Research Deep-Dive

### 1. Windows Task Scheduler S4U Logon (Microsoft Docs)

Source: learn.microsoft.com/en-us/windows/win32/taskschd/

**S4U = Service for User** (TASK_LOGON_S4U = 2):
- No password stored by the system
- No access to network resources or encrypted files (local-only token)
- Token obtained via Kerberos S4U2Self protocol
- Requires SeTcbPrivilege (Task Scheduler runs as SYSTEM)
- Requires "Logon as Batch Job" privilege
- Runs in non-interactive desktop (Session 0)
- When RunLevel=Highest, creates fully privileged token (bypasses UAC split-token)

### 2. Rclone Bisync Internal Mechanics (Source Code)

Source: github.com/rclone/rclone/cmd/bisync/

**Lock File System (lockfile.go)**:
- Location: `%LOCALAPPDATA%\rclone\bisync\PATH1..PATH2.lck`
- Format: JSON with PID, Session, TimeCreated, TimeExpires
- `--max-lock 2m` auto-expires stale locks
- Unreadable lock file: with `--max-lock` set → treats as expired (fix commit c490155, 2026-03-31)

**Listing Mechanism (operations.go, listing.go)**:
- `.lst` = current snapshot, `.lst-new` = building, `.lst-old` = backup for --recover, `.lst-err` = critical error marker
- `--resilient` enables retries (default 3), one direction at a time
- `--recover` reverts to `.lst-old` backups on listing failure

**--resync vs Normal Mode**:
- `--resync` is a separate code path (runs `rclone copy` both directions)
- `--resilient`/`--recover` have NO effect during `--resync`
- `--resync` implies `--resync-mode path1` by default
- `--conflict-resolve` only applies during normal (non-resync) runs

### 3. Google Drive Rate Limiting

| Limit | Value |
|-------|-------|
| Queries per 100s | 20,000 per user |
| Write requests/sec | ~3 per account |
| Daily upload cap | 750 GB |
| Single file throughput | ~40 MB/s |
| Default pacer | 100ms min sleep, 100 burst |

### 4. OpenCode Permission System

Source: github.com/sst/opencode/docs/permissions

| Key | Gated Tools |
|-----|-------------|
| `read` | `read` |
| `edit` | `write`, `edit`, `apply_patch` |
| `bash` | `bash` |
| `external_directory` | Any tool touching paths outside worktree |

### 5. Servy v8.7.0 CLI

Source: github.com/aelassas/servy/wiki

- Install syntax: `--name=` `--path=` `--params=` `--startupDir=` `--startupType=`
- `--params=` with `--` flags MUST use equals sign
- `--stopTimeout=` default 5s (rclone needs 60s)
- Services run in Session 0

---

## Tasks

### TASK 1: Create Servy Skill Documentation
**Status**: ✓ COMPLETE
**Location**: `C:\Users\Lance\.config\opencode\skills\servy\SKILL.md`

### TASK 2: Fix Bootstrap Script
**Status**: ✓ COMPLETE
**Location**: `C:\Users\Lance\Documents\PowerShell\Scripts\Bootstrap-GDriveBisync.ps1`

### TASK 3: Verify Servy Services
**Status**: ✓ VERIFIED

### TASK 4: Build Verification
**Status**: ✓ COMPLETE - 0 errors, 0 warnings

### TASK 5: Google Drive Folder Setup
**Status**: ✓ COMPLETE
- Created `C:\Users\Lance\Google Drive\Music\`
- Junction: `Classical` → `G:\Classical`
- Junction: `Songs` → `G:\Songs`

### TASK 6: Google-Drive-Sync Service Installation
**Status**: ✓ COMPLETE
- Service installed via Servy CLI
- Binary: `C:\ProgramData\Servy\Servy.Service.CLI.exe`
- Start type: Automatic
- Current state: STOPPED (needs manual start or reboot)

### TASK 7: Initial Sync (In Progress)
**Status**: ️ RUNNING
- Classical: rclone bisync --resync started (3,973 files, 100.3 GB)
- Songs: Pending (30,133 files, 411.66 GB)

---

## Files Modified

| File | Action |
|------|--------|
| `implementation_plan.md` | Created/Updated |
| `C:\Users\Lance\.config\opencode\skills\servy\SKILL.md` | Created |
| `C:\Users\Lance\Documents\PowerShell\Scripts\Bootstrap-GDriveBisync.ps1` | Rewritten |
| `C:\Users\Lance\Google Drive\Music\Classical` | Junction → G:\Classical |
| `C:\Users\Lance\Google Drive\Music\Songs` | Junction → G:\Songs |