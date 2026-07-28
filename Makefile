# Developer-experience targets for Kubernetes.EnvTest.
# Windows users: make.ps1 provides the same targets.

SOLUTION      := Kubernetes.EnvTest.slnx
CONFIGURATION ?= Release
ARTIFACTS     := artifacts

# Detect the current machine's RID (local builds never cross-compile; the
# release workflow owns the full six-RID matrix).
UNAME_S := $(shell uname -s)
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_S),Darwin)
  RID_OS := osx
else
  RID_OS := linux
endif
ifeq ($(UNAME_M),arm64)
  RID_ARCH := arm64
else ifeq ($(UNAME_M),aarch64)
  RID_ARCH := arm64
else
  RID_ARCH := x64
endif
RID := $(RID_OS)-$(RID_ARCH)

.PHONY: help build test test-integration test-all bench format format-check pack publish-tool clean

help: ## Show available targets
	@grep -E '^[a-zA-Z_-]+:.*## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*## "}; {printf "  %-18s %s\n", $$1, $$2}'

build: ## Build the whole solution
	dotnet build $(SOLUTION) -c $(CONFIGURATION)

test: ## Run unit tests
	dotnet test test/Kubernetes.EnvTest.Tests/Kubernetes.EnvTest.Tests.csproj -c $(CONFIGURATION)

test-integration: ## Run integration tests (downloads envtest binaries; needs network)
	dotnet test test/Kubernetes.EnvTest.IntegrationTests/Kubernetes.EnvTest.IntegrationTests.csproj -c $(CONFIGURATION)

test-all: test test-integration ## Run unit and integration tests

bench: ## Run all benchmarks
	dotnet run --project test/Kubernetes.EnvTest.Benchmarks -c Release -- --filter '*'

format: ## Apply code style fixes
	dotnet format $(SOLUTION)

format-check: ## Verify code style without changing files (same check as CI)
	dotnet format $(SOLUTION) --verify-no-changes

pack: ## Pack library NuGet packages and the AOT tool package for this machine's RID
	dotnet pack src/Kubernetes.EnvTest.Provisioning -c $(CONFIGURATION) -o $(ARTIFACTS)
	dotnet pack src/Kubernetes.EnvTest -c $(CONFIGURATION) -o $(ARTIFACTS)
	dotnet pack src/Kubernetes.EnvTest.Tool -c $(CONFIGURATION) -o $(ARTIFACTS) -r $(RID)

publish-tool: ## Publish the setup-envtest CLI as a native binary for this machine's RID
	dotnet publish src/Kubernetes.EnvTest.Tool -c Release -r $(RID) -o $(ARTIFACTS)/setup-envtest-$(RID)

clean: ## Remove build outputs
	dotnet clean $(SOLUTION) -c $(CONFIGURATION)
	rm -rf $(ARTIFACTS)
