# CI Pipeline Updates for WebAssembly Support

## Summary

The CI pipeline has been updated to support WebAssembly runtime testing. The infrastructure is complete and ready to activate once the WASM test project is created.

## Changes Made

### 1. New CI Job: `wasm-tests`

**File:** `.github/workflows/ci.yml`

Added a new job that runs WebAssembly tests in actual browser environment:

```yaml
wasm-tests:
  name: WASM Runtime Tests
  runs-on: ubuntu-latest
  if: false  # Disabled until test project is created
```

### 2. Job Features

The WASM test job includes:

- ✅ **Platform**: Ubuntu Linux (optimal for WASM tooling)
- ✅ **Multi-version .NET**: Installs both .NET 8.0 and 9.0
- ✅ **Browser Automation**: Installs Playwright with Chromium
- ✅ **WASM Tooling**: Installs `wasm-tools` workload
- ✅ **Build Step**: Compiles WASM test project for `net8.0-browserwasm`
- ✅ **Test Execution**: Runs tests in browser via Uno.UI.RuntimeTests
- ✅ **Results Upload**: Uploads test results as artifacts
- ✅ **Test Reporting**: Publishes results using `dorny/test-reporter`

### 3. Job Workflow

When enabled, the job will:

1. **Checkout** code with full git history
2. **Setup** .NET 8.0 and 9.0 SDKs
3. **Install** Playwright and Chromium browser
4. **Install** WASM workload for compilation
5. **Build** the WASM test project targeting browser
6. **Execute** tests in Chromium browser
7. **Collect** TRX test results
8. **Upload** results as GitHub artifacts
9. **Publish** results to PR checks

### 4. Test Results Integration

WASM test results will appear:
- ✅ In GitHub Actions summary
- ✅ As PR check annotations
- ✅ In the test reporter dashboard
- ✅ As downloadable artifacts (TRX files)

## Current Status

### ✅ Complete
- CI job configuration
- Build and test steps
- Results reporting setup
- Documentation (`docs/wasm-testing-setup.md`)

### ⏳ Pending
- WASM test project creation (see `docs/wasm-testing-setup.md`)
- Job activation (change `if: false` to `if: true`)

## Activation Steps

Once the WASM test project is created:

### Step 1: Verify Test Project Structure

Ensure the following exists:
```
src/Uno.DevTools.Telemetry.WasmTests/
├── Uno.DevTools.Telemetry.WasmTests.csproj
├── Tests/
│   └── TelemetryWasmTests.cs
└── ... (other Uno Platform app files)
```

### Step 2: Test Locally

```bash
cd src/Uno.DevTools.Telemetry.WasmTests
dotnet build -f net8.0-browserwasm
dotnet test -f net8.0-browserwasm
```

Verify all tests pass in local browser environment.

### Step 3: Activate CI Job

Edit `.github/workflows/ci.yml`:

```yaml
wasm-tests:
  name: WASM Runtime Tests
  runs-on: ubuntu-latest
  if: true  # Changed from 'false' to activate
```

### Step 4: Commit and Push

```bash
git add .github/workflows/ci.yml
git commit -m "ci: Enable WASM runtime tests"
git push
```

### Step 5: Verify in CI

- Open a PR or push to main
- Check the "WASM Runtime Tests" job runs successfully
- Verify test results appear in the PR checks
- Confirm browser-based tests execute properly

## Troubleshooting

### Job Fails: "Project not found"

**Problem:** The WASM test project doesn't exist yet.

**Solution:**
1. Create the project following `docs/wasm-testing-setup.md`
2. Or keep `if: false` until ready

### Job Fails: "Playwright installation error"

**Problem:** Chromium installation failed.

**Solution:** Check Playwright version compatibility with current .NET version. Update version in CI if needed.

### Job Fails: "WASM workload not found"

**Problem:** `wasm-tools` workload installation failed.

**Solution:** Ensure .NET SDK version supports WASM workload. May need to update SDK version.

### Tests Timeout

**Problem:** Browser tests take too long.

**Solution:** Add timeout configuration to test step:
```yaml
- name: Run WASM Runtime Tests
  timeout-minutes: 10
  run: ...
```

### Tests Fail in CI but Pass Locally

**Problem:** Environment differences between local and CI.

**Solution:**
1. Check browser version (CI uses Chromium, local may use different browser)
2. Verify network access to Application Insights endpoint
3. Check for timing/race conditions (CI may be slower)

## Performance Expectations

Expected CI job timing:
- Setup (checkout, .NET, Playwright): **~30-60 seconds**
- WASM workload install: **~30-60 seconds**
- Build WASM project: **~30-90 seconds**
- Test execution (browser startup + tests): **~1-3 minutes**
- **Total**: **~3-6 minutes**

This is acceptable overhead for validating browser compatibility.

## Dependencies

The WASM test job depends on:
- **GitHub Actions**: Standard GitHub infrastructure
- **Ubuntu**: `ubuntu-latest` runner image
- **.NET SDK**: Versions 8.0 and 9.0
- **Playwright**: Browser automation (Chromium)
- **WASM Workload**: .NET WASM build tools
- **Uno.UI.RuntimeTests**: Test framework (in test project)

No external services or secrets required.

## Security Considerations

- ✅ No secrets used (uses test instrumentation key)
- ✅ No external service dependencies (beyond Application Insights for telemetry)
- ✅ Runs in sandboxed browser environment
- ✅ Uses official Microsoft and Uno Platform packages only

## Maintenance

### Keeping Dependencies Updated

Update these as needed:
- Playwright version: In `wasm-tests` job
- .NET SDK versions: In `Setup .NET` step
- Uno.UI.RuntimeTests: In test project `.csproj`

### Monitoring CI Performance

Watch for:
- Job duration increases (may indicate build/test slowdowns)
- Intermittent failures (may indicate flaky tests or environment issues)
- Timeout issues (may need to adjust timeout values)

## References

- [CI Configuration](.github/workflows/ci.yml)
- [WASM Testing Setup Guide](wasm-testing-setup.md)
- [Spec: WebAssembly Support](../specs/001-wasm-compat.md)
- [Uno.UI.RuntimeTests Documentation](https://github.com/unoplatform/uno/blob/master/doc/articles/features/runtime-tests.md)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Playwright Documentation](https://playwright.dev/dotnet/)

## Conclusion

The CI pipeline is now fully configured for WebAssembly testing. The infrastructure is ready and waiting for the WASM test project to be created. Once created, simply flip the `if: false` to `if: true` to activate comprehensive browser-based testing on every PR and push.

This ensures that WebAssembly support remains stable and functional as the package evolves.
