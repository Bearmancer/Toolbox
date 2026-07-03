# Phase 1: Data Integration — YouTube + Last.fm

## Tasks

### Task 1: Load YouTube manifest dynamically

**What to do:**
Replace `MOCK_DATA` with a `fetch()` call to `state/youtube/manifest.json`. Parse the response and extract the playlist list. Each entry has a `name` and an `id` (or similar identifier). Store the manifest in a global `PLAYLISTS` variable.

Add a loading indicator: a `<div id="loading">Loading...</div>` that shows while fetch is in progress and hides on completion.

**Must NOT:**
- Hardcode any playlist names
- Load individual playlist files yet
- Handle errors (just let fetch fail visibly)

**References:**
- `state/youtube/manifest.json`

**Acceptance criteria:**
- `PLAYLISTS` is populated from manifest.json
- Loading indicator appears during fetch
- Tabs render playlist names from the manifest

**QA:**
```bash
# Verify manifest is fetched (no hardcoded MOCK_DATA)
grep -c "MOCK_DATA" web/index.html
grep -c "manifest.json" web/index.html
```
Expected: MOCK_DATA returns 0, manifest.json returns at least 1

**Commit:** `feat(fuzzy-search): load YouTube manifest dynamically`

---

### Task 2: Load individual playlist files on tab click

**What to do:**
When a playlist tab is clicked:
1. Fetch `state/youtube/raw/{playlist_id}.json`
2. Parse the response (array of YouTubeVideo objects)
3. Store the loaded videos in a `loadedVideos` map keyed by playlist id
4. Render the video list below the tabs
5. Show a loading spinner while fetching
6. Cache the result so subsequent clicks don't re-fetch

**Must NOT:**
- Load all playlists at once
- Modify the search logic yet
- Handle missing files gracefully

**References:**
- `state/youtube/raw/*.json`

**Acceptance criteria:**
- Clicking a tab fetches and displays that playlist's videos
- Second click on same tab uses cache (no network request)
- Video title, channel name, and duration are visible

**QA:**
```bash
# Verify tab click handler exists
grep -c "addEventListener" web/index.html
grep -c "loadedVideos" web/index.html
```
Expected: Both return at least 1

**Commit:** `feat(fuzzy-search): load playlist files on tab click`

---

### Task 3: Load Last.fm scrobbles

**What to do:**
On page load, fetch `state/lastfm/scrobbles.json`. Store in a global `SCROBBLES` array. Each entry has `trackTitle`, `artist`, `album`, `playedAt`. Add a count display showing total scrobbles loaded (e.g., "60,234 scrobbles loaded").

**Must NOT:**
- Parse or transform scrobble data
- Integrate scrobbles into search yet
- Show individual scrobble entries

**References:**
- `state/lastfm/scrobbles.json`

**Acceptance criteria:**
- `SCROBBLES` is populated from scrobbles.json
- Count display shows the correct number
- Page doesn't freeze during load (test with actual file)

**QA:**
```bash
# Verify scrobble loading code exists
grep -c "SCROBBLES" web/index.html
grep -c "scrobbles.json" web/index.html
```
Expected: Both return at least 1

**Commit:** `feat(fuzzy-search): load Last.fm scrobbles`

---

### Task 4: Implement "Search All" functionality

**What to do:**
Add a "Search All" button next to the search box. When clicked:
1. Gather all loaded YouTube videos from `loadedVideos` map
2. Combine with `SCROBBLES` (normalize to a common shape: `{ title, artist/channel, type: "video"|"scrobble" }`)
3. Run a single Fuse.js search across the combined dataset
4. Render results grouped by source type (Videos / Scrobbles)
5. Show result count per group

**Must NOT:**
- Load playlists that haven't been clicked yet
- Limit search results
- Add pagination

**References:**
- None

**Acceptance criteria:**
- Clicking "Search All" searches across all previously loaded playlists AND scrobbles
- Results show both video and scrobble matches
- Typing "Beethoven" shows classical videos (if loaded) and scrobbles

**QA:**
```bash
grep -c "Search All" web/index.html
grep -c "SCROBBLES" web/index.html
```
Expected: Both return at least 1

**Commit:** `feat(fuzzy-search): implement cross-source Search All`

---

### Task 5: Handle 60k record performance

**What to do:**
1. Measure load time of scrobbles.json in browser console (add `console.time`/`console.timeEnd`)
2. If load exceeds 2 seconds, add a Web Worker or chunked rendering
3. Test Fuse.js search performance with 60k records
4. If search exceeds 500ms, add debouncing (300ms delay on input)
5. Add a status message during search: "Searching {count} records..."

**Must NOT:**
- Add a backend
- Use pagination to hide results
- Modify the data structure

**References:**
- Fuse.js performance: `https://www.fusejs.io/api/options.html#threshold`

**Acceptance criteria:**
- Search across 60k scrobbles completes in under 1 second
- No visible UI freeze during search
- Debounce prevents search on every keystroke

**QA:**
```bash
grep -c "debounce" web/index.html
grep -c "console.time" web/index.html
```
Expected: Both return at least 1

**Commit:** `perf(fuzzy-search): add debounce and performance monitoring`

---

## Verify Phase 1

```bash
grep -c "MOCK_DATA" web/index.html
grep -c "manifest.json" web/index.html
grep -c "scrobbles.json" web/index.html
grep -c "Search All" web/index.html
grep -c "debounce" web/index.html
```
Expected: MOCK_DATA returns 0, all others return at least 1.

Manual test: Open in browser, see tabs from manifest, click a tab, see videos, type "Beethoven", see results, click "Search All", see combined results.

**Dependencies:** Phase 0
**Blocks:** Phase 2
