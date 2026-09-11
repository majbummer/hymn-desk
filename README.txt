LUTHERAN MUSIC LIBRARY — Web App
=================================

SETUP
-----
1. Open LutheranMusicLibrary.Web.sln in Visual Studio
2. Copy lutheran_music_planner.db into the LutheranMusicLibrary.Web\ folder
3. Press F5 — a browser will open automatically at https://localhost:5001

PAGES
-----
/           Home page with stats
/hymns      Browse all hymn texts (searchable, filterable by season)
/hymns/{slug}   Individual hymn profile page (e.g. /hymns/a-mighty-fortress-is-our-god)
/tunes      Browse all hymn tunes
/tunes/{slug}   Individual tune profile page
/people     Browse all people
/people/{slug}  Individual person profile page

HOSTING (later)
---------------
Railway.app, Render.com, or Azure App Service — all free at this scale.
The SQLite database file goes with the app.
No separate database server needed.

