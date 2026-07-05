using Services.Google.YouTube;

namespace CLI.Dashboard;

public static class DashboardHtmlGenerator
{
    public static string Generate(
        IReadOnlyList<PlaylistSnapshot> playlists,
        Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)
    {
        var data = DashboardDataBuilder.Build(playlists, videosByPlaylist);

        var template = $$"""
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
            select { padding: 8px 16px; margin-bottom: 20px; font-size: 14px; border: 1px solid #ddd; border-radius: 4px; cursor: pointer; }
            .view { display: none; }
            .view.active { display: block; }
            a { color: #007bff; text-decoration: none; }
            a:hover { text-decoration: underline; }
            .search-row { display: flex; gap: 15px; margin-bottom: 15px; }
            .search-box { flex: 0 1 400px; padding: 10px; border: 1px solid #ddd; border-radius: 4px; font-size: 14px; }
            .search-box-right { margin-left: auto; }
            .search-box:focus { outline: none; border-color: #007bff; }
            </style>
            </head>
            <body>
            <h1>YouTube Dashboard</h1>
            <p>{{data.PlaylistCount}} playlists &middot; {{data.VideoCount}} videos</p>
            {{data.DropdownHtml}}
            {{data.PlaylistViewHtml}}
            {{data.VideoViewsHtml}}
            <script src="https://cdn.jsdelivr.net/npm/tabulator-tables@5.5.2/dist/js/tabulator.min.js"></script>
            <script>
            var currentSearchWords = [];
            function switchPlaylist(playlistId) {
              document.querySelectorAll('.search-box').forEach(box => box.value = '');
              if (playlistTableInstance) playlistTableInstance.clearFilter();
              Object.values(videoTableInstances).forEach(t => t.clearFilter());
              if (allVideosTableInstance) allVideosTableInstance.clearFilter();
              const ptEl = document.getElementById('playlist-table');
              const avEl = document.getElementById('all-videos-table');
              if (ptEl) ptEl.style.display = '';
              if (avEl) avEl.style.display = 'none';
              currentSearchWords = [];
              document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
              document.getElementById(playlistId).classList.add('active');
              if (playlistId === 'playlist-list') {
                if (playlistTableInstance) playlistTableInstance.redraw();
              } else {
                const pid = playlistId.replace('playlist-', '');
                if (videoTableInstances[pid]) videoTableInstances[pid].redraw();
              }
            }
            function relevanceSorter(a, b, aRow, bRow) {
              const aTitle = ((aRow.getData().sortKey || '') + '').toLowerCase();
              const bTitle = ((bRow.getData().sortKey || '') + '').toLowerCase();
              if (currentSearchWords.length === 0) return aTitle.localeCompare(bTitle);
              const aMatches = currentSearchWords.filter(w => aTitle.includes(w)).length;
              const bMatches = currentSearchWords.filter(w => bTitle.includes(w)).length;
              if (aMatches !== bMatches) return bMatches - aMatches;
              return aTitle.localeCompare(bTitle);
            }
            function filterPlaylistList(query) {
              if (!playlistTableInstance) return;
              currentSearchWords = query.toLowerCase().split(/\s+/).filter(w => w.length > 0);
              if (currentSearchWords.length === 0) {
                playlistTableInstance.clearFilter();
                playlistTableInstance.setSort([{column: 'sortKey', dir: 'asc'}]);
                return;
              }
              playlistTableInstance.setFilter((data) => {
                const text = (data.sortKey || '').toLowerCase();
                return currentSearchWords.every(word => text.includes(word));
              });
              playlistTableInstance.setSort([{column: 'sortKey', dir: 'asc', sorter: relevanceSorter}]);
            }
            function filterPlaylistTable(playlistId, query) {
              const table = videoTableInstances[playlistId];
              if (!table) return;
              currentSearchWords = query.toLowerCase().split(/\s+/).filter(w => w.length > 0);
              if (currentSearchWords.length === 0) {
                table.clearFilter();
                table.setSort([{column: 'sortKey', dir: 'asc'}]);
                return;
              }
              table.setFilter((data) => {
                const title = (data.sortKey || '').toLowerCase();
                const description = (data.description || '').toLowerCase();
                const text = title + ' ' + description;
                const titleMatches = currentSearchWords.filter(w => title.includes(w)).length;
                if (titleMatches === 0) return false;
                return currentSearchWords.every(word => text.includes(word));
              });
              table.setSort([{column: 'sortKey', dir: 'asc', sorter: relevanceSorter}]);
            }
            function filterAllPlaylists(query) {
              switchPlaylist('playlist-list');
              const avSearch = document.getElementById('all-videos-search');
              if (avSearch) avSearch.value = query;
              filterAllVideos(query);
            }
            function filterAllVideos(query) {
              if (!allVideosTableInstance) return;
              currentSearchWords = query.toLowerCase().split(/\s+/).filter(w => w.length > 0);
              const playlistTable = document.getElementById('playlist-table');
              const allVideosTable = document.getElementById('all-videos-table');
              if (currentSearchWords.length === 0) {
                allVideosTableInstance.clearFilter();
                playlistTable.style.display = '';
                allVideosTable.style.display = 'none';
                return;
              }
              playlistTable.style.display = 'none';
              allVideosTable.style.display = '';
              allVideosTableInstance.redraw();
              allVideosTableInstance.setFilter((data) => {
                const title = (data.sortKey || '').toLowerCase();
                const description = (data.description || '').toLowerCase();
                const text = title + ' ' + description;
                const titleMatches = currentSearchWords.filter(w => title.includes(w)).length;
                if (titleMatches === 0) return false;
                return currentSearchWords.every(word => text.includes(word));
              });
              allVideosTableInstance.setSort([{column: 'sortKey', dir: 'asc', sorter: relevanceSorter}]);
            }
            var playlistTableInstance = null;
            var videoTableInstances = {};
            var allVideosTableInstance = null;
            var playlistData = {{System.Text.Json.JsonSerializer.Serialize(data.PlaylistData)}};
            playlistTableInstance = new Tabulator('#playlist-table', {
              data: playlistData,
              layout: 'fitColumns',
              responsiveLayout: 'hide',
              initialSort: [{column: 'sortKey', dir: 'asc'}],
              columns: [
                {title: 'Title', field: 'title', formatter: 'html'},
                {title: 'Video Count', field: 'videoCount'},
                {title: 'Last Updated', field: 'lastUpdated'},
                {title: 'Sort Key', field: 'sortKey', visible: false, sorter: relevanceSorter}
              ]
            });
            {{data.VideoDataJs}}
            allVideosTableInstance = new Tabulator('#all-videos-table', {
              data: allVideosData,
              layout: 'fitColumns',
              responsiveLayout: 'hide',
              initialSort: [{column: 'sortKey', dir: 'asc'}],
              columns: [
                {title: 'Title', field: 'title', formatter: 'html'},
                {title: 'Description', field: 'description', formatter: 'textarea'},
                {title: 'Duration', field: 'duration', width: 100},
                {title: 'Channel', field: 'channel', formatter: 'html'},
                {title: 'Playlist', field: 'playlist'},
                {title: 'Sort Key', field: 'sortKey', visible: false, sorter: relevanceSorter}
              ]
            });
            document.querySelectorAll('[id^="video-table-"]').forEach(div => {
              const playlistId = div.id.replace('video-table-', '');
              const dataVarName = `videoData_${playlistId.replace(/-/g, '_')}`;
              videoTableInstances[playlistId] = new Tabulator(div, {
                data: eval(dataVarName),
                layout: 'fitColumns',
                responsiveLayout: 'hide',
                initialSort: [{column: 'sortKey', dir: 'asc'}],
                columns: [
                  {title: 'Title', field: 'title', formatter: 'html'},
                  {title: 'Description', field: 'description', formatter: 'textarea'},
                  {title: 'Duration', field: 'duration', width: 100},
                  {title: 'Channel', field: 'channel', formatter: 'html'},
                  {title: 'Sort Key', field: 'sortKey', visible: false, sorter: relevanceSorter}
                ]
              });
            });
            </script>
            </body>
            </html>
            """;

        return template;
    }
}
