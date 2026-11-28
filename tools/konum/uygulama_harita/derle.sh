#!/bin/sh
# Unity'nin kendi .NET'i ve Roslyn derleyicisiyle uygulamanin harita kodunu PC araci olarak derler (dotnet SDK gerekmez).
# Uygulamanin UnityEngine kullanmayan butun kodu (ar_tarama/Assets/Scripts) degismeden derlenir (ZeminHaritasi, ZeminGeo...).
U="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Data}"
NET=$(ls -d "$U/NetCoreRuntime/shared/Microsoft.NETCore.App/"* | tail -1)
D=$(cd "$(dirname "$0")" && pwd); S="$D/../../../ar_tarama/Assets/Scripts"; O="$D/cikti"; mkdir -p "$O"
R="$O/derle.rsp"; w() { cygpath -w "$1"; }
{ echo "-nologo -nostdlib -optimize+ -nowarn:CS8632,CS0162,CS0414,CS0168,CS0219"
  for f in "$NET"/*.dll; do case "$(basename "$f")" in *.Native.dll) ;; System.*|netstandard.dll|mscorlib.dll) echo "-r:\"$(w "$f")\"";; esac; done
  echo "-out:\"$(w "$O/uygulama_harita.dll")\""
  for f in "$D/Program.cs" $(grep -L "^using UnityEngine" "$S"/*.cs); do echo "\"$(w "$f")\""; done; } > "$R"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" "@$(w "$R")" 2>&1 | grep -v "^$" | grep -v "warning" ; 
[ -f "$O/uygulama_harita.dll" ] || exit 1
printf '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}\n' "$(basename "$NET")" > "$O/uygulama_harita.runtimeconfig.json"
echo "derlendi: $O/uygulama_harita.dll"
