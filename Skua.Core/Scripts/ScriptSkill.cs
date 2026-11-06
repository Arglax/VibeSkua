using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Skills;
using Skua.Core.Skills;

namespace Skua.Core.Scripts;

public partial class ScriptSkill : IScriptSkill
{
    private const string genericSkills = "0 | 1 | 2 | 3 | 4";
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptSelfAuras> _lazySelf;
    private readonly Lazy<IScriptTargetAuras> _lazyTarget;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptInventory> _lazyInventory;

    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptSelfAuras Self => _lazySelf.Value;
    private IScriptTargetAuras Target => _lazyTarget.Value;
    private IScriptCombat Combat => _lazyCombat.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptInventory Inventory => _lazyInventory.Value;

    public IAdvancedSkillContainer AdvancedSkillContainer { get; set; }

    public ScriptSkill(
        IAdvancedSkillContainer advContainer,
        Lazy<IFlashUtil> flash,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptSelfAuras> self,
        Lazy<IScriptTargetAuras> target,
        Lazy<IScriptCombat> combat,
        Lazy<IScriptOption> options,
        Lazy<IScriptInventory> inventory,
        Lazy<IScriptWait> wait)
    {
        _lazyFlash = flash;
        _lazyPlayer = player;
        _lazySelf = self;
        _lazyTarget = target;
        _lazyCombat = combat;
        _lazyWait = wait;
        _lazyOptions = options;
        _lazyInventory = inventory;
        AdvancedSkillContainer = advContainer;
    }

    private ISkillProvider? _provider;
    private Thread? _skillThread;
    private CancellationTokenSource? _skillsCTS;

    [MethodCallBinding("canUseSkill")]
    private bool _canUseSkill(int index)
    {
        return false;
    }

    [MethodCallBinding("useSkill")]
    private bool _useSkill(int index)
    {
        return false;
    }

    public ISkillProvider? OverrideProvider { get; set; } = null;
    public ISkillProvider BaseProvider { get; private set; }
    public bool TimerRunning { get; private set; } = false;
    public bool IsPaused { get; private set; } = false;
    public int SkillInterval { get; set; } = 100;

    public int SkillTimeout { get; set; } = -1;

    public SkillUseMode SkillUseMode { get; set; } = SkillUseMode.UseIfAvailable;
    private ManualResetEvent _pauseEvent = new(true);

    public void Start()
    {
        if (BaseProvider is null)
        {
            BaseProvider = new AdvancedSkillProvider(Player, Self, Target, Combat, Flash);
            BaseProvider.Load(genericSkills);
            _provider = BaseProvider;
        }
        _provider = OverrideProvider ?? BaseProvider;

        if (TimerRunning)
            return;

        _skillThread = new(async () =>
        {
            _skillsCTS = new();
            try
            {
                await _Timer(_skillsCTS.Token);
            }
            catch { }
            finally
            {
                _skillsCTS?.Dispose();
                _skillsCTS = null;
                TimerRunning = false;
            }
        })
        {
            Name = "Skill Timer"
        };
        _skillThread.Start();
        TimerRunning = true;
    }

    public void Stop()
    {
        _provider?.Stop();
        _skillsCTS?.Cancel();
        Wait.ForTrue(() => !TimerRunning, 2);
    }

    public void Pause()
    {
        if (!TimerRunning || IsPaused)
            return;

        IsPaused = true;
        _pauseEvent.Reset();
    }

    public void Resume()
    {
        if (!TimerRunning || !IsPaused)
            return;

        IsPaused = false;
        _pauseEvent.Set();
    }

    public void SetProvider(ISkillProvider provider)
    {
        _provider = OverrideProvider = provider;
    }

    public void UseBaseProvider()
    {
        _provider = BaseProvider;
    }

    public void LoadAdvanced(string className, bool autoEquip, ClassUseMode useMode = ClassUseMode.Base)
    {
        OverrideProvider = new AdvancedSkillProvider(Player, Self, Target, Combat, Flash);

        if (string.Equals(className, "generic", StringComparison.OrdinalIgnoreCase))
        {
            OverrideProvider.Load(genericSkills);
            SkillUseMode = SkillUseMode.UseIfAvailable;
            return;
        }

        if (autoEquip)
        {
            Inventory.EquipItem(className);
            Wait.ForItemEquip(className);
        }

        List<AdvancedSkill> skills = new();
        foreach (AdvancedSkill s in AdvancedSkillContainer.LoadedSkills)
        {
            if (string.Equals(s.ClassName, className, StringComparison.CurrentCultureIgnoreCase))
                skills.Add(s);
        }
        if (skills.Count == 0)
        {
            OverrideProvider.Load(genericSkills);
            SkillUseMode = SkillUseMode.UseIfAvailable;
            return;
        }
        // The asked-for mode, else the general-purpose ones before whatever is listed first.
        AdvancedSkill skill = skills.Find(s => s.ClassUseMode == useMode)
            ?? skills.Find(s => s.ClassUseMode == ClassUseMode.Base)
            ?? skills.Find(s => s.ClassUseMode == ClassUseMode.Solo)
            ?? skills.Find(s => s.ClassUseMode == ClassUseMode.Farm)
            ?? skills.First();
        OverrideProvider.Load(skill.Skills);
        SkillTimeout = skill.SkillTimeout;
        SkillUseMode = skill.SkillUseMode;
    }

    public void LoadAdvanced(string skills, int skillTimeout = -1, SkillUseMode skillMode = SkillUseMode.UseIfAvailable)
    {
        OverrideProvider = new AdvancedSkillProvider(Player, Self, Target, Combat, Flash);
        SkillTimeout = skillTimeout;
        SkillUseMode = skillMode;
        OverrideProvider.Load(skills);
    }

    public void LoadAdvanced(string className, string mode, bool autoEquip = true)
    {
        if (Enum.TryParse<ClassUseMode>(mode, ignoreCase: true, out ClassUseMode classMode))
        {
            LoadAdvanced(className, autoEquip, classMode);
        }
        else
        {
            AdvancedSkill? skill = AdvancedSkillContainer.GetClassModeSkills(className, mode);
            if (skill != null)
            {
                if (autoEquip)
                {
                    Inventory.EquipItem(className);
                    Wait.ForItemEquip(className);
                }
                OverrideProvider = new AdvancedSkillProvider(Player, Self, Target, Combat, Flash);
                OverrideProvider.Load(skill.Skills);
                SkillTimeout = skill.SkillTimeout;
                SkillUseMode = skill.SkillUseMode;
            }
            else
            {
                OverrideProvider = new AdvancedSkillProvider(Player, Self, Target, Combat, Flash);
                OverrideProvider.Load(genericSkills);
                SkillUseMode = SkillUseMode.UseIfAvailable;
            }
        }
    }

    private int _lastRank = -1;
    private long _lastHealCheck;
    private SkillInfo[] _playerSkills = null!;

    private async Task _Timer(CancellationToken token)
    {
        SkillInfo[]? playerSkills = Player.Skills;
        if (playerSkills is not null && playerSkills.Length > 0)
            _playerSkills = playerSkills;

        while (!token.IsCancellationRequested)
        {
            _pauseEvent.WaitOne();
            if (Combat.StopAttacking)
            {
                _counterAttack.WaitOne(10000);
                Combat.StopAttacking = false;
            }

            if (!Player.Alive)
            {
                _provider?.OnPlayerDeath();
            }

            // if the player has target or bot attack without target option is on
            // then activate the skills
            if ((Options.AttackWithoutTarget && Player is { Loaded: true, Playing: true }) || Player is { HasTarget: true, Loaded: true, Playing: true })
            {
                // One failed game read must not end the skill thread for the rest of the fight.
                try
                {
                    _Poll(token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Trace.WriteLine($"[Skills] poll failed: {ex.Message}");
                }
            }

            // target reset if player has no target
            _provider?.OnTargetReset();

            // wait for skill interval
            if (!token.IsCancellationRequested)
                await Task.Delay(SkillInterval, token);
        }
    }

    private void _Poll(CancellationToken token)
    {
        // if the current player has skills and the current class rank is different from the last rank
        // then update the skills since classes will enable a certain skill base on the rank
        // Also refresh if _playerSkills is null
        if (Player.CurrentClassRank > _lastRank || _playerSkills is null)
        {
            // Update the player skills skills
            SkillInfo[]? playerSkills = Player.Skills;
            if (playerSkills is not null && playerSkills.Length > 0)
                _playerSkills = playerSkills;

            if (_playerSkills is not null)
            {
                int k = 0;
                using FlashArray<object> skills = (FlashArray<object>)Flash.CreateFlashObject<object>("world.actions.active").ToArray();
                foreach (FlashObject<object> skill in skills)
                {
                    if (k >= _playerSkills.Length)
                        break;
                    using FlashObject<long> ts = (FlashObject<long>)skill.GetChild<long>("ts");
                    ts.Value = _playerSkills[k++]?.LastUse ?? 0;
                }
            }
        }

        // Store the last rank if the player is ranking up
        _lastRank = Player.CurrentClassRank;
        if (token.IsCancellationRequested)
            return;

        // Heals first: a rotation entry with an "HP below" rule is cast as soon as it applies and the
        // skill is ready, instead of waiting for its turn (it often came up while on cooldown and was
        // skipped). Checked at most every 300 ms, since each check reads the player's HP from the game.
        if (_provider is AdvancedSkillProvider advanced && !Combat.StopAttacking
            && Environment.TickCount64 - _lastHealCheck >= 300)
        {
            _lastHealCheck = Environment.TickCount64;
            int heal = advanced.FindUrgentHeal(CanUseSkill);
            if (heal >= 0 && (_playerSkills is null || heal >= _playerSkills.Length || _playerSkills[heal].IsOk))
            {
                this.UseSkill(heal);
                return;
            }
        }

        (int index, int skillS) = _provider!.GetNextSkill();

        if (index == -1 || skillS == -1)
            return;

        switch (_provider?.ShouldUseSkill(index, CanUseSkill(skillS)))
        {
            case true:
                if (_playerSkills is not null && skillS < _playerSkills.Length && !_playerSkills[skillS].IsOk)
                {
                    break;
                }
                UseSkill(skillS);
                break;

            case null:
                (int indexS, int skill) = _provider!.GetNextSkill();
                if (indexS == -1 || skill == -1)
                    break;
                if (_playerSkills is not null && skill < _playerSkills.Length && !_playerSkills[skill].IsOk)
                {
                    break;
                }
                UseSkill(skill);
                break;

            default:
                break;
        }

        return;

        // This method will activate a skill
        void UseSkill(int skill)
        {
            switch (SkillUseMode)
            {
                // if SkillUseMode is UseIfAvailable then use skills without waiting for cooldown
                case SkillUseMode.UseIfAvailable:
                    if ((Options.AttackWithoutTarget || CanUseSkill(skill)) && !Combat.StopAttacking)
                        this.UseSkill(skill);
                    break;

                // if SkillUseMode is WaitForCooldown then use skills with waiting for cooldown
                case SkillUseMode.WaitForCooldown:
                    if (Options.AttackWithoutTarget || (skill != -1 && Wait.ForTrue(() => CanUseSkill(skill), null, SkillTimeout, SkillInterval) && !Combat.StopAttacking))
                        this.UseSkill(skill);
                    break;
            }
        }
    }

    private readonly AutoResetEvent _counterAttack = new(false);

    private void CounterAttack(ScriptSkill recipient, CounterAttackMessage message)
    {
        if (message.Faded)
            recipient._counterAttack.Set();
    }
}
