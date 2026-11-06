using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Skills;
using System.Diagnostics;

namespace Skua.Core.Scripts;

public partial class ScriptAuto : ObservableObject, IScriptAuto
{
    public ScriptAuto(
        ILogService logger,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptBoost> boosts,
        Lazy<IScriptOption> options,
        Lazy<IScriptMonster> monsters,
        Lazy<IScriptKill> kill,
        Lazy<IScriptWait> wait,
        Lazy<IScriptCombat> lazyCombat,
        Lazy<IScriptMap> lazyMap)
    {
        _logger = logger;
        _lazyPlayer = player;
        _lazyDrops = drops;
        _lazySkills = skills;
        _lazyBoosts = boosts;
        _lazyOptions = options;
        _lazyMonsters = monsters;
        _lazyKill = kill;
        _lazyWait = wait;
        _lazyCombat = lazyCombat;
        _lazyMap = lazyMap;
        _lastHuntTick = Environment.TickCount;
    }

    private readonly ILogService _logger;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptBoost> _lazyBoosts;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptKill> _lazyKill;
    private readonly Lazy<IScriptMonster> _lazyMonsters;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private readonly Lazy<IScriptMap> _lazyMap;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptBoost Boosts => _lazyBoosts.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptKill Kill => _lazyKill.Value;
    private IScriptMonster Monsters => _lazyMonsters.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptCombat Combat => _lazyCombat.Value;
    private IScriptMap Map => _lazyMap.Value;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _targetInfo;

    public bool IsHunting { get; private set; }

    private int _lastTargetMapID = -1;
    private int _failedAttacks;

    /// <summary>
    /// Attacks a monster in the cell, noting it as the target. Attacks that keep failing while the cell has
    /// living monsters mean the player stands somewhere the game can't fight from (a cell entered without a
    /// valid pad leaves the character "flat" in the middle): re-enter the cell at a valid pad.
    /// </summary>
    private bool AttackNoted(int mapID)
    {
        bool ok = Combat.Attack(mapID);
        AfterAttack(ok,
            () => Monsters.CurrentAvailableMonsters.FirstOrDefault(m => m.MapID == mapID),
            () => Monsters.CurrentAvailableMonsters.Any(m => m.MapID == mapID && m.Alive));
        return ok;
    }

    private bool AttackNoted(string name)
    {
        bool ok = Combat.Attack(name);
        AfterAttack(ok,
            () => Player.Target,
            () => Monsters.CurrentAvailableMonsters.Any(m => m.Alive && (name == "*" || m.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))));
        return ok;
    }

    /// <param name="ok">Whether the attack started.</param>
    /// <param name="target">Reads the engaged monster (only called after a successful attack).</param>
    /// <param name="aliveHere">Whether the monster that couldn't be attacked is alive in this cell; only
    /// those failures count, so waiting for a respawn or skipping other cells never triggers a re-entry.</param>
    private void AfterAttack(bool ok, Func<Monster?> target, Func<bool> aliveHere)
    {
        if (ok)
        {
            _failedAttacks = 0;
            Monster? monster = target();
            if (monster is null || monster.MapID == _lastTargetMapID)
                return;
            _lastTargetMapID = monster.MapID;
            TargetInfo = $"{monster.Name} · MapID {monster.MapID} · MonID {monster.ID} · {monster.Cell}";
            _logger.ScriptLog($"[Auto] Target: {TargetInfo}");
            MonsterIdLog.Record(Map.Name, monster);
            return;
        }

        if (!aliveHere() || ++_failedAttacks < 15)
            return;
        _failedAttacks = 0;
        if (Combat.StopAttacking)
        {
            _logger.ScriptLog("[Auto] Attacks are paused (counter-attack/ultra helper); waiting.");
            return;
        }
        _logger.ScriptLog($"[Auto] Can't attack from here; re-entering {Player.Cell} at a valid pad.");
        Map.Jump(Player.Cell, Player.Pad);
        Thread.Sleep(500);
    }

    private Task? _autoTask;
    private CancellationTokenSource? _ctsAuto;

    public void StartAutoAttack(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        IsHunting = false;
        _DoActionAuto(hunt: false, className, classUseMode, manualMapIDs);
    }

    public void StartAutoHunt(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        IsHunting = true;
        _DoActionAuto(hunt: true, className, classUseMode, manualMapIDs);
    }

    public void Stop()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        _autoTask?.Wait();
        Wait.ForTrue(() => _ctsAuto is null, 20);
        _autoTask?.Dispose();
        IsRunning = false;
    }

    public async ValueTask StopAsync()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        await Wait.ForTrueAsync(() => _ctsAuto is null && (_autoTask?.IsCompleted ?? true), 40);
        _autoTask?.Dispose();
        IsRunning = false;
    }

    private void _DoActionAuto(bool hunt, string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        if (_autoTask is { IsCompleted: false })
            return;

        if (!Player.LoggedIn)
            return;

        CheckDropsandBoosts();
        Options.InfiniteRange = true;
        if (className is not null)
            Skills.StartAdvanced(className, true, classUseMode);
        else
            Skills.StartAdvanced(Player.CurrentClass?.Name ?? "Generic", true);

        _autoTask = Task.Run(async () =>
        {
            try
            {
                if (hunt)
                    await _Hunt(_ctsAuto!.Token, manualMapIDs);
                else
                    await _Attack(_ctsAuto!.Token, manualMapIDs);
            }
            catch { /* ignored */ }
            finally
            {
                Drops.Stop();
                Skills.Stop();
                Boosts.Stop();
                Options.InfiniteRange = false;
                // Leave the fight: re-entering the current cell drops the monsters' aggro, so they stop
                // hitting a player who stopped fighting. Other players see nothing.
                try
                {
                    if (Player.Playing && Player.InCombat)
                        Combat.Exit();
                }
                catch { /* ignored */ }
                _ctsAuto?.Dispose();
                _ctsAuto = null;
                IsRunning = false;
            }
        });
        IsRunning = true;
    }

    private string _target = "";
    private int _targetMapID = -1;
    private int[]? _priorityMapIDs = null;

    private async Task _Attack(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto attack started.");
        _lastTargetMapID = -1;
        _failedAttacks = 0;
        TargetInfo = null;
        Player.SetSpawnPoint();

        _priorityMapIDs = manualMapIDs;

        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = Player.Target?.MapID ?? -1;
        }
        else if (!Combat.StopAttacking)
        {
            _target = "*";
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Attack] Attacking {_target}");

        // Fast path flag for wildcard mode (no manual IDs, no specific target, target is "*")
        bool fastWildcard = (_priorityMapIDs is null || _priorityMapIDs.Length == 0) && _targetMapID <= 0 && (string.IsNullOrEmpty(_target) || _target == "*");

        while (!token.IsCancellationRequested)
        {
            if (_priorityMapIDs?.Length > 0)
            {
                // Sequential priority targeting - always prefer earlier indices if alive
                int? currentTarget = null;

                // Find the highest priority (lowest index) alive monster
                for (int i = 0; i < _priorityMapIDs.Length; i++)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                    bool alive = monster != null && monster.Alive;

                    if (alive)
                    {
                        currentTarget = _priorityMapIDs[i];
                        break;
                    }
                }

                if (currentTarget.HasValue)
                {
                    AttackNoted(currentTarget.Value);
                    Thread.Sleep(200);
                    // Immediately re-evaluate priorities (no extra delay)
                    continue;
                }
                // No valid target found right now, short yield
                Thread.Sleep(50);
            }
            else if (_targetMapID > 0)
            {
                // Attack specific monster by MapID only
                if (Monsters.Exists(_targetMapID))
                {
                    if (AttackNoted(_targetMapID))
                        Kill.Monster(_targetMapID, token);
                }
                Thread.Sleep(Options.ActionDelay);
            }
            else if (_target is not "*" and not "")
            {
                // Target is a player (like yourself)
                Combat.AttackPlayer(_target);
                Thread.Sleep(Options.ActionDelay);
            }
            else
            {
                if (fastWildcard)
                {
                    // Fast wildcard: only alive monsters in this cell, break after first successful engage
                    bool engaged = false;
                    foreach (Monster monster in Monsters.CurrentMonsters)
                    {
                        if (token.IsCancellationRequested)
                            return;
                        if (!monster.Alive)
                            continue;
                        if (!AttackNoted(monster.MapID))
                            continue;
                        Kill.Monster(monster.MapID, token);
                        engaged = true;
                        break;
                    }
                    // Nothing to fight (empty cell, all dead): without a pause this asked the game for
                    // its monster list nonstop, which on Android also slows the game's own frames.
                    if (!engaged)
                        Thread.Sleep(100);
                    continue;
                }

                // Generic wildcard fallback
                List<Monster> monsters = Monsters.CurrentMonsters;
                foreach (Monster monster in monsters)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (!AttackNoted(monster.MapID))
                        continue;

                    Kill.Monster(monster.MapID, token);
                }
            }
        }
        Trace.WriteLine("Auto attack stopped.");
    }

    private int _lastHuntTick;

    private async Task _Hunt(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto hunt started.");
        _lastTargetMapID = -1;
        _failedAttacks = 0;
        TargetInfo = null;

        _priorityMapIDs = manualMapIDs;

        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = -1;
        }
        else if (!Combat.StopAttacking)
        {
            // Everything in the cell; with an empty cell, everything on the map.
            List<Monster> here = Monsters.CurrentMonsters;
            _target = string.Join('|', (here.Count > 0 ? here : Monsters.MapMonsters).Select(m => m.Name).Distinct());
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Hunt] Hunting for {_target}");

        if (_priorityMapIDs?.Length > 0)
        {
            // Hunt with priority MapIDs
            while (!token.IsCancellationRequested)
            {
                // Sequential priority targeting - always prefer earlier indices if alive
                int? currentTarget = null;

                // Find the highest priority (lowest index) alive monster
                for (int i = 0; i < _priorityMapIDs.Length; i++)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                    bool alive = monster != null && monster.Alive;

                    if (alive)
                    {
                        currentTarget = _priorityMapIDs[i];
                        break;
                    }
                }

                if (currentTarget.HasValue)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                    if (monster != null)
                    {
                        if (Player.Cell != monster.Cell && !token.IsCancellationRequested)
                        {
                            if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                                Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                            Map.Jump(monster.Cell, "Left");
                            _lastHuntTick = Environment.TickCount;
                        }
                    }

                    if (Monsters.Exists(currentTarget.Value) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            AttackNoted(currentTarget.Value);
                            Thread.Sleep(Options.ActionDelay);
                            targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                        }
                    }
                    Thread.Sleep(200);
                }
                else
                {
                    // No priority targets available, wait a bit
                    Thread.Sleep(500);
                }
            }
        }
        else if (_targetMapID > 0)
        {
            while (!token.IsCancellationRequested)
            {
                List<string> cells = Monsters.GetLivingMonsterDataLeafCells(_targetMapID);

                if (cells.Count == 0)
                {
                    Thread.Sleep(200);
                    continue;
                }

                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (Player.Cell != cell && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    if (Monsters.Exists(_targetMapID) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _targetMapID);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            AttackNoted(_targetMapID);
                            Thread.Sleep(Options.ActionDelay);
                            targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _targetMapID);
                        }
                    }

                    Thread.Sleep(200);
                }
            }
        }
        else if (!string.IsNullOrEmpty(_target) && _target != "*" && !_target.Contains('|'))
        {
            while (!token.IsCancellationRequested)
            {
                List<string> cells = Monsters.GetLivingMonsterDataLeafCells(_target);

                if (cells.Count == 0)
                {
                    Thread.Sleep(200);
                    continue;
                }

                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (Player.Cell != cell && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    if (Monsters.Exists(_target) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.CurrentMonsters.FirstOrDefault(m => m.Name == _target);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            AttackNoted(_target);
                            Thread.Sleep(Options.ActionDelay);
                            targetMonster = Monsters.CurrentMonsters.FirstOrDefault(m => m.Name == _target);
                        }
                    }

                    Thread.Sleep(200);
                }
            }
        }
        else
        {
            // Several names (or "*"): the targets are recomputed every pass, so cells that respawn are
            // found again. The current cell comes first, so a fight in progress isn't left for a jump.
            string[] names = _target == "*" || string.IsNullOrEmpty(_target)
                ? new[] { "*" }
                : _target.Split('|', StringSplitOptions.RemoveEmptyEntries);

            while (!token.IsCancellationRequested)
            {
                List<string> cells = names.SelectMany(n => Monsters.GetLivingMonsterDataLeafCells(n)).Distinct().ToList();
                if (cells.Count == 0)
                {
                    Thread.Sleep(300);
                    continue;
                }
                if (cells.Remove(Player.Cell))
                    cells.Insert(0, Player.Cell);

                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (Player.Cell != cell)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            Thread.Sleep(Options.HuntDelay - Environment.TickCount + _lastHuntTick);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    bool fought = false;
                    foreach (string mon in names)
                    {
                        while (!token.IsCancellationRequested && Monsters.Exists(mon) && AttackNoted(mon))
                        {
                            Thread.Sleep(Options.ActionDelay);
                            Kill.Monster(mon, token);
                            fought = true;
                        }
                    }
                    if (!fought)
                        Thread.Sleep(200);
                }
            }
        }
        Trace.WriteLine("Auto hunt stopped.");
    }

    private void CheckDropsandBoosts()
    {
        if (Drops.ToPickupIDs.Any() || Drops.ToPickup.Any())
            Drops.Start();

        if (Boosts.UsingBoosts)
            Boosts.Start();
    }
}