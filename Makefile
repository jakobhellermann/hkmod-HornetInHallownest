# Generate Silksong.*.dll libs in Source/lib
# Generate modified monoscript/resources.assets

PIPELINE := $(CURDIR)/tools/asset-pipeline
LIB      := $(CURDIR)/Source/lib

.PHONY: all setup-libs remap-monoscripts repack-resources help

help:
	@echo "HornetInHallownest asset pipeline (regenerates Source/lib/):"
	@echo "  make setup-libs        prefix Silksong Assembly-CSharp/firstpass/PlayMaker/TeamCherry -> Silksong.*.dll"
	@echo "  make remap-monoscripts rebuild monoscripts.silksong.bundle (m_AssemblyName -> Silksong.*)"
	@echo "  make repack-resources  repack Silksong resources.assets -> silksong-resources.bundle"
	@echo "  make all               all of the above"

all: setup-libs remap-monoscripts repack-resources

# Prefix Silksong's required libs into Source/libs
setup-libs:
	dotnet msbuild $(CURDIR)/Source/HornetInHallownest.csproj -t:SetupSilksongLibs

# Rebuild monoscripts bundle, rewriting m_AssemblyName -> Silksong.*
remap-monoscripts:
	cd $(PIPELINE) && cargo run --release --bin remap-monoscripts

# Repack Silksong's resources.assets -> silksong-resources.bundle assetbundle
repack-resources:
	cd $(PIPELINE) && cargo run --release --bin repack-resources
