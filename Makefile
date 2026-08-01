PIPELINE   := $(CURDIR)/tools/asset-pipeline
LOCAL_REFS := $(CURDIR)/local-refs

# Managed dirs the csproj references
GAME_PATH  ?= $(HOME)/.local/share/Steam/steamapps/common/Hollow Knight
HK_MANAGED ?= $(GAME_PATH)/hollow_knight_Data/Managed
SS_MANAGED ?= $(GAME_PATH)/../Hollow Knight Silksong/Hollow Knight Silksong_Data/Managed

CI_REFS := $(CURDIR)/ci-refs

# Assemblies the csproj <Reference>s
HK_REFS := Assembly-CSharp TeamCherry.TK2D TeamCherry.SharedUtils TeamCherry.NestedFadeGroup \
           PlayMaker TeamCherry.Localization Newtonsoft.Json Mono.Cecil \
           MonoMod.RuntimeDetour MonoMod.Utils \
           UnityEngine UnityEngine.CoreModule UnityEngine.Physics2DModule UnityEngine.AudioModule \
           UnityEngine.ParticleSystemModule UnityEngine.AnimationModule UnityEngine.UI \
           UnityEngine.JSONSerializeModule UnityEngine.InputLegacyModule UnityEngine.ImageConversionModule \
           UnityEngine.ScreenCaptureModule UnityEngine.AssetBundleModule
SS_REFS := Unity.Addressables Unity.ResourceManager Newtonsoft.Json.UnityConverters

.PHONY: local-refs ci-refs remap-monoscripts repack-resources docs help

help:
	@echo "HornetInHallownest asset pipeline:"
	@echo "  make local-refs        prefix dlls with Silksong into local-refs/Silksong.*.dll"
	@echo "  make ci-refs           Stub local-refs + game dlls into ci-refs"
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
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:GenerateLocalRefs

# Stub the references into ci-refs
ci-refs: local-refs
	rm -rf $(CI_REFS)
	mkdir -p $(CI_REFS)/managed $(CI_REFS)/lib
	refasmer --omit-non-api-members true -O $(CI_REFS)/managed $(patsubst %,"$(HK_MANAGED)/%.dll",$(HK_REFS)) $(patsubst %,"$(SS_MANAGED)/%.dll",$(SS_REFS))
	refasmer --omit-non-api-members true -O $(CI_REFS)/lib $(LOCAL_REFS)/*.dll

# Rebuild monoscripts bundle, rewriting m_AssemblyName -> Silksong.*
remap-monoscripts:
	cd $(PIPELINE) && cargo run --release --bin remap-monoscripts

# Repack Silksong's resources.assets -> silksong-resources.bundle assetbundle
repack-resources:
	cd $(PIPELINE) && cargo run --release --bin repack-resources
