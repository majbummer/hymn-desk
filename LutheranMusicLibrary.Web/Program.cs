using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Register database path
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "lutheran_music_planner.db");
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
