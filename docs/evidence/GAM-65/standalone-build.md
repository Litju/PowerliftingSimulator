# GAM-65 Windows standalone build evidence

Unity 6000.3.22f1 attempted a `StandaloneWindows64` IL2CPP build through `GAM65DemoGameBuilder.BuildWindowsStandalonePlayer`.

**Result: failed.** The Unity build report recorded `BuildResult.Failed` with one error. `WinPlayerBuildProgram.SetupUnityLinker` threw `DirectoryNotFoundException` because `Editor/Data/il2cpp/build/deploy` is missing from the installed Unity editor. The output folder contains a partial executable left by that failed attempt; it is not a successful player build. No standalone launch or standalone gameplay smoke was completed.

The raw Unity log is retained locally in the ignored `Artifacts/Evidence/GAM-65/windows_standalone_build.log`; the failure excerpt is reproduced here:

```text
System.IO.DirectoryNotFoundException: The path D:/Dev/Unity/6000.3.22f1/Editor/Data/il2cpp/build/deploy does not exist.
InvalidOperationException: GAM-65 Windows standalone build failed: result=Failed, totalErrors=1
```

The Editor PlayMode screenshots in `gameplay-contact-sheet.png` are not standalone captures.
