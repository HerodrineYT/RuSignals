namespace RuSignals;

public static class RouteBoardRules
{
    public static bool HasHousing(SignalRole role,bool dwarf) => !dwarf &&
        (role==SignalRole.Entry || role==SignalRole.Route || role==SignalRole.Exit);
    public static bool Enabled(SignalRole role,bool dwarf,bool shunting,bool junction) =>
        !shunting && HasHousing(role,dwarf) && (role==SignalRole.Entry || junction);

    // +Z is the face of the RUS board. Viewed from there, positive model X
    // is screen-left. Do not reflect the sigcfg X coordinate a second time.
    public static float LampX(float sourceX) => sourceX;
    public const float Emission = .45f;
    public const float MainLampEmission = 1.8f;
    // The original RUS board's clamps surround (0, 7.105, -.5), not the
    // origin. Align that mount with the pole, including when uniformly scaled.
    public const float MountY = 7.105f, MountZ = -.5f, Top = 7.575f;
    public const float HeadMargin = .55f, HeadGap = .10f, CompactScale = .65f;
    public const float CompactLowestLens = 1.65f;
    public static float OffsetY(float lowestLens,float lowering,float scale) =>
        lowestLens - lowering - HeadMargin - HeadGap - Top * scale;
    public static float OffsetZ(float scale) => -MountZ * scale;
    public static bool ModelHasBoard(string model) =>
        model=="entry" || model=="junction" || model=="exit" || model=="exit_mainline";
}
