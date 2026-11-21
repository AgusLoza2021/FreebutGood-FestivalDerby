# Running Unity as an AI agent (no harness needed)

You can compile, test and run Editor scripts for this project **without opening the Unity Editor**.
Everything goes through one script, `tools/unity.ps1`, which runs Unity in batchmode, waits, and prints a summary.
You only need the ability to run a shell command.

## The four commands

Run them from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action compile
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform EditMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform PlayMode
powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action exec -Method MyEditorClass.Run
```

**Options:**

| Option | Use |
|---|---|
| `-TestFilter <name>` | Runs only matching tests |
| `-Grep <regex>` | Chooses which log lines `exec` prints (default: `\[agent\]\|Exception`) |
| `-TimeoutMinutes <n>` | Changes the time limit (default: 30) |

**How long they take:** a run takes **5–20 minutes**, and the first run after many changes takes the longest.

- Give your shell call a timeout of at least 30 minutes, or run it in the background and wait for it to end.
- Never start a second run while one is still going.

## How to read the output

The last line is always `RESULT: <something>`. It is the only verdict. Do not judge from the other lines.

| RESULT | Meaning | What you do |
|---|---|---|
| `PASS` | It compiled, no new test failures, and for `exec`/`compile` Unity exited with 0 | Continue |
| `FAIL` | See `COMPILE_ERRORS`, `NEW_FAILURES` or `UNITY_EXIT` above it | Fix the first compile error, or the new failing test, then run again |
| `BLOCKED` | A Unity Editor has this project open | Ask the owner to close the Editor. Do **not** kill it yourself |
| `TIMEOUT` | Unity did not finish | Read the end of the `LOG:` file and report |
| `ERROR` | Unity is missing or you used the script wrong | Read the message |

Other things to know about the output:

- `KNOWN_FAILURES` lists tests that already fail on the base. They are listed in `tools/known-test-failures.txt` and do **not** make the result `FAIL`.
- A missing-script or missing-prefab line under `MISSING_REFS` that names something you touched is a real problem, even if `RESULT` says `PASS`.
- The full Unity log and the result XML are saved in `Logs/agent-unity/<timestamp>-<action>/`, which git ignores. Open the log only when the summary isn't enough. It is several MB, so search it instead of reading all of it.

## Rules

1. **Never say "it compiles" or "tests pass" unless a `RESULT: PASS` line appeared in this session.** When you report, paste the summary block.
2. **Run `compile` after editing C#**, and `test` before every commit.
3. **Do not edit `tools/known-test-failures.txt` to make a failure go away.** Remove a line only when you have actually fixed that test.
4. **Editor open versus batchmode.** If you control Unity through an Editor connection (for example a Unity MCP plugin), the Editor is open and this script returns `BLOCKED`. Then choose one mode:
   - use the Editor connection's own test and compile tools;
   - or ask the owner to close the Editor and use this script.
   Both cannot run on the same project at the same time.

## Running Editor code with `exec`

Use `exec` to change scenes or assets through Unity instead of hand-editing YAML.

1. Create a static method in a file under an `Editor/` folder, for example `Assets/Editor/AgentTask.cs`.
2. Make it log its progress with the prefix `[agent]`.
3. Make it end with `EditorApplication.Exit(0)` on success, or `EditorApplication.Exit(1)` on failure.
4. Run `-Action exec -Method AgentTask.Run`.
5. Delete the temporary script and its `.meta` before committing.

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AgentTask
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/FreebutGood/FestivalDerby/Scenes/Drive.unity");
        Debug.Log("[agent] opened " + scene.name);
        // ... change things through the Unity API ...
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[agent] saved");
        EditorApplication.Exit(0);
    }
}
```

`odd/ORCHESTRATOR.md` §5.3 has a complete example that deletes scene objects safely.
