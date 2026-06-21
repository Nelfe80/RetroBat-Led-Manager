using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LedManager.Core.Configuration;
using LedManager.Core.Effects;
using LedManager.Core.Routing;
using LedManager.Core.Runtime;

var app = new LedManagerApp();
return await app.RunAsync(args);

internal sealed class LedManagerApp
{
    private const int IngameIdleRestoreDelayMs = 2000;
    private const int IngameIdleRestoreRetryDelayMs = 350;
    private const int IngameIdleRestoreMaxDeferMs = 3500;
    private const int GameStartPanelSettleDelayMs = 80;
    private const long PanelSequenceResetDropThreshold = 10;

    private LedManagerConfig _config = new();
    private CommandRouter _router = null!;
    private SenderHost _senders = null!;
    private DefaultEffectCatalog _effectCatalog = DefaultEffectCatalog.Empty;
    private readonly IngamePanelOverlay _ingamePanel = new();
    private readonly object _ingameIdleRestoreLock = new();
    private readonly object _arcadeOutputLock = new();
    private readonly Dictionary<string, ArcadeOutputState> _arcadeOutputStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedArcadeOutputMappings = new(StringComparer.OrdinalIgnoreCase);
    private PanelState? _currentPanel;
    private CancellationTokenSource? _ingameIdleRestoreCts;
    private long _ingameIdleRestoreVersion;
    private long _arcadeOutputVersion;
    private long _lastPanelSequence;
    private int _activeEffectSequences;

    public async Task<int> RunAsync(string[] args)
    {
        if (!HasFlag(args, "--no-kill-previous"))
        {
            StopPreviousLedManagerInstances();
        }

        var iniPath = GetOption(args, "--ini") ?? "LedManager.ini";
        _config = LedManagerConfig.Load(iniPath);
        _router = new CommandRouter(_config);
        _effectCatalog = _config.Effects.Enabled ? DefaultEffectCatalog.Load(_config.Effects.CatalogPath, _config.Hardware) : DefaultEffectCatalog.Empty;
        _senders = new SenderHost(_config);

        Console.WriteLine($"[startup] ini={Path.GetFullPath(iniPath)} senders={_config.Senders.Count} apiExpose={_config.ApiExpose.Enabled} effects={_config.Effects.Enabled}");
        Console.Out.Flush();
        try
        {
            await _senders.StartAsync();

            var eventFile = GetOption(args, "--event-file");
            if (!string.IsNullOrWhiteSpace(eventFile))
            {
                var eventDelayMs = GetIntOption(args, "--event-delay-ms", 0);
                Console.WriteLine($"[startup] replay events={Path.GetFullPath(eventFile)}");
                foreach (var line in File.ReadLines(eventFile))
                {
                    await HandleJsonAsync(line);
                    if (eventDelayMs > 0)
                    {
                        await Task.Delay(eventDelayMs);
                    }
                }

                return 0;
            }

            if (_config.ApiExpose.Enabled)
            {
                await RunWebSocketsAsync();
                return 0;
            }

            Console.WriteLine("LedManager ready. Paste one JSON event per line, Ctrl+C to stop.");
            while (Console.ReadLine() is { } line)
            {
                await HandleJsonAsync(line);
            }

            return 0;
        }
        finally
        {
            CancelIngameIdleSystemPanelRestore();
            await _senders.StopAsync();
        }
    }

    private async Task RunWebSocketsAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var streams = new Dictionary<string, string>
        {
            ["frontend"] = _config.ApiExpose.FrontendPath,
            ["panel"] = _config.ApiExpose.PanelPath,
            ["ingame"] = _config.ApiExpose.IngamePath,
            ["arcade"] = _config.ApiExpose.ArcadePath,
            ["hiscore"] = _config.ApiExpose.HiscorePath
        };

        var tasks = streams.Select(s => ListenWebSocketAsync(s.Key, _config.ApiExpose.BuildUri(s.Value), cts.Token));
        await Task.WhenAll(tasks);
    }

    private async Task ListenWebSocketAsync(string stream, Uri uri, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(uri, cancellationToken);
                Console.WriteLine($"[ws:{stream}] connected {uri}");
                if (stream.Equals("panel", StringComparison.OrdinalIgnoreCase))
                {
                    _lastPanelSequence = 0;
                    Console.WriteLine("[panel] sequence guard reset after websocket connect");
                }

                var buffer = new byte[64 * 1024];
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        await HandleJsonAsync(Encoding.UTF8.GetString(ms.ToArray()), stream);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ws:{stream}] {ex.Message}");
                await Task.Delay(1500, cancellationToken);
            }
        }
    }

    private async Task HandleJsonAsync(string json, string? forcedStream = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        LedEvent evt;
        try
        {
            evt = LedEvent.FromJson(json);
            if (!string.IsNullOrWhiteSpace(forcedStream) && string.IsNullOrWhiteSpace(evt.Stream))
            {
                evt = CloneWithStream(evt, forcedStream);
            }

            // Live APIExpose mem.action signals carry no player id; route them to the
            // configured default player so they reach a real (non dry-run) sender.
            if (evt.Player is null && evt.Stream.Equals("mem", StringComparison.OrdinalIgnoreCase))
            {
                evt = CloneWithPlayer(evt, _config.ApiExpose.DefaultPlayer);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[event] invalid JSON: {ex.Message}");
            return;
        }

        if (evt.IsPanelState)
        {
            var nextPanel = PanelState.FromPanelEvent(evt).EnrichFromDynpanel(_config.BaseDirectory);
            if (nextPanel.Sequence > 0 && nextPanel.Sequence <= _lastPanelSequence)
            {
                if (LooksLikePanelSequenceReset(nextPanel.Sequence))
                {
                    Console.WriteLine($"[panel] sequence reset accepted sequence={nextPanel.Sequence} previous={_lastPanelSequence}");
                    _lastPanelSequence = 0;
                }
                else
                {
                    Console.WriteLine($"[panel] stale ignored sequence={nextPanel.Sequence} last={_lastPanelSequence}");
                    return;
                }
            }

            if (nextPanel.Sequence > 0)
            {
                _lastPanelSequence = nextPanel.Sequence;
            }

            _currentPanel = nextPanel;
            Console.WriteLine($"[panel] current panel={_currentPanel.PanelId ?? "unknown"} layout={_currentPanel.LayoutId ?? "unknown"} sequence={_currentPanel.Sequence} outputs={_currentPanel.Outputs.Count}");
            await DispatchAsync(_router.RoutePanelState(_currentPanel));
            return;
        }

        if (evt.IsFrontendGameStart)
        {
            await Task.Delay(GameStartPanelSettleDelayMs);
            if (_currentPanel is not null && !_currentPanel.IsEmpty)
            {
                ClearArcadeOutputStates("game-start");
                _currentPanel.Save(_config.PanelSnapshotPath);
                _ingamePanel.Begin(_currentPanel);
                Console.WriteLine($"[frontend] game start: panel snapshot saved to {_config.PanelSnapshotPath}");
                ScheduleIngameIdleSystemPanelRestore("game-start");
            }
            else
            {
                CancelIngameIdleSystemPanelRestore();
                _ingamePanel.Clear();
                Console.WriteLine("[frontend] game start: no panel snapshot available yet");
            }

            return;
        }

        if (evt.IsFrontendGameEnd)
        {
            if (_config.RestorePanelOnGameEnd)
            {
                var snapshot = PanelState.Load(_config.PanelSnapshotPath);
                if (snapshot is not null && !snapshot.IsEmpty)
                {
                    Console.WriteLine("[frontend] game end: restoring panel snapshot");
                    await DispatchAsync(_router.RoutePanelState(snapshot));
                }
            }

            _ingamePanel.Clear();
            ClearArcadeOutputStates("game-end");
            CancelIngameIdleSystemPanelRestore();
            return;
        }

        if (evt.IsHiscore)
        {
            Console.WriteLine($"[hiscore] observed durable score event {evt.Type}; not treated as live matrix score");
            return;
        }

        if (await TryHandleMameOutputAsync(evt))
        {
            return;
        }

        var effectEvents = _effectCatalog.Resolve(evt);
        if (effectEvents.Count > 0)
        {
            Console.WriteLine($"[effects] action={evt.Action ?? "-"} family={evt.Family ?? "-"} expanded={effectEvents.Count}");
            // Play the (possibly multi-step, delayed) effect sequence in the background so
            // it never blocks this stream's receive loop - live events must keep flowing.
            _ = PlayEffectSequenceAsync(effectEvents);
            return;
        }

        await DispatchIngameAwareEventAsync(evt);
    }

    private bool LooksLikePanelSequenceReset(long sequence)
    {
        return _lastPanelSequence > 0 &&
            sequence > 0 &&
            sequence < _lastPanelSequence &&
            _lastPanelSequence - sequence >= PanelSequenceResetDropThreshold;
    }

    private async Task PlayEffectSequenceAsync(IReadOnlyList<LedEvent> effectEvents)
    {
        Interlocked.Increment(ref _activeEffectSequences);
        try
        {
            foreach (var effectEvent in effectEvents)
            {
                try
                {
                    await DispatchIngameAwareEventAsync(effectEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[effects] dispatch error: {ex.Message}");
                }

                if (effectEvent.DelayMs > 0)
                {
                    await Task.Delay(effectEvent.DelayMs);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeEffectSequences);
            ScheduleIngameIdleSystemPanelRestore("effect-complete");
        }
    }

    private async Task<bool> TryHandleMameOutputAsync(LedEvent evt)
    {
        if (evt.Type.Equals("mame.session.started", StringComparison.OrdinalIgnoreCase))
        {
            ClearArcadeOutputStates("mame-session-start");
            ClearArcadeOutputMappingDiagnostics();
            Console.WriteLine($"[mame-output] session started machine={ReadMachineName(evt.Payload) ?? "-"}");
            return true;
        }

        if (!evt.Type.Equals("mame.output.changed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var referencePanel = _ingamePanel.IsActive ? _ingamePanel.BasePanel : _currentPanel;
        if (referencePanel is null || referencePanel.IsEmpty)
        {
            Console.WriteLine("[mame-output] ignored: no current panel");
            return true;
        }

        foreach (var signal in ReadMameSignals(evt.Payload))
        {
            var candidates = (_ingamePanel.IsActive
                    ? _ingamePanel.FindBaseOutputSignals(signal.Key)
                    : referencePanel.FindOutputSignals(signal.Key))
                .ToArray();
            var outputs = candidates
                .Where(output => output.Slot is >= 1 and <= 8)
                .GroupBy(output => output.Slot!.Value)
                .Select(group => group.First())
                .OrderBy(output => output.Slot)
                .ToArray();

            if (outputs.Length == 0)
            {
                ReportUnmappedMameOutput(referencePanel, signal.Key, signal.Value, candidates);
                continue;
            }

            SetArcadeOutputState(signal.Key, signal.Value);
            var mode = signal.Value != 0 ? "lamp-on" : "lamp-off";

            Console.WriteLine($"[mame-output] signal={signal.Key} value={signal.Value} -> slots={string.Join(",", outputs.Select(output => output.Slot))} mode={mode}");
            foreach (var output in outputs)
            {
                var slot = output.Slot!.Value;
                var color = signal.Value != 0 ? output.Color : "BLACK";
                await DispatchAsync(_router.RouteEvent(CloneForSlot(evt, output.Player ?? _config.ApiExpose.DefaultPlayer, slot, color)));
            }
        }

        return true;
    }

    private void ReportUnmappedMameOutput(PanelState referencePanel, string signal, int value, IReadOnlyList<PanelOutput> candidates)
    {
        var diagnosticKey = $"{referencePanel.Rom ?? "-"}:{signal}";
        lock (_arcadeOutputLock)
        {
            if (!_reportedArcadeOutputMappings.Add(diagnosticKey))
            {
                return;
            }
        }

        if (candidates.Count > 0)
        {
            var knownWithoutSlot = string.Join(",", candidates.Select(DescribePanelOutputSignal).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            Console.WriteLine($"[mame-output] known-without-panel-slot signal={signal} value={value} candidates={knownWithoutSlot}");
            return;
        }

        var knownSignals = string.Join(",", referencePanel.Outputs
            .Where(output => !string.IsNullOrWhiteSpace(output.Id) || !string.IsNullOrWhiteSpace(output.Name) || !string.IsNullOrWhiteSpace(output.OutputName))
            .Select(DescribePanelOutputSignal)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .Take(24));
        Console.WriteLine($"[mame-output] unmapped signal={signal} value={value} known={knownSignals}");
    }

    private static string DescribePanelOutputSignal(PanelOutput output)
    {
        var name = output.OutputName ?? output.Name ?? output.Id ?? output.Function ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        return output.Slot.HasValue ? $"{name}->slot{output.Slot.Value}" : $"{name}->no-slot";
    }

    private async Task DispatchIngameAwareEventAsync(LedEvent evt)
    {
        if (_ingamePanel.IsActive && ShouldResetButtonPanelIdleTimer(evt))
        {
            ScheduleIngameIdleSystemPanelRestore("button-activity");
        }

        if (_ingamePanel.IsActive && IngamePanelOverlay.IsOffColor(evt.Color))
        {
            if (IngamePanelOverlay.IsAllTarget(evt))
            {
                var fullRestore = _ingamePanel.PlanFullRestore();
                if (fullRestore.ShouldSend && fullRestore.Panel is not null)
                {
                    Console.WriteLine($"[ingame] full base panel restore sent targeted=true reason={fullRestore.Reason} hash={fullRestore.Fingerprint}");
                    await DispatchSnapshotButtonSlotsAsync(fullRestore.Panel, bypassStateDedupe: true);
                    return;
                }

                Console.WriteLine($"[ingame] full base panel restore skipped reason={fullRestore.Reason} hash={fullRestore.Fingerprint}");
                return;
            }

            var restoredOutput = _ingamePanel.FindEffectiveOutputForEvent(evt);
            if (restoredOutput is not null)
            {
                Console.WriteLine($"[ingame] restore target={evt.Target ?? evt.Slot?.ToString() ?? "-"} color={restoredOutput.Color}");
                await DispatchAsync(_router.RouteEvent(CloneForOutput(evt, restoredOutput)));
                return;
            }
        }

        await DispatchAsync(_router.RouteEvent(evt));
    }

    private void ScheduleIngameIdleSystemPanelRestore(string reason)
    {
        if (!_ingamePanel.IsActive || _ingamePanel.BasePanel is null)
        {
            return;
        }

        lock (_ingameIdleRestoreLock)
        {
            if (_ingameIdleRestoreCts is not null && !_ingameIdleRestoreCts.IsCancellationRequested)
            {
                return;
            }
        }

        StartIngameIdleSystemPanelRestoreTimer(reason, IngameIdleRestoreDelayMs, Environment.TickCount64, replaceExisting: false);
    }

    private void StartIngameIdleSystemPanelRestoreTimer(string reason, int delayMs, long firstScheduledAtTicks, bool replaceExisting)
    {
        if (!_ingamePanel.IsActive || _ingamePanel.BasePanel is null)
        {
            return;
        }

        CancellationTokenSource cts;
        long version;
        lock (_ingameIdleRestoreLock)
        {
            if (_ingameIdleRestoreCts is not null && !_ingameIdleRestoreCts.IsCancellationRequested)
            {
                if (!replaceExisting)
                {
                    return;
                }

                _ingameIdleRestoreCts.Cancel();
            }

            cts = new CancellationTokenSource();
            _ingameIdleRestoreCts = cts;
            version = ++_ingameIdleRestoreVersion;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);

                if (Interlocked.Read(ref _ingameIdleRestoreVersion) != version ||
                    !_ingamePanel.IsActive ||
                    _ingamePanel.BasePanel is not { } basePanel)
                {
                    return;
                }

                var buttonPanel = BuildSnapshotButtonPanel(basePanel);
                if (buttonPanel.IsEmpty)
                {
                    return;
                }

                var activeEffects = Volatile.Read(ref _activeEffectSequences);
                var elapsedMs = Environment.TickCount64 - firstScheduledAtTicks;
                if (activeEffects > 0 && elapsedMs < IngameIdleRestoreMaxDeferMs)
                {
                    Console.WriteLine($"[ingame] idle restore deferred reason={reason} activeEffects={activeEffects} elapsed={elapsedMs}ms");
                    StartIngameIdleSystemPanelRestoreTimer(reason, IngameIdleRestoreRetryDelayMs, firstScheduledAtTicks, replaceExisting: true);
                    return;
                }

                if (activeEffects > 0)
                {
                    Console.WriteLine($"[ingame] idle restore forced after max defer reason={reason} activeEffects={activeEffects} elapsed={elapsedMs}ms");
                }

                Console.WriteLine($"[ingame] idle restore system panel slots targeted=true after {elapsedMs}ms reason={reason} outputs={buttonPanel.Outputs.Count}");
                await DispatchSnapshotButtonSlotsAsync(buttonPanel, bypassStateDedupe: true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ingame] idle restore error: {ex.Message}");
            }
            finally
            {
                lock (_ingameIdleRestoreLock)
                {
                    if (ReferenceEquals(_ingameIdleRestoreCts, cts))
                    {
                        _ingameIdleRestoreCts = null;
                    }
                }

                cts.Dispose();
            }
        });
    }

    private static bool ShouldResetButtonPanelIdleTimer(LedEvent evt)
    {
        if (IngamePanelOverlay.IsAllTarget(evt))
        {
            return true;
        }

        if (evt.Slot is >= 1 and <= 8)
        {
            return true;
        }

        if (TryParseSlotTarget(evt.Target) is >= 1 and <= 8)
        {
            return true;
        }

        return false;
    }

    private static int? TryParseSlotTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var normalized = target.Trim();
        if (normalized.Length > 3 && (normalized[0] == 'P' || normalized[0] == 'p') && char.IsDigit(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["SLOT:".Length..];
        }

        return int.TryParse(normalized, out var slot) ? slot : null;
    }

    private void CancelIngameIdleSystemPanelRestore()
    {
        lock (_ingameIdleRestoreLock)
        {
            _ingameIdleRestoreCts?.Cancel();
            _ingameIdleRestoreCts = null;
            _ingameIdleRestoreVersion++;
        }
    }

    private async Task DispatchAsync(IEnumerable<RoutedCommand> commands, bool bypassStateDedupe = false)
    {
        foreach (var command in commands)
        {
            await _senders.SendAsync(command, bypassStateDedupe);
        }
    }

    private async Task DispatchSnapshotButtonSlotsAsync(PanelState panel, bool bypassStateDedupe)
    {
        foreach (var output in panel.Outputs.Where(IsSnapshotButtonOutput).OrderBy(output => output.Slot))
        {
            if (output.Slot is not int slot)
            {
                continue;
            }

            var evt = new LedEvent
            {
                Type = "ledmanager.snapshot.restore",
                Stream = "ingame",
                Player = output.Player ?? _config.ApiExpose.DefaultPlayer,
                Target = $"SLOT:{slot}",
                Slot = slot,
                Color = output.Color
            };

            await DispatchAsync(_router.RouteEvent(evt), bypassStateDedupe);
        }
    }

    private PanelState BuildSnapshotButtonPanel(PanelState panel)
    {
        return new PanelState
        {
            System = panel.System,
            Rom = panel.Rom,
            PanelId = panel.PanelId,
            LayoutId = panel.LayoutId,
            Sequence = panel.Sequence,
            CapturedAt = panel.CapturedAt,
            Outputs = panel.Outputs
                .Where(IsSnapshotButtonOutput)
                .OrderBy(output => output.Slot)
                .Select(output => ApplyArcadeOutputState(panel, output))
                .ToArray()
        };
    }

    private PanelOutput ApplyArcadeOutputState(PanelState referencePanel, PanelOutput output)
    {
        if (output.Slot is not int slot)
        {
            return output;
        }

        var states = SnapshotArcadeOutputStates();
        ArcadeOutputState? latestState = null;
        PanelOutput? latestMappedOutput = null;
        foreach (var state in states)
        {
            foreach (var mappedOutput in referencePanel.FindOutputSignals(state.Key).Where(output => output.Slot == slot))
            {
                if (latestState is null || state.Version > latestState.Value.Version)
                {
                    latestState = state;
                    latestMappedOutput = mappedOutput;
                }
            }
        }

        if (latestState is null)
        {
            return output;
        }

        var color = latestState.Value.Value != 0
            ? latestMappedOutput?.Color ?? output.Color
            : "BLACK";
        return CopyWithColor(output, color);
    }

    private IReadOnlyList<ArcadeOutputState> SnapshotArcadeOutputStates()
    {
        lock (_arcadeOutputLock)
        {
            return _arcadeOutputStates.Values.ToArray();
        }
    }

    private void SetArcadeOutputState(string key, int value)
    {
        lock (_arcadeOutputLock)
        {
            _arcadeOutputStates[key] = new ArcadeOutputState(key, value, ++_arcadeOutputVersion);
        }
    }

    private void ClearArcadeOutputStates(string reason)
    {
        lock (_arcadeOutputLock)
        {
            if (_arcadeOutputStates.Count == 0)
            {
                return;
            }

            _arcadeOutputStates.Clear();
            _arcadeOutputVersion++;
        }

        Console.WriteLine($"[mame-output] state cleared reason={reason}");
    }

    private void ClearArcadeOutputMappingDiagnostics()
    {
        lock (_arcadeOutputLock)
        {
            _reportedArcadeOutputMappings.Clear();
        }
    }

    private static bool IsSnapshotButtonOutput(PanelOutput output)
    {
        if (output.Slot is not (>= 1 and <= 8))
        {
            return false;
        }

        return !IsStartOrSelect(output.Target) &&
            !IsStartOrSelect(output.Id) &&
            !IsStartOrSelect(output.Name) &&
            !IsStartOrSelect(output.Function) &&
            !IsStartOrSelect(output.OutputName);
    }

    private static bool IsStartOrSelect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalized is "START" or "SELECT" or "P1START" or "P1SELECT" or "PLAYER1START" or "PLAYER1SELECT";
    }

    private static PanelOutput CopyWithColor(PanelOutput output, string color) => new()
    {
        Player = output.Player,
        Slot = output.Slot,
        Target = output.Target,
        Color = color,
        Id = output.Id,
        Name = output.Name,
        Function = output.Function,
        OutputName = output.OutputName
    };

    private static LedEvent CloneWithPlayer(LedEvent evt, int player)
    {
        return new LedEvent
        {
            Type = evt.Type,
            Stream = evt.Stream,
            Player = player,
            Sender = evt.Sender,
            Action = evt.Action,
            Family = evt.Family,
            Effect = evt.Effect,
            Target = evt.Target,
            Slot = evt.Slot,
            Color = evt.Color,
            State = evt.State,
            Value = evt.Value,
            Text = evt.Text,
            System = evt.System,
            Rom = evt.Rom,
            DelayMs = evt.DelayMs,
            FlashMs = evt.FlashMs,
            Payload = evt.Payload
        };
    }

    private static LedEvent CloneForOutput(LedEvent evt, PanelOutput output)
    {
        return new LedEvent
        {
            Type = evt.Type,
            Stream = evt.Stream,
            Player = output.Player ?? evt.Player,
            Sender = evt.Sender,
            Action = evt.Action,
            Family = evt.Family,
            Effect = evt.Effect,
            Target = output.Slot.HasValue ? $"SLOT:{output.Slot.Value}" : output.Target ?? evt.Target,
            Slot = output.Slot ?? evt.Slot,
            Color = output.Color,
            State = evt.State,
            Value = evt.Value,
            Text = evt.Text,
            System = evt.System,
            Rom = evt.Rom,
            DelayMs = evt.DelayMs,
            FlashMs = evt.FlashMs,
            Payload = evt.Payload
        };
    }

    private static LedEvent CloneWithStream(LedEvent evt, string stream)
    {
        return new LedEvent
        {
            Type = evt.Type,
            Stream = stream,
            Player = evt.Player,
            Sender = evt.Sender,
            Action = evt.Action,
            Family = evt.Family,
            Effect = evt.Effect,
            Target = evt.Target,
            Slot = evt.Slot,
            Color = evt.Color,
            State = evt.State,
            Value = evt.Value,
            Text = evt.Text,
            System = evt.System,
            Rom = evt.Rom,
            DelayMs = evt.DelayMs,
            FlashMs = evt.FlashMs,
            Payload = evt.Payload
        };
    }

    private static LedEvent CloneForSlot(LedEvent evt, int player, int slot, string color)
    {
        return new LedEvent
        {
            Type = evt.Type,
            Stream = evt.Stream,
            Player = player,
            Sender = evt.Sender,
            Action = evt.Action,
            Family = evt.Family,
            Effect = evt.Effect,
            Target = $"SLOT:{slot}",
            Slot = slot,
            Color = color,
            State = evt.State,
            Value = evt.Value,
            Text = evt.Text,
            System = evt.System,
            Rom = evt.Rom,
            DelayMs = evt.DelayMs,
            FlashMs = evt.FlashMs,
            Payload = evt.Payload
        };
    }

    private static IEnumerable<(string Key, int Value)> ReadMameSignals(JsonElement root)
    {
        var payload = GetObject(root, "Payload") ?? GetObject(root, "payload") ?? root;
        var signals = GetPropertyCaseInsensitive(payload, "Signals");
        if (!signals.HasValue || signals.Value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var signal in signals.Value.EnumerateArray())
        {
            var key = LedEvent.ReadString(signal, "Key") ?? LedEvent.ReadString(signal, "key");
            var value = LedEvent.ReadInt(signal, "Value") ?? LedEvent.ReadInt(signal, "value");
            if (!string.IsNullOrWhiteSpace(key) && value.HasValue)
            {
                yield return (key, value.Value);
            }
        }
    }

    private static string? ReadMachineName(JsonElement root)
    {
        var payload = GetObject(root, "Payload") ?? GetObject(root, "payload") ?? root;
        return LedEvent.ReadString(payload, "MachineName") ??
            LedEvent.ReadString(payload, "machineName");
    }

    private static JsonElement? GetObject(JsonElement root, string property)
    {
        var value = GetPropertyCaseInsensitive(root, property);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static JsonElement? GetPropertyCaseInsensitive(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var item in root.EnumerateObject())
        {
            if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetIntOption(string[] args, string name, int fallback)
    {
        var value = GetOption(args, name);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static void StopPreviousLedManagerInstances()
    {
        var currentProcess = Process.GetCurrentProcess();
        var root = FindLedManagerRoot();
        var stopped = 0;

        foreach (var process in Process.GetProcessesByName("LedManager"))
        {
            using (process)
            {
                if (process.Id == currentProcess.Id)
                {
                    continue;
                }

                if (!TryGetProcessPath(process, out var processPath) || !IsUnderDirectory(processPath, root))
                {
                    continue;
                }

                try
                {
                    Console.WriteLine($"[startup] stopping previous LedManager pid={process.Id} path={processPath}");
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                    {
                        Console.Error.WriteLine($"[startup] previous LedManager pid={process.Id} did not exit after kill");
                    }

                    stopped++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[startup] failed to stop previous LedManager pid={process.Id}: {ex.Message}");
                }
            }
        }

        if (stopped > 0)
        {
            Console.WriteLine($"[startup] stopped previous LedManager instances={stopped}");
        }

        StopPreviousSenderProcesses(root);
    }

    private static void StopPreviousSenderProcesses(string root)
    {
        var stopped = 0;
        foreach (var process in Process.GetProcessesByName("PicoCommandSender"))
        {
            using (process)
            {
                if (!TryGetProcessPath(process, out var processPath) || !IsUnderDirectory(processPath, root))
                {
                    continue;
                }

                try
                {
                    Console.WriteLine($"[startup] stopping previous PicoCommandSender pid={process.Id} path={processPath}");
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                    {
                        Console.Error.WriteLine($"[startup] previous PicoCommandSender pid={process.Id} did not exit after kill");
                    }

                    stopped++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[startup] failed to stop previous PicoCommandSender pid={process.Id}: {ex.Message}");
                }
            }
        }

        if (stopped > 0)
        {
            Console.WriteLine($"[startup] stopped previous PicoCommandSender instances={stopped}");
        }
    }

    private static string FindLedManagerRoot()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var directory = baseDirectory; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LedManager.ini")))
            {
                return directory.FullName;
            }
        }

        return baseDirectory.FullName;
    }

    private static bool TryGetProcessPath(Process process, out string path)
    {
        try
        {
            path = process.MainModule?.FileName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            path = string.Empty;
            return false;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class SenderHost
{
    private const int DuplicateCommandWindowMs = 200;
    private const int FullPanelCommandWindowMs = 750;

    private readonly LedManagerConfig _config;
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource> _readySignals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Channel<QueuedCommand>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Channel<RoutedCommand>> _panelQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RoutedCommand> _latestPanelCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _lastOutputStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandDedupeEntry> _lastQueuedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopCts = new();
    private readonly object _stateLock = new();
    private readonly object _queueDedupeLock = new();

    public SenderHost(LedManagerConfig config)
    {
        _config = config;
    }

    public async Task StartAsync()
    {
        var startupTimeoutMs = 0;
        foreach (var sender in _config.Senders.Values.Where(s => s.Enabled))
        {
            EnsureStarted(sender);
            if (!sender.DryRun && sender.UseStdIn)
            {
                startupTimeoutMs = Math.Max(startupTimeoutMs, sender.StartupDelayMs);

                // Commands are queued and paced to match the serial bridge's own throughput;
                // anything that waits too long is dropped so the firmware never falls further behind.
                var queue = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(Math.Max(1, sender.QueueCapacity))
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
                _queues[sender.Id] = queue;

                // Panel-state updates get their own single-slot, drop-oldest channel so a
                // burst of /ws/panel pushes (e.g. fast ES navigation) always converges on the
                // most recent panel instead of draining a backlog of now-stale BATCH commands.
                var panelQueue = Channel.CreateBounded<RoutedCommand>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });
                _panelQueues[sender.Id] = panelQueue;
                _ = Task.Run(() => PumpQueueAsync(sender, queue, panelQueue, _stopCts.Token));
            }
        }

        var pendingReady = _readySignals.Values.Select(t => t.Task).Where(t => !t.IsCompleted).ToArray();
        if (pendingReady.Length > 0)
        {
            Console.WriteLine($"[sender] waiting for {pendingReady.Length} daemon(s) ready, timeout={startupTimeoutMs}ms");
            var allReady = Task.WhenAll(pendingReady);
            var completed = await Task.WhenAny(allReady, Task.Delay(Math.Max(1000, startupTimeoutMs)));
            if (completed != allReady)
            {
                Console.Error.WriteLine("[sender] readiness timeout; continuing with available senders");
            }
        }
    }

    public Task StopAsync()
    {
        _stopCts.Cancel();

        foreach (var queue in _queues.Values)
        {
            queue.Writer.TryComplete();
        }

        foreach (var panelQueue in _panelQueues.Values)
        {
            panelQueue.Writer.TryComplete();
        }

        foreach (var pair in _processes.ToArray())
        {
            StopProcess(pair.Key, pair.Value);
        }

        _processes.Clear();
        return Task.CompletedTask;
    }

    public Task SendAsync(RoutedCommand command, bool bypassStateDedupe = false)
    {
        if (!_config.Senders.TryGetValue(command.SenderId, out var sender) || !sender.Enabled)
        {
            Console.Error.WriteLine($"[route] sender unavailable: {command.SenderId}");
            return Task.CompletedTask;
        }

        command = CoalesceUniformAllButtonBatch(command);
        var latestPanelCommand = command;
        var originalCommand = command.Command;
        if (command.IsPanelUpdate)
        {
            RecordState(command);
            Console.WriteLine($"[route] sender={command.SenderId} player={command.Player?.ToString() ?? "-"} command=\"{command.Command}\"");
            if (sender.DryRun)
            {
                return Task.CompletedTask;
            }

            if (sender.UseStdIn)
            {
                _latestPanelCommands[sender.Id] = latestPanelCommand;
                _panelQueues[sender.Id].Writer.TryWrite(command);
                return Task.CompletedTask;
            }

            return SendOneShotAsync(sender, command);
        }

        var dedupedCommand = command;
        var dedupeReason = "";
        if (!bypassStateDedupe && !TryApplyStateDedupe(command, out dedupedCommand, out dedupeReason))
        {
            Console.WriteLine($"[route] sender={command.SenderId} player={command.Player?.ToString() ?? "-"} skip duplicate reason={dedupeReason} command=\"{originalCommand}\"");
            return Task.CompletedTask;
        }

        if (bypassStateDedupe)
        {
            RecordState(command);
            Console.WriteLine($"[route] sender={command.SenderId} player={command.Player?.ToString() ?? "-"} force=true command=\"{command.Command}\"");
        }
        else
        {
            command = dedupedCommand;
            if (!command.IsPanelUpdate && !TryApplyQueueDedupe(command, out var queueDedupeReason))
            {
                Console.WriteLine($"[route] sender={command.SenderId} player={command.Player?.ToString() ?? "-"} skip duplicate reason={queueDedupeReason} command=\"{command.Command}\"");
                return Task.CompletedTask;
            }

            Console.WriteLine($"[route] sender={command.SenderId} player={command.Player?.ToString() ?? "-"} command=\"{command.Command}\"");
        }

        if (sender.DryRun)
        {
            return Task.CompletedTask;
        }

        if (sender.UseStdIn)
        {
            if (command.IsPanelUpdate)
            {
                _latestPanelCommands[sender.Id] = latestPanelCommand;
                // Capacity-1 drop-oldest: only the latest panel state is ever pending.
                _panelQueues[sender.Id].Writer.TryWrite(command);
            }
            else
            {
                // Non-blocking: the pump task drains this at its own pace and drops stale entries.
                _queues[sender.Id].Writer.TryWrite(new QueuedCommand(command, Environment.TickCount64, bypassStateDedupe));
            }

            return Task.CompletedTask;
        }

        return SendOneShotAsync(sender, command);
    }

    private Task SendOneShotAsync(CommandSenderConfig sender, RoutedCommand command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sender.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _config.BaseDirectory
        };
        foreach (var arg in SplitArgs(sender.Arguments))
        {
            psi.ArgumentList.Add(arg);
        }
        psi.ArgumentList.Add(command.Command);
        using var oneShot = Process.Start(psi);
        oneShot?.WaitForExit(3000);
        return Task.CompletedTask;
    }

    private bool TryApplyStateDedupe(RoutedCommand command, out RoutedCommand dedupedCommand, out string reason)
    {
        dedupedCommand = command;
        reason = "";

        var changes = ParseStateChanges(command.Command).ToArray();
        if (changes.Length == 0)
        {
            return true;
        }

        lock (_stateLock)
        {
            if (!_lastOutputStates.TryGetValue(command.SenderId, out var senderStates))
            {
                senderStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _lastOutputStates[command.SenderId] = senderStates;
            }

            var changedItems = new List<StateChange>();
            foreach (var change in changes)
            {
                if (senderStates.TryGetValue(change.Key, out var previous) &&
                    previous.Equals(change.Color, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changedItems.Add(change);
            }

            if (changedItems.Count == 0)
            {
                reason = "same-state";
                return false;
            }

            foreach (var change in changedItems)
            {
                senderStates[change.Key] = change.Color;
            }

            if (changes.Length != changedItems.Count && IsBatchCommand(command.Command))
            {
                dedupedCommand = command with
                {
                    Command = "BATCH " + string.Join(';', changedItems.Select(change => change.Item))
                };
                reason = "partial-batch";
            }

            return true;
        }
    }

    private void RecordState(RoutedCommand command)
    {
        var changes = ParseStateChanges(command.Command).ToArray();
        if (changes.Length == 0)
        {
            return;
        }

        lock (_stateLock)
        {
            if (!_lastOutputStates.TryGetValue(command.SenderId, out var senderStates))
            {
                senderStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _lastOutputStates[command.SenderId] = senderStates;
            }

            foreach (var change in changes)
            {
                senderStates[change.Key] = change.Color;
            }
        }
    }

    private bool TryApplyQueueDedupe(RoutedCommand command, out string reason)
    {
        reason = "";
        var now = Environment.TickCount64;
        var normalizedCommand = NormalizeCommandForDedupe(command.Command);
        var key = $"{command.SenderId}|{command.Player?.ToString() ?? "-"}";
        var windowMs = IsFullPanelCommand(command.Command) ? FullPanelCommandWindowMs : DuplicateCommandWindowMs;

        lock (_queueDedupeLock)
        {
            if (_lastQueuedCommands.TryGetValue(key, out var previous) &&
                previous.Command.Equals(normalizedCommand, StringComparison.OrdinalIgnoreCase) &&
                now - previous.Ticks < windowMs)
            {
                reason = IsFullPanelCommand(command.Command) ? "same-full-panel-window" : "same-command-window";
                return false;
            }

            _lastQueuedCommands[key] = new CommandDedupeEntry(now, normalizedCommand);
            return true;
        }
    }

    private static IEnumerable<StateChange> ParseStateChanges(string command)
    {
        var trimmed = command.Trim();
        if (TryParseAllButtonState(trimmed, out var allColor))
        {
            for (var slot = 1; slot <= 8; slot++)
            {
                yield return new StateChange("slot:" + slot, allColor, $"SLOT {slot} {allColor}");
            }

            yield break;
        }

        if (trimmed.StartsWith("BATCH ", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in trimmed["BATCH ".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseStateChange(item, out var change))
                {
                    yield return change;
                }
            }

            yield break;
        }

        if (TryParseStateChange(trimmed, out var single))
        {
            yield return single;
        }
    }

    private static RoutedCommand CoalesceUniformAllButtonBatch(RoutedCommand command)
    {
        if (!IsBatchCommand(command.Command))
        {
            return command;
        }

        var changes = ParseStateChanges(command.Command).ToArray();
        if (changes.Length != 8)
        {
            return command;
        }

        var expectedSlots = Enumerable.Range(1, 8).Select(slot => "slot:" + slot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changes.Any(change => !expectedSlots.Remove(change.Key)) || expectedSlots.Count != 0)
        {
            return command;
        }

        var color = changes[0].Color;
        if (changes.Any(change => !change.Color.Equals(color, StringComparison.OrdinalIgnoreCase)))
        {
            return command;
        }

        if (color.Equals("BLACK", StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        return command with { Command = $"ALL {color}" };
    }

    private static bool TryParseAllButtonState(string command, out string color)
    {
        color = "";
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        color = NormalizeRouteToken(parts[1]);
        return color.Length > 0;
    }

    private static bool TryParseStateChange(string command, out StateChange change)
    {
        change = default;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var head = parts[0].ToUpperInvariant();
        if (head is "ALL" or "FLASH" or "MATRIXSCORE" or "MATRIXTEXT" or "CLEAR" or "PING" or "GET" or "HW")
        {
            return false;
        }

        string key;
        string color;
        if ((head is "SLOT" or "SLOTPWM") && parts.Length >= 3)
        {
            key = "slot:" + NormalizeRouteToken(parts[1]);
            color = NormalizeRouteToken(parts[2]);
        }
        else if ((head is "SET" or "SETPWM") && parts.Length >= 3)
        {
            key = KeyForTarget(parts[1]);
            color = NormalizeRouteToken(parts[2]);
        }
        else if (parts.Length >= 2)
        {
            key = KeyForTarget(parts[0]);
            color = NormalizeRouteToken(parts[1]);
        }
        else
        {
            return false;
        }

        change = new StateChange(key, color, command);
        return true;
    }

    private static bool IsBatchCommand(string command) => command.TrimStart().StartsWith("BATCH ", StringComparison.OrdinalIgnoreCase);

    private static bool IsFullPanelCommand(string command)
    {
        var trimmed = command.TrimStart();
        return trimmed.StartsWith("ALL ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("ALLPCT ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommandForDedupe(string command)
    {
        return string.Join(' ', command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();
    }

    private static string KeyForTarget(string target)
    {
        var normalized = NormalizeRouteToken(target);
        return int.TryParse(normalized, out _)
            ? "slot:" + normalized
            : "target:" + normalized;
    }

    private static string NormalizeRouteToken(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length > 3 && (normalized[0] == 'P' || normalized[0] == 'p') && char.IsDigit(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith("GLOBAL:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["GLOBAL:".Length..];
        }

        if (normalized.StartsWith("SLOT:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["SLOT:".Length..];
        }

        return normalized.ToUpperInvariant();
    }

    private async Task PumpQueueAsync(CommandSenderConfig sender, Channel<QueuedCommand> queue, Channel<RoutedCommand> panelQueue, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? command;

            // Panel updates take priority and always reflect the latest /ws/panel push -
            // never stale, so no age check needed.
            if (panelQueue.Reader.TryRead(out var panelCommand))
            {
                command = panelCommand.Command;
            }
            else if (queue.Reader.TryRead(out var item))
            {
                var ageMs = Environment.TickCount64 - item.EnqueuedAtTicks;
                if (!item.BypassStale && sender.MaxQueueAgeMs > 0 && ageMs > sender.MaxQueueAgeMs)
                {
                    Console.WriteLine($"[sender:{sender.Id}] skip stale command (age={ageMs}ms > {sender.MaxQueueAgeMs}ms): \"{item.Command.Command}\"");
                    continue;
                }

                command = item.Command.Command;
            }
            else
            {
                try
                {
                    var panelWait = panelQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    var queueWait = queue.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    await Task.WhenAny(panelWait, queueWait);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            var process = EnsureStarted(sender);
            if (process is null || process.HasExited)
            {
                continue;
            }

            try
            {
                await process.StandardInput.WriteAsync(command + sender.LineEnding);
                await process.StandardInput.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[sender:{sender.Id}] write failed: {ex.Message}");
                _processes.Remove(sender.Id);
                ClearSenderState(sender.Id);
            }

            if (sender.SendIntervalMs > 0)
            {
                try
                {
                    await Task.Delay(sender.SendIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private Process? EnsureStarted(CommandSenderConfig sender)
    {
        if (sender.DryRun)
        {
            Console.WriteLine($"[sender:{sender.Id}] dry-run enabled");
            return null;
        }

        if (!sender.UseStdIn)
        {
            return null;
        }

        if (_processes.TryGetValue(sender.Id, out var existing) && !existing.HasExited)
        {
            return existing;
        }

        var psi = new ProcessStartInfo
        {
            FileName = sender.Executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _config.BaseDirectory
        };

        foreach (var arg in SplitArgs(sender.Arguments))
        {
            psi.ArgumentList.Add(arg);
        }

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start sender {sender.Id}.");
        _processes[sender.Id] = process;
        ClearSenderState(sender.Id);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _readySignals[sender.Id] = ready;
        _ = Task.Run(() => PumpAsync(sender.Id, process.StandardOutput, ready));
        _ = Task.Run(() => PumpAsync(sender.Id, process.StandardError, null));
        Console.WriteLine($"[sender:{sender.Id}] started pid={process.Id}");
        return process;
    }

    private async Task PumpAsync(string senderId, StreamReader reader, TaskCompletionSource? ready)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            Console.WriteLine($"[sender:{senderId}] {line}");
            if (line.Contains($"READY sender={senderId}", StringComparison.OrdinalIgnoreCase))
            {
                ready?.TrySetResult();
                ClearSenderState(senderId);
                if (_latestPanelCommands.TryGetValue(senderId, out var latestPanel) &&
                    _panelQueues.TryGetValue(senderId, out var panelQueue))
                {
                    Console.WriteLine($"[sender:{senderId}] replay latest panel after READY");
                    panelQueue.Writer.TryWrite(latestPanel);
                }
            }
        }
    }

    private static void StopProcess(string senderId, Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            Console.WriteLine($"[sender:{senderId}] stopping pid={process.Id}");
            try
            {
                process.StandardInput.Close();
            }
            catch
            {
            }

            if (!process.WaitForExit(2000))
            {
                Console.Error.WriteLine($"[sender:{senderId}] did not exit after stdin close; killing process tree");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sender:{senderId}] stop failed: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ClearSenderState(string senderId)
    {
        lock (_stateLock)
        {
            _lastOutputStates.Remove(senderId);
        }
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var ch in args)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

internal readonly record struct QueuedCommand(RoutedCommand Command, long EnqueuedAtTicks, bool BypassStale);

internal readonly record struct ArcadeOutputState(string Key, int Value, long Version);

internal readonly record struct StateChange(string Key, string Color, string Item);

internal readonly record struct CommandDedupeEntry(long Ticks, string Command);
