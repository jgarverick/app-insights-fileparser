using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Text;

namespace AppInsightsFileParser.FileSystem
{
    public class BackgroundLogWatcher : BackgroundService
    {
        FileSystemWatcher _watcher;
        Dictionary<string,long> _logCounts = new Dictionary<string,long>();
        public Dictionary<string, long> LogFileLineCounts { get { return _logCounts; } }
        TelemetryClient _client;
        string appInsightsKey;
        string logDirectory;
        string filter;
        public bool IsOperational { get; private set; }
        public BackgroundLogWatcher(string logFilter = "*")
        {
            IsOperational = false;
            filter = logFilter;
            logDirectory = Environment.GetEnvironmentVariable("LOG_PATH");
            appInsightsKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
            if (logDirectory == string.Empty) throw new InvalidOperationException("Path cannot be determined.");
            if (appInsightsKey == string.Empty) throw new InvalidOperationException("App Insights Key must be present to use this service.");

            
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        { 
            var fileName = Path.GetFileName(e.FullPath);
            if (_logCounts.ContainsKey(fileName))
            {
                _logCounts.Remove(fileName);
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);
            if (_logCounts.ContainsKey(fileName))
            {
                throw new InvalidDataException($"File name already exists in monitoring regiter: {e.FullPath}");
            }
            await ReadLogLines(e.FullPath);
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);
            if (!_logCounts.ContainsKey(fileName))
            {
                _logCounts.Add(fileName, 0);
                //throw new InvalidDataException($"File name is not currently in monitoring registry: {e.FullPath}. Please check general output for any issues with asynchronous deletes.");
            }
            await ReadLogLines(e.FullPath);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                await StopAsync(stoppingToken);
            }
            else
            {
                await Task.Factory.StartNew(() =>
               {
                   _client = new TelemetryClient(new TelemetryConfiguration(appInsightsKey));
                   _watcher = new FileSystemWatcher(logDirectory, filter);
                   _watcher.EnableRaisingEvents = true;
                   _watcher.NotifyFilter = NotifyFilters.LastWrite;
                   _watcher.Changed += OnFileChanged;
                   _watcher.Created += OnFileCreated;
                   _watcher.Deleted += OnFileDeleted;
                   IsOperational = true;
               });
            }
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return ExecuteAsync(cancellationToken);
        }

        private async Task ReadLogLines(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var lineCount = _logCounts[fileName];

            var sb = await File.ReadAllLinesAsync(filePath);
            while (lineCount < sb.Length)
            {
                await LogTelemetry(sb[lineCount]);
                lineCount++;
            }
            _logCounts[fileName] = lineCount;
            
        }
        private async Task LogTelemetry(string line)
        {
            await Task.Run(() =>
            {
                if (line.ToUpper().Contains("ERR"))
                {
                    _client.TrackException(new Exception(line));
                }
                _client.TrackTrace(line);
            });
           
        }
    }
}
