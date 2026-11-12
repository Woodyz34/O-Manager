#!/bin/sh
# Builds the harness with mono's mcs from the game's own source files
cd "$(dirname "$0")"
R=../../..
mcs -nowarn:0219,0414,0168,0649 -out:harness.exe -r:$R/Packages/Newtonsoft.Json.13.0.3/lib/net45/Newtonsoft.Json.dll \
  Stubs.cs Program.cs $R/Assets/Scripts/Mistakes/ForestMistakes.cs $R/Assets/Scripts/Mistakes/MapFeatureIndex.cs \
  $R/Assets/Scripts/MapImport/SymbolRules.cs $R/Assets/Scripts/MapImport/MapModel.cs && cp $R/Packages/Newtonsoft.Json.13.0.3/lib/net45/Newtonsoft.Json.dll .
