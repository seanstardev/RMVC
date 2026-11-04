"""Build and exercise the README's exact C# snippets. Requires Python 3 and .NET 9 SDK."""
from pathlib import Path
import queue
import re
import subprocess
import tempfile
import threading
import time
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
README = (ROOT / "README.md").read_text(encoding="utf-8-sig")


def snippet(name):
    pattern = r"<!-- quickstart:" + re.escape(name) + r" -->\s*```csharp\n(.*?)\n```"
    match = re.search(pattern, README, re.S)
    if not match:
        raise RuntimeError("Missing README code block: " + name)
    return match.group(1) + "\n"


def exercise(folder, composed):
    folder.mkdir()
    core = snippet("CounterApp.cs") + "\n" + snippet("IncrementTwiceCmd.cs")
    if composed:
        old = "private void OnIncrement() => Execute(new IncrementCountCmd());"
        if core.count(old) != 1:
            raise RuntimeError("The documented composition variation needs reviewing.")
        core = core.replace(old, "private void OnIncrement() => Execute(new IncrementTwiceCmd());")
    (folder / "CounterApp.cs").write_text(core, encoding="utf-8")
    (folder / "Program.cs").write_text(snippet("Program.cs"), encoding="utf-8")
    reference = escape(str(ROOT / "RMVC" / "RMVC.csproj"), {'"': '&quot;'})
    (folder / "CounterDemo.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework>'
        '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
        '</PropertyGroup><ItemGroup><ProjectReference Include="' + reference + '" />'
        '</ItemGroup></Project>', encoding="utf-8")
    artifacts = folder / "artifacts"
    built = subprocess.run(
        ["dotnet", "build", str(folder / "CounterDemo.csproj"),
         "--artifacts-path", str(artifacts), "--nologo", "-v", "quiet"],
        capture_output=True, text=True, timeout=60)
    if built.returncode:
        raise RuntimeError(built.stdout + built.stderr)
    print("PASS " + folder.name + ": README code builds without warnings", flush=True)

    dll = artifacts / "bin" / "CounterDemo" / "debug" / "CounterDemo.dll"
    process = subprocess.Popen(["dotnet", str(dll)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               text=True, bufsize=1)
    output = queue.Queue()
    transcript = []

    def read_output():
        for line in process.stdout:
            transcript.append(line.rstrip())
            output.put(line.rstrip())
        output.put(None)

    reader = threading.Thread(target=read_output, daemon=True)
    reader.start()

    def expect(value):
        deadline = time.monotonic() + 12
        while True:
            left = deadline - time.monotonic()
            if left <= 0:
                raise RuntimeError("Timed out waiting for " + value + "\n" + "\n".join(transcript))
            line = output.get(timeout=left)
            if line is None:
                raise RuntimeError("Process ended before " + value + "\n" + "\n".join(transcript))
            if line == value:
                return

    try:
        expect("Count: 0")
        process.stdin.write("\n")
        process.stdin.flush()
        expect("Count: 1")
        if composed:
            expect("Count: 2")
        print("PASS " + folder.name + ": view event reaches model and typed presentation", flush=True)
        process.stdin.write("q\n")
        process.stdin.flush()
        code = process.wait(timeout=12)
        reader.join(timeout=2)
        if code != 0 or any("RMVC: Failed" in line for line in transcript):
            raise RuntimeError("Shutdown failed\n" + "\n".join(transcript))
        print("PASS " + folder.name + ": host awaits successful framework shutdown", flush=True)
    finally:
        if process.poll() is None:
            process.kill()  # Only the child process created by this fixture.
            process.wait(timeout=5)
        process.stdin.close()
        reader.join(timeout=2)
        process.stdout.close()


with tempfile.TemporaryDirectory(prefix="rmvc-readme-") as temporary:
    work = Path(temporary).resolve()
    # TemporaryDirectory recursively cleans only this verified, freshly created tree.
    if work.parent != Path(tempfile.gettempdir()).resolve() or not work.name.startswith("rmvc-readme-"):
        raise RuntimeError("Unexpected temporary test directory")
    exercise(work / "single-command", composed=False)
    exercise(work / "composed-command", composed=True)
print("README verified: both runnable journeys pass.")
