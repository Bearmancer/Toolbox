# Phase 2: Deploy — Responsive + GitHub Pages

## Tasks

### Task 1: Add mobile-responsive CSS

**What to do:**
Add media queries to `web/index.html` for screens under 768px:
- Search box: full width, 40px height, 14px font
- Playlist tabs: vertical stack instead of horizontal row
- Results: single column, smaller font
- Touch-friendly tap targets (min 44px height on buttons)

**Must NOT:**
- Use a CSS framework
- Add a hamburger menu
- Change the search logic

**References:**
- None

**Acceptance criteria:**
- Layout adapts on resize below 768px
- Tabs stack vertically on mobile
- Search box is tappable (44px+ height)

**QA:**
```bash
grep -c "@media" web/index.html
```
Expected: Returns at least 1

**Commit:** `feat(fuzzy-search): add mobile-responsive CSS`

---

### Task 2: Create GitHub repository

**What to do:**
1. Create a new GitHub repo named `fuzzy-music-search` (or use an existing repo)
2. Initialize git in the project root
3. Add `web/` as the only tracked directory
4. Commit with message "initial commit"

**Must NOT:**
- Push any state/ or logs/ files
- Include .env files
- Add the src/ directory

**References:**
- GitHub CLI: `gh repo create`

**Acceptance criteria:**
- Repo exists on GitHub
- `web/` directory is in the repo root
- No sensitive files committed

**QA:**
```bash
gh repo view lance/fuzzy-music-search --json name,url
```
Expected: Returns repo info

**Commit:** N/A (this IS the commit)

---

### Task 3: Deploy to GitHub Pages

**What to do:**
1. Create a `gh-pages` branch
2. Copy `web/` contents to the branch root
3. Push the `gh-pages` branch
4. Enable GitHub Pages in repo settings (source: gh-pages branch)
5. Wait for deployment, then verify the URL works

**Must NOT:**
- Use GitHub Actions
- Add a build step
- Modify the HTML for deployment

**References:**
- GitHub Pages: `https://docs.github.com/en/pages`

**Acceptance criteria:**
- GitHub Pages URL is accessible
- Page loads without errors
- Fuse.js CDN loads correctly

**QA:**
```bash
# Check Pages deployment status
gh api repos/lance/fuzzy-music-search/pages --jq '.html_url'
```
Expected: Returns a github.io URL

**Commit:** N/A (branch push)

---

### Task 4: Test on mobile browser

**What to do:**
1. Open the GitHub Pages URL on a phone browser
2. Verify search works with touch input
3. Verify tabs are tappable
4. Verify no horizontal scroll
5. Test with a real search query (e.g., "Beethoven")

**Must NOT:**
- Skip this step
- Assume it works without testing

**References:**
- None

**Acceptance criteria:**
- Page loads on mobile
- Search returns results
- No horizontal overflow
- Tabs are tappable

**QA:**
```bash
# Manual verification required - no automated test
echo "Open GitHub Pages URL on phone and verify:"
echo "1. Page loads"
echo "2. Search works"
echo "3. Tabs are tappable"
echo "4. No horizontal scroll"
```
Expected: All checks pass

**Commit:** N/A (manual verification)

---

### Task 5: Final polish and README

**What to do:**
1. Add a `<title>` tag: "Fuzzy Music Search"
2. Add a brief description above the search box: "Search across YouTube playlists and Last.fm scrobbles"
3. Create a `web/README.md` with:
   - What this is
   - How to use it
   - Data sources (YouTube + Last.fm)
   - How to update data (run the CLI tools)

**Must NOT:**
- Add analytics
- Add a favicon
- Add complex documentation

**References:**
- None

**Acceptance criteria:**
- Page has a title
- Description is visible
- README explains the project

**QA:**
```bash
grep -c "Fuzzy Music Search" web/index.html
test -f web/README.md
```
Expected: grep returns 1, test returns true

**Commit:** `docs(fuzzy-search): add README and page title`

---

## Verify Phase 2

```bash
grep -c "@media" web/index.html
grep -c "Fuzzy Music Search" web/index.html
test -f web/README.md
gh api repos/lance/fuzzy-music-search/pages --jq '.html_url'
```
Expected: All pass. GitHub Pages URL is live and accessible on mobile.

**Dependencies:** Phase 1
**Blocks:** None (project complete)
