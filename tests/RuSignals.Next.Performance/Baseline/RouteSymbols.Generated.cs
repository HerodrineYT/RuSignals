// Original RUS six-column/seven-row lamp drawings.
namespace RuSignals;
public static partial class RouteSymbols
{
    private static ulong Original(string symbol, bool small) => ((small ? "route_pointer_small:" : "route_pointer:") + symbol) switch
    {
        "route_pointer:0" => 1688535714072UL,
        "route_pointer:1" => 558482096648UL,
        "route_pointer:2" => 4127602116888UL,
        "route_pointer:3" => 1688465443096UL,
        "route_pointer:4" => 1117703454980UL,
        "route_pointer:5" => 1688467521852UL,
        "route_pointer:6" => 1688533483832UL,
        "route_pointer:7" => 279242147900UL,
        "route_pointer:8" => 1688532568344UL,
        "route_pointer:9" => 1959056787512UL,
        "route_pointer:10" => 69810262081UL,
        "route_pointer:Left" => 34630287489UL,
        "route_pointer:Right" => 1108378656UL,
        "route_pointer:-" => 16515072UL,
        "route_pointer_small:0" => 1688535714072UL,
        "route_pointer_small:1" => 558482096648UL,
        "route_pointer_small:2" => 4127602116888UL,
        "route_pointer_small:3" => 1688465443096UL,
        "route_pointer_small:4" => 1117703454980UL,
        "route_pointer_small:5" => 1688467521852UL,
        "route_pointer_small:6" => 1688533483832UL,
        "route_pointer_small:7" => 279242147900UL,
        "route_pointer_small:8" => 1688532568344UL,
        "route_pointer_small:9" => 1959056787736UL,
        "route_pointer_small:10" => 69810262081UL,
        "route_pointer_small:Left" => 34630287489UL,
        "route_pointer_small:Right" => 1108378656UL,
        "route_pointer_small:-" => 16515072UL,
        _ => 0UL
    };
}
