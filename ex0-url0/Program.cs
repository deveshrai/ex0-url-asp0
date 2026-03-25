using System.Net.WebSockets;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(80);
});
var app = builder.Build();


app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

void InitDb()
{
    using var con = new SqliteConnection("Data Source=appDB.db");
    con.Open();
    var cmd = con.CreateCommand();
    cmd.CommandText =
    @"CREATE TABLE IF NOT EXISTS namedata (
        id TEXT PRIMARY KEY,
        name TEXT,
        number TEXT,
        ip TEXT,
        timestamp TEXT
    );";
    cmd.ExecuteNonQuery();
}
InitDb();

app.MapGet("/hello", () => "Hello World!");

app.Map("/register", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[1024 * 4];

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            break;
        }

        var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        var id = Guid.NewGuid().ToString();
        var name = data["name"];
        var number = data["number"];
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var timestamp = DateTime.UtcNow.ToString("o");

        using (var con = new SqliteConnection("Data Source=appDB.db"))
        {
            con.Open();
            var cmd = con.CreateCommand();
            cmd.CommandText =
            @"INSERT INTO namedata (id, name, number, ip, timestamp)
              VALUES ($id, $name, $number, $ip, $timestamp)";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$number", number);
            cmd.Parameters.AddWithValue("$ip", ip);
            cmd.Parameters.AddWithValue("$timestamp", timestamp);
            cmd.ExecuteNonQuery();
        }

        var reply = System.Text.Encoding.UTF8.GetBytes($"OK: {id}");
        await socket.SendAsync(reply, WebSocketMessageType.Text, true, CancellationToken.None);
    }
});

app.MapGet("/log", () =>
{
    var list = new List<object>();

    using var con = new SqliteConnection("Data Source=appDB.db");
    con.Open();
    var cmd = con.CreateCommand();
    cmd.CommandText = "SELECT id, name, number, ip, timestamp FROM namedata";

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        list.Add(new {
            id = reader.GetString(0),
            name = reader.GetString(1),
            number = reader.GetString(2),
            ip = reader.GetString(3),
            timestamp = reader.GetString(4)
        });
    }

    return Results.Json(list);
});


app.MapGet("/{code:regex(^[A-Za-z0-9]{{5}}$)}", (string code) =>
{
    return Results.Ok($"You entered alphanumeric code: {code}");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
    {
        Console.WriteLine($"App is running at: {url}");
    }
});


app.Run();
