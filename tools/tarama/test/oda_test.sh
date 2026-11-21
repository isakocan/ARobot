#!/bin/bash
# Unity'nin kendi Roslyn ve .NET calisma zamanini kullanarak oda modeli cekirdegini Unity acmadan derler ve calistirir.
# Kullanim: tools/tarama/test/oda_test.sh [cikti_klasoru | kayit_klasoru [cikti_klasoru]]
set -e
U="${UNITY_EDITOR_DATA:-/c/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Data}"
HERE="$(cd "$(dirname "$0")" && pwd)"
AQ="$(cygpath -m "$HERE/../../../ar_tarama/Assets")"
OUT="$(cygpath -m "${TMPDIR:-/tmp}/oda_cli")"; mkdir -p "$OUT"
RT="$(cygpath -m "$U/NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21")"
ARGS=()
for n in System.Private.CoreLib System.Runtime System.Collections System.Console System.IO.Compression System.Linq System.Runtime.Extensions System.Memory; do
  ARGS+=("-r:$RT/$n.dll")
done
DOTNET="$(cygpath -m "$U/NetCoreRuntime/dotnet.exe")"
CSC="$(cygpath -m "$U/DotNetSdkRoslyn/csc.dll")"
"$DOTNET" exec "$CSC" -nologo -debug:portable -nullable:disable "-out:$OUT/oda_cli.dll" -target:exe "${ARGS[@]}" \
  "$AQ/Scripts/OdaKanit.cs" "$AQ/Scripts/OdaGeo.cs" "$AQ/Scripts/OdaModeli.cs" "$AQ/Scripts/OdaModeli.Duvar.cs" "$AQ/Scripts/OdaModeli.Kutu.cs" "$AQ/Scripts/OdaModeli.Bant.cs" "$AQ/Scripts/OdaModeli.Esya.cs" "$AQ/Scripts/OdaKoc.cs" "$AQ/Scripts/ZeminHaritasi.cs" "$AQ/Scripts/ZeminGeo.cs" "$AQ/Scripts/EvHaritasi.cs" "$AQ/Scripts/IsaretPoz.cs" "$(cygpath -m "$HERE")/oda_cli/EvTest.cs" "$AQ/Scripts/OdaJson.cs" "$AQ/Editor/OdaTestCekirdek.cs" "$(cygpath -m "$HERE")/oda_cli/Program.cs"
echo '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"}}}' > "$OUT/oda_cli.runtimeconfig.json"
"$DOTNET" exec --runtimeconfig "$OUT/oda_cli.runtimeconfig.json" "$OUT/oda_cli.dll" "$@"
