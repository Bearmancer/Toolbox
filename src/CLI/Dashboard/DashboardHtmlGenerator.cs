namespace CLI.Dashboard;

public static class DashboardHtmlGenerator
{
    public static string Generate(DashboardData data) => $$"""
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
        .subtitle { color: #555; margin: 0 0 16px; font-size: 14px; }
        .toolbar { display: flex; gap: 12px; align-items: center; margin-bottom: 20px; flex-wrap: wrap; }
        #global-search { flex: 1; min-width: 220px; max-width: 520px; padding: 10px 14px; font-size: 15px; border: 2px solid #007bff; border-radius: 6px; outline: none; }
        #global-search:focus { border-color: #0056b3; box-shadow: 0 0 0 3px rgba(0,123,255,.2); }
        select { padding: 8px 14px; font-size: 14px; border: 1px solid #ddd; border-radius: 4px; cursor: pointer; background: #fff; }
        .per-search { padding: 9px 12px; border: 1px solid #ddd; border-radius: 4px; font-size: 14px; margin-bottom: 12px; width: 380px; outline: none; }
        .per-search:focus { border-color: #007bff; }
        .view { display: none; }
        .view.active { display: block; }
        a { color: #007bff; text-decoration: none; }
        a:hover { text-decoration: underline; }
        #results-count { color: #555; margin-bottom: 10px; font-size: 13px; }
        </style>
        </head>
        <body>
        <h1>YouTube Dashboard</h1>
        <p class="subtitle">{{data.PlaylistCount}} playlists &middot; {{data.VideoCount}} videos</p>
        <div class="toolbar">
          <input type="text" id="global-search" placeholder="Fuzzy search {{data.VideoCount}} videos across all playlists..." oninput="onGlobalSearch(this.value)">
          {{data.DropdownHtml}}
        </div>

        <div id="view-results" class="view">
          <div id="results-count"></div>
          <div id="results-table"></div>
        </div>

        <div id="view-all" class="view active">
          <div id="playlist-table"></div>
        </div>

        {{data.VideoViewsHtml}}

        <script src="https://cdn.jsdelivr.net/npm/fuse.js@7.0.0/dist/fuse.min.js"></script>
        <script src="https://cdn.jsdelivr.net/npm/tabulator-tables@5.5.2/dist/js/tabulator.min.js"></script>
        <script src="dashboard-data.js"></script>
        <script>
        var FUSE_OPTS = {
          keys: [
            { name: 'title', weight: 0.85 },
            { name: 'description', weight: 0.15 }
          ],
          threshold: 0.35,
          ignoreLocation: true,
          includeScore: true,
          minMatchCharLength: 2
        };

        var globalFuse = null;
        var playlistFuse = {};
        var videoTables = {};
        var playlistTable = null;
        var resultsTable = null;
        var debounceTimer = null;

        function esc(s) {
          return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
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
          document.getElementById('global-search').value = '';
          document.querySelectorAll('.per-search').forEach(function(s) { s.value = ''; });
          if (playlistId === 'all') {
            showView('view-all');
            if (playlistTable) playlistTable.redraw(true);
          } else {
            showView('view-' + playlistId);
            if (!videoTables[playlistId]) {
              initPlaylistTable(playlistId);
            } else {
              videoTables[playlistId].redraw(true);
            }
          }
        }

        function initPlaylistTable(playlistId) {
          var videos = window.allVideos.filter(function(v) { return v.playlistId === playlistId; });
          videoTables[playlistId] = new Tabulator('#video-table-' + playlistId, {
            data: videos,
            layout: 'fitColumns',
            initialSort: [{ column: 'title', dir: 'asc' }],
            columns: [
              { title: 'Title', field: 'title', formatter: titleFmt, minWidth: 250 },
              { title: 'Description', field: 'description', formatter: 'textarea', minWidth: 300 },
              { title: 'Duration', field: 'duration', width: 100 },
              { title: 'Channel', field: 'channelName', formatter: channelFmt, minWidth: 150 }
            ]
          });
          playlistFuse[playlistId] = new Fuse(videos, FUSE_OPTS);
        }

        function onGlobalSearch(query) {
          clearTimeout(debounceTimer);
          debounceTimer = setTimeout(function() { doGlobalSearch(query.trim()); }, 200);
        }

        function doGlobalSearch(q) {
          if (!q) {
            showView('view-all');
            document.getElementById('playlist-dropdown').value = 'all';
            return;
          }
          showView('view-results');
          var results = globalFuse.search(q).map(function(r) { return r.item; });
          document.getElementById('results-count').textContent =
            results.length + ' result' + (results.length !== 1 ? 's' : '') + ' for "' + q + '"';
          resultsTable.setData(results);
        }

        function onPlaylistSearch(playlistId, query) {
          clearTimeout(debounceTimer);
          debounceTimer = setTimeout(function() { doPlaylistSearch(playlistId, query.trim()); }, 200);
        }

        function doPlaylistSearch(playlistId, q) {
          var table = videoTables[playlistId];
          if (!table) return;
          if (!q) {
            table.setData(window.allVideos.filter(function(v) { return v.playlistId === playlistId; }));
            return;
          }
          if (!playlistFuse[playlistId]) {
            var videos = window.allVideos.filter(function(v) { return v.playlistId === playlistId; });
            playlistFuse[playlistId] = new Fuse(videos, FUSE_OPTS);
          }
          table.setData(playlistFuse[playlistId].search(q).map(function(r) { return r.item; }));
        }

        globalFuse = new Fuse(window.allVideos, FUSE_OPTS);

        playlistTable = new Tabulator('#playlist-table', {
          data: window.allPlaylists,
          layout: 'fitColumns',
          initialSort: [{ column: 'title', dir: 'asc' }],
          columns: [
            { title: 'Playlist', field: 'title', formatter: playlistTitleFmt, minWidth: 250 },
            { title: 'Videos', field: 'videoCount', width: 80 },
            { title: 'Last Updated', field: 'lastUpdated', width: 130 }
          ]
        });
        playlistTable.on('rowClick', function(e, row) {
          var pid = row.getData().playlistId;
          document.getElementById('playlist-dropdown').value = pid;
          switchView(pid);
        });

        resultsTable = new Tabulator('#results-table', {
          data: [],
          layout: 'fitColumns',
          columns: [
            { title: 'Title', field: 'title', formatter: titleFmt, minWidth: 250 },
            { title: 'Description', field: 'description', formatter: 'textarea', minWidth: 300 },
            { title: 'Duration', field: 'duration', width: 100 },
            { title: 'Channel', field: 'channelName', formatter: channelFmt, minWidth: 150 },
            { title: 'Playlist', field: 'playlistName', minWidth: 150 }
          ]
        });
        </script>
        </body>
        </html>
        """;
}
