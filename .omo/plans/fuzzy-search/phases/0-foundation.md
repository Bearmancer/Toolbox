# Phase 0: Foundation — Static Shell + Fuse.js

## Tasks

### Task 1: Create web/index.html skeleton

**What to do:**
Create `web/index.html` with a minimal HTML5 boilerplate. Include:
- `<meta name="viewport">` for mobile
- A search `<input>` with id `search-box`
- A `<div id="playlist-tabs">` for playlist tab buttons
- A `<div id="results">` for search output
- A `<script>` tag loading Fuse.js from CDN: `https://cdn.jsdelivr.net/npm/fuse.js@7.0.0`
- An inline `<script>` with a `DOMContentLoaded` listener that logs "app loaded" to confirm wiring

**Must NOT:**
- Add any CSS framework (Tailwind, Bootstrap, etc.)
- Implement search logic yet
- Load any external data files

**References:**
- Fuse.js CDN: `https://cdn.jsdelivr.net/npm/fuse.js@7.0.0`

**Acceptance criteria:**
- `web/index.html` exists and is valid HTML
- Opening the file in a browser shows the search box and empty results div
- Browser console shows "app loaded"

**QA:**
```bash
# Verify file exists and has expected structure
grep -c "search-box" web/index.html
grep -c "playlist-tabs" web/index.html
grep -c "fuse.js" web/index.html
```
Expected: All return 1

**Commit:** `feat(fuzzy-search): add HTML skeleton with Fuse.js CDN`

---

### Task 2: Add basic CSS layout

**What to do:**
Add a `<style>` block inside `web/index.html` with:
- Box-sizing reset (`* { box-sizing: border-box; }`)
- Search box: full width, 48px height, 16px font, padding 0 16px
- Playlist tabs: horizontal flex row, gap 8px, padding 8px 0
- Tab button: padding 8px 16px, border-radius 4px, cursor pointer
- Active tab: bold, background `#e8e8e8`
- Results div: padding 16px

**Must NOT:**
- Add media queries (Phase 2 handles responsive)
- Use external CSS files
- Add animations or transitions

**References:**
- None

**Acceptance criteria:**
- Search box is full width and visually distinct
- Tab buttons appear in a horizontal row
- No layout overflow on a 1280px wide viewport

**QA:**
```bash
grep -c "box-sizing" web/index.html
grep -c "flex" web/index.html
```
Expected: Both return at least 1

**Commit:** `feat(fuzzy-search): add base CSS layout`

---

### Task 3: Implement Fuse.js search with hardcoded data

**What to do:**
Add a `<script>` block with:
1. A hardcoded array `MOCK_DATA` containing 3 mock playlist objects:
   - Each has `name` (string) and `videos` (array of objects with `title`, `channelName`, `description`)
   - Example playlist names: "Classical Essentials", "Jazz Nights", "Rock Classics"
   - Each playlist has 2-3 mock videos
2. A function `renderTabs(playlists)` that creates a button for each playlist inside `#playlist-tabs`
3. A function `searchVideos(query, playlists)` that:
   - Creates a Fuse instance with keys `["videos.title", "videos.channelName", "videos.description"]`
   - Searches across all playlists
   - Returns results grouped by playlist
4. A function `renderResults(results)` that renders results into `#results` as a simple list
5. An `input` event listener on `#search-box` that calls `searchVideos` and `renderResults`
6. On page load, call `renderTabs(MOCK_DATA)`

**Must NOT:**
- Load any JSON files from disk
- Implement "Search All" button (Phase 1)
- Add sorting functionality
- Handle empty query gracefully (show nothing is fine)

**References:**
- Fuse.js options: `https://www.fusejs.io/api/options.html`

**Acceptance criteria:**
- Typing "classical" in the search box shows results from the "Classical Essentials" playlist
- Typing "jazz" shows results from "Jazz Nights"
- Typing "xyz" shows no results
- Tabs are visible but non-functional (static render)

**QA:**
```bash
# Verify Fuse.js is loaded and search function exists
grep -c "new Fuse" web/index.html
grep -c "MOCK_DATA" web/index.html
grep -c "renderTabs" web/index.html
```
Expected: All return at least 1

**Commit:** `feat(fuzzy-search): implement Fuse.js search with mock data`

---

## Verify Phase 0

```bash
# File exists and has all required components
grep -c "search-box" web/index.html
grep -c "playlist-tabs" web/index.html
grep -c "fuse.js" web/index.html
grep -c "new Fuse" web/index.html
grep -c "MOCK_DATA" web/index.html
```
All return at least 1. Open `web/index.html` in browser, type in search box, see filtered results.

**Dependencies:** None
**Blocks:** Phase 1
