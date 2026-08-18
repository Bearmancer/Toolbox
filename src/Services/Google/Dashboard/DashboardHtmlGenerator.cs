namespace Services.Google.Dashboard;

public static class DashboardHtmlGenerator
{
	public static string Generate(DashboardData data) =>
		$$"""
			<!DOCTYPE html>
			<html lang="en">
			<head>
			<meta charset="UTF-8">
			<meta name="viewport" content="width=device-width, initial-scale=1.0">
			<title>YouTube Dashboard</title>
			<link href="https://cdn.jsdelivr.net/npm/tabulator-tables@5.5.2/dist/css/tabulator.min.css" rel="stylesheet">
			<style>
			* { box-sizing: border-box; }
			body { font-family: system-ui, sans-serif; margin: 0; padding: 20px; }
			h1 { margin: 0 0 4px; }
			h2 { margin: 0 0 12px; }
			.subtitle { color: #555; margin: 0 0 16px; font-size: 14px; }
			.toolbar { display: flex; gap: 12px; align-items: center; margin-bottom: 12px; flex-wrap: wrap; }
			select { padding: 8px 14px; font-size: 14px; border: 1px solid #ddd; border-radius: 4px; cursor: pointer; background: #fff; }
			.per-search { padding: 9px 12px; border: 1px solid #ddd; border-radius: 4px; font-size: 14px; margin-bottom: 12px; width: 380px; outline: none; }
			.per-search:focus { border-color: #007bff; }
			.view { display: none; }
			.view.active { display: block; }
			a { color: #007bff; text-decoration: none; }
			a:hover { text-decoration: underline; }
			#results-count { color: #555; margin-bottom: 10px; font-size: 13px; }
			.toggle-bar { display: flex; gap: 10px; align-items: center; margin-bottom: 12px; flex-wrap: wrap; }
			.toggle-bar .per-search { margin-bottom: 0; }
			.col-toggle { position: relative; }
			.toggle-btn { padding: 8px 12px; font-size: 13px; border: 1px solid #ddd; border-radius: 4px; cursor: pointer; background: #f8f8f8; }
			.toggle-btn:hover { background: #eee; }
			.col-dropdown { display: none; position: absolute; top: calc(100% + 4px); left: 0; background: #fff; border: 1px solid #ddd; border-radius: 4px; padding: 8px 12px; z-index: 99; min-width: 180px; max-height: 320px; overflow-y: auto; box-shadow: 0 2px 8px rgba(0,0,0,.12); }
			.col-dropdown.open { display: block; }
			.col-dropdown label { display: flex; align-items: center; gap: 6px; padding: 4px 0; font-size: 13px; cursor: pointer; white-space: nowrap; }
			.col-dropdown .divider { border-top: 1px solid #eee; margin: 6px 0; }
			.sort-cycle[data-sort-state='asc'] .tabulator-col-title::after { content: ' \25B2'; color: #007bff; }
			.sort-cycle[data-sort-state='desc'] .tabulator-col-title::after { content: ' \25BC'; color: #007bff; }
			</style>
			</head>
			<body>
			<h1>YouTube Dashboard</h1>
			<p class="subtitle">{{data.PlaylistCount}} playlists &middot; {{data.VideoCount}} videos</p>
			<div class="toolbar">
			  {{data.DropdownHtml}}
			</div>

			<div id="view-all" class="view active">
			  <h2>Playlist Overview</h2>
			  <input type="text" class="per-search" id="overview-search" placeholder="Search playlists..." oninput="onOverviewSearch(this.value)">
			  <div id="results-count"></div>
			  <div id="playlist-table"></div>
			</div>

			<div id="view-all-videos" class="view">
			  <h2>All Videos</h2>
			  <div class="toggle-bar">
			    <input type="text" class="per-search" id="all-videos-search" placeholder="Search all videos..." oninput="onAllVideosSearch(this.value)">
			    <div class="col-toggle" id="playlist-filter-toggle">
			      <button class="toggle-btn" onclick="toggleDropdown('playlist-filter-cols')">Playlists &#9660;</button>
			      <div class="col-dropdown" id="playlist-filter-cols">
			        <label><input type="checkbox" id="pl-all" checked onchange="onToggleAllPlaylists(this.checked)"> <strong>All Playlists</strong></label>
			        <div class="divider"></div>
			        {{data.PlaylistFilterHtml}}
			      </div>
			    </div>
			    <div class="col-toggle" id="all-videos-toggle">
			      <button class="toggle-btn" onclick="toggleDropdown('all-videos-cols')">Columns &#9660;</button>
			      <div class="col-dropdown" id="all-videos-cols">
			        <label><input type="checkbox" checked onchange="toggleCol(allVideoTable,'title',this.checked)"> Title</label>
			        <label><input type="checkbox" checked onchange="toggleCol(allVideoTable,'channelName',this.checked)"> Channel</label>
			        <label><input type="checkbox" checked onchange="toggleCol(allVideoTable,'duration',this.checked)"> Duration</label>
			        <label><input type="checkbox" checked onchange="toggleCol(allVideoTable,'playlistName',this.checked)"> Playlist</label>
			        <label><input type="checkbox" onchange="toggleCol(allVideoTable,'description',this.checked)"> Description</label>
			      </div>
			    </div>
			  </div>
			  <div id="all-videos-table"></div>
			</div>

			{{data.VideoViewsHtml}}

			<script src="https://cdn.jsdelivr.net/npm/tabulator-tables@5.5.2/dist/js/tabulator.min.js"></script>
			<script src="dashboard-data.js"></script>
			<script>
			var DEBOUNCE_MS = 200;
			var videoTables = {};
			var playlistTable = null;
			var allVideoTable = null;
			var debounceTimer = null;

			var overviewQuery = '';
			var allVideosQuery = '';
			var playlistQueries = {};
			var playlistIncluded = {};
			var sortState = {};

			window.allPlaylists.forEach(function(p) { playlistIncluded[p.playlistId] = true; });

			function esc(s) {
			  return String(s == null ? '' : s)
			    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
			}

			function tokenize(q) {
			  return q.trim().toLowerCase().split(/\s+/).filter(Boolean);
			}

			function isAlnum(c) {
			  if (!c) return false;
			  return /[a-z0-9]/i.test(c);
			}

			function matchWholeWord(value, token) {
			  if (value == null) return false;
			  var v = String(value).toLowerCase();
			  var t = String(token).toLowerCase();
			  if (!t) return false;
			  var idx = 0;
			  while ((idx = v.indexOf(t, idx)) !== -1) {
			    var beforeOk = idx === 0 || !isAlnum(v[idx - 1]);
			    var afterIdx = idx + t.length;
			    var afterOk = afterIdx >= v.length || !isAlnum(v[afterIdx]);
			    if (beforeOk && afterOk) return true;
			    idx += 1;
			  }
			  return false;
			}

			function matchTokens(data, tokens, fields) {
			  for (var i = 0; i < tokens.length; i++) {
			    var t = tokens[i];
			    var matched = false;
			    for (var j = 0; j < fields.length; j++) {
			      if (matchWholeWord(data[fields[j]], t)) { matched = true; break; }
			    }
			    if (!matched) return false;
			  }
			  return true;
			}

			var VIDEO_FIELDS = ['title', 'description', 'channelName', 'playlistName'];
			var PLAYLIST_FIELDS = ['title', 'lastUpdated'];

			function overviewFilter(data) {
			  if (!overviewQuery) return true;
			  return matchTokens(data, tokenize(overviewQuery), PLAYLIST_FIELDS);
			}

			function allVideosFilter(data) {
			  if (!playlistIncluded[data.playlistId]) return false;
			  if (!allVideosQuery) return true;
			  return matchTokens(data, tokenize(allVideosQuery), VIDEO_FIELDS);
			}

			function playlistVideoFilterFactory(playlistId) {
			  var q = playlistQueries[playlistId] || '';
			  var tokens = tokenize(q);
			  return function(data) {
			    if (data.playlistId !== playlistId) return false;
			    if (!tokens.length) return true;
			    return matchTokens(data, tokens, VIDEO_FIELDS);
			  };
			}

			function titleFmt(cell) {
			  var d = cell.getData();
			  return '<a href="https://www.youtube.com/watch?v=' + d.videoId + '" target="_blank">' + esc(d.title) + '</a>';
			}

			function channelFmt(cell) {
			  var d = cell.getData();
			  return '<a href="https://www.youtube.com/channel/' + d.channelId + '" target="_blank">' + esc(d.channelName) + '</a>';
			}

			function playlistTitleFmt(cell) {
			  var d = cell.getData();
			  return '<a href="https://www.youtube.com/playlist?list=' + d.playlistId + '" target="_blank">' + esc(d.title) + '</a>';
			}

			function showView(id) {
			  document.querySelectorAll('.view').forEach(function(v) { v.classList.remove('active'); });
			  document.getElementById(id).classList.add('active');
			}

			function switchView(playlistId) {
			  document.querySelectorAll('.per-search').forEach(function(s) {
			    if (s.id !== 'overview-search' && s.id !== 'all-videos-search') s.value = '';
			  });
			  playlistQueries = {};
			  if (playlistId === 'all') {
			    showView('view-all');
			    if (playlistTable) { playlistTable.redraw(true); updateResultsCount(); }
			  } else if (playlistId === 'all-videos') {
			    showView('view-all-videos');
			    if (allVideoTable) allVideoTable.redraw(true);
			  } else {
			    showView('view-' + playlistId);
			    if (!videoTables[playlistId]) initPlaylistTable(playlistId);
			    else videoTables[playlistId].redraw(true);
			  }
			}

			function cycleSort(tableKey, table, e, column) {
			  e.preventDefault();
			  e.stopPropagation();
			  var field = column.getField();
			  if (!field) return;
			  var st = sortState[tableKey] = sortState[tableKey] || {};
			  Object.keys(st).forEach(function(k) { if (k !== field) st[k] = 0; });
			  st[field] = ((st[field] || 0) + 1) % 3;
			  if (st[field] === 0) table.clearSort();
			  else table.setSort(field, st[field] === 1 ? 'asc' : 'desc');
			  updateSortIndicators(tableKey, table);
			}

			function updateSortIndicators(tableKey, table) {
			  var st = sortState[tableKey] || {};
			  table.getColumns().forEach(function(col) {
			    var el = col.getElement();
			    if (!el) return;
			    var f = col.getField();
			    if (st[f] === 1) el.setAttribute('data-sort-state', 'asc');
			    else if (st[f] === 2) el.setAttribute('data-sort-state', 'desc');
			    else el.removeAttribute('data-sort-state');
			  });
			}

			function makeHeaderClick(tableKey, tableRef) {
			  return function(e, column) {
			    var t = (typeof tableRef === 'function') ? tableRef() : tableRef;
			    if (t) cycleSort(tableKey, t, e, column);
			  };
			}

			function sortColumn(columnDef, tableKey, tableRef) {
			  return Object.assign(columnDef, { headerSort: false, cssClass: 'sort-cycle', headerClick: makeHeaderClick(tableKey, tableRef) });
			}

			function initPlaylistTable(playlistId) {
			  var videos = window.allVideos.filter(function(v) { return v.playlistId === playlistId; });
			  var tableKey = 'pl-' + playlistId;
			  var getTable = function() { return videoTables[playlistId]; };
			  var columns = [
			    sortColumn({ title: 'Title', field: 'title', formatter: titleFmt, minWidth: 250 }, tableKey, getTable),
			    sortColumn({ title: 'Description', field: 'description', formatter: 'textarea', minWidth: 300 }, tableKey, getTable),
			    sortColumn({ title: 'Duration', field: 'duration', width: 100 }, tableKey, getTable),
			    sortColumn({ title: 'Channel', field: 'channelName', formatter: channelFmt, minWidth: 150 }, tableKey, getTable)
			  ];
			  videoTables[playlistId] = new Tabulator('#video-table-' + playlistId, {
			    data: videos,
			    layout: 'fitColumns',
			    columns: columns
			  });
			}

			function onOverviewSearch(query) {
			  clearTimeout(debounceTimer);
			  debounceTimer = setTimeout(function() {
			    overviewQuery = query.trim();
			    if (playlistTable) {
			      playlistTable.setFilter(overviewFilter);
			      updateResultsCount();
			    }
			  }, DEBOUNCE_MS);
			}

			function updateResultsCount() {
			  var el = document.getElementById('results-count');
			  if (!el || !playlistTable) return;
			  var n = overviewQuery ? playlistTable.getDataCount() : 0;
			  el.textContent = overviewQuery
			    ? n + ' playlist' + (n !== 1 ? 's' : '') + ' matching "' + overviewQuery + '"'
			    : '';
			}

			function onAllVideosSearch(query) {
			  clearTimeout(debounceTimer);
			  debounceTimer = setTimeout(function() {
			    allVideosQuery = query.trim();
			    if (allVideoTable) allVideoTable.setFilter(allVideosFilter);
			  }, DEBOUNCE_MS);
			}

			function onPlaylistSearch(playlistId, query) {
			  clearTimeout(debounceTimer);
			  debounceTimer = setTimeout(function() {
			    playlistQueries[playlistId] = query.trim();
			    var t = videoTables[playlistId];
			    if (t) t.setFilter(playlistVideoFilterFactory(playlistId));
			  }, DEBOUNCE_MS);
			}

			function onTogglePlaylistIncluded(playlistId, checked) {
			  playlistIncluded[playlistId] = checked;
			  var allChecked = window.allPlaylists.every(function(p) { return playlistIncluded[p.playlistId]; });
			  var allCb = document.getElementById('pl-all');
			  if (allCb) allCb.checked = allChecked;
			  if (allVideoTable) allVideoTable.setFilter(allVideosFilter);
			}

			function onToggleAllPlaylists(checked) {
			  window.allPlaylists.forEach(function(p) {
			    playlistIncluded[p.playlistId] = checked;
			    var cb = document.getElementById('pl-cb-' + p.playlistId);
			    if (cb) cb.checked = checked;
			  });
			  if (allVideoTable) allVideoTable.setFilter(allVideosFilter);
			}

			function toggleDropdown(id) {
			  var el = document.getElementById(id);
			  el.classList.toggle('open');
			}

			function toggleCol(table, field, visible) {
			  if (!table) return;
			  if (visible) table.showColumn(field); else table.hideColumn(field);
			}

			playlistTable = new Tabulator('#playlist-table', {
			  data: window.allPlaylists,
			  layout: 'fitColumns',
			  pagination: 'local',
			  paginationSize: 100,
			  paginationSizeSelector: [25, 50, 100, 200],
			  columns: [
			    sortColumn({ title: 'Playlist', field: 'title', formatter: playlistTitleFmt, minWidth: 250 }, 'overview', function() { return playlistTable; }),
			    sortColumn({ title: 'Videos', field: 'videoCount', width: 80 }, 'overview', function() { return playlistTable; }),
			    sortColumn({ title: 'Last Updated', field: 'lastUpdated', width: 130 }, 'overview', function() { return playlistTable; })
			  ]
			});
			playlistTable.on('rowClick', function(e, row) {
			  var pid = row.getData().playlistId;
			  document.getElementById('playlist-dropdown').value = pid;
			  switchView(pid);
			});

			allVideoTable = new Tabulator('#all-videos-table', {
			  data: window.allVideos,
			  layout: 'fitColumns',
			  pagination: 'local',
			  paginationSize: 100,
			  paginationSizeSelector: [25, 50, 100, 200],
			  columns: [
			    sortColumn({ title: 'Title', field: 'title', formatter: titleFmt, minWidth: 250 }, 'allvideos', function() { return allVideoTable; }),
			    sortColumn({ title: 'Channel', field: 'channelName', formatter: channelFmt, minWidth: 150 }, 'allvideos', function() { return allVideoTable; }),
			    sortColumn({ title: 'Duration', field: 'duration', width: 100 }, 'allvideos', function() { return allVideoTable; }),
			    sortColumn({ title: 'Playlist', field: 'playlistName', minWidth: 150 }, 'allvideos', function() { return allVideoTable; }),
			    sortColumn({ title: 'Description', field: 'description', formatter: 'textarea', minWidth: 300, visible: false }, 'allvideos', function() { return allVideoTable; })
			  ]
			});

			document.addEventListener('click', function(e) {
			  if (!e.target.closest('.col-toggle')) {
			    document.querySelectorAll('.col-dropdown.open').forEach(function(d) { d.classList.remove('open'); });
			  }
			});
			</script>
			</body>
			</html>
			""";
}
