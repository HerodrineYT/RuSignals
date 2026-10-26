using System;
namespace RuSignals;
internal static class SignalTiming
{
    // RuSignals 2.0.9 delegates blinking to DV's LampControl and SmoothDamp.
    // Its phase changes at the native brightness thresholds, not on a timer.
    internal const float NativeBlinkLag=.15f;
    internal const double FadeSeconds=.18;
    internal static float Fade(float from,float to,double elapsed)
    {
        if(elapsed<=0)return from;if(elapsed>=FadeSeconds)return to;
        float t=(float)(elapsed/FadeSeconds);return from+(to-from)*t*t*(3-2*t);
    }
}
