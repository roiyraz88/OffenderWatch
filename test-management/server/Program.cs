using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;
using OffenderWatch.TestManagement.Server.Hubs;
using OffenderWatch.TestManagement.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SignalR (TM-03, Step 5) — RunHub broadcasts persisted Run/ScenarioResult
// changes to clients subscribed to a specific run's group.
builder.Services.AddSignalR();

// CORS — the React (Vite) dev client runs on a different origin. Origins are
// read from configuration so a deployed client's origin never has to be
// hard-coded here.
var clientOrigins = builder.Configuration.GetSection("ClientOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApp", policy =>
    {
        policy.WithOrigins(clientOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // required for SignalR
    });
});

// SQLite — the connection string's *pattern* comes from configuration
// (appsettings.json's ConnectionStrings:Default), never hard-coded here.
// What IS resolved here is the relative DataSource path in that pattern,
// against the project's own ContentRootPath rather than the process's
// current working directory — `dotnet run` from test-management/server and
// `dotnet ef` invoked from the same folder both land on the same absolute
// file either way, regardless of the shell that launched them.
var configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=../data/testmanagement.db";

var sqliteBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(sqliteBuilder.DataSource))
{
    sqliteBuilder.DataSource = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, sqliteBuilder.DataSource));
}
Directory.CreateDirectory(Path.GetDirectoryName(sqliteBuilder.DataSource)!);
var connectionString = sqliteBuilder.ToString();

builder.Services.AddDbContext<TestManagementDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IEnvironmentService, EnvironmentService>();

// TM-02 (Step 4) — run execution. RunQueue/RunCancellationRegistry are
// singletons shared between the HTTP-facing RunService and the background
// worker; RunOrchestrator is Scoped so each run execution gets its own
// DbContext (4.16) via a scope the background service creates itself.
builder.Services.Configure<RunnerOptions>(builder.Configuration.GetSection("Runner"));
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddSingleton<RunCancellationRegistry>();
builder.Services.AddScoped<IRunService, RunService>();
builder.Services.AddScoped<RunOrchestrator>();
builder.Services.AddHostedService<RunExecutionBackgroundService>();

// TM-04 (Step 6, Part A) — derived, read-only; no execution-side state.
builder.Services.AddScoped<ITestHistoryService, TestHistoryService>();

// TM-07 (Step 8) — derived release overview; reuses ITestHistoryService's
// own CurrentFailureSince output rather than re-deriving it.
builder.Services.AddScoped<IDashboardService, DashboardService>();

// TM-06 (Step 7) — cleanup calls the real target OffenderWatch API through
// a plain HttpClient from the standard factory; no fixed BaseAddress here
// since the target varies per TestRun (its own BaseUrlSnapshot).
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITestDataService, TestDataService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Maps the Services-layer exceptions (Step 3.3/3.4) to HTTP status codes in
// one place, so controllers stay thin instead of try/catching in every
// action. Anything not one of these three is a genuine unhandled error
// (500) — not swallowed.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var (statusCode, title, detail) = feature?.Error switch
        {
            EnvironmentValidationException ex => (StatusCodes.Status400BadRequest, "Validation failed", ex.Message),
            EnvironmentNotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            EnvironmentConflictException ex => (StatusCodes.Status409Conflict, "Conflict", ex.Message),
            RunNotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            RunConflictException ex => (StatusCodes.Status409Conflict, "Conflict", ex.Message),
            ScenarioResultNotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            TestCaseNotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            TestDataRecordNotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            TestDataValidationException ex => (StatusCodes.Status400BadRequest, "Validation failed", ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { title, status = statusCode, detail });
    });
});

app.UseHttpsRedirection();

app.UseCors("ClientApp");

app.UseAuthorization();

app.MapControllers();
app.MapHub<RunHub>("/hubs/runs");

app.Run();
