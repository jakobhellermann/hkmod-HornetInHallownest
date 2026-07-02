# HornetPlayer asset pipeline — regenerates the Source/lib/ artifacts the mod loads:
# the prefixed Silksong assemblies + remapped/repacked bundles. See CLAUDE.md "Build pipeline".
# Run `make all` after a fresh checkout / `git clean`, or a single target as needed.
# Outputs are gitignored except monoscripts.silksong.bundle (tracked in git).

PIPELINE := $(CURDIR)/tools/asset-pipeline
LIB      := $(CURDIR)/Source/lib

.PHONY: all setup-libs remap-monoscripts repack-resources help

help:
	@echo "HornetPlayer asset pipeline (regenerates Source/lib/):"
	@echo "  make setup-libs        prefix Silksong Assembly-CSharp/firstpass/PlayMaker/TeamCherry -> Silksong.*.dll"
	@echo "  make remap-monoscripts rebuild monoscripts.silksong.bundle (m_AssemblyName -> Silksong.*)"
	@echo "  make repack-resources  repack Silksong resources.assets -> silksong-resources.bundle"
	@echo "  make all               all of the above"

all: setup-libs remap-monoscripts repack-resources

# Prefix Silksong's Assembly-CSharp/firstpass/PlayMaker (+ the PlayMaker-action TeamCherry asms) -> Silksong.* into
# Source/lib/, and copy the Unity-package deps HK lacks. Re-run after a Silksong update.
setup-libs:
	bash $(CURDIR)/tools/setup-silksong-libs.sh

# Rebuild the hero MonoScripts bundle, rewriting m_AssemblyName -> Silksong.* (incl. PlayMaker.dll -> Silksong.PlayMaker).
remap-monoscripts:
	cd $(PIPELINE) && cargo run --release --bin remap-monoscripts

# Repack Silksong's resources.assets (the whole ResourceManager container) -> silksong-resources.bundle, which
# ResourcesShim serves Silksong's Resources.Load from.
repack-resources:
	cd $(PIPELINE) && cargo run --release --bin repack-resources
