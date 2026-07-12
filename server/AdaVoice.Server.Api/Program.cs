// Phase 0 scaffold: the server builds and runs but exposes no endpoints yet.
// The real API surface (auth, licensing, billing, admin) arrives in Phases 2+.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
