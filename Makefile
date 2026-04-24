TFM      := netstandard2.1
CONFIG   := Debug
DLL      := VGMissionJournal.dll

BUILDDIR := VGMissionJournal/bin/$(CONFIG)/$(TFM)
BUILDDLL := $(BUILDDIR)/$(DLL)

# WSL path to the game install — adjust if Steam lives elsewhere.
GAME_DIR   := /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
PLUGIN_DIR := $(GAME_DIR)/BepInEx/plugins
VGMISSIONJOURNAL_DIR := $(PLUGIN_DIR)/VGMissionJournal

# Path to the sibling VGTTS checkout — we reuse its publicized stub so all
# three mods (VGTTS, VGAnima, VGMissionJournal) compile against the same stub.
VGTTS_LIB := ../vanguard-galaxy-tts/VGTTS/lib

DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)

# Our test project targets net8.0, but CI/dev boxes frequently run a newer
# .NET SDK (10.x on this host). LatestMajor tells the host to roll forward
# past unavailable majors so `dotnet test` keeps working on single-runtime
# hosts without pinning a specific SDK.
export DOTNET_ROLL_FORWARD := LatestMajor

.PHONY: all build link-asm deploy clean test

all: build

# Symlink the VGTTS-maintained publicized Assembly-CSharp.dll into
# VGMissionJournal/lib/ so we compile against the same stub (single source of
# truth across VGTTS / VGAnima / VGMissionJournal).
link-asm:
	@mkdir -p VGMissionJournal/lib
	@if [ ! -e "VGMissionJournal/lib/Assembly-CSharp.dll" ]; then \
		ln -sf "$(abspath $(VGTTS_LIB))/Assembly-CSharp.dll" VGMissionJournal/lib/Assembly-CSharp.dll ; \
		echo "Linked Assembly-CSharp.dll from $(VGTTS_LIB)" ; \
	fi

build: link-asm
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGMissionJournal/VGMissionJournal.csproj -c $(CONFIG)

test:
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) test VGMissionJournal.Tests/VGMissionJournal.Tests.csproj -c $(CONFIG)

deploy: build
	@test -d "$(PLUGIN_DIR)" || { echo "BepInEx plugins dir not found at $(PLUGIN_DIR)" ; exit 1 ; }
	@mkdir -p "$(VGMISSIONJOURNAL_DIR)"
	# Copy every runtime assembly from bin/. CopyLocalLockFileAssemblies=true
	# in VGMissionJournal.csproj restricts bin/ to VGMissionJournal.dll + any NuGet
	# runtime dep the game doesn't ship; BepInEx / Harmony / UnityEngine /
	# Newtonsoft are compile-only so they don't land here.
	cp "$(BUILDDIR)"/*.dll "$(VGMISSIONJOURNAL_DIR)/"
	@if [ -f "$(BUILDDIR)/VGMissionJournal.pdb" ]; then cp "$(BUILDDIR)/VGMissionJournal.pdb" "$(VGMISSIONJOURNAL_DIR)/"; fi
	@echo "Deployed $(shell ls $(BUILDDIR)/*.dll | wc -l) DLL(s) to $(VGMISSIONJOURNAL_DIR)"

clean:
	-$(DOTNET) clean VGMissionJournal/VGMissionJournal.csproj
	rm -rf VGMissionJournal/bin VGMissionJournal/obj VGMissionJournal.Tests/bin VGMissionJournal.Tests/obj dist/
