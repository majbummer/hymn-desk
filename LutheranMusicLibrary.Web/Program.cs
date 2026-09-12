using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Register database path — use the persistent Railway volume if attached, otherwise
// fall back to the content root (used for local development, where the .db file
// sits next to Program.cs).
var dbDir = Environment.GetEnvironmentVariable("RAILWAY_VOLUME_MOUNT_PATH") ?? builder.Environment.ContentRootPath;
var dbPath = Path.Combine(dbDir, "lutheran_music_planner.db");
builder.Services.AddSingleton<DatabaseService>(new DatabaseService(dbPath));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
