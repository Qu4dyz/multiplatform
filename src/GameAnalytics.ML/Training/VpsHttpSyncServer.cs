using System.Net;
using System.Text;
using System.Text.Json;
using GameAnalytics.Core.Entities;

namespace GameAnalytics.ML.Training;

public class VpsHttpSyncServer
{
    private readonly int _port;
    private readonly string _modelDirectory;
    private readonly string _databasePath;
    private readonly Func<VpsServerStatus> _statusProvider;
    private HttpListener? _listener;

    public VpsHttpSyncServer(
        int port,
        string modelDirectory,
        string databasePath,
        Func<VpsServerStatus> statusProvider)
    {
        _port = port;
        _modelDirectory = modelDirectory;
        _databasePath = databasePath;
        _statusProvider = statusProvider;
    }

    public void Start(Action<string>? logger = null, CancellationToken ct = default)
    {
        var log = logger ?? Console.WriteLine;

        try
        {
            _listener = new HttpListener();
            if (OperatingSystem.IsWindows())
            {
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            }
            else
            {
                _listener.Prefixes.Add($"http://*:{_port}/");
            }
            _listener.Start();

            log($"[VPS Sync Server] HTTP сервер синхронізації запущено на порту {_port} (http://0.0.0.0:{_port}/)");
            _ = ListenLoopAsync(log, ct);
        }
        catch (Exception ex)
        {
            log($"[VPS Sync Server] Не вдалося запустити HTTP сервер на порту {_port}: {ex.Message}");
        }
    }

    private async Task ListenLoopAsync(Action<string> log, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, log));
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                log($"[VPS Sync Server] Помилка в циклі обробки запитів: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, Action<string> log)
    {
        var req = context.Request;
        var resp = context.Response;

        // Enable CORS
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = (int)HttpStatusCode.NoContent;
            resp.Close();
            return;
        }

        var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? string.Empty;

        try
        {
            if (path == "" || path == "/api/status" || path == "/api/health")
            {
                var status = _statusProvider();
                var json = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
                var bytes = Encoding.UTF8.GetBytes(json);

                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.StatusCode = (int)HttpStatusCode.OK;
                await resp.OutputStream.WriteAsync(bytes);
            }
            else if (path == "/api/model")
            {
                var modelFile = Path.Combine(_modelDirectory, "fasttree_model.zip");
                if (File.Exists(modelFile))
                {
                    resp.ContentType = "application/zip";
                    resp.AddHeader("Content-Disposition", "attachment; filename=\"fasttree_model.zip\"");
                    using var fs = File.OpenRead(modelFile);
                    resp.ContentLength64 = fs.Length;
                    resp.StatusCode = (int)HttpStatusCode.OK;
                    await fs.CopyToAsync(resp.OutputStream);
                    log($"[VPS Sync Server] Успішно передано модель клієнту: {req.RemoteEndPoint?.Address}");
                }
                else
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                    var msg = Encoding.UTF8.GetBytes("{\"error\": \"Model file not found yet.\"}");
                    resp.ContentType = "application/json";
                    await resp.OutputStream.WriteAsync(msg);
                }
            }
            else if (path == "/api/database")
            {
                if (File.Exists(_databasePath))
                {
                    resp.ContentType = "application/octet-stream";
                    resp.AddHeader("Content-Disposition", "attachment; filename=\"game_analytics.db\"");
                    using var fs = new FileStream(_databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    resp.ContentLength64 = fs.Length;
                    resp.StatusCode = (int)HttpStatusCode.OK;
                    await fs.CopyToAsync(resp.OutputStream);
                    log($"[VPS Sync Server] Успішно передано базу даних клієнту: {req.RemoteEndPoint?.Address}");
                }
                else
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                    var msg = Encoding.UTF8.GetBytes("{\"error\": \"Database file not found yet.\"}");
                    resp.ContentType = "application/json";
                    await resp.OutputStream.WriteAsync(msg);
                }
            }
            else
            {
                resp.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception ex)
        {
            log($"[VPS Sync Server] Помилка відправки відповіді {path}: {ex.Message}");
            try { resp.StatusCode = (int)HttpStatusCode.InternalServerError; } catch { }
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    public void Stop()
    {
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
    }
}
