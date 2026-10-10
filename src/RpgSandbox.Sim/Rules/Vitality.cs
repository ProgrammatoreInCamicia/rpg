namespace RpgSandbox.Sim.Rules;

// Hit Points, dropping to 0, death and Death Saving Throws (SRD 5.2.1: "Dropping to 0 Hit Points", "Instant Death",
// "Death Saving Throws", "Knocking Out a Creature", "Healing").

public enum LifeState
{
    /// <summary>Above 0 Hit Points (possibly knocked out: see <see cref="Vitality.KnockedOut"/>).</summary>
    Alive,

    /// <summary>At 0 Hit Points, Unconscious, making Death Saving Throws.</summary>
    Dying,

    /// <summary>At 0 Hit Points, Unconscious, no longer making Death Saving Throws.</summary>
    Stable,

    Dead,
}

/// <summary>What a hit did to a creature, for the dice log and the reactions of the world.</summary>
public enum DamageOutcome
{
    /// <summary>Still standing.</summary>
    Hurt,

    /// <summary>Spared at 1 Hit Point, Unconscious (Knocking Out).</summary>
    KnockedOut,

    /// <summary>Down at 0 Hit Points and dying.</summary>
    Down,

    /// <summary>Already at 0: one or two Death Saving Throw failures (still alive).</summary>
    DeathSaveFailed,

    Killed,

    /// <summary>The creature was already dead.</summary>
    NoEffect,
}

public enum DeathSaveOutcome { Success, Failure, Stabilized, Died, RegainedConsciousness }

/// <summary>
/// The Hit Points of one creature and what happens at 0. Characters (the player, companions) make Death Saving Throws;
/// other creatures die at 0 Hit Points (SRD "Monster Death"). Anyone can be spared by Knocking Out.
/// </summary>
public sealed class Vitality
{
    public Vitality(int maximum, bool makesDeathSaves, int? current = null)
    {
        if (maximum < 1)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Hit Point maximum must be at least 1.");
        Maximum = maximum;
        MakesDeathSaves = makesDeathSaves;
        Current = Math.Clamp(current ?? maximum, 0, maximum);
        State = Current > 0 ? LifeState.Alive : makesDeathSaves ? LifeState.Dying : LifeState.Dead;
    }

    public int Maximum { get; }
    public int Current { get; private set; }
    public bool MakesDeathSaves { get; }
    public LifeState State { get; private set; }

    /// <summary>Spared at 1 Hit Point by a Knocking Out blow: Unconscious until healed or given first aid.</summary>
    public bool KnockedOut { get; private set; }

    public int DeathSaveSuccesses { get; private set; }
    public int DeathSaveFailures { get; private set; }

    /// <summary>Unconscious: dying, stable, or knocked out.</summary>
    public bool Unconscious => State is LifeState.Dying or LifeState.Stable || (State == LifeState.Alive && KnockedOut);

    /// <summary>
    /// Takes damage (SRD). Above 0: Hit Points drop; at 0 a creature without Death Saving Throws dies, a character
    /// falls Unconscious and dying, or dies outright if the damage left over reaches its Hit Point maximum (Massive
    /// Damage). <paramref name="knockOut"/> (a melee attack whose attacker chooses to spare): instead of dropping to 0,
    /// the creature stays at 1 Hit Point, knocked out. At 0 Hit Points: one Death Saving Throw failure, two for a
    /// Critical Hit, death if the damage reaches the maximum; a stable creature starts dying again.
    /// </summary>
    public DamageOutcome TakeDamage(int amount, bool critical = false, bool knockOut = false)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (State == LifeState.Dead)
            return DamageOutcome.NoEffect;

        if (State is LifeState.Dying or LifeState.Stable)
        {
            if (amount >= Maximum)
                return Die();
            State = LifeState.Dying;
            DeathSaveFailures += critical ? 2 : 1;
            return DeathSaveFailures >= 3 ? Die() : DamageOutcome.DeathSaveFailed;
        }

        if (amount < Current)
        {
            Current -= amount;
            return DamageOutcome.Hurt;
        }
        if (knockOut)
        {
            Current = 1;
            KnockedOut = true;
            return DamageOutcome.KnockedOut;
        }
        var remainder = amount - Current;
        Current = 0;
        KnockedOut = false;
        if (!MakesDeathSaves || remainder >= Maximum)
            return Die();
        State = LifeState.Dying;
        ResetDeathSaves();
        return DamageOutcome.Down;
    }

    /// <summary>
    /// SRD Death Saving Throw (at the start of a dying creature's turn): 1d20, 10 or more succeeds. A 20 brings it back
    /// with 1 Hit Point; a 1 counts as two failures. Three successes: Stable; three failures: dead.
    /// </summary>
    internal DeathSaveOutcome RollDeathSave(SplitMix64 rng, out int rolled)
    {
        if (State != LifeState.Dying)
            throw new InvalidOperationException("Only a dying creature makes Death Saving Throws.");
        rolled = rng.Roll(20);
        if (rolled == 20)
        {
            Heal(1);
            return DeathSaveOutcome.RegainedConsciousness;
        }
        if (rolled == 1 || rolled < 10)
        {
            DeathSaveFailures += rolled == 1 ? 2 : 1;
            if (DeathSaveFailures < 3)
                return DeathSaveOutcome.Failure;
            Die();
            return DeathSaveOutcome.Died;
        }
        DeathSaveSuccesses++;
        if (DeathSaveSuccesses < 3)
            return DeathSaveOutcome.Success;
        Stabilize();
        return DeathSaveOutcome.Stabilized;
    }

    /// <summary>
    /// A dying creature becomes Stable (three successes, or someone's successful DC 10 Wisdom (Medicine) check with the
    /// Help action: the caller makes that check). Returns false if it was not dying.
    /// </summary>
    public bool Stabilize()
    {
        if (State != LifeState.Dying)
            return false;
        State = LifeState.Stable;
        ResetDeathSaves();
        return true;
    }

    /// <summary>
    /// Regains Hit Points, never above the maximum. Any healing brings a creature at 0 back to consciousness and ends
    /// a knock-out; the dead cannot be healed. Returns the Hit Points actually regained.
    /// </summary>
    public int Heal(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (State == LifeState.Dead || amount == 0)
            return 0;
        var before = Current;
        Current = Math.Min(Maximum, Current + amount);
        if (State is LifeState.Dying or LifeState.Stable)
        {
            State = LifeState.Alive;
            ResetDeathSaves();
        }
        KnockedOut = false;
        return Current - before;
    }

    private DamageOutcome Die()
    {
        Current = 0;
        KnockedOut = false;
        State = LifeState.Dead;
        return DamageOutcome.Killed;
    }

    private void ResetDeathSaves()
    {
        DeathSaveSuccesses = 0;
        DeathSaveFailures = 0;
    }

    /// <summary>For the player's eyes about others: never the exact Hit Points (C2 will refine the thresholds).</summary>
    public string Describe() => State switch
    {
        LifeState.Dead => "morto",
        LifeState.Dying => "a terra, morente",
        LifeState.Stable => "a terra, privo di sensi",
        _ when KnockedOut => "tramortito",
        _ when Current == Maximum => "illeso",
        _ when Current * 2 > Maximum => "ferito",
        _ => "malconcio",
    };
}
