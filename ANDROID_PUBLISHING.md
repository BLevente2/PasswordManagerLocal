# Android publishing

The project always compiles against and targets Android 16 (API 36). The optional number after `-A` selects the minimum Android version that may install the APK.

## Commands

```text
publish -A
publish -A 10
publish -A 16
publish -F 10
```

- `publish -A` and `publish -A 10` create Android 10+ APKs.
- `publish -A 16` creates an Android 16+ APK. It does not create a more compatible APK than the Android 10 build.
- `publish -F 10` publishes Windows and Android.
- Accepted Android versions are whole numbers from 10 through 16.
- Text such as `publish -A sixteen` is reported as an invalid value.
- Numbers outside 10 through 16 are reported as unsupported.

## Output

Android output is written under:

```text
artifacts\publish\PasswordManagerLocal.Android\android-<minimum-version>-plus
```

For Android 10 through 15, the script creates separate ARM64 (`arm64-v8a`) and ARM32 (`armeabi-v7a`) APKs. Separate APKs avoid multi-runtime packaging problems and make the required CPU architecture explicit.

For Android 16, the script creates ARM64 only. Avalonia lists Android 16 ARM64/x64 as its supported Tier 1 configuration, and real Android phones use ARM rather than the emulator-oriented x64 ABI.

## Validation

After each build, the script:

- treats .NET for Android warning `XA0141` as an error so an incorrectly aligned native library cannot be silently distributed;
- checks the APK minimum SDK, target SDK, and CPU ABI with `aapt2`;
- checks 16 KB APK ZIP alignment with `zipalign -c -P 16 -v 4`.

The app contains native dependencies, so a successful build and install must still be tested on the actual Android 10 and Android 16 phones.

## Getting an exact installation failure

With USB debugging enabled, run:

```text
adb install -r "path-to-the-signed.apk"
adb shell getprop ro.product.cpu.abilist
adb shell getconf PAGE_SIZE
```

The install command normally reports the concrete package-manager reason. A page size of `16384` means the device is using 16 KB memory pages.

## Native dependency caveat

The Android package graph contains native SkiaSharp, HarfBuzzSharp, libsodium, and SQLCipher libraries. The SkiaSharp 3 override is already present. `SQLitePCLRaw.lib.e_sqlcipher.android` is a deprecated package containing unofficial SQLCipher native builds, so it is the dependency most likely to require separate future replacement if the build reports `XA0141` for `libe_sqlcipher.so`. The publish script intentionally does not suppress that warning.
