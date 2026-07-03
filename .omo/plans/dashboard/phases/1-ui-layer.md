# Phase 1: UI Layer — View 3 + Column Toggles

## Task 2: Add View 3 (all videos) HTML structure

**What to do:**
Add new div `all-videos-view` with search box and table container. Update tab switching logic to handle YouTube sub-views.

**Must NOT:**
- Modify existing views
- Modify existing search functionality

**References:**
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:135-162` (YouTube tab HTML)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:268-275` (tab switching logic)

**Acceptance criteria:**
- HTML contains `<div id="all-videos-view">` with search box and table container

**QA:**
```bash
grep -n "all-videos-view" src/CLI/Dashboard/DashboardHtmlGenerator.cs
```
Expected: Finds the new div

**Commit:** N/A (combined with Task 5)

---

## Task 3: Add Tabulator table initialization for View 3

**What to do:**
Add new Tabulator instance `allVideoTable` with columns: Title, Channel, Duration, Playlist. Add search filter for View 3.

**Must NOT:**
- Modify existing table instances

**References:**
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:188-210` (playlistTable initialization)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:212-221` (videoTable initialization)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:223-233` (scrobbleTable initialization)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:253-258` (video search filter)

**Acceptance criteria:**
- HTML contains `var allVideoTable = new Tabulator('#all-videos-table', {...})` with correct columns

**QA:**
```bash
grep -n "allVideoTable" src/CLI/Dashboard/DashboardHtmlGenerator.cs
```
Expected: Finds table initialization

**Commit:** N/A (combined with Task 5)

---

## Task 4: Add column toggle dropdown to all tables

**What to do:**
Add dropdown HTML element for each table with column checkboxes. Add JavaScript to toggle column visibility.

**Must NOT:**
- Change table data
- Change search functionality

**References:**
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:54-122` (CSS styles)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:188-233` (table initializations)

**Acceptance criteria:**
- HTML contains `<div class="column-toggle">` with checkboxes for each table column

**QA:**
```bash
grep -n "column-toggle" src/CLI/Dashboard/DashboardHtmlGenerator.cs
```
Expected: Finds dropdown HTML

**Commit:** N/A (combined with Task 5)

---

## Task 5: Update YouTube sub-view switching logic and verify build

**What to do:**
Update tab switching logic to handle YouTube sub-views (playlist list ↔ playlist detail ↔ all videos). Ensure back button works for all transitions. Verify build succeeds and HTML is generated correctly.

**Must NOT:**
- Break existing functionality

**References:**
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:268-275` (tab switching logic)
- `src/CLI/Dashboard/DashboardHtmlGenerator.cs:238-245` (back button handler)

**Acceptance criteria:**
- `dotnet build` succeeds
- `dotnet run --project src/App -- dashboard generate` produces HTML with all 3 YouTube views and column toggles

**QA:**
```bash
dotnet build
dotnet run --project src/App -- dashboard generate
```
Expected: Exit 0, HTML file > 1MB with correct structure

**Commit:** `feat(dashboard): add View 3 and column toggle dropdowns`

---

## Final Verification

```bash
dotnet build
dotnet run --project src/App -- dashboard generate
```

**Success criteria:**
1. `dotnet build` succeeds with exit 0
2. HTML file > 1MB
3. HTML contains 3 YouTube views: playlist list, playlist detail, all videos
4. HTML contains column toggle dropdown for each table
5. Search functionality works for all views
6. Tab switching works correctly between YouTube/Last.fm and YouTube sub-views
7. Back button works from playlist detail and all videos views

**Dependencies:** Phase 0
**Blocks:** None (final phase)
