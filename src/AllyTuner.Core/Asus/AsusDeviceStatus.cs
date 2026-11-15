namespace AllyTuner.Core.Asus;

public enum AsusPresence
{
    /// <summary>The presence bit (0x00010000) is set: the BIOS implements this device ID.</summary>
    Present,

    /// <summary>The call worked but the presence bit is clear.</summary>
    NotPresent,

    /// <summary>The BIOS answered 0xFFFFFFFE: unknown device ID.</summary>
    Unsupported,
}

/// <summary>
/// A decoded 32-bit DSTS result.
/// Decoding follows Linux asus-armoury, which clears the presence bit to get the value
/// (<see href="https://github.com/torvalds/linux/blob/fa2933aac170dd19c6a7f0d7b81a8f537e40915c/drivers/platform/x86/asus-armoury.c#L141-L187"/>),
/// and G-Helper, which subtracts 0x10000 and treats a negative result as unsupported
/// (<see href="https://github.com/seerge/g-helper/blob/9d1b2a5b79b4ec608c0bc938eef63f3918eecfec/app/AsusACPI.cs#L491-L499"/>).
/// The two agree whenever the presence bit is set.
/// </summary>
public readonly record struct AsusDeviceStatus(uint Raw)
{
    public AsusPresence Presence => Raw switch
    {
        AsusAcpiProtocol.UnsupportedResult => AsusPresence.Unsupported,
        _ when (Raw & AsusAcpiProtocol.PresenceBit) != 0 => AsusPresence.Present,
        _ => AsusPresence.NotPresent,
    };

    /// <summary>The result with the presence bit cleared. Meaningful only when <see cref="Presence"/> is Present.</summary>
    public uint Value => Raw & ~AsusAcpiProtocol.PresenceBit;

    /// <summary>The low 16 bits of <see cref="Value"/>; most ASUS readings live here.</summary>
    public ushort Low16 => (ushort)(Value & 0xFFFF);
}
