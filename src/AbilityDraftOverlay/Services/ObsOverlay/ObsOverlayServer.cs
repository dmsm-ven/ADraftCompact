using ADraftCompact.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ADraftCompact.Services;

/// <summary>Small loopback-only HTTP/WebSocket host for OBS Browser Source. Uses only the .NET runtime.</summary>
public sealed class ObsOverlayServer : IAsyncDisposable, IDisposable
{
    private const int Port = 49171;
    private const string OverlayPath = "/ability-draft-overlay";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sendLocks = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;
    private OverlaySettings _settings = new();
    private DraftSession? _session;
    private string _lastState = "{}";
    private string? _url;

    public string? Url { get { lock (_gate) return _url; } }
    public bool IsRunning { get { lock (_gate) return _listener?.IsListening == true; } }

    public async Task ConfigureAsync(OverlaySettings settings, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _settings = settings;
            if (settings.EnableObsOverlay && string.IsNullOrWhiteSpace(settings.ObsAccessToken))
                settings.ObsAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            if (!settings.EnableObsOverlay)
            {
                _url = null;
            }
            else
            {
                _url = $"http://127.0.0.1:{Port}{OverlayPath}";
            }
        }

        if (!settings.EnableObsOverlay)
        {
            await StopAsync();
            return;
        }

        lock (_gate)
        {
            if (_listener?.IsListening != true)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                try { listener.Start(); }
                catch
                {
                    listener.Close();
                    _url = null;
                    throw new InvalidOperationException($"Could not start the OBS overlay server on port {Port}. Check whether another application is using it.");
                }
                _listener = listener;
                _shutdown = new CancellationTokenSource();
                _acceptTask = AcceptLoopAsync(listener, _shutdown.Token);
            }
            _lastState = BuildState(_session, _settings);
        }
        await BroadcastAsync(cancellationToken);
    }

    public void UpdateSession(DraftSession? session)
    {
        lock (_gate)
        {
            _session = session;
            _lastState = BuildState(_session, _settings);
        }
        _ = BroadcastAsync();
    }

    public void UpdateSettings(OverlaySettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            _lastState = BuildState(_session, _settings);
        }
        _ = BroadcastAsync();
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = HandleRequestAsync(context, cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            string token;
            lock (_gate) token = _settings.ObsAccessToken;
            if (path == OverlayPath && context.Request.HttpMethod == "GET")
            {
                await WriteTextAsync(context.Response, Html, "text/html; charset=utf-8", cancellationToken);
                return;
            }
            if (path == "/overlay.css" && context.Request.HttpMethod == "GET")
            {
                await WriteTextAsync(context.Response, Css, "text/css; charset=utf-8", cancellationToken);
                return;
            }
            if (path == "/overlay.js" && context.Request.HttpMethod == "GET")
            {
                await WriteTextAsync(context.Response, JavaScript, "text/javascript; charset=utf-8", cancellationToken);
                return;
            }
            if (path == "/ws" && context.Request.IsWebSocketRequest)
            {
                await HandleWebSocketAsync(context, cancellationToken);
                return;
            }
            var assetPrefix = "/asset/";
            if (path.StartsWith(assetPrefix, StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
            {
                await HandleAssetAsync(context, path[assetPrefix.Length..], cancellationToken);
                return;
            }
            context.Response.StatusCode = 404;
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Abort(); } catch { }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var accepted = await context.AcceptWebSocketAsync(null);
        var socket = accepted.WebSocket;
        var id = Guid.NewGuid();
        _clients[id] = socket;
        _sendLocks[id] = new SemaphoreSlim(1, 1);
        try
        {
            string state;
            lock (_gate) state = _lastState;
            await SendClientAsync(id, socket, state, cancellationToken);
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(id, out _);
            _sendLocks.TryRemove(id, out _);
            try { if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); } catch { }
            socket.Dispose();
        }
    }

    private async Task HandleAssetAsync(HttpListenerContext context, string relative, CancellationToken cancellationToken)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Regex.IsMatch(parts[1], "^[a-zA-Z0-9_]+$"))
        {
            context.Response.StatusCode = 404; context.Response.Close(); return;
        }
        var (kind, id) = (parts[0], parts[1]);
        string[] candidates = kind switch
        {
            "hero" => new[] {
                Path.Combine(AppPaths.Assets, "heroes", "portraits", id.Replace("npc_dota_hero_", "", StringComparison.OrdinalIgnoreCase) + ".png"),
                Path.Combine(AppPaths.Assets, "heroes", "legacy", id.Replace("npc_dota_hero_", "", StringComparison.OrdinalIgnoreCase) + "_lg.png"),
                Path.Combine(AppPaths.Assets, "heroes", "icons", id.Replace("npc_dota_hero_", "", StringComparison.OrdinalIgnoreCase) + ".png"),
                Path.Combine(AppPaths.Assets, "heroes", "legacy", id.Replace("npc_dota_hero_", "", StringComparison.OrdinalIgnoreCase) + "_icon.png") },
            "ability" => new[] {
                Path.Combine(AppPaths.Assets, "abilities", "modern", id + ".png"),
                Path.Combine(AppPaths.Assets, "abilities", "legacy", id + "_lg.png"),
                Path.Combine(AppPaths.Assets, "abilities", "legacy", id + "_md.png") },
            _ => Array.Empty<string>()
        };
        var file = candidates.FirstOrDefault(File.Exists);
        if (file is null) { context.Response.StatusCode = 404; context.Response.Close(); return; }
        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        context.Response.ContentType = "image/png";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers["Cache-Control"] = "no-cache";
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        string state;
        lock (_gate) state = _lastState;
        foreach (var pair in _clients)
        {
            if (pair.Value.State != WebSocketState.Open) { _clients.TryRemove(pair.Key, out _); continue; }
            try { await SendClientAsync(pair.Key, pair.Value, state, cancellationToken); }
            catch { _clients.TryRemove(pair.Key, out _); }
        }
    }

    private async Task SendClientAsync(Guid id, WebSocket socket, string message, CancellationToken cancellationToken)
    {
        if (!_sendLocks.TryGetValue(id, out var gate)) return;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string value, string contentType, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-cache";
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static string BuildState(DraftSession? session, OverlaySettings settings)
    {
        object Player(int number)
        {
            var draft = session?.Players.FirstOrDefault(p => p.Player == number);
            var hero = draft?.Picks.FirstOrDefault(p => p.Type == SlotType.Hero)?.Id;
            var abilities = draft?.AbilitySlots.Take(4).Select(id => id).ToArray() ?? Array.Empty<string?>();
            return new { hero, abilities };
        }

        var state = new
        {
            visible = session is not null,
            radiant = Enumerable.Range(1, 5).Select(i => Player(i * 2 - 1)),
            dire = Enumerable.Range(1, 5).Select(i => Player(i * 2)),
            settings = new
            {
                iconSize = settings.IconSize,
                heroWidth = Math.Round(settings.IconSize * 16.0 / 9.0),
                rowGap = settings.PlayerRowGap,
                heroGap = settings.HeroGap,
                abilityGap = settings.AbilityGap,
                teamGap = settings.TeamGap,
                horizontal = settings.HorizontalLayout,
                showNames = settings.ShowTeamNames,
                round = settings.RoundIconBorders,
                contentOpacity = settings.ContentOpacityPercent / 100.0,
                background = settings.OverlayColorHex,
                backgroundOpacity = settings.OverlayOpacityPercent / 100.0,
                borderColor = settings.IconBorderColorHex,
                borderSize = settings.IconBorderThickness
            },
            assetBase = "/asset/"
        };
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    private async Task StopAsync()
    {
        HttpListener? listener; CancellationTokenSource? shutdown; Task? accept;
        lock (_gate)
        {
            listener = _listener; _listener = null;
            shutdown = _shutdown; _shutdown = null;
            accept = _acceptTask; _acceptTask = null;
        }
        if (shutdown is not null) { shutdown.Cancel(); shutdown.Dispose(); }
        try { listener?.Stop(); listener?.Close(); } catch { }
        if (accept is not null) try { await accept; } catch { }
        foreach (var client in _clients.Values)
        {
            try { client.Abort(); client.Dispose(); } catch { }
        }
        _clients.Clear();
        _sendLocks.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private const string Html = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/overlay.css"></head><body><main id="overlay" class="hidden"><section class="team radiant"><div class="team-name">RADIANT</div><div class="players" id="radiant"></div></section><section class="team dire"><div class="team-name">DIRE</div><div class="players" id="dire"></div></section></main><script src="/overlay.js"></script></body></html>
""";

    private const string Css = """
*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;background:transparent;overflow:hidden;font-family:Segoe UI,Arial,sans-serif}#overlay{display:flex;gap:var(--team-gap);padding:6px;background:var(--background);opacity:1;transform-origin:top left}.hidden{display:none!important}.team{display:flex;flex-direction:column;gap:5px}.team-name{opacity:var(--content-opacity);color:#fff;font-size:12px;font-weight:700;letter-spacing:1px;text-shadow:0 1px 3px #000}.players{display:flex;flex-direction:column;gap:var(--row-gap)}.player{display:flex;align-items:center;gap:var(--hero-gap);opacity:var(--content-opacity)}.hero{width:var(--hero-width);height:var(--icon-size);object-fit:cover}.abilities{display:flex;gap:var(--ability-gap)}img{display:block;object-fit:cover;border:var(--border-size) solid var(--border-color);border-radius:var(--radius);background:rgba(20,24,30,.45)}.ability{width:var(--icon-size);height:var(--icon-size)}#overlay.horizontal{flex-direction:row}.horizontal .players{flex-direction:column}.horizontal .team{min-width:0}
""";

    private const string JavaScript = """
(() => {const root=document.getElementById('overlay');let socket;let latest=null;function image(kind,id,base,cls){if(!id)return null;const img=document.createElement('img');img.className=cls;img.src=base+kind+'/'+encodeURIComponent(id);img.onerror=()=>{img.style.visibility='hidden'};return img;}function render(s){latest=s;if(!s.visible){root.classList.add('hidden');return;}root.classList.remove('hidden');const x=s.settings;root.classList.toggle('horizontal',!!x.horizontal);root.style.setProperty('--icon-size',x.iconSize+'px');root.style.setProperty('--hero-width',x.heroWidth+'px');root.style.setProperty('--row-gap',x.rowGap+'px');root.style.setProperty('--hero-gap',x.heroGap+'px');root.style.setProperty('--ability-gap',x.abilityGap+'px');root.style.setProperty('--team-gap',x.teamGap+'px');root.style.setProperty('--content-opacity',x.contentOpacity);root.style.setProperty('--background',hexRgba(x.background,x.backgroundOpacity));root.style.setProperty('--border-color',x.borderColor);root.style.setProperty('--border-size',x.borderSize+'px');root.style.setProperty('--radius',x.round?'3px':'0');document.querySelectorAll('.team-name').forEach(n=>n.style.display=x.showNames?'':'none');for(const [id,players] of [['radiant',s.radiant],['dire',s.dire]]){const host=document.getElementById(id);host.replaceChildren();for(const p of players){const row=document.createElement('div');row.className='player';const hero=image('hero',p.hero,s.assetBase,'hero');if(hero)row.append(hero);const abilities=document.createElement('div');abilities.className='abilities';for(const a of (p.abilities||[]).slice(0,4)){const icon=image('ability',a,s.assetBase,'ability');if(icon)abilities.append(icon);}row.append(abilities);host.append(row);}}}function hexRgba(hex,a){let h=(hex||'#171A21').replace('#','');if(h.length===8)h=h.slice(2);const n=parseInt(h,16)||0;return `rgba(${n>>16&255},${n>>8&255},${n&255},${Math.max(0,Math.min(1,a))})`;}function connect(){const scheme=location.protocol==='https:'?'wss:':'ws:';socket=new WebSocket(`${scheme}//${location.host}/ws`);socket.onmessage=e=>{try{render(JSON.parse(e.data))}catch{}};socket.onclose=()=>setTimeout(connect,1500);socket.onerror=()=>socket.close();}connect();})();
""";
}
