namespace RimworldStatistics.Core;

/// <summary>Who caused a death, as far as the damage record shows (evidence #health-death: the actor and the means are separate axes).</summary>
public enum KillerSide
{
    /// <summary>No instigator, or one whose side cannot be told.</summary>
    Unknown,

    /// <summary>An instigator that is not on the player's side.</summary>
    NotPlayer,

    PlayerPawn,
    PlayerTurret,
    PlayerTrap,
    PlayerAnimal,
    PlayerMech,

    /// <summary>Another player-owned thing (a building, fire started by a player pawn's attack, …).</summary>
    PlayerOther,
}

/// <summary>How a kill was made; exclusive, decided in the order explosive → environment → ranged / melee / unarmed → unknown.</summary>
public enum KillMethod
{
    Unknown,
    Explosive,
    Environment,
    Ranged,
    Melee,
    Unarmed,
}

/// <summary>A confirmed death (Pawn.Kill turned a living pawn dead), with the victim's state from before the death.</summary>
public sealed class DeathFacts
{
    public const string Humanlike = "humanlike";
    public const string Insect = "insect";
    public const string Mechanoid = "mechanoid";
    public const string Other = "other";

    public required string PawnId { get; init; }
    public bool FreeColonist { get; init; }
    public bool ColonyAnimal { get; init; }
    public bool Wild { get; init; }
    public bool HostileToPlayer { get; init; }

    /// <summary><see cref="Humanlike"/>, <see cref="Insect"/>, <see cref="Mechanoid"/> or <see cref="Other"/>.</summary>
    public required string VictimKind { get; init; }

    public double? AgeYears { get; init; }
    public bool InPlayerCaravan { get; init; }

    /// <summary>"hediff:&lt;def&gt;" for the culprit hediff, "damage:&lt;def&gt;" for the killing damage, null when neither is known.</summary>
    public string? Cause { get; init; }

    /// <summary>The culprit hediff is a sickness (the game's own HediffDef.makesSickThought).</summary>
    public bool Disease { get; init; }

    public KillerSide Killer { get; init; }
    public string? KillerPawnId { get; init; }
    public KillMethod Method { get; init; }

    /// <summary>The killer belonged to a tracked raid (null: the raid family does not record, so this is not known).</summary>
    public bool? KillerIsRaider { get; init; }

    public bool HuntDesignated { get; init; }
    public bool SlaughterDesignated { get; init; }
    public bool ExecutionCut { get; init; }
}

/// <summary>A confirmed downing (not downed → downed, the pawn alive).</summary>
public sealed class DownFacts
{
    public bool FreeColonist { get; init; }
    public bool HostileToPlayer { get; init; }

    /// <summary>The downing hediff is toxic buildup (Biotech pollution or toxic fallout).</summary>
    public bool Toxic { get; init; }

    /// <summary>Both the pawn and the attacker were in a social fight with each other.</summary>
    public bool SocialFight { get; init; }
}

/// <summary>A raid that arrived: the final incident parameters and the pawns generated for it.</summary>
public sealed class RaidArrival
{
    public required string MapId { get; init; }
    public required IReadOnlyList<string> PawnIds { get; init; }
    public double Points { get; init; }
    public string? ArrivalMode { get; init; }
    public string? Strategy { get; init; }
    public string? FactionDef { get; init; }
}

/// <summary>Why a raider stopped taking part (from the lord's loss condition).</summary>
public enum RaidLoss
{
    Killed,
    Exited,
    Captured,
    Downed,
    Other,
}
