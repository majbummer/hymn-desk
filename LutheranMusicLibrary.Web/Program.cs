using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Register database path — use the persistent Railway volume if attached, otherwise
// fall back to the content root (used for local development, where the .db file
// sits next to Program.cs).
var dbDir = Environment.GetEnvironmentVariable("RAILWAY_VOLUME_MOUNT_PATH") ?? builder.Environment.ContentRootPath;
var dbPath = Path.Combine(dbDir, "lutheran_music_planner.db");
builder.Services.AddSingleton<DatabaseService>(new DatabaseService(dbPath));
builder.Services.AddSingleton<AdminService>(new AdminService(dbPath));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{

}

// The /admin pages exist only when the app runs on your own PC: Development
// environment, no Railway variables present, and the request itself coming from
// this machine. Anywhere else (including the live site) /admin is a plain 404.
var adminEnabled = app.Environment.IsDevelopment()
    && Environment.GetEnvironmentVariable("RAILWAY_VOLUME_MOUNT_PATH") == null
    && Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT") == null;

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        var ip = context.Connection.RemoteIpAddress;
        var isLocal = ip != null && System.Net.IPAddress.IsLoopback(ip);
        if (!adminEnabled || !isLocal)
        {
            context.Response.StatusCode = 404;
            return;
        }
    }
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
