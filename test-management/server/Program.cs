using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OffenderWatch.TestManagement.Server.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SignalR — the hub itself is added in Step 5 (Real-Time Execution). The
// service is wired up now so later steps only need to add a Hub class and
// map its endpoint.
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("ClientApp");

app.UseAuthorization();

app.MapControllers();

app.Run();
