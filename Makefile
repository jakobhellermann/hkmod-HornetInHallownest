PIPELINE   := $(CURDIR)/tools/asset-pipeline
LOCAL_REFS := $(CURDIR)/local-refs
LOCAL_REFS_1578 := $(CURDIR)/local-refs-hk1578
BUNDLES_1578    := $(CURDIR)/Assets/bundles-hk1578
UNITY1578       := 2020.2.2f1

CI_REFS := $(CURDIR)/ci-refs

.PHONY: local-refs ci-refs remap-monoscripts repack-resources docs help

help:
	@echo "HornetInHallownest asset pipeline:"
	@echo "  make local-refs        prefix dlls with Silksong into local-refs/Silksong.*.dll (+ downgraded for hk1578)"
	@echo "  make ci-refs           Stub local-refs into ci-refs"
	@echo "  make remap-monoscripts rebuild monoscripts.silksong.bundle (m_AssemblyName -> Silksong.*)"
	@echo "  make repack-resources  repack Silksong resources.assets -> silksong-resources.bundle"
	@echo "  make docs              regenerate tag docs"

TAGS_JQ := .layers | to_entries[] | (.key|tostring) + " " + .value

docs: docs/tags-hk.txt docs/tags-hkss.txt

docs/tags-hk.txt:
	rabex --steam-game 'Hollow Knight' file globalgamemanagers object TagManager cat --jq '$(TAGS_JQ)' | sed 's/^"//;s/"$$//' > $@

docs/tags-hkss.txt:
	rabex --steam-game 'Silksong'      file globalgamemanagers object TagManager cat --jq '$(TAGS_JQ)' | sed 's/^"//;s/"$$//' > $@

local-refs:
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:GenerateLocalRefs -p:TargetFramework=hk1512620
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:GenerateLocalRefs -p:TargetFramework=hk1578

# Stub the references into ci-refs
ci-refs: local-refs
	rm -rf $(CI_REFS)
	mkdir -p $(CI_REFS)/lib $(CI_REFS)/hk1578
	refasmer --omit-non-api-members true -O $(CI_REFS)/lib $(LOCAL_REFS)/*.dll
	refasmer --omit-non-api-members true -O $(CI_REFS)/hk1578 $(LOCAL_REFS_1578)/Silksong.*.dll \
	  $(addprefix $(LOCAL_REFS_1578)/,Unity.Addressables.dll Unity.ResourceManager.dll Newtonsoft.Json.UnityConverters.dll)

# Rebuild monoscripts bundle, rewriting m_AssemblyName -> Silksong.*. HK 1.5.78 keeps tk2d in its Assembly-CSharp.
remap-monoscripts:
	cd $(PIPELINE) && cargo run --release --bin remap-monoscripts
	cd $(PIPELINE) && TARGET_UNITY_VERSION=$(UNITY1578) HORNET_BUNDLE_OUT="$(BUNDLES_1578)" cargo run --release --bin remap-monoscripts
	cd $(PIPELINE) && cargo run --release --bin retarget-monoscripts -- \
	  "$(BUNDLES_1578)/monoscripts.silksong.bundle" "$(BUNDLES_1578)/.retarget.tmp" \
	  "TeamCherry.TK2D=Assembly-CSharp" "TeamCherry.SharedUtils=Silksong.TeamCherry.SharedUtils"
	mv "$(BUNDLES_1578)/.retarget.tmp" "$(BUNDLES_1578)/monoscripts.silksong.bundle"

# Repack Silksong's resources.assets -> silksong-resources.bundle assetbundle
repack-resources:
	cd $(PIPELINE) && cargo run --release --bin repack-resources
	cd $(PIPELINE) && TARGET_UNITY_VERSION=$(UNITY1578) HORNET_BUNDLE_OUT="$(BUNDLES_1578)" cargo run --release --bin repack-resources
