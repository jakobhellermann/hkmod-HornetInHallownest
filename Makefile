PIPELINE   := $(CURDIR)/tools/asset-pipeline
LOCAL_REFS := $(CURDIR)/local-refs

CI_REFS := $(CURDIR)/ci-refs

# HK 1.5.78 (Unity 2020.2.2f1). The real Silksong install (SS_INSTALL) is only ever READ from.
HK1578      ?= $(HOME)/.local/share/Steam/steamapps/common/Hollow Knight-1.5.78.11833
SS_INSTALL ?= $(HOME)/.local/share/Steam/steamapps/common/Hollow Knight Silksong
HK1578_MANAGED := $(HK1578)/hollow_knight_Data/Managed
SS_DATA        := $(SS_INSTALL)/Hollow Knight Silksong_Data
SS_MANAGED     := $(SS_DATA)/Managed
LOCAL_REFS_1578 := $(CURDIR)/local-refs-hk1578
BUNDLES_1578    := $(CURDIR)/bundles-hk1578
UNITY1578       := 2020.2.2f1
UNITYCOMPAT_DLL  := $(CURDIR)/tools/UnityCompat/bin/Release/net472/HornetInHallownest.UnityCompat.dll
UNITYDOWNGRADER := $(CURDIR)/tools/UnityDowngrader/bin/Release/net8.0/UnityDowngrader.dll

.PHONY: local-refs ci-refs remap-monoscripts repack-resources docs help \
    hk1578 hk1578-tools local-refs-hk1578 bundles-hk1578

help:
	@echo "HornetInHallownest asset pipeline:"
	@echo "  make local-refs        prefix dlls with Silksong into local-refs/Silksong.*.dll"
	@echo "  make ci-refs           Stub local-refs into ci-refs"
	@echo "  make remap-monoscripts rebuild monoscripts.silksong.bundle (m_AssemblyName -> Silksong.*)"
	@echo "  make repack-resources  repack Silksong resources.assets -> silksong-resources.bundle"
	@echo "  make docs              regenerate tag docs"
	@echo "  make hk1578            full HK 1.5.78 dev setup: local-refs + bundles"
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
	dotnet build -c Release -v q --nologo $(CURDIR)/tools/UnityCompat -p:HKRefs="$(HK1578_MANAGED)"
	dotnet build -c Release -v q --nologo $(CURDIR)/tools/UnityDowngrader

# Silksong install Managed -> local-refs-hk1578/ (compile refs for the hk1578 target), built exactly like the
# in-game setup builds them
local-refs-hk1578: hk1578-tools
	rm -rf "$(LOCAL_REFS_1578)"
	dotnet "$(UNITYDOWNGRADER)" --hk "$(HK1578_MANAGED)" --silksong "$(SS_MANAGED)" \
	  --shim "$(UNITYCOMPAT_DLL)" --out "$(LOCAL_REFS_1578)"

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

# Silksong install (read-only) -> everything building the hk1578 target needs
hk1578: local-refs-hk1578 bundles-hk1578
