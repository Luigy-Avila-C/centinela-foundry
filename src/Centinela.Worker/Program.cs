using Centinela.Infrastructure;
using Centinela.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCentinela(builder.Configuration);
builder.Services.AddCentinelaTelemetry(builder.Configuration, "centinela-worker");
builder.Services.AddHostedService<WatcherService>();

builder.Build().Run();
