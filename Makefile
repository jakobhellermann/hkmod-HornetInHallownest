PIPELINE   := $(CURDIR)/tools/asset-pipeline
LOCAL_REFS := $(CURDIR)/local-refs

CI_REFS := $(CURDIR)/ci-refs

# HK 1.5.78 (Unity 2020.2.2f1): everything the older host needs that the Unity 6 flow derives at runtime.
# The real Silksong install (SS_INSTALL) is only ever READ from; the in-place rewrites (IL downgrade, aa
# conversion) happen inside the derived copy in silksong-hk1578/, which the runtime reads via the mod's
# SilksongPath setting.
HK1578      ?= $(HOME)/.local/share/Steam/steamapps/common/Hollow Knight-1.5.78.11833
SS_INSTALL ?= $(HOME)/.local/share/Steam/steamapps/common/Hollow Knight Silksong
HK1578_MANAGED := $(HK1578)/hollow_knight_Data/Managed
SS_DATA        := $(SS_INSTALL)/Hollow Knight Silksong_Data
SS_MANAGED     := $(SS_DATA)/Managed
SS1578         := $(CURDIR)/silksong-hk1578
SS1578_DATA    := $(SS1578)/Hollow Knight Silksong_Data
SS1578_MANAGED := $(SS1578_DATA)/Managed
LOCAL_REFS_1578 := $(CURDIR)/local-refs-hk1578
BUNDLES_1578    := $(CURDIR)/bundles-hk1578
UNITY1578       := 2020.2.2f1
UNITYCOMPAT_DLL  := $(CURDIR)/tools/UnityCompat/bin/Release/net472/HornetInHallownest.UnityCompat.dll
UNITYDOWNGRADER := $(CURDIR)/tools/UnityDowngrader/bin/Release/net8.0/UnityDowngrader.dll

# Assemblies the runtime installs (mirror of SilksongSetup deps + prefixInputs); entries absent from the
# Silksong install (SharpDX: dropped in 1.0.30000) are skipped.
SS1578_ASSEMBLIES := Assembly-CSharp Assembly-CSharp-firstpass PlayMaker ConditionalExpression \
    TeamCherry.NestedFadeGroup TeamCherry.Localization TeamCherry.SharedUtils \
    Unity.Addressables Unity.ResourceManager Unity.Profiling.Core Unity.Mathematics Unity.Burst Unity.Burst.Unsafe \
    Newtonsoft.Json.UnityConverters TeamCherry.Splines Coffee.SoftMaskForUGUI TeamCherry.Cinematics \
    TeamCherry.BuildBot Unity.InputSystem com.rlabrecque.steamworks.net

.PHONY: local-refs ci-refs remap-monoscripts repack-resources docs help \
    hk1578 hk1578-tools silksong-hk1578 aa-hk1578 local-refs-hk1578 bundles-hk1578

help:
	@echo "HornetInHallownest asset pipeline:"
	@echo "  make local-refs        prefix dlls with Silksong into local-refs/Silksong.*.dll"
	@echo "  make ci-refs           Stub local-refs into ci-refs"
	@echo "  make remap-monoscripts rebuild monoscripts.silksong.bundle (m_AssemblyName -> Silksong.*)"
	@echo "  make repack-resources  repack Silksong resources.assets -> silksong-resources.bundle"
	@echo "  make docs              regenerate tag docs"
	@echo "  make hk1578            full HK 1.5.78 setup: silksong copy + aa + local-refs + bundles"
	@echo "  make silksong-hk1578   create + downgrade the derived Silksong copy (silksong-hk1578/)"
	@echo "  make aa-hk1578         convert the Addressables tree into that copy (7.5 GB, slow) + fix discrete curves"
	@echo "  make local-refs-hk1578 compile refs for the hk1578 target (prefix, then downgrade)"
	@echo "  make bundles-hk1578    bundles for the hk1578 target (2020.2-declared + tk2d retargeted)"

TAGS_JQ := .layers | to_entries[] | (.key|tostring) + " " + .value

docs: docs/tags-hk.txt docs/tags-hkss.txt

docs/tags-hk.txt:
	rabex --steam-game 'Hollow Knight' file globalgamemanagers object TagManager cat --jq '$(TAGS_JQ)' | sed 's/^"//;s/"$$//' > $@

docs/tags-hkss.txt:
	rabex --steam-game 'Silksong'      file globalgamemanagers object TagManager cat --jq '$(TAGS_JQ)' | sed 's/^"//;s/"$$//' > $@

# Silksong install Managed -> local-refs/ (IL-prefixed compile refs for the Unity 6 hk1512620 target)
local-refs:
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:GenerateLocalRefs \
	  -p:SilksongManaged="$(SS_MANAGED)" -p:LocalRefs="$(LOCAL_REFS)" \
	  -p:SuppressImplicitGitSourceLink=true

# local-refs/ -> ci-refs/lib/ (refasmer stubs so CI builds without a Silksong install)
ci-refs: local-refs
	rm -rf $(CI_REFS)
	mkdir -p $(CI_REFS)/lib
	refasmer --omit-non-api-members true -O $(CI_REFS)/lib $(LOCAL_REFS)/*.dll

# Silksong install -> Assets/bundles/monoscripts.silksong.bundle (MonoScripts remapped to Silksong.*)
remap-monoscripts:
	cd $(PIPELINE) && cargo run --release --bin remap-monoscripts

# Silksong install resources.assets -> Assets/bundles/silksong-resources.bundle
repack-resources:
	cd $(PIPELINE) && cargo run --release --bin repack-resources

# ---- HK 1.5.78 (Unity 2020.2) ---------------------------------------------------------------------------

# UnityCompat/UnityDowngrader sources -> built tools (run by the other hk1578 targets)
hk1578-tools:
	dotnet build -c Release -v q --nologo $(CURDIR)/tools/UnityCompat \
	  -p:HKRefs="$(HK1578_MANAGED)" -p:GalaxyCSharpFallback="$(SS_MANAGED)/GalaxyCSharp.dll"
	dotnet build -c Release -v q --nologo $(CURDIR)/tools/UnityDowngrader

# Silksong install (Managed + boot.config, read-only) -> silksong-hk1578/ (derived copy, Managed IL-downgraded
# for the 2020.2 host; runtime input via the mod's SilksongPath). The shim sits next to the downgraded assemblies
# as an install dep and is never itself downgraded.
silksong-hk1578: hk1578-tools
	mkdir -p "$(SS1578_DATA)"
	rm -rf "$(SS1578_MANAGED)"
	cp -R "$(SS_MANAGED)" "$(SS1578_MANAGED)"
	cp "$(SS_DATA)/boot.config" "$(SS1578_DATA)/"
	cp "$(UNITYCOMPAT_DLL)" "$(SS1578_MANAGED)/"
	set -- ; for n in $(SS1578_ASSEMBLIES); do \
	  [ -f "$(SS1578_MANAGED)/$$n.dll" ] && set -- "$$@" "$(SS1578_MANAGED)/$$n.dll"; done; \
	dotnet "$(UNITYDOWNGRADER)" --hk "$(HK1578_MANAGED)" \
	  --shim "$(SS1578_MANAGED)/HornetInHallownest.UnityCompat.dll" "$$@"
	@echo "done. Point SilksongPath in the mod settings at: $(SS1578)"

# Silksong install StreamingAssets/aa (read-only) -> silksong-hk1578/.../aa (UnityFS v8 -> v7, host version
# declared; then discrete AnimationCurves made readable by Unity 2020 - the game crashes on Hornet's HUD otherwise)
aa-hk1578:
	mkdir -p "$(SS1578_DATA)/StreamingAssets"
	cd $(PIPELINE) && cargo run --release --quiet --bin convert-aa -- \
	  "$(SS_DATA)/StreamingAssets/aa" "$(SS1578_DATA)/StreamingAssets/aa" $(UNITY1578)
	cd $(PIPELINE) && cargo run --release --quiet --bin fix-discrete-curves -- \
	  "$(SS1578_DATA)/StreamingAssets/aa"

# Silksong install Managed -> local-refs-hk1578/ (IL-prefixed, then IL-downgraded, plus the downgraded direct
# references and the shim; compile refs for the hk1578 target). Prefix FIRST: prefixing after the downgrade would
# rename the downgrader's tk2d retargets (they point at the host's Assembly-CSharp) onto the prefixed copy,
# where no tk2d type lives.
local-refs-hk1578: hk1578-tools
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:GenerateLocalRefs \
	  -p:SilksongManaged="$(SS_MANAGED)" -p:LocalRefs="$(LOCAL_REFS_1578)" \
	  -p:SuppressImplicitGitSourceLink=true
	cp "$(SS_MANAGED)/Unity.Addressables.dll" "$(SS_MANAGED)/Unity.ResourceManager.dll" \
	   "$(SS_MANAGED)/Newtonsoft.Json.UnityConverters.dll" "$(UNITYCOMPAT_DLL)" "$(LOCAL_REFS_1578)/"
	set -- ; for f in "$(LOCAL_REFS_1578)"/*.dll; do \
	  [ "$$(basename "$$f")" = "HornetInHallownest.UnityCompat.dll" ] && continue; \
	  set -- "$$@" "$$f"; done; \
	dotnet "$(UNITYDOWNGRADER)" --hk "$(HK1578_MANAGED)" \
	  --shim "$(LOCAL_REFS_1578)/HornetInHallownest.UnityCompat.dll" "$$@"

# Silksong install -> bundles-hk1578/ (v7 containers declaring 2020.2.2f1; tk2d/SharedUtils MonoScripts
# retargeted to where the 2020.2 host keeps those types. Assets/bundles stays the Unity 6 set.)
bundles-hk1578:
	mkdir -p $(BUNDLES_1578)
	cd $(PIPELINE) && TARGET_UNITY_VERSION=$(UNITY1578) SILKSONG_PATH="$(SS_INSTALL)" \
	  HORNET_BUNDLE_OUT="$(BUNDLES_1578)" cargo run --release --quiet --bin remap-monoscripts
	cd $(PIPELINE) && TARGET_UNITY_VERSION=$(UNITY1578) SILKSONG_PATH="$(SS_INSTALL)" \
	  HORNET_BUNDLE_OUT="$(BUNDLES_1578)" cargo run --release --quiet --bin repack-resources
	cd $(PIPELINE) && cargo run --release --quiet --bin retarget-monoscripts -- \
	  "$(BUNDLES_1578)/monoscripts.silksong.bundle" "$(BUNDLES_1578)/.retarget.tmp" \
	  "TeamCherry.TK2D=Assembly-CSharp" "TeamCherry.SharedUtils=Silksong.TeamCherry.SharedUtils"
	mv "$(BUNDLES_1578)/.retarget.tmp" "$(BUNDLES_1578)/monoscripts.silksong.bundle"

# Silksong install (read-only) -> everything the 1.5.78 host needs, in order
hk1578: silksong-hk1578 aa-hk1578 local-refs-hk1578 bundles-hk1578
