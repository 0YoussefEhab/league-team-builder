var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// Step A1 skeleton only. The League code arrives in A2, the database and login in A3.
app.MapGet("/", () => "League Team Builder: skeleton (step A1).");
app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();
